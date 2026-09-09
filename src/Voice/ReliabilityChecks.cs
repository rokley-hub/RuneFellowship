using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Json;
using Rune.Shared;

namespace Rune.Voice;
internal static class ReliabilityChecks
{
    internal static async Task LiveMemory(string bridgeFolder, string output)
    {
        try {
            var state = new GameState { world = "synthetic-memory-check" };
            var brain = new Brain { MemoryFolder = Path.Combine(Path.GetDirectoryName(output)!, "live-memory-fixture") };
            brain.RememberExchange(state, "My boat is named Raven.", "Raven is a fine name for your boat.");
            using var connection = new ChatGptConnection(bridgeFolder);
            var decision = await ChatGptPlanner.Interpret(connection, "", "What did I name my boat?", state,
                new CompanionProfile(), false, CancellationToken.None, "en", true, brain.ConversationHistory(state));
            bool passed = decision.kind == "chat" && decision.action == "none" && decision.message.Contains("Raven", StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed, decision.message, decision.delivery, note = "One signed-in ChatGPT dialogue call with synthetic conversation context; no game commands or ordinary memories." }, Brain.Json));
            if (!passed) Environment.ExitCode = 1;
        } catch (Exception e) { File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, error = e.Message }, Brain.Json)); Environment.ExitCode = 1; }
    }
    internal static void Run(string output)
    {
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
        try {
            string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "memory-" + Guid.NewGuid().ToString("N"));
            var state = new GameState { world = "isolated-review-test" };
            var brain = new Brain { MemoryFolder = root, CompanionId = "rune" };
            brain.RememberExchange(state, "My boat is called Raven.", "Raven is a fine name.");
            Check(brain.ConversationHistory(state).Last().content.Contains("Raven"), "Cloud context contains the actual prior exchange");
            var reload = new Brain { MemoryFolder = root, CompanionId = "rune" };
            Check(reload.ConversationHistory(state).First().content.Contains("Raven"), "Memory survives process reload and provider changes");
            reload.CompanionId = "eira";
            Check(reload.ConversationHistory(state).Length == 0, "Another companion cannot inherit Rune's history");
            reload.CompanionId = "rune";
            Check(reload.ConversationHistory(new GameState { world = "another-world" }).Length == 0, "World context stays isolated");
            for (int i = 0; i < 40; i++) brain.RememberExchange(state, "question " + i, new string('a', 3000));
            Check(brain.ConversationHistory(state).Length == 12 && brain.ConversationHistory(state).All(m => m.content.Length <= 1200), "Model context is bounded");
            brain.DeleteMemoryFor("rune");
            Check(brain.ConversationHistory(state).Length == 0, "Deleting a companion clears shared memory");
            var profile = new CompanionProfile { UseStoredMaterials = false, CraftAndBuild = true };
            Check(ChatGptPlanner.Permitted(profile, "craft_item") && ChatGptPlanner.Permitted(profile, "finish_plan"), "Carried-only crafting is permitted");
            profile.CraftAndBuild = false;
            Check(!ChatGptPlanner.Permitted(profile, "craft_item"), "Crafting permission remains independent");
            Check(Rules.BlueprintKey("Large Viking Long House") == Rules.BlueprintKey("large_viking_long-house"), "Blueprint speech separators match");
            Check(Rules.BlueprintKey("New city") == Rules.BlueprintKey("NewCity"), "Joined blueprint words match");
            Check(Rules.Parse("resume task")?.action == "resume_task" && Rules.Parse("continue my work")?.action == "resume_task", "Resume synonyms route deterministically");
            Check(Rules.Parse("don't resume task")?.action != "resume_task", "Negated resume does not execute");
            Check(VoiceExpression.From("not grumpy", "We are not in danger.").Delivery == "natural", "Negated emotion does not become grumpy or urgent");
            Check(VoiceExpression.From("playful", "I understand.", "concerned").Delivery == "concerned", "Contextual delivery overrides resting personality");
            Check(VoiceExpression.From("calm", "Ha, a fine day!", "warm").Cue == "chuckle", "Explicit laugh still reaches the voice engine");
            var c = new Command { action = "status", useStoredMaterials = false };
            string wire = JsonSerializer.Serialize(c, Brain.Json);
            foreach (string field in new[] { "objective", "useStoredMaterials", "allowCrafting", "allowBaseWork", "protocolVersion" }) {
                var node = System.Text.Json.Nodes.JsonNode.Parse(wire)!.AsObject(); node.Remove(field); wire = node.ToJsonString();
            }
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(wire));
            var legacy = (Command)new DataContractJsonSerializer(typeof(Command)).ReadObject(stream)!;
            Check(legacy.action == "status", "Legacy wire commands deserialize with optional new fields");
            var bridge = new GameBridge(Path.Combine(root, "bridge"));
            File.WriteAllText(Path.Combine(bridge.Folder, "state.json"), JsonSerializer.Serialize(new GameState { ready = true, timestamp = Rules.Now - 60, error = "old objective error" }, Brain.Json));
            Check(!bridge.State().ready && bridge.State().error == "", "Old offline errors are not fresh incidents");
            File.WriteAllText(Path.Combine(bridge.Folder, "state.json"), JsonSerializer.Serialize(new GameState { ready = true, world = "fixture", timestamp = Rules.Now }, Brain.Json));
            var incompatible = bridge.Send(new Command { action = "gather_wood" }, CancellationToken.None).GetAwaiter().GetResult();
            Check(!incompatible.accepted && incompatible.message.Contains("matching versions") && !File.Exists(Path.Combine(bridge.Folder, "command.json")), "An older game plugin cannot bypass the new permission contract");
            string profiles = Path.Combine(root, "profiles");
            string source = OwnedMods.Create(profiles, "Source");
            string sourceConfig = Path.Combine(source, "BepInEx/config/marcopogo.PlanBuild.cfg");
            string originalConfig = File.ReadAllText(sourceConfig);
            File.WriteAllText(Path.Combine(source, "blueprints/Cabin.blueprint"), "isolated fixture");
            string imported = OwnedMods.Import(profiles, "Imported", source);
            Check(File.Exists(Path.Combine(imported, "blueprints/Cabin.blueprint")), "Import copies the source profile's local blueprints");
            Check(File.ReadAllText(Path.Combine(imported, "BepInEx/config/marcopogo.PlanBuild.cfg")).Contains(Path.Combine(imported, "blueprints").Replace('\\', '/')),
                "Imported PlanBuild config uses the final independent profile path");
            Check(File.ReadAllText(sourceConfig) == originalConfig, "Import leaves the original mod profile configuration untouched");
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, checks }, Brain.Json));
        } catch (Exception e) {
            File.WriteAllText(output, JsonSerializer.Serialize(new { passed = false, checks, error = e.ToString() }, Brain.Json)); Environment.ExitCode = 1;
        }
    }
}
