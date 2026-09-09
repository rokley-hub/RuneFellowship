using System.Text.Json;
using Rune.Shared;
using System.Net;
using System.Reflection;

namespace Rune.Voice;

internal static class PerformanceChecks
{
    internal static void Run(string output)
    {
        var checks = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        try {
            foreach (string text in new[] { "pick up stone", "gather twelve wood", "collect 10 flint" }) Check(FastOrder.CanBypass(text, Rules.Parse(text), false), "Direct material order: " + text);
            foreach (string text in new[] { "don't gather stone", "if I gather wood", "can you tell me how to gather wood", "gather wood and build a house", "collect courage", "pick up the best axe" }) Check(!FastOrder.CanBypass(text, Rules.Parse(text), false), "Planner retains: " + text);
            Check(!FastOrder.CanBypass("gather wood", Rules.Parse("gather wood"), true), "Pending clarification cannot be bypassed");
            string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "performance-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string prefs = Path.Combine(folder, "preferences.json");
            File.WriteAllText(prefs, "{\"aiMode\":\"chatgpt\",\"notes\":\"keep my notes\"}");
            var settings = ProfileStore.Load(prefs);
            Check(settings.PerformanceMode == "automatic" && settings.KeepVoiceReady && settings.SpeechPauseMs == 900, "Old preferences gain safe defaults");
            settings.PerformanceMode = "expressive"; settings.KeepVoiceReady = false; settings.SpeechPauseMs = 600;
            File.WriteAllText(prefs, JsonSerializer.Serialize(settings, Brain.Json)); settings = ProfileStore.Load(prefs);
            Check(settings.PerformanceMode == "expressive" && !settings.KeepVoiceReady && settings.SpeechPauseMs == 600 && settings.Notes == "keep my notes", "Performance settings persist without losing profile fields");
            using (var trace = new TaskTrace(folder)) {
                trace.Timing("speech-recognition", 420); Check(trace.LatestTimings().Contains("0.42s"), "Latest timing visible");
                var hardware = new PerformanceMonitor();
                hardware.Sample(trace, CancellationToken.None).GetAwaiter().GetResult();
                hardware.Sample(trace, CancellationToken.None).GetAwaiter().GetResult();
                Check(hardware.Summary.StartsWith("CPU ") && hardware.Summary.Contains("GPU"), "Real hardware sampling returns bounded measurements or explicit unavailability");
                File.WriteAllText(Path.Combine(folder, "hardware.txt"), hardware.Summary);
            }
            Check(CacheRouting().GetAwaiter().GetResult(), "Speech cache separates voices, personality, delivery and modes; recovery is never cached; cancellation wins");
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, checks }, Brain.Json));
        } catch (Exception error) { Environment.ExitCode = 1; File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, error = error.ToString(), checks }, Brain.Json)); }
    }
    private sealed class VoiceHandler : HttpMessageHandler
    {
        internal int Calls;
        internal bool Recovery;
        internal string Body = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++; Body = await request.Content!.ReadAsStringAsync(token);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[128]) };
            if (Recovery) response.Headers.Add("X-Rune-Voice-Fallback", "kokoro");
            return response;
        }
    }
    private static async Task<bool> CacheRouting()
    {
        using var voice = new NeuralVoice(); var handler = new VoiceHandler();
        typeof(NeuralVoice).GetField("http", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(voice, new HttpClient(handler));
        var generate = typeof(NeuralVoice).GetMethod("Generate", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Task<byte[]> Request(string speaker = "am_onyx", string personality = "warm", CancellationToken token = default) => (Task<byte[]>)generate.Invoke(voice, new object[] { "Keep your shield close.", "chatterbox-turbo", speaker, "en", personality, token })!;
        await Request(); await Request(); if (handler.Calls != 1) return false;
        await Request("af_heart"); await Request(personality: "grumpy"); voice.Delivery = "urgent"; await Request(); if (handler.Calls != 4) return false;
        voice.PerformanceMode = "expressive"; await Request();
        using (var body = JsonDocument.Parse(handler.Body)) if (body.RootElement.GetProperty("allowFallback").GetBoolean()) return false;
        voice.PerformanceMode = "lightweight"; await Request();
        using (var body = JsonDocument.Parse(handler.Body)) if (!body.RootElement.GetProperty("fastVoice").GetBoolean()) return false;
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await Request(token: canceled.Token); return false; } catch (OperationCanceledException) { }
        handler.Recovery = true; voice.PerformanceMode = "automatic";
        int before = handler.Calls; await Request(personality: "new"); await Request(personality: "new");
        return handler.Calls == before + 2;
    }
}
