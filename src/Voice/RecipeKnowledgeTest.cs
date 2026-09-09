using System.Text.Json;
using Rune.Shared;

namespace Rune.Voice;

internal static class RecipeKnowledgeTest
{
    internal static async Task LiveAnswer(string catalog, string output)
    {
        var book = JsonSerializer.Deserialize<RecipeBook>(File.ReadAllText(catalog), Brain.Json)!;
        var recipe = book.recipes.First(r => r.prefab == "AxeStone" && r.known);
        var brain = new Brain { ConversationOnly = true };
        var result = await brain.Chat("What exact materials and quantities do we need to craft a " + recipe.name + "? This is just a question, not an order.",
            new GameState { world = "synthetic-recipe-answer", ready = true, recipes = new[] { recipe } }, CancellationToken.None);
        File.WriteAllText(output, JsonSerializer.Serialize(new { recipe, result.reply, result.action, result.steps }, Brain.Json));
        if (result.action != "none" || result.steps.Length != 0) throw new InvalidOperationException("Recipe question incorrectly proposed an action.");
    }

    internal static void Run(string output)
    {
        var recipes = new[] {
            new RecipeInfo { prefab = "AxeBronze", name = "Bronze axe", known = true, materials = new[] { new RecipeIngredient { name = "Bronze", required = 8 } } },
            new RecipeInfo { prefab = "AxeStone", name = "Stone axe", known = true },
            new RecipeInfo { prefab = "AxeIron", name = "Iron axe", known = false },
            new RecipeInfo { prefab = "Karve", name = "Karve", known = true }
        };
        var checks = new Dictionary<string, bool> {
            ["exactRecipeOutranksSharedWords"] = RecipeKnowledge.Match(recipes, "What do we need for a bronze axe?").First().prefab == "AxeBronze",
            ["internalPrefabNamesResolve"] = RecipeKnowledge.Match(recipes, "AxeStone").Single().prefab == "AxeStone",
            ["unlearnedStatusPreserved"] = !RecipeKnowledge.Match(recipes, "Iron axe").First().known,
            ["unknownItemsDoNotInventRecipe"] = RecipeKnowledge.Match(recipes, "What do we need for a jetpack?").Length == 0,
            ["exactIngredientQuantityPreserved"] = RecipeKnowledge.Match(recipes, "Bronze axe").First().materials[0].required == 8
        };
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "recipe-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var book = new RecipeBook { world = "test-world", timestamp = Rules.Now, recipes = recipes };
        string path = Path.Combine(root, "recipes.json"); File.WriteAllText(path, JsonSerializer.Serialize(book, Brain.Json));
        checks["liveCatalogReadable"] = RecipeKnowledge.ReadMatches(root, new GameState { ready = true, world = "test-world" }, "bronze axe").Length > 0;
        checks["differentWorldRejected"] = RecipeKnowledge.ReadMatches(root, new GameState { ready = true, world = "other" }, "bronze axe").Length == 0;
        checks["offlineCatalogRejected"] = RecipeKnowledge.ReadMatches(root, new GameState { world = "test-world" }, "bronze axe").Length == 0;
        book.timestamp -= 120; File.WriteAllText(path, JsonSerializer.Serialize(book, Brain.Json));
        checks["staleCatalogRejected"] = RecipeKnowledge.ReadMatches(root, new GameState { ready = true, world = "test-world" }, "bronze axe").Length == 0;
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = checks.Values.All(p => p), checks }, Brain.Json));
        if (checks.Values.Any(p => !p)) throw new InvalidOperationException("Recipe knowledge regression failed.");
    }
}
