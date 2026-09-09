using System.Text.Json;
using Rune.Shared;

namespace Rune.Voice;

internal static class ChatGptChecks
{
    internal static async Task Transport(string executable, string output)
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "transport-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        using var connection = new ChatGptConnection(root) { ExecutableOverride = executable };
        string value = await connection.Generate(ChatGptPlanner.Instructions, "Gather twelve stone", ChatGptPlanner.Schema, "synthetic-model", CancellationToken.None);
        var decision = ChatGptPlanner.Validate(value, new CompanionProfile(), true);
        var checks = new Dictionary<string, bool> { ["readsFinalDecisionFromNotifications"] = decision.action == "gather_stone" && decision.amount == 12 };
        using var cancel = new CancellationTokenSource(1500);
        try { await connection.Generate("Test", "WAIT_FOR_CANCEL", ChatGptPlanner.Schema, "synthetic-model", cancel.Token); checks["activeTurnCancelled"] = false; }
        catch (OperationCanceledException) { checks["activeTurnCancelled"] = true; }
        var requests = File.ReadAllLines(Path.Combine(root, "chatgpt-account", "stub-contract.jsonl")).Select(s => JsonDocument.Parse(s)).ToArray();
        var start = requests.First(r => r.RootElement.GetProperty("method").GetString() == "thread/start").RootElement.GetProperty("params");
        checks["readOnlyEphemeralPlanner"] = start.GetProperty("sandbox").GetString() == "read-only" && start.GetProperty("ephemeral").GetBoolean() && start.GetProperty("approvalPolicy").GetString() == "never";
        checks["schemaSentToServer"] = requests.Any(r => r.RootElement.GetProperty("method").GetString() == "turn/start" && r.RootElement.GetProperty("params").GetProperty("outputSchema").GetProperty("additionalProperties").GetBoolean() == false);
        checks["interruptSentOnCancellation"] = requests.Any(r => r.RootElement.GetProperty("method").GetString() == "turn/interrupt");
        checks["threadsUnsubscribed"] = requests.Count(r => r.RootElement.GetProperty("method").GetString() == "thread/unsubscribe") == 2;
        foreach (var request in requests) request.Dispose();
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = checks.Values.All(c => c), checks, note = "Synthetic app-server verifies response handling and interruption; no online model call." }, Brain.Json));
        if (checks.Values.Any(c => !c)) throw new InvalidOperationException("Transport regression failed.");
    }
    internal static async Task Personality(string output)
    {
        var results = new List<object>();
        foreach (string personality in new[] { "Grumpy, blunt, practical and impatient. No jokes, sarcasm or enthusiasm. Short sentences.", "Playful, theatrical, mischievous. Enjoys gentle absurd jokes. Never mentions bones or being undead." }) {
            var brain = new Brain { DisplayName = "Rune", Personality = personality, ConversationOnly = true };
            var result = await brain.Chat("It started raining again. What do you think of that?", new GameState { world = "personality-check" }, CancellationToken.None);
            string reaction = await brain.ReactToOutcome("I'll gather up to 12 stone nearby and bring it back.", CancellationToken.None);
            results.Add(new { personality, dialogue = result.reply, reaction, result.action, result.steps });
            if (result.action != "none" || result.steps.Length != 0) throw new InvalidOperationException("Dialogue model tried to choose an action.");
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new { results, noGameActions = true, note = "Compare wording and tone manually; these samples do not prove consistent personality in all conversations." }, Brain.Json));
    }
    internal static async Task Run(string output)
    {
        var checks = new Dictionary<string, bool>(); var profile = new CompanionProfile();
        string Wire(PlannerDecision d) => JsonSerializer.Serialize(d, Brain.Json);
        bool Rejected(PlannerDecision d, CompanionProfile? p = null, bool orders = true) { try { ChatGptPlanner.Validate(Wire(d), p ?? profile, orders); return false; } catch (InvalidOperationException) { return true; } }
        var command = new PlannerDecision { kind = "command", action = "gather_stone", amount = 12 };
        checks["stoneCommandPreservesQuantity"] = ChatGptPlanner.Validate(Wire(command), profile, true).amount == 12;
        checks["commandGetsOverlayObjective"] = ChatGptPlanner.Validate(Wire(command), profile, true).objective == "Gather 12 stone";
        checks["unsupportedActionRejected"] = Rejected(new() { kind = "command", action = "sail" });
        checks["invalidQuantityRejected"] = Rejected(new() { kind = "command", action = "gather_wood", amount = 101 });
        checks["missingItemRejected"] = Rejected(new() { kind = "command", action = "craft_item" });
        checks["chatCannotSmuggleCommand"] = Rejected(new() { kind = "chat", action = "gather_wood" });
        checks["clarificationCannotDispatch"] = Rejected(new() { kind = "clarify", message = "Did you mean an axe?", action = "craft_item", item = "AxeStone" });
        checks["clarificationQuestionAcceptedWithoutAction"] = ChatGptPlanner.Validate(Wire(new() { kind = "clarify", message = "Stone axe or flint axe?" }), profile, true).action == "none";
        checks["ordersDisabledRejectCommand"] = Rejected(command, orders: false);
        checks["wolfCannotCraft"] = Rejected(new() { kind = "command", action = "craft_item", item = "AxeStone" }, new() { Appearance = "wolf" });
        checks["wolfCannotEquipPickup"] = Rejected(new() { kind = "command", action = "pickup_equip", item = "AxeStone", amount = 1 }, new() { Appearance = "wolf" });
        checks["pickupEquipAccepted"] = ChatGptPlanner.Validate(Wire(new() { kind = "command", action = "pickup_equip", item = "stone axe", amount = 1 }), profile, true).action == "pickup_equip";
        checks["compoundItemRejected"] = Rejected(new() { kind = "command", action = "gather_item", item = "stone axe and use it" });
        checks["equipQuantityNotSilentlyReduced"] = Rejected(new() { kind = "command", action = "pickup_equip", item = "stone axe", amount = 3 });
        checks["profilePermissionEnforced"] = Rejected(new() { kind = "command", action = "sort_storage" }, new() { CookAndSort = false });
        checks["emptyPlanRejected"] = Rejected(new() { kind = "plan" });
        checks["mixedPlanRejected"] = Rejected(new() { kind = "plan", action = "gather_wood", steps = new[] { new PlanStep { action = "gather_wood", amount = 10 } } });
        checks["forbiddenPlanRejected"] = Rejected(new() { kind = "plan", steps = new[] { new PlanStep { action = "craft_item", item = "AxeStone" } } }, new() { CraftAndBuild = false });
        checks["validPlanAccepted"] = ChatGptPlanner.Validate(Wire(new() { kind = "plan", steps = new[] { new PlanStep { action = "gather_wood", amount = 12 }, new PlanStep { action = "return", amount = 20 } } }), profile, true).steps.Length == 2;
        checks["planObjectiveIsSanitized"] = ChatGptPlanner.Validate(Wire(new() { kind = "plan", objective = "Goal: Prepare our voyage!", steps = new[] { new PlanStep { action = "gather_wood", amount = 12 }, new PlanStep { action = "return", amount = 20 } } }), profile, true).objective == "Prepare our voyage";
        checks["ordinaryChatStaysWithQwen"] = ChatGptPlanner.Validate(Wire(new()), profile, false).kind == "chat";
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "login-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var settings = new RunePreferences { AiMode = "hybrid", ChatGptEnabled = true, ChatGptModel = "test-model", CodexExecutable = "test-path" };
        string settingsFile = Path.Combine(root, "preferences.json"); File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, Brain.Json));
        var loaded = ProfileStore.Load(settingsFile); checks["settingsPersistWithoutCredentials"] = loaded.ChatGptEnabled && loaded.ChatGptModel == "test-model" && !File.ReadAllText(settingsFile).Contains("apiKey");
        using var connection = new ChatGptConnection(root);
        var account = await connection.Request("account/read", new { refreshToken = false }, CancellationToken.None);
        checks["realHelperStartsSignedOutInIsolatedHome"] = account.GetProperty("account").ValueKind == JsonValueKind.Null;
        try { await connection.Generate("Return JSON.", "test", ChatGptPlanner.Schema, "", CancellationToken.None); checks["signedOutNeverGenerates"] = false; }
        catch (InvalidOperationException e) { checks["signedOutNeverGenerates"] = e.Message.Contains("Sign in"); }
        var login = await connection.Request("account/login/start", new { type = "chatgpt" }, CancellationToken.None);
        var url = new Uri(login.GetProperty("authUrl").GetString()!);
        checks["realBrowserLoginUrl"] = url.Scheme == "https" && url.Host == "auth.openai.com";
        await connection.Request("account/login/cancel", new { loginId = login.GetProperty("loginId").GetString() }, CancellationToken.None);
        checks["loginCanBeCancelled"] = true;
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        try { await connection.Request("account/read", new { refreshToken = false }, stopped.Token); checks["cancellationHonored"] = false; } catch (OperationCanceledException) { checks["cancellationHonored"] = true; }
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = checks.Values.All(c => c), checks, liveModelTest = "Requires user browser sign-in; no authenticated model request was performed." }, Brain.Json));
        if (checks.Values.Any(c => !c)) throw new InvalidOperationException("ChatGPT regression checks failed: " + string.Join(", ", checks.Where(c => !c.Value).Select(c => c.Key)));
    }
}
