namespace Rune.Voice;

internal static class VoiceAudition
{
    // One fixed sentence makes comparisons quick and does not contact a brain.
    // "Ha" triggers the existing Turbo chuckle cue; other engines render the text.
    internal static string Line(string language) => language switch {
        "de" => "Ha, was für ein herrlicher Tag—schnapp dir deine Axt, das Abenteuer wartet!",
        "nl" => "Ha, wat een heerlijke dag—pak je bijl, vriend, het avontuur wacht!",
        _ => "Ha, what a glorious day—grab your axe, friend, adventure awaits!"
    };
    internal const string Delivery = "Warm, cheerful, playful and excited about a new adventure.";
}
