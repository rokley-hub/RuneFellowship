using System.Text.RegularExpressions;
using Rune.Shared;

namespace Rune.Voice;

internal static class CompanionAddress
{
    internal sealed record Result(string Text, string? Companion, bool NameOnly);
    internal static Result Parse(string text, IEnumerable<CompanionProfile> profiles)
    {
        text = text.Trim();
        foreach (var p in profiles) foreach (string alias in new[] { p.Name, p.RecognitionName }.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(a => a.Length)) {
            string name = Regex.Escape(alias);
            var prefix = Regex.Match(text, @"^(?:(?:hey|hi|hello|a|ah|okay|ok|i)[,\s]+)?" + name + @"(?=$|[\s,.!?])[,\s.!?]*", RegexOptions.IgnoreCase);
            if (prefix.Success) {
                string rest = text[prefix.Length..].Trim();
                return new(rest, p.Id, rest.Length == 0);
            }
            var suffix = Regex.Match(text, @"(?<separator>,\s*|\s+)" + name + @"[.!?]*$", RegexOptions.IgnoreCase);
            if (suffix.Success) {
                string rest = text[..suffix.Index].Trim();
                // A comma is an explicit address. Without punctuation, strip a
                // trailing name only when the remainder is a complete known order.
                // This preserves actual items such as "Blueprint Rune".
                var order = Rules.Parse(rest);
                if (suffix.Groups["separator"].Value.Contains(',') || (order != null && order.action != "gather_item")) return new(rest, p.Id, false);
            }
        }
        return new(text, null, false);
    }
}
