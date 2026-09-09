using System.Text.Json;
using Rune.Shared;

namespace Rune.Voice;

internal static class MemoryPersistenceTest
{
    internal static async Task Run(string output)
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "memory-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var state = new GameState { world = "synthetic-memory-test" };
        string folder = Path.Combine(root, "memories");
        var checks = new Dictionary<string, bool>();
        var brain = new Brain { MemoryFolder = folder, ConversationOnly = true };
        string runeMessage = "My favorite tree is a birch. Just acknowledge that briefly.";
        var first = await brain.Chat(runeMessage, state, CancellationToken.None);
        string runeFile = Directory.GetFiles(Path.Combine(folder, "rune"), "*.json").Single();
        var original = JsonSerializer.Deserialize<List<Brain.Message>>(File.ReadAllText(runeFile), Brain.Json)!;
        checks["firstConversationCreatesCompanionFolderAndSavesReply"] = original.Count == 2 && original[0].content == runeMessage && original[1].content == first.reply && brain.MemoryWarning == "";

        brain.CompanionId = "eira"; brain.DisplayName = "Eira";
        await brain.Chat("My favorite flower is a daisy. Acknowledge briefly.", state, CancellationToken.None);
        var eira = JsonSerializer.Deserialize<List<Brain.Message>>(File.ReadAllText(Directory.GetFiles(Path.Combine(folder, "eira"), "*.json").Single()), Brain.Json)!;
        checks["companionsKeepSeparateHistory"] = eira.Count == 2 && !eira.Any(m => m.content == runeMessage) && File.ReadAllText(runeFile).Contains("birch");

        var reopened = new Brain { MemoryFolder = folder, ConversationOnly = true };
        await reopened.Chat("Thanks. What tree did I mention?", state, CancellationToken.None);
        var restored = JsonSerializer.Deserialize<List<Brain.Message>>(File.ReadAllText(runeFile), Brain.Json)!;
        checks["reopeningRetainsPreviousMessages"] = restored.Count == 4 && restored[0] == original[0] && restored[1] == original[1];

        string blocked = Path.Combine(root, "blocked-folder"); File.WriteAllText(blocked, "A file deliberately blocks directory creation.");
        var unavailable = new Brain { MemoryFolder = blocked, ConversationOnly = true };
        var reply = await unavailable.Chat("Say hello briefly.", state, CancellationToken.None);
        checks["saveFailureStillReturnsReplyWithMemoryWarning"] = reply.reply.Length > 0 && unavailable.MemoryWarning.Length > 0;

        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = checks.Values.All(p => p), checks }, Brain.Json));
        if (checks.Values.Any(p => !p)) throw new InvalidOperationException("Memory persistence regression failed.");
    }
}
