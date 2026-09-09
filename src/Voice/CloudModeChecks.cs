using System.Text.Json;

namespace Rune.Voice;

internal static class CloudModeChecks
{
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        string preferencesPath = Path.Combine(folder, "preferences.json");
        var saved = new RunePreferences { AiMode = "chatgpt", ChatGptEnabled = true, VoiceReplies = false, Profiles = new[] { new CompanionProfile { Voice = "af_heart" } } };
        File.WriteAllText(preferencesPath, JsonSerializer.Serialize(saved, Brain.Json));
        var loaded = ProfileStore.Load(preferencesPath);
        bool mode = loaded.AiMode == "chatgpt" && loaded.ChatGptEnabled;
        bool perCompanionVoice = loaded.Profiles.Single().Voice == "af_heart";
        bool voicePreference = !loaded.VoiceReplies;
        bool schema = ChatGptPlanner.Validate("{\"kind\":\"chat\",\"message\":\"A dry cloud-borne greeting.\",\"action\":\"none\",\"amount\":20,\"item\":\"\",\"steps\":[]}", loaded.Profiles.Single(), true).message.Length > 0;
        bool noApiVoiceType = typeof(CloudModeChecks).Assembly.GetType("Rune.Voice.OpenAiVoice") == null;
        bool noApiProfileFields = typeof(CompanionProfile).GetProperties().All(p => !p.Name.Contains("CloudVoice", StringComparison.OrdinalIgnoreCase) && !p.Name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
        bool complete = mode && perCompanionVoice && voicePreference && schema && noApiVoiceType && noApiProfileFields;
        File.WriteAllText(Path.Combine(folder, "report.txt"), string.Join(Environment.NewLine, new[] {
            (mode ? "PASS" : "FAIL") + ": Full ChatGPT mode survives preference reload.",
            (perCompanionVoice ? "PASS" : "FAIL") + ": The selected local voice is stored per companion in Full ChatGPT mode.",
            (voicePreference ? "PASS" : "FAIL") + ": Speak replies is saved and restored.",
            (schema ? "PASS" : "FAIL") + ": Full-mode conversational replies pass the validated planner schema.",
            (noApiVoiceType ? "PASS" : "FAIL") + ": The OpenAI API speech implementation is absent from the application.",
            (noApiProfileFields ? "PASS" : "FAIL") + ": Companion profiles contain no API voice or API key fields.",
            "No network request, model inference, audio playback, or API charge was made by this check."
        }));
        if (!complete) throw new InvalidOperationException("API-free Full ChatGPT mode checks failed.");
    }
}
