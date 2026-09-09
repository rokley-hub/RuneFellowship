using System.Text.Json;

namespace Rune.Voice;

internal static class VoiceEngineChecks
{
    internal static void Run(string output)
    {
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "voice-engine-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string oldProfile = Path.Combine(folder, "old.json");
        File.WriteAllText(oldProfile, "{\"profiles\":[{\"id\":\"rune\",\"name\":\"Rune\",\"voice\":\"af_heart\"}]}");
        var migrated = ProfileStore.Load(oldProfile).Profiles.Single();
        var expressive = new CompanionProfile { VoiceEngine = "chatterbox-v3", Voice = "af_bella", Personality = "Warm, playful and fierce in danger." }.Copy();
        var warm = VoiceExpression.From(expressive.Personality, "The shelter is ready. Well fought!");
        var danger = VoiceExpression.From(expressive.Personality, "Enemies behind you! Run!");
        var dry = VoiceExpression.From("Quiet, dry, deadpan and reserved.", "We need a hammer before I can build that.");
        var amused = VoiceExpression.From("Warm, playful and fond of dry Viking humor.", "Heh, even the troll chose better weather than this.");
        var checks = new Dictionary<string, bool> {
            ["oldProfileDefaultsToKokoro"] = migrated.VoiceEngine == "kokoro" && migrated.Voice == "af_heart",
            ["expressiveEngineSurvivesCopy"] = expressive.VoiceEngine == "chatterbox-v3",
            ["warmSuccessHasPersonality"] = warm.Delivery == "pleased" && warm.Intensity >= .58,
            ["dangerOverridesRestingTone"] = danger.Delivery == "urgent" && danger.Intensity >= .8,
            ["dryBlockedReplyStaysRestrained"] = dry.Delivery == "concerned" && dry.Intensity < .7,
            ["vocalCueRequiresMatchingReply"] = amused.Cue == "chuckle" && danger.Cue == "none",
            ["defaultRuneIsSpecificAndBounded"] = CompanionProfile.RuneDefaultPersonality.Contains("Norse shieldmaiden") && CompanionProfile.RuneDefaultPersonality.Contains("never jokes when someone is in real danger") && CompanionProfile.RuneDefaultPersonality.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= 200,
            ["unknownEngineIsSafe"] = VoiceCatalog.NormalizeEngine("made-up") == "kokoro",
            ["kokoroHasSixVoices"] = VoiceCatalog.ForEngine("kokoro").Length == 6,
            ["chatterboxEnginesHave28References"] = VoiceCatalog.ForEngine("chatterbox-turbo").Distinct().Count() == 28 && VoiceCatalog.ForEngine("chatterbox-v3").SequenceEqual(VoiceCatalog.ForEngine("chatterbox-turbo")),
            ["unsupportedKokoroSelectionFallsBackByGender"] = VoiceCatalog.SelectVoice("kokoro", "am_onyx") == "bm_george" && VoiceCatalog.SelectVoice("kokoro", "bf_isabella") == "af_bella",
            ["supportedReferencePreserved"] = VoiceCatalog.SelectVoice("chatterbox-turbo", "am_onyx") == "am_onyx",
            ["allAdditionalMaleVoicesHaveMaleBodies"] = VoiceCatalog.ReferenceVoiceIds.Where(v => v[1] == 'm').All(VoiceCatalog.IsMale) && VoiceCatalog.ReferenceVoiceIds.Where(v => v[1] == 'f').All(v => !VoiceCatalog.IsMale(v))
        };
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = checks.Values.All(v => v), checks, samples = new { warm, danger, dry, amused } }, Brain.Json));
        Directory.Delete(folder, true);
        if (checks.Values.Any(v => !v)) throw new InvalidOperationException("Voice engine regression failed.");
    }
}
