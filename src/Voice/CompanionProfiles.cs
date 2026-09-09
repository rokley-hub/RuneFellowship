using System.Text.Json;

namespace Rune.Voice;

public sealed class CompanionProfile
{
    internal const string RuneDefaultPersonality = "Rune is a battle-tested Norse shieldmaiden with a warm hearthside manner and a stubborn streak. She speaks plainly and values courage, loyalty, good preparation, and earned trust. Her humor is dry, teasing, and rooted in Viking life—bad weather, empty mead cups, boastful warriors—but she never jokes when someone is in real danger. She enjoys exploration and friendly competition, celebrates hard-won progress, admits mistakes without excuses, and becomes focused and commanding in combat. She can be protective and opinionated without being controlling. With friends she relaxes, tells vivid stories, laughs readily, and stays curious about life beyond Valheim.";
    public string Id { get; set; } = "rune";
    public string Name { get; set; } = "Rune";
    public string RecognitionName { get; set; } = "Rune";
    public string Appearance { get; set; } = "dwarf";
    public string VoiceEngine { get; set; } = "kokoro";
    public string Voice { get; set; } = "bm_george";
    public string Personality { get; set; } = RuneDefaultPersonality;
    public string[] Traits { get; set; } = new[] { "Playful", "Dry humor", "Curious", "Loyal" };
    public string Role { get; set; } = "Builder & Gatherer";
    public string CombatStyle { get; set; } = "Cautious";
    public string ConversationFrequency { get; set; } = "Natural";
    public bool UseStoredMaterials { get; set; } = true;
    public bool CraftAndBuild { get; set; } = true;
    public bool CookAndSort { get; set; } = true;
    public bool ShowOnMap { get; set; } = true;
    public bool JoinBossFights { get; set; } = true;

    public static CompanionProfile[] Defaults() => new CompanionProfile[]
    {
        new CompanionProfile(),
        new CompanionProfile() { Id = "eira", Name = "Eira", RecognitionName = "Eira", Appearance = "dwarf", Voice = "af_heart", Personality = "Curious, quietly mischievous, perceptive, kind, and always ready with an unexpected question.", Traits = new[] { "Curious", "Playful", "Calm", "Loyal" }, Role = "Scout & Gatherer", CombatStyle = "Balanced" },
        new CompanionProfile() { Id = "bjorn", Name = "Bjorn", RecognitionName = "Bjorn", Appearance = "wolf", Voice = "am_fenrir", Personality = "Warm, practical, brave, fond of tall tales, and calm when everyone else is panicking.", Traits = new[] { "Loyal", "Brave", "Calm", "Playful" }, Role = "Guard & Explorer", CombatStyle = "Balanced", CraftAndBuild = false, CookAndSort = false, UseStoredMaterials = false }
    };

    public CompanionProfile Copy() => new()
    {
        Id = Id, Name = Name, RecognitionName = RecognitionName, Appearance = Appearance, VoiceEngine = VoiceCatalog.NormalizeEngine(VoiceEngine), Voice = Voice,
        Personality = Personality, Traits = Traits.ToArray(), Role = Role, CombatStyle = CombatStyle,
        ConversationFrequency = ConversationFrequency, UseStoredMaterials = UseStoredMaterials,
        CraftAndBuild = CraftAndBuild, CookAndSort = CookAndSort, JoinBossFights = JoinBossFights, ShowOnMap = ShowOnMap
    };
}

public sealed class RunePreferences
{
    public string MicShortcut { get; set; } = "Ctrl + Alt + R";
    public string SwitchCompanionShortcut { get; set; } = "Ctrl + Alt + C";
    public string OverlayShortcut { get; set; } = "F7";
    public string ControlsShortcut { get; set; } = "F8";
    public string GamePath { get; set; } = GameDiscovery.FindValheim();
    public string ModProfilePath { get; set; } = "";
    public string ModSource { get; set; } = "thunderstore";
    public Dictionary<string, string> ModSourcePages { get; set; } = new();
    public string Language { get; set; } = "en";
    public bool ChatGptEnabled { get; set; }
    // local = Qwen + selected local voice, hybrid = ChatGPT commands + local dialogue/voice,
    // chatgpt = ChatGPT dialogue/commands + local voice.
    public string AiMode { get; set; } = "local";
    public string PerformanceMode { get; set; } = "automatic";
    public bool KeepVoiceReady { get; set; } = true;
    public int SpeechPauseMs { get; set; } = 900;
    public string ChatGptModel { get; set; } = "";
    public string CodexExecutable { get; set; } = "";
    public string Notes { get; set; } = "";
    public string OutputDeviceName { get; set; } = "";
    public string LocalModel { get; set; } = "qwen3.5:4b";
    public int MicrophoneMode { get; set; } = 1;
    public int MicrophoneKey { get; set; }
    public int MicrophoneDevice { get; set; } = -1;
    public bool AllowGameOrders { get; set; } = true;
    public bool VoiceReplies { get; set; } = true;
    public int VoiceVolume { get; set; } = 100;
    public CompanionProfile[] Profiles { get; set; } = CompanionProfile.Defaults();
}

internal static class ProfileStore
{
    public static RunePreferences Load(string path)
    {
        var settings = new RunePreferences();
        if (!File.Exists(path)) return settings;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.TryGetProperty("modSource", out var modSource) && modSource.ValueKind == JsonValueKind.String) settings.ModSource = ModSources.Normalize(modSource.GetString());
            if (root.TryGetProperty("modSourcePages", out var sourcePages) && sourcePages.ValueKind == JsonValueKind.Object)
                foreach (var page in sourcePages.EnumerateObject().Take(5000)) {
                    string source = page.Name.Split(':')[0];
                    if (page.Value.ValueKind == JsonValueKind.String && ModSources.ValidPage(source, page.Value.GetString() ?? "")) settings.ModSourcePages[page.Name] = page.Value.GetString()!;
                }
            if (root.TryGetProperty("language", out var language)) settings.Language = LanguageSettings.Normalize(language.GetString());
            if (root.TryGetProperty("chatGptEnabled", out var online)) settings.ChatGptEnabled = online.GetBoolean();
            if (root.TryGetProperty("aiMode", out var aiMode) && aiMode.GetString() is string selectedAiMode && selectedAiMode is "local" or "hybrid" or "chatgpt") settings.AiMode = selectedAiMode;
            else settings.AiMode = settings.ChatGptEnabled ? "hybrid" : "local";
            settings.ChatGptEnabled = settings.AiMode != "local";
            if (root.TryGetProperty("performanceMode", out var performance) && performance.GetString() is "automatic" or "expressive" or "lightweight") settings.PerformanceMode = performance.GetString()!;
            if (root.TryGetProperty("keepVoiceReady", out var warm)) settings.KeepVoiceReady = warm.GetBoolean();
            if (root.TryGetProperty("speechPauseMs", out var pause)) settings.SpeechPauseMs = Math.Clamp(pause.GetInt32(), 600, 1500);
            if (root.TryGetProperty("chatGptModel", out var cloudModel)) settings.ChatGptModel = cloudModel.GetString() ?? "";
            if (root.TryGetProperty("codexExecutable", out var executable)) settings.CodexExecutable = executable.GetString() ?? "";
            if (root.TryGetProperty("notes", out var notes)) settings.Notes = notes.GetString() ?? "";
            if (root.TryGetProperty("localModel", out var model) && model.GetString() is string local && (local == "qwen3.5:4b" || local == "qwen3.5:9b")) settings.LocalModel = local;
            if (root.TryGetProperty("microphoneMode", out var mode)) settings.MicrophoneMode = Math.Clamp(mode.GetInt32(), 0, 2);
            if (root.TryGetProperty("microphoneKey", out var key)) settings.MicrophoneKey = Math.Clamp(key.GetInt32(), 0, 2);
            settings.MicShortcut = settings.MicrophoneKey == 1 ? "Ctrl + Alt + V" : settings.MicrophoneKey == 2 ? "Off" : "Ctrl + Alt + R";
            foreach (string name in new[] { "MicShortcut", "SwitchCompanionShortcut", "OverlayShortcut", "ControlsShortcut", "GamePath", "ModProfilePath" }) {
                string jsonName = char.ToLowerInvariant(name[0]) + name.Substring(1);
                if (root.TryGetProperty(jsonName, out var value) && value.ValueKind == JsonValueKind.String) typeof(RunePreferences).GetProperty(name)!.SetValue(settings, value.GetString());
            }
            if (root.TryGetProperty("microphoneDevice", out var device)) settings.MicrophoneDevice = device.GetInt32();
            if (root.TryGetProperty("voiceVolume", out var volume)) settings.VoiceVolume = Math.Clamp(volume.GetInt32(), 0, 100);
            if (root.TryGetProperty("outputDeviceName", out var output)) settings.OutputDeviceName = output.GetString() ?? "";
            if (root.TryGetProperty("allowGameOrders", out var orders)) settings.AllowGameOrders = orders.GetBoolean();
            if (root.TryGetProperty("voiceReplies", out var replies)) settings.VoiceReplies = replies.GetBoolean();
            if (root.TryGetProperty("profiles", out var profiles))
            {
                var loaded = JsonSerializer.Deserialize<CompanionProfile[]>(profiles.GetRawText(), Brain.Json);
                if (loaded != null) {
                    var valid = loaded.Where(p => p != null && Rune.Shared.Rules.IsCompanion(p.Id)).GroupBy(p => p.Id).Select(g => g.First()).Take(8).ToArray();
                    foreach (var profile in valid) profile.VoiceEngine = VoiceCatalog.NormalizeEngine(profile.VoiceEngine);
                    if (valid.Length > 0) settings.Profiles = valid;
                }
            }
            // Migrate preferences written by Rune Fellowship 0.2.1.
            if (!root.TryGetProperty("profiles", out _) && root.TryGetProperty("skins", out var skins)) foreach (var pair in skins.EnumerateObject())
            {
                var profile = settings.Profiles.FirstOrDefault(p => p.Id == pair.Name);
                if (profile != null && pair.Value.GetString() is string skin) profile.Appearance = skin;
            }
            if (!root.TryGetProperty("profiles", out _) && root.TryGetProperty("voices", out var voices)) foreach (var pair in voices.EnumerateObject())
            {
                var profile = settings.Profiles.FirstOrDefault(p => p.Id == pair.Name);
                if (profile != null && pair.Value.GetString() is string voice) profile.Voice = voice;
            }
        }
        catch { }
        return settings;
    }
}


