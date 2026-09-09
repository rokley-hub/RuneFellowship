using System.Text.Json;

namespace Rune.Voice;
public sealed partial class RuneWindow
{
    internal static void TestShellVoicePreview(string settings, string output)
    {
        ApplicationConfiguration.Initialize();
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "audition-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var prefs = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settings))!;
        prefs["microphoneMode"] = 0;
        File.WriteAllText(Path.Combine(folder, "preferences.json"), prefs.ToJsonString());
        using var window = new RuneWindow(folder) { Opacity = 0, ShowInTaskbar = false };
        var report = new Dictionary<string, object>();
        window.Shown += async (_, _) => {
            try {
                window.microphoneMode.SelectedIndex = 0;
                // A voice audition must work even with no brain/helper available.
                window.chatGpt.ExecutableOverride = Path.Combine(folder, "missing-login-helper.exe");
                window.brain.LocalModel = "nonexistent-preview-model";
                var draft = window.CurrentProfile.Copy();
                string before = File.ReadAllText(window.settingsPath);
                bool guarded = false, cancelled = false;
                try {
                    await window.ShellPreviewVoice(draft, CancellationToken.None, _ => {
                        guarded = window.ReplyPlaying;
                        window.CancelTurn();
                    });
                } catch (OperationCanceledException) { cancelled = true; }
                if (!guarded || !cancelled || window.ReplyPlaying) throw new Exception("Preview did not share the normal interruption lifecycle.");
                report["normalInterruptCancelsPreview"] = true;
                var stages = new List<string>(); bool playbackStarted = false; double firstPlaybackMs = 0;
                using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(4));
                var watch = System.Diagnostics.Stopwatch.StartNew();
                string sample = await window.ShellPreviewVoice(draft, limit.Token, stage => {
                    stages.Add(stage);
                    if (stage.StartsWith("Playing")) { playbackStarted |= window.neural.IsSpeaking; firstPlaybackMs = watch.Elapsed.TotalMilliseconds; }
                    File.WriteAllText(output + ".progress", stage);
                });
                report["engine"] = draft.VoiceEngine;
                report["output"] = window.neural.OutputDeviceName;
                report["sample"] = sample;
                report["stages"] = stages;
                report["seconds"] = watch.Elapsed.TotalSeconds;
                report["firstPlaybackMilliseconds"] = firstPlaybackMs;
                report["fixedEmotionalLine"] = sample == VoiceAudition.Line(window.preferences.Language) && VoiceExpression.From(VoiceAudition.Delivery, sample).Cue == "chuckle";
                bool cacheUsed = false; double replayPlaybackMs = 0;
                watch.Restart();
                await window.ShellPreviewVoice(draft, limit.Token, stage => {
                    if (stage.StartsWith("Replaying")) cacheUsed = true;
                    if (stage.StartsWith("Playing")) replayPlaybackMs = watch.Elapsed.TotalMilliseconds;
                });
                report["repeatUsesCachedSample"] = cacheUsed;
                report["repeatPlaybackMilliseconds"] = replayPlaybackMs;
                report["noBrainHelperStarted"] = !Directory.Exists(Path.Combine(folder, "chatgpt-account"));
                report["playbackStarted"] = playbackStarted;
                report["previewStateCleared"] = !window.ReplyPlaying;
                report["sampleInConversation"] = window.ShellTranscript.Contains(sample);
                report["settingsUnchanged"] = File.ReadAllText(window.settingsPath) == before;
                report["noGameCommandsOrMemory"] = !Directory.Exists(Path.Combine(folder, "memories")) && !Directory.EnumerateFiles(folder, "command*.json", SearchOption.AllDirectories).Any();
                report["passed"] = playbackStarted && cacheUsed && (bool)report["fixedEmotionalLine"] && (bool)report["noBrainHelperStarted"] && !window.ReplyPlaying && (bool)report["settingsUnchanged"] && (bool)report["noGameCommandsOrMemory"];
            } catch (Exception ex) { report["passed"] = false; report["error"] = ex.ToString(); }
            finally { File.WriteAllText(output, JsonSerializer.Serialize(report, Brain.Json)); window.Close(); }
        };
        System.Windows.Forms.Application.Run(window);
    }
}
