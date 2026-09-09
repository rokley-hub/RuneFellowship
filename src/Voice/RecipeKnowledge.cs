using System.Text.Json;
using System.Text.RegularExpressions;
using Rune.Shared;

namespace Rune.Voice;

internal static class RecipeKnowledge
{
    internal static RecipeInfo[] ReadMatches(string folder, GameState state, string text)
    {
        if (!state.ready) return Array.Empty<RecipeInfo>();
        try {
            string path = Path.Combine(folder, "recipes.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 8_000_000) return Array.Empty<RecipeInfo>();
            var book = JsonSerializer.Deserialize<RecipeBook>(File.ReadAllText(path), Brain.Json);
            if (book == null || book.world != state.world || Rules.Now - book.timestamp > 60) return Array.Empty<RecipeInfo>();
            return Match(book.recipes, text);
        } catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return Array.Empty<RecipeInfo>(); }
    }
    internal static RecipeInfo[] Match(RecipeInfo[] recipes, string text)
    {
        text = Regex.Replace(text, @"\bwork(?:ing)?\s+bench\b", "workbench", RegexOptions.IgnoreCase);
        string normalized = " " + Normalize(text) + " ";
        var ignore = new HashSet<string>("a an the to for of and or me us we you i my our can could would please craft crafting make build need needed materials ingredients recipe recipes what how do does is it all items".Split(' '));
        var words = Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !ignore.Contains(w)).ToHashSet();
        int Score(RecipeInfo r) {
            string name = Normalize(r.name), prefab = Normalize(r.prefab);
            if (normalized.Contains(" " + name + " ") || normalized.Contains(" " + prefab + " ")) return 1000 + name.Length;
            return name.Split(' ').Count(words.Contains) * 10;
        }
        var ranked = recipes.Select(r => new { recipe = r, score = Score(r) }).Where(r => r.score > 0)
            .OrderByDescending(r => r.score).ThenBy(r => r.recipe.name).ToArray();
        bool exact = ranked.Any(r => r.score >= 1000);
        return ranked.Where(r => !exact || r.score >= 1000).Take(6).Select(r => r.recipe).ToArray();
    }
    private static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
}
