using System.Net.Http.Json;
using System.Text.Json;
namespace Rune.Voice;

public sealed partial class RuneWindow
{
    private readonly ComboBox microphoneMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly ComboBox microphoneKey = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 156 };
    private readonly ProgressBar microphoneLevel = new() { Width = 80, Height = 18, Maximum = 100, Margin = new Padding(8, 6, 6, 0) };
    private readonly RuneCheckBox allowGameOrders = new() { Text = "Allow game orders", Checked = true, AutoSize = true };
    private readonly HttpClient microphoneHttp = new() { BaseAddress = new Uri((LocalServices.Audio + "")), Timeout = TimeSpan.FromSeconds(5) };
    private readonly SemaphoreSlim microphoneGate = new(1, 1);
    private int microphoneDevice = -1, microphonePending, busyGeneration = -1;
    private bool discardCapture, pollingMicrophone, speakingReply, microphoneHasSpeech;
    private sealed record HeardSpeech(string Text, string Id, DateTime Received);
    private readonly Queue<HeardSpeech> heardSpeech = new();
    private HeardSpeech? pendingAddress;
    private DateTime listenAfter, nextMicPoll;
    private bool AlwaysOn => microphoneMode.SelectedIndex == 2;
    private bool ReplyPlaying => voicePreviewGeneration >= 0 || speakingReply || neural.IsSpeaking || speaker.State == System.Speech.Synthesis.SynthesizerState.Speaking;
    private string MicrophoneStatus => AlwaysOn && microphoneMuted ? "MIC MUTED" : AlwaysOn && recognizing ? "Listening · headset mode" : collecting ? "Listening" : recognizing ? "Transcribing locally" : "Mic off";

    private Control BuildMicrophonePanel()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        var controls = new FlowLayoutPanel { Width = 780, Height = 34, WrapContents = false, Margin = Padding.Empty };
        microphoneMode.Items.AddRange(new object[] { "Microphone off", "Hold to talk", "Always on · local AI" }); microphoneMode.SelectedIndex = 1;
        microphoneKey.Items.AddRange(new object[] { "Ctrl + Alt + R", "Ctrl + Alt + V", "Mouse button only" }); microphoneKey.SelectedIndex = 0;
        controls.Controls.Add(microphoneMode); var keysButton = RuneTheme.Button("Keybinds"); keysButton.Click += (_, _) => ShowPage("keybinds"); controls.Controls.Add(keysButton); controls.Controls.Add(microphoneLevel);
        var setup = new Button { Text = "Audio setup", AutoSize = true, FlatStyle = FlatStyle.Flat, Height = 28 };
        setup.Click += async (_, _) => await ShowMicrophoneSetup(); controls.Controls.Add(setup); muteMicrophone.Click += (_, _) => ToggleMicrophoneMute(); controls.Controls.Add(muteMicrophone); panel.Controls.Add(controls);
        var permissions = new FlowLayoutPanel { Width = 740, Height = 28, WrapContents = false, Margin = Padding.Empty }; permissions.Controls.Add(allowGameOrders); panel.Controls.Add(permissions);
        var setupHint = new Label { Width = 780, Height = 51, Font = new Font("Segoe UI", 9), ForeColor = RuneTheme.Muted, Text = "Use your microphone keybind, or choose Always on for hands-free chat.\nIn Always on, the same talk shortcut toggles mute. Input and output stay separate." }; panel.Controls.Add(setupHint);
        try { using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (doc.RootElement.TryGetProperty("microphoneMode", out var mode)) microphoneMode.SelectedIndex = Math.Clamp(mode.GetInt32(), 0, 2);
            if (doc.RootElement.TryGetProperty("microphoneKey", out var key)) microphoneKey.SelectedIndex = Math.Clamp(key.GetInt32(), 0, 2);
            if (doc.RootElement.TryGetProperty("microphoneDevice", out var device)) microphoneDevice = device.GetInt32();
            if (doc.RootElement.TryGetProperty("outputDeviceName", out var output)) neural.OutputDeviceName = output.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("allowGameOrders", out var orders)) allowGameOrders.Checked = orders.GetBoolean();
        } catch { }
        muteMicrophone.Enabled = AlwaysOn; if (conversationTalk != null) conversationTalk.Text = AlwaysOn ? "Mute mic" : "Hold to talk";
        microphoneMode.SelectedIndexChanged += (_, _) => { muteMicrophone.Enabled = AlwaysOn; microphoneMuted = false; muteMicrophone.Text = "Mute mic"; if (conversationTalk != null) conversationTalk.Text = AlwaysOn ? "Mute mic" : "Hold to talk"; StopCapture(true); SavePreferences(); listenAfter = DateTime.UtcNow.AddMilliseconds(600); listeningLabel.Text = AlwaysOn ? "Always on · local Qwen conversation. Your selected input stays open during replies." : microphoneMode.SelectedIndex == 0 ? "Microphone muted. You can still type." : "Hold the selected shortcut or the Hold to talk button, then release to send."; };
        microphoneKey.SelectedIndexChanged += (_, _) => { preferences.MicShortcut = microphoneKey.SelectedIndex == 0 ? "Ctrl + Alt + R" : microphoneKey.SelectedIndex == 1 ? "Ctrl + Alt + V" : "Off"; StopCapture(true); keyDown = false; SavePreferences(); };
        allowGameOrders.CheckedChanged += (_, _) => SavePreferences();
        return panel;
    }
    private void TickMicrophone()
    {
        TickCompanionShortcut();
        bool pressed = microphoneMode.SelectedIndex != 0 && ShortcutHeld(preferences.MicShortcut) && !keyChoices.Values.Any(c => c.Focused || c.DroppedDown);
        ProcessMicrophoneShortcut(pressed);
        if (AlwaysOn && !microphoneMuted && !recognizing && microphonePending == 0 && DateTime.UtcNow >= listenAfter) BeginListening();
        if (recognizing && !pollingMicrophone && DateTime.UtcNow >= nextMicPoll) _ = PollMicrophone();
    }
    private void ProcessMicrophoneShortcut(bool pressed)
    {
        if (pressed && !keyDown) { if (AlwaysOn) ToggleMicrophoneMute(); else BeginListening(); }
        if (!pressed && keyDown && !buttonDown && !AlwaysOn) EndListening(); keyDown = pressed;
    }
    private async void BeginListening()
    {
        if (recognizing || microphonePending > 0 || closing || microphoneMode.SelectedIndex == 0 || microphoneMuted) return;
        if (!AlwaysOn) CancelTurn(); discardCapture = false; listenGeneration = generation; recognizing = collecting = true; microphonePending++;
        listeningLabel.Text = "Starting local microphone…";
        await microphoneGate.WaitAsync();
        try {
            string names = string.Join(",", preferences.Profiles.SelectMany(p => new[] { p.Name, p.RecognitionName }).Distinct(StringComparer.OrdinalIgnoreCase));
            using var response = await microphoneHttp.PostAsync("/listen", LocalJson(new { automatic = AlwaysOn, device = microphoneDevice, companionNames = names, language = preferences.Language }));
            await CheckMicrophoneResponse(response);
            if (!closing && !discardCapture) listeningLabel.Text = AlwaysOn ? "LISTENING · Speak naturally. A pause sends your words to local Qwen." : "LISTENING · Release your shortcut or the button to send.";
        } catch (Exception e) { MicrophoneFailed(e.Message); }
        finally { microphonePending--; microphoneGate.Release(); }
    }
    private async Task PollMicrophone()
    {
        int pollRecipient = recipientVersion; pollingMicrophone = true; nextMicPoll = DateTime.UtcNow.AddMilliseconds(220); var recognized = new List<string>();
        var durations = new List<double?>();
        await microphoneGate.WaitAsync();
        try {
            using var response = await microphoneHttp.GetAsync("/status"); await CheckMicrophoneResponse(response);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (closing) return;
            var status = doc.RootElement; microphoneLevel.Value = Math.Clamp(status.GetProperty("level").GetInt32(), 0, 100);
            collecting = recognizing && !discardCapture && status.GetProperty("state").GetString() == "listening";
            if (status.GetProperty("state").GetString() == "transcribing") listeningLabel.Text = "Recognizing your words locally…";
            microphoneHasSpeech = status.TryGetProperty("hasSpeech", out var activeSpeech) && activeSpeech.GetBoolean();
            foreach (var result in status.GetProperty("events").EnumerateArray()) {
                bool accept = pollRecipient == recipientVersion && recognizing && !microphoneMuted && !discardCapture && (AlwaysOn || listenGeneration == generation) && microphoneMode.SelectedIndex != 0;
                if (!AlwaysOn) recognizing = collecting = false; listenAfter = DateTime.UtcNow.AddMilliseconds(150);
                if (!accept) continue;
                if (result.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
                string? send = result.GetProperty("text").GetString();
                if (!string.IsNullOrWhiteSpace(send)) { recognized.Add(send); durations.Add(result.TryGetProperty("seconds", out var seconds) ? seconds.GetDouble() * 1000 : null); }
                else AddLine("Microphone", "Speech was detected, but I could not make out the words. Please repeat that.");
                listeningLabel.Text = string.IsNullOrWhiteSpace(send) ? "No clear speech detected. Check the level meter and Mic setup." : "Heard: " + send;
            }
        } catch (Exception e) { MicrophoneFailed(e.Message); }
        finally { microphoneGate.Release(); pollingMicrophone = false; }
        AcceptRecognizedSpeech(recognized, durations);
    }
    private bool IsImmediateRecall(string text)
    {
        var address = CompanionAddress.Parse(text, preferences.Profiles);
        if (address.Companion != null && address.Companion != bridge.CompanionId) return false;
        string? action = Rune.Shared.Rules.Parse(address.Text)?.action;
        return action is "follow" or "stay" or "defend" or "stop_pickup";
    }
    private void QueueSpeech(HeardSpeech speech)
    {
        if (heardSpeech.Count >= 8) {
            var old = heardSpeech.Dequeue();
            taskTrace.Record("request-cancelled", new { reason = "Speech queue limit reached; old request was not executed" }, old.Id);
            AddLine("Voice", "I could not keep up. I skipped an older queued request; please repeat it if still needed.");
        }
        heardSpeech.Enqueue(speech);
    }
    private void AcceptRecognizedSpeech(List<string> recognized, List<double?>? durations = null)
    {
        if (closing || microphoneMuted) return;
        var accepted = new List<HeardSpeech>();
        for (int i = 0; i < recognized.Count; i++) {
            string text = recognized[i]; AddLine("You", text);
            string id = taskTrace.BeginRequest(text, "speech");
            if (durations != null && i < durations.Count && durations[i].HasValue) taskTrace.Timing("speech-recognition", durations[i]!.Value, id);
            if (TryBookmark(text, id)) continue;
            var address = CompanionAddress.Parse(text, preferences.Profiles);
            if (address.NameOnly && address.Companion == bridge.CompanionId) {
                if (pendingAddress != null) taskTrace.Record("address-replaced", new { by = id }, pendingAddress.Id);
                pendingAddress = new HeardSpeech(text, id, DateTime.UtcNow);
                taskTrace.Record("address-wait", new { companion = bridge.CompanionId, graceSeconds = 3 }, id);
                if (ReplyPlaying) CancelTurn();
                continue;
            }
            if (pendingAddress != null) { taskTrace.Record("address-joined", new { addressRequest = pendingAddress.Id }, id); pendingAddress = null; }
            accepted.Add(new HeardSpeech(text, id, DateTime.UtcNow));
        }
        int recall = accepted.FindLastIndex(s => IsImmediateRecall(s.Text));
        if (recall >= 0) {
            foreach (var old in heardSpeech.Concat(accepted.Take(recall))) taskTrace.Record("request-cancelled", new { reason = "Replaced by immediate recall before processing" }, old.Id);
            heardSpeech.Clear(); gameReplies.Clear();
            foreach (var speech in accepted.Skip(recall + 1)) QueueSpeech(speech);
            _ = Submit(accepted[recall].Text, true, accepted[recall].Id); return;
        }
        foreach (var speech in accepted) QueueSpeech(speech);
        if (accepted.Count > 0 && ReplyPlaying) CancelTurn();
        DrainHeardSpeech();
    }
    private void DrainHeardSpeech()
    {
        if (!closing && busyGeneration < 0 && heardSpeech.Count == 0 && pendingAddress != null && !microphoneHasSpeech && DateTime.UtcNow - pendingAddress.Received >= TimeSpan.FromSeconds(3)) {
            var address = pendingAddress; pendingAddress = null; _ = AcknowledgeAddress(address.Id);
        }
        if (closing || heardSpeech.Count == 0 || busyGeneration >= 0) return;
        while (heardSpeech.Count > 0 && DateTime.UtcNow - heardSpeech.Peek().Received > TimeSpan.FromSeconds(30)) {
            var old = heardSpeech.Dequeue();
            taskTrace.Record("request-cancelled", new { reason = "Queued speech expired after 30 seconds; no order sent" }, old.Id);
            AddLine("Voice", "That queued request became too old to act on safely: “" + old.Text + "”. Please repeat it if still needed.");
        }
        if (heardSpeech.Count == 0) return;
        var speech = heardSpeech.Dequeue(); taskTrace.Timing("speech-queue-wait", (DateTime.UtcNow - speech.Received).TotalMilliseconds, speech.Id);
        _ = Submit(speech.Text, true, speech.Id);
    }
    private async void StopCapture(bool discard)
    {
        if (discard) pendingAddress = null;
        discardCapture |= discard;
        if (!recognizing && microphonePending == 0) return;
        if (discard) collecting = false;
        microphonePending++;
        await microphoneGate.WaitAsync();
        try {
            using var response = await microphoneHttp.PostAsync(discard ? "/stop" : "/finish", LocalJson(new { }));
            if (discard) recognizing = collecting = false; else await CheckMicrophoneResponse(response);
        } catch (Exception e) { recognizing = collecting = false; if (!discard) MicrophoneFailed(e.Message); }
        finally { microphonePending--; microphoneGate.Release(); }
    }
    private void EndListening() { if (!AlwaysOn) { listeningLabel.Text = "Finishing speech…"; StopCapture(false); } }
    private void MicrophoneFailed(string message)
    {
        recognizing = collecting = false;
        if (closing) return;
        taskTrace?.IncidentFor("microphone-error", bridge.CompanionId, "speech-input", message);
        microphoneMode.SelectedIndex = 0; microphoneLevel.Value = 0;
        listeningLabel.Text = "MIC ERROR · " + message;
        AddLine("Microphone", message + " Open Rune using Open Rune.exe to start local audio, then check Mic setup. Typing still works.");
    }
    private async Task AcknowledgeAddress(string requestId)
    {
        using var scope = taskTrace.Scope(requestId);
        string answer = preferences.Language == "de" ? "Ja, ich höre zu." : preferences.Language == "nl" ? "Ja, ik luister." : "Aye, I'm listening.";
        AddLine(DisplayName, answer); taskTrace.Record("reply-text", new { companion = bridge.CompanionId, answer });
        if (voice.Checked) await Speak(answer, turn.Token);
    }
    private static StringContent LocalJson(object value) => new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");
    private static async Task CheckMicrophoneResponse(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        string detail = await response.Content.ReadAsStringAsync();
        try { using var doc = JsonDocument.Parse(detail); detail = doc.RootElement.GetProperty("error").GetString() ?? detail; } catch { }
        throw new InvalidOperationException(detail);
    }
    private async Task ShowMicrophoneSetup()
    {
        int previousMode = microphoneMode.SelectedIndex; microphoneMode.SelectedIndex = 0;
        using var dialog = new RunePopupForm { Text = "Audio setup · headset input and output", ClientSize = new Size(610, 365), StartPosition = FormStartPosition.CenterParent, Font = Font, BackColor = BackColor };
        var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(22, 65), Width = 565 };
        var devices = new List<int> { -1 }; choice.Items.Add("Windows default microphone"); choice.SelectedIndex = 0;
        try { using var doc = await microphoneHttp.GetFromJsonAsync<JsonDocument>("/devices");
            foreach (var d in doc!.RootElement.GetProperty("devices").EnumerateArray()) { devices.Add(d.GetProperty("id").GetInt32()); choice.Items.Add(d.GetProperty("name").GetString() ?? "Microphone"); }
            int index = devices.IndexOf(microphoneDevice); choice.SelectedIndex = Math.Max(0, index);
        } catch (Exception e) { AddLine("Microphone", "Could not list inputs: " + e.Message); }
        dialog.Controls.Add(new Label { Text = "Input · your microphone (speech recognition):", Location = new Point(22, 23), Size = new Size(565, 31) }); dialog.Controls.Add(choice);
        var output = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(22, 146), Width = 565 };
        string[] outputs = OutputAudio.Devices(); output.Items.Add("Windows default output"); output.Items.AddRange(outputs.Select(n => n.Split(" (")[0]).ToArray());
        output.SelectedIndex = Math.Max(0, Array.IndexOf(outputs, neural.OutputDeviceName) + 1);
        dialog.Controls.Add(new Label { Text = "Output · where companion voices play:", Location = new Point(22, 114), Size = new Size(565, 28) }); dialog.Controls.Add(output);
        dialog.Controls.Add(new Label { Text = "Always on keeps your chosen input listening during replies.\nUse your headset output to avoid the microphone hearing Rune.\nYour next recognized words interrupt playback and appear in Conversation.\nAudio stays local and is not saved.", Location = new Point(22, 192), Size = new Size(565, 92) });
        var save = new Button { Text = "Save audio devices", Location = new Point(390, 305), Size = new Size(197, 32) };
        save.Click += (_, _) => { microphoneDevice = devices[choice.SelectedIndex]; neural.OutputDeviceName = output.SelectedIndex <= 0 ? "" : outputs[output.SelectedIndex - 1]; SavePreferences(); dialog.Close(); }; dialog.Controls.Add(save); dialog.ShowDialog(this); microphoneMode.SelectedIndex = previousMode;
    }
}
