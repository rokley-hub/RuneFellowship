using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace Rune.Voice;

public sealed class NeuralVoice : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(5) };
    public string Engine = "kokoro";
    internal string PerformanceMode = "automatic";
    private readonly Dictionary<string, byte[]> replyCache = new();
    private long replyCacheBytes;
    public string VoiceId = "bm_george";
    public string Language = "en";
    public string Personality = "";
    public string OutputDeviceName = "";
    internal int Volume = 100;
    internal TaskTrace? Trace;
    internal Action? RecoveryVoice;
    private OutputAudio? player;
    private readonly SemaphoreSlim modelGate = new(1, 1);
    private CancellationTokenSource? warming;
    internal void CancelWarm() { warming?.Cancel(); }
    private async Task<HttpResponseMessage> VoiceRequest(string path, object value, CancellationToken token)
    {
        await modelGate.WaitAsync(token);
        string id = Guid.NewGuid().ToString("N");
        try {
            var json = System.Text.Json.JsonSerializer.SerializeToNode(value)!; json["requestId"] = id;
            using var body = new StringContent(json.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
            return await http.PostAsync((LocalServices.Audio + "/") + path, body, token);
        } catch (OperationCanceledException) {
            // HTTP cancellation alone leaves CUDA running. Wait for the worker
            // to release this exact job before the next speech request starts.
            using var cancelHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            try {
                using var body = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { requestId = id }), System.Text.Encoding.UTF8, "application/json");
                using var result = await cancelHttp.PostAsync((LocalServices.Expressive + "/cancel"), body);
            } catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { }
            throw;
        } finally { modelGate.Release(); }
    }
    internal async Task ReleaseSpeechModel()
    {
        CancelWarm();
        await modelGate.WaitAsync();
        try {
            using var timeout = new CancellationTokenSource(8000);
            foreach (string uri in new[] { (LocalServices.Expressive + "/unload"), (LocalServices.Audio + "/voice/unload") }) {
                using var body = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"); using var result = await http.PostAsync(uri, body, timeout.Token);
            }
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { }
        finally { modelGate.Release(); }
    }
    private readonly Dictionary<string, byte[]> previewCache = new();
    public bool IsSpeaking => player?.IsPlaying == true;
    public void Stop() { player?.Dispose(); player = null; }
    internal async Task Warm(string engine, CancellationToken token)
    {
        if (VoiceCatalog.NormalizeEngine(engine) == "kokoro") return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(65));
        try {
            using var body = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { engine }), System.Text.Encoding.UTF8, "application/json");
            using var response = await http.PostAsync((LocalServices.Expressive + "/warm"), body, deadline.Token);
            Trace?.Record("voice-model-preload", new { engine, ready = response.IsSuccessStatusCode });
        } catch (Exception e) when (e is OperationCanceledException or HttpRequestException) {
            if (!token.IsCancellationRequested) Trace?.Record("voice-model-preload", new { engine, ready = false });
        }
    }
    internal async Task<bool> WarmProfile(string engine, string voice, string language, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(65));
        warming = deadline;
        try {
            using var response = await VoiceRequest("warm", new { engine, voice, language }, deadline.Token);
            string reason = response.IsSuccessStatusCode ? "" : (await response.Content.ReadAsStringAsync(deadline.Token));
            Trace?.Record("voice-profile-preload", new { engine, voice, ready = response.IsSuccessStatusCode, reason = reason[..Math.Min(512, reason.Length)] });
            return response.IsSuccessStatusCode;
        } catch (Exception e) when (e is OperationCanceledException or HttpRequestException) {
            if (!token.IsCancellationRequested) Trace?.Record("voice-profile-preload", new { engine, voice, ready = false, reason = e is OperationCanceledException ? "Preparation timed out" : e.Message[..Math.Min(512, e.Message.Length)] });
            return false;
        } finally { if (warming == deadline) warming = null; }
    }
    private async Task<byte[]> Generate(string text, string engine, string voice, string language, string personality, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string key = System.Text.Json.JsonSerializer.Serialize(new { text, engine, voice, language, personality, Delivery, PerformanceMode });
        if (replyCache.TryGetValue(key, out var cached)) { Trace?.Record("voice-cache-hit", new { engine, voice }); return cached; }
        bool recovered = false;
        var bytes = await GenerateRequest(text, engine, voice, language, personality, token, PerformanceMode == "automatic", () => recovered = true, PerformanceMode == "lightweight" && language == "en");
        token.ThrowIfCancellationRequested();
        if (!recovered && text.Length <= 160 && bytes.Length <= 512 * 1024) {
            while (replyCache.Count > 0 && (replyCache.Count >= 24 || replyCacheBytes + bytes.Length > 4 * 1024 * 1024)) {
                string oldest = replyCache.Keys.First(); replyCacheBytes -= replyCache[oldest].Length; replyCache.Remove(oldest);
            }
            replyCache[key] = bytes; replyCacheBytes += bytes.Length;
        }
        return bytes;
    }
    private async Task<byte[]> GenerateRequest(string text, string engine, string voice, string language, string personality, CancellationToken token, bool allowFallback, Action? recovered = null, bool fastVoice = false)
    {
        CancelWarm();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var expression = VoiceExpression.From(personality, text, Delivery);
        // The local HTTP server reads Content-Length, not chunked transfer encoding.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(allowFallback ? 35 : 75));
        using var response = await VoiceRequest("tts", new { text, engine, voice, language, personality, delivery = expression.Delivery, intensity = expression.Intensity, cue = expression.Cue, allowFallback, fastVoice }, deadline.Token);
        if (!response.IsSuccessStatusCode) {
            string detail = await response.Content.ReadAsStringAsync(token);
            try { using var error = System.Text.Json.JsonDocument.Parse(detail); detail = error.RootElement.GetProperty("error").GetString() ?? detail; } catch (System.Text.Json.JsonException) { }
            throw new InvalidOperationException("Local voice service: " + detail);
        }
        var bytes = await response.Content.ReadAsByteArrayAsync(token);
        if (response.Headers.TryGetValues("X-Rune-Voice-Engine", out var engines)) Trace?.Record("voice-route", new { requestedEngine = engine, playedEngine = engines.FirstOrDefault(), mode = PerformanceMode });
        if (response.Headers.Contains("X-Rune-Voice-Fallback")) {
            string reason = response.Headers.TryGetValues("X-Rune-Voice-Fallback-Reason", out var reasons) ? Uri.UnescapeDataString(reasons.First()).Substring(0, Math.Min(512, Uri.UnescapeDataString(reasons.First()).Length)) : "Voice service did not provide a reason";
            Trace?.Record("voice-recovery", new { requestedEngine = engine, playedEngine = "kokoro", voice, reason });
            recovered?.Invoke(); RecoveryVoice?.Invoke();
        }
        Trace?.Timing("voice-synthesis", watch.Elapsed.TotalMilliseconds);
        return bytes;
    }
    public async Task Speak(string text, CancellationToken token, Action<string>? progress = null)
    {
        // Start the first sentence promptly; prepare the next while audio plays.
        var chunks = Regex.Split(text.Trim(), @"(?<=[.!?])\s+").Where(s => s.Length > 0).ToArray();
        if (chunks.Length == 0) return;
        string engine = VoiceCatalog.NormalizeEngine(Engine), voice = VoiceId, language = Language, personality = Personality, output = OutputDeviceName;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var work = CancellationTokenSource.CreateLinkedTokenSource(token);
        progress?.Invoke(engine == "kokoro" ? "Preparing voice…" : "Preparing " + VoiceCatalog.EngineName(engine) + "… First use can take about 30 seconds.");
        Task<byte[]>? next = Generate(chunks[0], engine, voice, language, personality, work.Token);
        try {
            for (int i = 0; i < chunks.Length; i++) {
                var bytes = await next!; token.ThrowIfCancellationRequested(); Stop();
                player = new OutputAudio(bytes, output, Volume);
                progress?.Invoke("Playing through " + (string.IsNullOrWhiteSpace(output) ? "your default output" : output) + "…");
                if (i == 0) Trace?.Timing("voice-first-playback", watch.Elapsed.TotalMilliseconds);
                next = i + 1 < chunks.Length ? Generate(chunks[i + 1], engine, voice, language, personality, work.Token) : null;
                while (IsSpeaking) await Task.Delay(35, token);
            }
        } finally {
            work.Cancel(); Stop();
            if (next != null) { try { await next; } catch (OperationCanceledException) { } catch (HttpRequestException) { } }
        }
    }
    internal string Delivery = "";
    public void Dispose() { Stop(); previewCache.Clear(); replyCache.Clear(); http.Dispose(); }

    internal async Task SpeakPreview(string text, CancellationToken token, Action<string>? progress)
    {
        string engine = VoiceCatalog.NormalizeEngine(Engine), voice = VoiceId, language = Language, personality = Personality, output = OutputDeviceName;
        string key = System.Text.Json.JsonSerializer.Serialize(new { engine, voice, language, personality, text });
        token.ThrowIfCancellationRequested();
        bool cached = previewCache.TryGetValue(key, out var bytes);
        progress?.Invoke(cached ? "Replaying the saved voice sample…" : "Preparing " + VoiceCatalog.EngineName(engine) + "… First use may take longer while the voice loads.");
        if (!cached) {
            bytes = await GenerateRequest(text, engine, voice, language, personality, token, false);
            token.ThrowIfCancellationRequested();
            // Preview-only cache: bounded to eight short samples, never dialogue.
            if (bytes.Length <= 1024 * 1024) {
                if (previewCache.Count >= 8) previewCache.Remove(previewCache.Keys.First());
                previewCache[key] = bytes;
            }
        }
        token.ThrowIfCancellationRequested();
        try {
            Stop(); player = new OutputAudio(bytes!, output, Volume);
            Trace?.Record("voice-preview-playback", new { engine, voice, cached });
            progress?.Invoke("Playing through " + (string.IsNullOrWhiteSpace(output) ? "your default output" : output) + "…");
            while (IsSpeaking) await Task.Delay(35, token);
        } finally { Stop(); }
    }
}
