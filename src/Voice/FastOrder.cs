using Rune.Shared;
using System.Text.RegularExpressions;

namespace Rune.Voice;

internal static class FastOrder
{
    // Only complete, familiar material orders use this extra fast path. Complex
    // requests and unresolved clarifications still go to the selected planner.
    internal static bool CanBypass(string text, Command? parsed, bool clarificationPending)
    {
        if (clarificationPending || parsed == null || !Rules.IsDirectOrder(text)) return false;
        string normalized = Rules.NormalizeOrder(text);
        if (parsed.action is "gather_wood" or "gather_stone") return true;
        return parsed.action == "gather_item" && Regex.IsMatch(normalized, @"^(?:pick up|gather|collect|fetch|get)(?: me)?(?: some)?(?: \d+)? (?:flint|resin|dandelions|raspberries|blueberries|mushrooms)(?: please| for me)?$");
    }
}
