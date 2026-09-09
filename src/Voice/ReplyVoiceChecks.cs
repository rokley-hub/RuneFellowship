using System.Text.Json;
using Rune.Shared;

namespace Rune.Voice;

public sealed partial class RuneWindow
{
    private sealed class PendingVoiceRequest : HttpMessageHandler
    {
        public bool Started;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Started = true;
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Pending voice must end by cancellation.");
        }
    }

    internal static void TestReplyVoice(string settings, string output, bool live)
    {
        ApplicationConfiguration.Initialize();
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "reply-voice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var prefs = ProfileStore.Load(settings);
        prefs.MicrophoneMode = 0; prefs.VoiceReplies = true;
        File.WriteAllText(Path.Combine(folder, "preferences.json"), JsonSerializer.Serialize(prefs, Brain.Json));
        string statePath = Path.Combine(folder, "state.json");
        void State(string world) => File.WriteAllText(statePath, JsonSerializer.Serialize(new GameState { timestamp = Rules.Now, world = world, ready = false }, Brain.Json));
        State("voice-fixture");
        using var window = new RuneWindow(folder) { Opacity = 0, ShowInTaskbar = false };
        var report = new Dictionary<string, object>();
        window.Shown += async (_, _) => {
            try {
                window.timer.Stop();
                window.microphoneMode.SelectedIndex = 0;
                using var pending = new PendingVoiceRequest();
                if (!live) {
                    typeof(NeuralVoice).GetField("http", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window.neural, new HttpClient(pending));
                }
                await window.Submit("follow me"); // Offline fixture: no model call or game command.
                if (!window.speakingReply || window.busyGeneration >= 0) throw new Exception("Written reply did not start independent speech.");
                int generation = window.generation;
                using (var locked = new FileStream(statePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                    window.statusTicks = 14; window.Tick();
                    if (window.generation != generation || window.turn.IsCancellationRequested) throw new Exception("Unreadable game state cancelled a voice reply.");
                }
                File.WriteAllText(statePath, "{incomplete");
                window.statusTicks = 14; window.Tick();
                if (window.generation != generation) throw new Exception("Malformed game state cancelled a voice reply.");
                State("voice-fixture");
                window.statusTicks = 14; window.Tick();
                if (window.generation != generation) throw new Exception("Recovered game state cancelled a voice reply.");
                window.WriteVoiceState();
                using (var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "voice-state.json"))))
                    if (!status.RootElement.GetProperty("activity").GetString()!.Contains("voice")) throw new Exception("Pending speech is invisible after command processing ends.");
                report["speechSurvivesUnreadableMalformedAndRecoveredState"] = true;
                report["speechVisibleAfterWrittenReply"] = true;
                if (live) {
                    bool playback = false;
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (window.speakingReply && watch.Elapsed < TimeSpan.FromSeconds(100)) {
                        playback |= window.neural.IsSpeaking;
                        await Task.Delay(20);
                    }
                    if (window.speakingReply || !playback || window.ShellTranscript.Contains("Voice unavailable:")) throw new Exception("Normal reply never completed real output playback. " + window.ShellTranscript);
                    report["normalReplyRealPlaybackCompleted"] = true;
                    report["engine"] = window.CurrentProfile.VoiceEngine;
                    report["playedEngine"] = window.recoveryVoice ? "kokoro" : window.CurrentProfile.VoiceEngine;
                    report["usedRecoveryVoice"] = window.recoveryVoice;
                    report["output"] = window.neural.OutputDeviceName;
                    report["seconds"] = watch.Elapsed.TotalSeconds;
                } else {
                    if (!pending.Started) throw new Exception("Normal speech did not request audio.");
                    State("different-world"); window.statusTicks = 14; window.Tick();
                    if (window.generation == generation) throw new Exception("Confirmed world change failed to cancel speech.");
                    await Task.Delay(100);
                    if (window.speakingReply) throw new Exception("Cancelled voice remained active.");
                    report["confirmedWorldChangeCancelsSpeech"] = true;
                }
                if (Directory.EnumerateFiles(folder, "command*.json").Any()) throw new Exception("Offline test dispatched a game command.");
                report["passed"] = true;
            } catch (Exception e) { report["passed"] = false; report["error"] = e.ToString(); Environment.ExitCode = 1; }
            finally { window.CancelTurn(); File.WriteAllText(output, JsonSerializer.Serialize(report, Brain.Json)); window.Close(); }
        };
        Application.Run(window);
    }
}
