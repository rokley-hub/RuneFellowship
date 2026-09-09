using System.Net;
using System.Text.Json;

namespace Rune.Voice;
public sealed partial class RuneWindow
{
    private sealed class FakeMicrophone : HttpMessageHandler
    {
        public Queue<string> Status = new();
        public int Starts;
        public string LastListen = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath == "/listen") { Starts++; LastListen = request.Content!.ReadAsStringAsync(token).GetAwaiter().GetResult(); }
            string body = request.RequestUri.AbsolutePath == "/status" ? Status.Dequeue() : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
    public static void TestDuplex(string output)
    {
        ApplicationConfiguration.Initialize();
        using var window = new RuneWindow(Path.Combine(Path.GetDirectoryName(output)!, "duplex-test-bridge"));
        var report = new Dictionary<string, object>();
        window.Opacity = 0; window.ShowInTaskbar = false;
        window.Shown += async (_, _) => {
            try {
                window.timer.Stop();
                using var fake = new FakeMicrophone();
                window.microphoneHttp.Dispose();
                typeof(RuneWindow).GetField("microphoneHttp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, new HttpClient(fake) { BaseAddress = new Uri((LocalServices.Audio + "")) });
                window.microphoneMode.SelectedIndex = 2; window.busyGeneration = 999;
                int before = window.generation; window.BeginListening(); await Task.Delay(30);
                if (fake.Starts != 1 || !window.recognizing || before != window.generation) throw new Exception("Always-on capture interrupted thinking or failed to start.");
                fake.Status.Enqueue("{\"state\":\"listening\",\"level\":10,\"hasSpeech\":false,\"events\":[{\"text\":\"Gather sticks.\"},{\"text\":\"I like movies.\"}]}");
                await window.PollMicrophone();
                if (window.heardSpeech.Count != 2 || !window.transcript.Text.Contains("Gather sticks.") || !window.transcript.Text.Contains("I like movies.") || !window.recognizing) throw new Exception("A successive utterance was lost while thinking.");
                report["bothUtterancesVisibleWhileThinking"] = true;
                report["inputStayedListening"] = true;
                window.speakingReply = true; before = window.generation;
                fake.Status.Enqueue("{\"state\":\"listening\",\"level\":0,\"events\":[{\"text\":\"Stop talking.\"}]}");
                await window.PollMicrophone(); window.speakingReply = false;
                if (window.generation <= before || window.heardSpeech.Count != 3) throw new Exception("Speech did not interrupt reply playback.");
                report["speechInterruptsPlaybackAndIsRetained"] = true;
                window.voice.Checked = false; window.busyGeneration = 999; before = window.generation;
                window.AcceptRecognizedSpeech(new List<string> { "Rune, follow me." });
                await Task.Delay(20);
                if (window.generation <= before || window.heardSpeech.Count != 0 || window.busyGeneration >= 0) throw new Exception("Follow did not preempt queued work while thinking.");
                if (window.IsImmediateRecall("Rune, don't follow me.")) throw new Exception("Negated follow interrupted work.");
                report["followPreemptsThinkingAndQueuedRequests"] = true;
                window.speakingReply = true; window.busyGeneration = -1;
                window.AcceptRecognizedSpeech(new List<string> { "gather stone" });
                await Task.Delay(20);
                if (window.heardSpeech.Count != 0 || window.busyGeneration >= 0) throw new Exception("A work order waited for speech playback to finish.");
                window.speakingReply = false;
                report["workDoesNotWaitForSpeechPlayback"] = true;
                window.heardSpeech.Enqueue(new HeardSpeech("gather wood", "expired-fixture", DateTime.UtcNow.AddMinutes(-1)));
                window.DrainHeardSpeech();
                if (window.heardSpeech.Count != 0 || !window.transcript.Text.Contains("too old")) throw new Exception("Expired queued order was not explained and discarded.");
                if (!window.IsImmediateRecall("Rune, defend me") || window.IsImmediateRecall("Rune, don't defend me")) throw new Exception("Urgent defense routing failed.");
                report["expiredOrdersSkippedAndDefensePrioritized"] = true;
                window.busyGeneration = 999; before = window.generation;
                window.AcceptRecognizedSpeech(new List<string> { "report a bug: the axe disappeared", "note an improvement: better archers" });
                if (window.heardSpeech.Count != 0 || window.generation != before || !window.taskTrace.Summary().Contains("better archers")) throw new Exception("Bookmarks interrupted work or were not recorded.");
                report["bookmarksDoNotInterruptWork"] = true;
                window.AcceptRecognizedSpeech(new List<string> { "gather stone" });
                window.ProcessMicrophoneShortcut(true); window.ProcessMicrophoneShortcut(true);
                await Task.Delay(30);
                if (!window.microphoneMuted || window.recognizing || window.heardSpeech.Count != 0 || window.microphoneMode.SelectedIndex != 2 || window.generation != before) throw new Exception("Shortcut mute failed, repeated while held, or cancelled active work.");
                window.AcceptRecognizedSpeech(new List<string> { "do not record me" });
                if (window.transcript.Text.Contains("do not record me")) throw new Exception("Muted speech was accepted.");
                window.ProcessMicrophoneShortcut(false); window.ProcessMicrophoneShortcut(true); window.ProcessMicrophoneShortcut(false);
                if (window.microphoneMuted) throw new Exception("Second shortcut press did not unmute.");
                window.BeginListening(); await Task.Delay(30);
                if (!window.recognizing || fake.Starts != 2) throw new Exception("Capture did not resume after unmute.");
                report["sameShortcutMutesAndUnmutesAlwaysOn"] = true;
                before = window.generation;
                window.ShellSetVoiceReplies(false);
                if (!window.recognizing || window.microphoneMuted || window.generation != before || window.voice.Checked) throw new Exception("Spoken replies off interrupted microphone or command processing.");
                window.busyGeneration = 999;
                window.AcceptRecognizedSpeech(new List<string> { "gather wood" });
                if (!window.transcript.Text.Contains("gather wood")) throw new Exception("Speech recognition unavailable with spoken replies off.");
                window.heardSpeech.Clear();
                report["silentRepliesPreserveSpeechInputAndCommandTurn"] = true;
                string control = Path.Combine(window.bridge.Folder, "voice-control.json"); string controlId = Guid.NewGuid().ToString();
                File.WriteAllText(control, JsonSerializer.Serialize(new { id = controlId, session = window.taskTrace.SessionId, timestamp = Rune.Shared.Rules.Now, action = "toggle-mic" }));
                window.PollVoiceControls(); window.PollVoiceControls();
                if (!window.microphoneMuted) throw new Exception("In-game mute control failed or replayed twice.");
                File.WriteAllText(control, JsonSerializer.Serialize(new { id = Guid.NewGuid().ToString(), session = "old-session", timestamp = Rune.Shared.Rules.Now, action = "toggle-mic" })); window.PollVoiceControls();
                if (!window.microphoneMuted) throw new Exception("Old session control was accepted.");
                report["inGameMuteProtocolAndReplayProtection"] = true;
                window.microphoneMode.SelectedIndex = 1; window.ProcessMicrophoneShortcut(true); await Task.Delay(30);
                if (!window.recognizing || window.microphoneMuted) throw new Exception("Hold-to-talk press no longer starts capture.");
                window.ProcessMicrophoneShortcut(false);
                report["holdToTalkPreserved"] = true;
                string[] devices = OutputAudio.Devices(); report["outputs"] = devices;
                string chosen = devices.FirstOrDefault(n => n.Contains("Sonar", StringComparison.OrdinalIgnoreCase)) ?? "";
                // Silent PCM verifies device routing and release without audible test speech.
                using var data = new MemoryStream(); using (var writer = new BinaryWriter(data, System.Text.Encoding.UTF8, true)) {
                    writer.Write("RIFF"u8); writer.Write(3240 - 4); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(16000); writer.Write(32000); writer.Write((ushort)2); writer.Write((ushort)16); writer.Write("data"u8); writer.Write(3200); writer.Write(new byte[3200]);
                }
                using (var playback = new OutputAudio(data.ToArray(), chosen)) { if (!playback.IsPlaying) throw new Exception("Output did not start."); await Task.Delay(180); }
                report["explicitOutputPlayback"] = chosen.Length > 0 ? chosen : "Windows default";
                window.neural.OutputDeviceName = chosen; window.SavePreferences();
                if (!File.ReadAllText(window.settingsPath).Contains("outputDeviceName")) throw new Exception("Output preference not saved.");
                string modelState = JsonSerializer.Serialize(Brain.ModelState(new Rune.Shared.GameState { world = "synthetic", error = "private diagnostic", roster = new[] { new Rune.Shared.CompanionState { commandId = "debug-command", tools = "Axe usable", workProgress = "debug inventory" } } }), Brain.Json);
                if (modelState.Contains("debug-command") || modelState.Contains("private diagnostic") || modelState.Contains("debug inventory") || !modelState.Contains("Axe usable")) throw new Exception("Diagnostic data leaked into model context or existing tool facts were lost.");
                report["diagnosticsExcludedFromModelPrompts"] = true;
                window.StopCapture(true); await Task.Delay(50); window.preferences.Language = "de"; window.BeginListening(); await Task.Delay(50);
                if (!fake.LastListen.Contains("\"language\":\"de\"")) throw new Exception("Chosen language missing from microphone request.");
                window.SavePreferences(); if (ProfileStore.Load(window.settingsPath).Language != "de") throw new Exception("Chosen language not retained.");
                if (LanguageSettings.Normalize("xx") != "en" || !LanguageSettings.Rule("de").Contains("only in German")) throw new Exception("Language fallback or reply rule failed.");
                report["explicitRecognitionLanguageAndPreference"] = true;
                window.busyGeneration = window.generation; window.WriteVoiceState();
                string activity() { using var state = JsonDocument.Parse(File.ReadAllText(Path.Combine(window.bridge.Folder, "voice-state.json"))); return state.RootElement.GetProperty("activity").GetString()!; }
                if (!activity().Contains("thinking")) throw new Exception("Thinking indicator missing.");
                window.speakingReply = true; window.busyGeneration = -1; window.WriteVoiceState(); if (!activity().Contains("preparing voice")) throw new Exception("Pending voice missing after command processing ended.");
                window.speakingReply = false; window.busyGeneration = -1; window.WriteVoiceState(); if (activity().Length != 0) throw new Exception("Thinking indicator remained after completion.");
                report["thinkingSpeakingAndIdleStatus"] = true;
                var question = new ClarificationContext(window.bridge.CompanionId, window.preferences.Language, "make an axe", "Stone axe or flint axe?", DateTime.UtcNow);
                window.pendingClarification = question;
                if (window.TakeClarification() != question || window.TakeClarification() != null) throw new Exception("Clarification context lost or reused.");
                foreach (var stale in new[] { question with { Companion = "another-companion" }, question with { Language = "other-language" }, question with { At = DateTime.UtcNow.AddMinutes(-3) } }) {
                    window.pendingClarification = stale; if (window.TakeClarification() != null) throw new Exception("Unrelated or expired confirmation context used.");
                }
                report["clarificationScopedToCompanionLanguageAndTime"] = true;
                window.StopCapture(true); window.heardSpeech.Clear(); window.voice.Checked = false; window.busyGeneration = 999;
                window.AcceptRecognizedSpeech(new List<string> { "Hey, Rune." });
                if (window.pendingAddress == null || window.heardSpeech.Count != 0) throw new Exception("Name-only address reached the model queue.");
                window.AcceptRecognizedSpeech(new List<string> { "gather wood" });
                if (window.pendingAddress != null || window.heardSpeech.Count != 1 || window.heardSpeech.Peek().Text != "gather wood") throw new Exception("Split address delayed or altered the following order.");
                window.StopCapture(true); window.heardSpeech.Clear(); window.busyGeneration = -1; window.preferences.Language = "en";
                window.pendingAddress = new HeardSpeech("Hey Rune", "address-fixture", DateTime.UtcNow.AddSeconds(-4)); window.microphoneHasSpeech = false;
                window.DrainHeardSpeech();
                if (window.pendingAddress != null || !window.transcript.Text.Contains("listening") || window.busyGeneration >= 0) throw new Exception("Name-only acknowledgment did not complete locally.");
                report["splitAddressDoesNotQueueAModelGreeting"] = true;
                report["passed"] = true;
            } catch (Exception e) { report["passed"] = false; report["error"] = e.ToString(); }
            File.WriteAllText(output, JsonSerializer.Serialize(report, Brain.Json)); window.Close();
        };
        Application.Run(window);
    }
}
