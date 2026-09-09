namespace Rune.Voice;

internal static class VoiceCatalog
{
    internal static readonly string[] EngineIds = { "kokoro", "chatterbox-turbo", "chatterbox-v3" };
    internal static readonly string[] EngineLabels = {
        "Kokoro · fast",
        "Chatterbox Turbo",
        "Chatterbox V3"
    };
    internal static readonly string[] VoiceIds = { "bm_george", "am_michael", "am_fenrir", "bf_emma", "af_heart", "af_bella" };
    internal static readonly string[] VoiceLabels = {
        "George · British male", "Michael · American male", "Fenrir · American male",
        "Emma · British female", "Heart · American female", "Bella · American female"
    };
    // Chatterbox conditions on these locally generated reference recordings.
    // They are source-speaker identities, not built-in Chatterbox preset voices.
    internal static readonly string[] ReferenceVoiceIds = {
        "af_alloy", "af_aoede", "af_bella", "af_heart", "af_jessica", "af_kore", "af_nicole", "af_nova", "af_river", "af_sarah", "af_sky",
        "am_adam", "am_echo", "am_eric", "am_fenrir", "am_liam", "am_michael", "am_onyx", "am_puck", "am_santa",
        "bf_alice", "bf_emma", "bf_isabella", "bf_lily", "bm_daniel", "bm_fable", "bm_george", "bm_lewis"
    };
    internal static string[] ForEngine(string? engine) => NormalizeEngine(engine) == "kokoro" ? VoiceIds : ReferenceVoiceIds;
    internal static string Label(string id) {
        string name = id.Length > 3 ? id[3..] : id;
        return char.ToUpperInvariant(name[0]) + name[1..] + " · " + (id.StartsWith('b') ? "British" : "American") + (IsMale(id) ? " male" : " female");
    }
    internal static string SelectVoice(string? engine, string? preferred) => ForEngine(engine).Contains(preferred) ? preferred! : preferred == null || IsMale(preferred) ? "bm_george" : "af_bella";

    internal static string NormalizeEngine(string? value) => EngineIds.Contains(value) ? value! : "kokoro";
    internal static string EngineName(string? value) => NormalizeEngine(value) switch {
        "chatterbox-turbo" => "Chatterbox Turbo",
        "chatterbox-v3" => "Chatterbox V3",
        _ => "Kokoro"
    };
    internal static bool IsMale(string? voice) => voice != null && ReferenceVoiceIds.Contains(voice) && (voice.StartsWith("am_") || voice.StartsWith("bm_"));
}
