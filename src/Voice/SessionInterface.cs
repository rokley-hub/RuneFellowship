using System.Text.Json;
using Rune.Shared;

namespace Rune.Voice;

public sealed partial class RuneWindow
{
    private Button? conversationTalk;
    private string lastVoiceControl = "";
    private string controlNotice = "";
    private DateTime controlNoticeUntil;
    private bool microphoneMuted;
    private readonly Button muteMicrophone = new() { Text = "Mute mic", AutoSize = true, FlatStyle = FlatStyle.Flat, Height = 28 };
    private bool TryBookmark(string text, string? request = null)
    {
        if (!TaskTrace.ParseBookmark(text, out string kind, out string note)) return false;
        using var scope = taskTrace.Scope(request ?? "");
        taskTrace.Bookmark(kind, note, bridge.CompanionId);
        string message = (kind == "improvement" ? "Improvement noted" : "Problem marked") + ". Recent session events are recorded locally; your game task continues.";
        AddLine("Session recorder", message); listeningLabel.Text = message; return true;
    }
    private void ToggleMicrophoneMute()
    {
        if (!AlwaysOn) { listeningLabel.Text = "Choose Always on to use the mute toggle. Hold to talk still uses the same shortcut."; return; }
        microphoneMuted = !microphoneMuted;
        if (microphoneMuted) {
            StopCapture(true);
            foreach (var heard in heardSpeech) taskTrace.Record("request-cancelled", new { reason = "Microphone muted before processing" }, heard.Id);
            heardSpeech.Clear(); microphoneHasSpeech = false;
        } else listenAfter = DateTime.UtcNow;
        muteMicrophone.Text = microphoneMuted ? "Unmute mic" : "Mute mic";
        if (conversationTalk != null) conversationTalk.Text = muteMicrophone.Text;
        listeningLabel.Text = microphoneMuted ? "MIC MUTED · Press your talk shortcut or Unmute mic to resume." : "Microphone unmuted · listening resumes on your selected input.";
        taskTrace.Record("microphone-mute", new { muted = microphoneMuted }); statusTicks = 14;
    }
    private void PollVoiceControls()
    {
        try {
            string path = Path.Combine(bridge.Folder, "voice-control.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 2048) return;
            using var json = JsonDocument.Parse(File.ReadAllText(path)); var c = json.RootElement;
            string id = c.GetProperty("id").GetString() ?? "";
            if (!Guid.TryParse(id, out _) || id == lastVoiceControl) return;
            lastVoiceControl = id;
            if (c.GetProperty("session").GetString() != taskTrace.SessionId || Math.Abs(Rules.Now - c.GetProperty("timestamp").GetInt64()) > 5) return;
            switch (c.GetProperty("action").GetString()) {
                case "toggle-mic": ToggleMicrophoneMute(); break;
                case "report-bug": TryBookmark("report a bug"); controlNotice = "Bug marked locally"; controlNoticeUntil = DateTime.UtcNow.AddSeconds(8); break;
                case "note-improvement": TryBookmark("note an improvement"); controlNotice = "Idea marked locally"; controlNoticeUntil = DateTime.UtcNow.AddSeconds(8); break;
            }
        } catch (IOException) { } catch (JsonException) { } catch (KeyNotFoundException) { } catch (InvalidOperationException) { } catch (UnauthorizedAccessException) { }
    }
    private void WriteVoiceState()
    {
        try {
            string path = Path.Combine(bridge.Folder, "voice-state.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new {
                timestamp = Rules.Now, session = taskTrace.SessionId, companion = bridge.CompanionId,
                provider = preferences.AiMode == "chatgpt" ? "Full ChatGPT · " + VoiceCatalog.EngineName(CurrentProfile.VoiceEngine) + " offline voice" : preferences.ChatGptEnabled ? "ChatGPT commands · Qwen conversation · " + VoiceCatalog.EngineName(CurrentProfile.VoiceEngine) + " voice" : "Qwen brain · " + VoiceCatalog.EngineName(CurrentProfile.VoiceEngine) + " voice", state = MicrophoneStatus,
                alwaysOn = AlwaysOn, muted = microphoneMuted, hotkey = preferences.MicShortcut, switchKey = preferences.SwitchCompanionShortcut, companionName = DisplayName, overlayKey = preferences.OverlayShortcut, controlsKey = preferences.ControlsShortcut,
                recorder = taskTrace.Health, controlAck = lastVoiceControl, caption = DateTime.UtcNow < controlNoticeUntil ? controlNotice : "",
                activity = ReplyPlaying ? DisplayName + (neural.IsSpeaking ? recoveryVoice ? " · speaking · fast voice" : " · speaking" : " · preparing voice… " + Math.Max(0, (int)(DateTime.UtcNow - voiceStarted).TotalSeconds) + "s") : busyGeneration == generation ? DisplayName + " · thinking…" : ""
            }));
            File.Move(path + ".tmp", path, true);
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
