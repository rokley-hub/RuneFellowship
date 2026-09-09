using System.Reflection;
using System.Text.Json;

namespace Rune.Voice;
internal static class VoiceRequestChecks
{
    internal static async Task Run(string output)
    {
        var report = new Dictionary<string, object>();
        try {
            report["legacyChunkedRequest"] = "Previously reproduced as HTTP 400; not replayed because an intentionally incomplete request can leave the local server waiting for a body.";
            using var voice = new NeuralVoice { VoiceId = "af_heart" };
            var generate = typeof(NeuralVoice).GetMethod("Generate", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var responses = new List<object>();
            foreach (string text in new[] { "I will defend you.", "I can gather wood from small trees.", "I need a stronger tool for that tree." }) {
                byte[] wav = await (Task<byte[]>)generate.Invoke(voice, new object[] { text, "kokoro", "af_heart", "en", "Warm and practical.", CancellationToken.None })!;
                if (wav.Length < 1000 || System.Text.Encoding.ASCII.GetString(wav, 0, 4) != "RIFF") throw new Exception("Voice did not return a complete WAV.");
                responses.Add(new { text, bytes = wav.Length });
            }
            report["fixedRequests"] = responses; report["passed"] = true;
        } catch (Exception e) { report["passed"] = false; report["error"] = e.ToString(); }
        File.WriteAllText(output, JsonSerializer.Serialize(report, Brain.Json));
    }
}
