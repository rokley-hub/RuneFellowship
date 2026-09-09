using System.Text.RegularExpressions;

namespace Rune.Voice;

internal sealed record VoiceExpression(string Delivery, double Intensity, string Cue)
{
    internal static readonly string[] Deliveries = { "natural", "calm", "dry", "warm", "amused", "energetic", "urgent", "concerned", "pleased" };
    internal static VoiceExpression From(string personality, string text, string requestedDelivery = "")
    {
        string profile = RemoveNegatedTraits(personality.ToLowerInvariant());
        string line = RemoveNegatedTraits(text.ToLowerInvariant());
        string delivery = "natural";
        double intensity = .5;

        if (Has(profile, "calm", "quiet", "soft", "gentle", "stoic", "reserved")) { delivery = "calm"; intensity = .34; }
        if (Has(profile, "dry", "deadpan", "sarcastic", "grumpy")) { delivery = "dry"; intensity = .42; }
        if (Has(profile, "warm", "kind", "compassionate", "caring")) { delivery = "warm"; intensity = .52; }
        if (Has(profile, "playful", "mischievous", "cheerful", "humor", "humour", "joke")) { delivery = "amused"; intensity = .62; }
        if (Has(profile, "fierce", "bold", "dramatic", "energetic", "excitable", "competitive")) { delivery = "energetic"; intensity = .72; }

        // The immediate situation may temporarily override the resting temperament.
        if (Has(line, "enemy", "enemies", "attack", "danger", "run!", "behind you", "low health")) { delivery = "urgent"; intensity = .82; }
        else if (Has(line, "can't", "cannot", "blocked", "unsafe", "hurt", "need a", "missing")) { delivery = "concerned"; intensity = Math.Max(intensity, .58); }
        else if (Has(line, "done", "finished", "ready", "success", "got it", "excellent")) { delivery = delivery == "dry" ? "dry" : "pleased"; intensity = Math.Max(intensity, .58); }

        string cue = Regex.IsMatch(line, @"\b(ha+|heh|haha|chuckle|laughs?|giggles?)\b", RegexOptions.IgnoreCase) ? "chuckle" :
            Regex.IsMatch(line, @"\b(sigh|exhausted|tired)\b", RegexOptions.IgnoreCase) ? "sigh" : "none";
        if (Deliveries.Contains(requestedDelivery)) {
            delivery = requestedDelivery;
            intensity = delivery switch { "calm" => .34, "dry" => .42, "warm" => .52,
                "amused" => .62, "energetic" => .72, "urgent" => .82, "concerned" => .58, "pleased" => .58, _ => .5 };
        }
        return new(delivery, Math.Clamp(intensity, 0, 1), cue);
    }

    private static string RemoveNegatedTraits(string value) => Regex.Replace(value,
        @"\b(?:not|never|no|without|isn't|isnt|aren't|arent|don't|dont|doesn't|doesnt)\s+(?:(?:very|really|in|any|being)\s+){0,2}\w+", "");
    private static bool Has(string value, params string[] terms) => terms.Any(value.Contains);
}
