using System.Text.Json;
namespace Rune.Voice;
public sealed partial class RuneWindow
{
    public static async Task TestAudioConnection(string output)
    {
        var results = new Dictionary<string, object>();
        try {
            using var http = new HttpClient { BaseAddress = new Uri((LocalServices.Audio + "")), Timeout = TimeSpan.FromSeconds(15) };
            using var stop = await http.PostAsync("/stop", LocalJson(new { }));
            results["stopAccepted"] = stop.IsSuccessStatusCode;
            // Exercise the same request format as live capture, with an invalid input ID.
            // This cannot open or record a physical microphone.
            using var listen = await http.PostAsync("/listen", LocalJson(new { automatic = true, device = 999999 }));
            string response = await listen.Content.ReadAsStringAsync();
            results["listenReachedDeviceValidation"] = response.Contains("999999") && !response.Contains("Invalid request");
            results["listenResponse"] = response;
            using var voice = new NeuralVoice();
            var playback = voice.Speak("Rune's local voice is ready.", CancellationToken.None);
            while (!playback.IsCompleted && !voice.IsSpeaking) await Task.Delay(20);
            results["voicePlaybackStarted"] = voice.IsSpeaking;
            await playback;
            results["passed"] = results.Values.OfType<bool>().All(v => v);
        } catch (Exception e) { results["passed"] = false; results["error"] = e.ToString(); }
        File.WriteAllText(output, JsonSerializer.Serialize(results, Brain.Json));
    }
}
