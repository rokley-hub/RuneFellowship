using System.Net.Http.Json;
using System.Text.Json;

namespace Rune.Voice;

public sealed partial class RuneWindow
{
    private readonly PerformanceMonitor performance = new();
    private readonly HttpClient performanceHttp = new() { Timeout = TimeSpan.FromSeconds(70) };
    private readonly string voiceLease = Guid.NewGuid().ToString("N");
    private DateTime nextPerformance;
    private bool performanceBusy, preparingProfile;
    private string previousBrainMode = "", warmIdentity = "";
    private string lastPerformanceError = "";
    internal bool performanceTestEnabled;
    private bool PerformanceEnabled => !Environment.GetCommandLineArgs().Any(a => a.StartsWith("--") && (a.Contains("check") || a.Contains("-test") || a.Contains("preview") || a.Contains("benchmark")));
    internal string ShellVoiceReadiness { get; private set; } = "Not prepared";
    internal string ShellPerformance => performance.Summary + "\n" + taskTrace.LatestTimings();
    internal string ShellPerformanceMode => preferences.PerformanceMode;
    internal bool ShellKeepVoiceReady => preferences.KeepVoiceReady;
    internal int ShellSpeechPause => preferences.SpeechPauseMs;
    internal void ShellSetPerformance(string mode, bool keepReady, int pause)
    {
        if (mode is not ("automatic" or "expressive" or "lightweight")) throw new ArgumentOutOfRangeException(nameof(mode));
        CancelTurn(); preferences.PerformanceMode = mode; preferences.KeepVoiceReady = keepReady;
        preferences.SpeechPauseMs = Math.Clamp(pause, 600, 1500);
        neural.PerformanceMode = mode; SavePreferences(); warmIdentity = ""; nextPerformance = DateTime.MinValue;
        taskTrace.Record("performance-settings", new { mode, keepReady, pause = preferences.SpeechPauseMs });
        TickPerformance();
    }
    private void TickPerformance()
    {
        if ((!PerformanceEnabled && !performanceTestEnabled) || closing || performanceBusy || DateTime.UtcNow < nextPerformance) return;
        nextPerformance = DateTime.UtcNow.AddSeconds(10); performanceBusy = true;
        _ = RefreshPerformance();
    }
    private async Task RefreshPerformance()
    {
        try {
            var token = voiceLifetime.Token;
            await Task.Run(() => performance.Sample(taskTrace, token), token);
            // This is Rune's dedicated Ollama endpoint. Never stop another app's processes.
            if (previousBrainMode != preferences.AiMode) {
                if (preferences.AiMode == "chatgpt") {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(3000);
                    try {
                        using var result = await PostLocal(performanceHttp, (LocalServices.Brain + "/api/generate"), new { model = preferences.LocalModel, keep_alive = 0 }, deadline.Token);
                        taskTrace.Record("unused-qwen-release", new { released = result.IsSuccessStatusCode });
                    } catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { }
                }
                previousBrainMode = preferences.AiMode;
            }
            var profile = CurrentProfile.Copy();
            string engine = VoiceCatalog.NormalizeEngine(profile.VoiceEngine);
            bool lightweight = preferences.PerformanceMode == "lightweight" && preferences.Language == "en";
            bool retain = voice.Checked && preferences.KeepVoiceReady && !lightweight && engine != "kokoro";
            using (var result = await PostLocal(performanceHttp, (LocalServices.Expressive + "/lease"), new { owner = voiceLease, retain }, token)) { result.EnsureSuccessStatusCode(); }
            if (!voice.Checked && retain) { await ReleaseVoiceLease(); retain = false; }
            using (var result = await PostLocal(performanceHttp, (LocalServices.Audio + "/performance"), new { speechPauseMs = preferences.SpeechPauseMs }, token)) { result.EnsureSuccessStatusCode(); }
            if (lastPerformanceError.Length > 0) { taskTrace.Record("voice-maintenance-recovered", new { }); lastPerformanceError = ""; }
            using var health = await performanceHttp.GetFromJsonAsync<JsonDocument>((LocalServices.Expressive + "/health"), token);
            var state = health!.RootElement;
            string loaded = state.GetProperty("loaded").GetString() ?? "", phase = state.GetProperty("phase").GetString() ?? "";
            string identity = engine + "|" + profile.Voice + "|" + preferences.Language;
            ShellVoiceReadiness = !voice.Checked ? "Spoken replies off · microphone remains available" : lightweight ? "Fast local voice · CPU" : engine == "kokoro" ? "Kokoro · CPU" : loaded == engine && phase == "idle" ? (warmIdentity == identity ? "Voice ready · " : "Voice model ready · ") + state.GetProperty("device").GetString() : phase == "idle" ? "Not prepared" : phase;
            if (voice.Checked && retain && !preparingProfile && !ReplyPlaying && busyGeneration < 0 && (phase is "idle" or "failed") && (loaded != engine || warmIdentity != identity)) {
                ShellVoiceReadiness = "Preparing companion voice…";
                preparingProfile = true;
                _ = PrepareProfile(engine, profile.Voice, preferences.Language, identity, token);
            }
        } catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException) {
            if (!closing) {
                ShellVoiceReadiness = "Voice preparation unavailable · check Session report";
                if (lastPerformanceError != e.Message) { lastPerformanceError = e.Message; taskTrace.Record("voice-maintenance-error", new { reason = e.Message }); }
            }
        } finally { performanceBusy = false; }
    }
    private async Task PrepareProfile(string engine, string voiceId, string language, string identity, CancellationToken token)
    {
        try {
            bool ready = await neural.WarmProfile(engine, voiceId, language, token);
            if (ready) warmIdentity = identity;
            if (!closing && voice.Checked) ShellVoiceReadiness = ready ? "Voice ready" : "Voice preparation unavailable · retrying";
            nextPerformance = DateTime.UtcNow.AddSeconds(ready ? 0 : 30);
        } finally { preparingProfile = false; }
    }
    private async Task ReleaseVoiceLease()
    {
        // Best effort on close; the lease also expires if the app crashes.
        try {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var result = await PostLocal(http, (LocalServices.Expressive + "/lease"), new { owner = voiceLease, retain = false }, CancellationToken.None);
        } catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { }
    }
    private static async Task<HttpResponseMessage> PostLocal(HttpClient client, string uri, object value, CancellationToken token)
    {
        using var body = LocalJson(value);
        return await client.PostAsync(uri, body, token);
    }
}
