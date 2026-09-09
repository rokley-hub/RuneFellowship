using System.Net.Http.Json;
using System.Text.Json;

namespace Rune.Voice;

public sealed partial class RuneWindow
{
    internal static void TestVoiceMaintenance(string settings, string output)
    {
        ApplicationConfiguration.Initialize();
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "maintenance-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var original = ProfileStore.Load(settings); var prefs = ProfileStore.Load(settings);
        prefs.AiMode = "local"; prefs.ChatGptEnabled = false; prefs.MicrophoneMode = 0; prefs.VoiceReplies = true;
        prefs.KeepVoiceReady = true; prefs.PerformanceMode = "automatic"; prefs.Language = "en"; prefs.SpeechPauseMs = 600;
        prefs.Profiles = new[] { new CompanionProfile { VoiceEngine = "chatterbox-turbo", Voice = "am_onyx" }, new CompanionProfile { Id = "eira", Name = "Eira", RecognitionName = "Eira", VoiceEngine = "chatterbox-turbo", Voice = "af_heart" } };
        File.WriteAllText(Path.Combine(folder, "preferences.json"), JsonSerializer.Serialize(prefs, Brain.Json));
        using var window = new RuneWindow(folder) { Opacity = 0, ShowInTaskbar = false };
        var report = new Dictionary<string, object>();
        void Save(string stage) { report["stage"] = stage; File.WriteAllText(output, JsonSerializer.Serialize(report, Brain.Json)); }
        window.Shown += async (_, _) => {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            try {
                window.timer.Stop(); window.performanceTestEnabled = true;
                async Task Ready(string identity) {
                    var until = DateTime.UtcNow.AddSeconds(80);
                    while (DateTime.UtcNow < until) {
                        window.TickPerformance();
                        if (!window.preparingProfile && window.warmIdentity == identity) return;
                        await Task.Delay(250);
                    }
                    throw new Exception("Runtime maintenance failed: " + window.ShellVoiceReadiness);
                }
                Save("Starting real desktop voice maintenance");
                await Ready("chatterbox-turbo|am_onyx|en"); report["startupPreparedReference"] = true;
                using (var health = await http.GetFromJsonAsync<JsonDocument>((LocalServices.Audio + "/health"))) {
                    if (health!.RootElement.GetProperty("speechPauseMs").GetInt32() != 600) throw new Exception("Actual speech-pause request did not apply");
                }
                report["pauseAppliedThroughDesktop"] = true;
                Save("Checking retention for 130 idle seconds");
                DateTime until = DateTime.UtcNow.AddSeconds(130);
                while (DateTime.UtcNow < until) { window.TickPerformance(); await Task.Delay(500); }
                using (var health = await http.GetFromJsonAsync<JsonDocument>((LocalServices.Expressive + "/health"))) {
                    if (health!.RootElement.GetProperty("loaded").GetString() != "chatterbox-turbo") throw new Exception("Voice unloaded during active desktop lease");
                }
                report["retainedBeyondTwoMinutes"] = true;
                Save("Switching companion using the normal selector");
                window.SelectCompanion("eira"); await Ready("chatterbox-turbo|af_heart|en"); report["companionSwitchPreparedReference"] = true;
                window.performanceTestEnabled = false;
                int commandGeneration = window.generation; int micMode = window.microphoneMode.SelectedIndex;
                window.ShellSetVoiceReplies(false); await window.speechDisableTask;
                if (window.generation != commandGeneration || window.microphoneMode.SelectedIndex != micMode) throw new Exception("Voice-off changed command or input state");
                using (var health = await http.GetFromJsonAsync<JsonDocument>((LocalServices.Expressive + "/health"))) {
                    if (health!.RootElement.GetProperty("loaded").GetString() != "") throw new Exception("Released model remained loaded");
                }
                report["voiceOffUnloadedModelWithoutChangingInputOrCommands"] = true; report["passed"] = true;
            } catch (Exception error) { report["passed"] = false; report["error"] = error.ToString(); Environment.ExitCode = 1; }
            finally {
                window.performanceTestEnabled = false; await window.ReleaseVoiceLease();
                try { using var restored = await PostLocal(http, (LocalServices.Audio + "/performance"), new { speechPauseMs = original.SpeechPauseMs }, CancellationToken.None); } catch { }
                Save("Finished"); window.Close();
            }
        };
        Application.Run(window);
    }
}
