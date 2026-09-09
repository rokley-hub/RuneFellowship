using Rune.Shared;

int checks = 0;
Check(Rules.Capabilities.Select(c => c.action).Distinct().Count() == Rules.Capabilities.Length, "Capability actions are unique");
Check(Rules.Actions.SequenceEqual(Rules.Capabilities.Select(c => c.action)), "Action validation comes from capability catalog");
Check(Rules.PlanActions.SequenceEqual(Rules.Capabilities.Where(c => c.plan).Select(c => c.action)), "Plan validation comes from capability catalog");
Check(Rules.Capabilities.Where(c => !c.hidden).All(c => c.group.Length > 0 && c.example.Length > 0 && c.description.Length > 0), "Visible capabilities contain generated help and model guidance");
Check(Rules.NormalizeObjective("Goal: I'll gather 20 wood.\n", "gather_wood", 20) == "gather 20 wood", "Overlay objective strips labels, promises and line breaks");
Check(Rules.NormalizeObjective("", "gather_stone", 12) == "Gather 12 stone", "Direct commands receive a useful objective fallback");
Check(Rules.NormalizeObjective(new string('x', 120)).Length <= 96, "Overlay objectives are bounded");
Check(Rules.DescribeAction("planbuild_player", 1, "cabin") == "Planbuild player cabin", "Plan step descriptions include the target item");
foreach (string verb in new[] { "craft", "create", "make", "build" }) {
 Check(Rules.Parse(verb + " a stone axe")?.item == "stone axe" && Rules.Parse(verb + " a stone axe")?.action == "craft_item", verb + " item synonym");
 Check(Rules.Parse(verb + " hammer")?.action == "craft_item", verb + " without article");
 Check(Rules.Parse(verb + " a karve")?.action == "build_boat", verb + " boat synonym");
 Check(Rules.Parse("gather materials to " + verb + " a hammer")?.action == "gather_recipe", verb + " materials synonym");
 Check(Rules.Parse("don't " + verb + " a hammer") == null, verb + " negation");
 Check(Rules.Parse("what if you " + verb + " a hammer") == null, verb + " hypothetical");
 Check(Rules.Parse(verb + " a house") == null && Rules.OrderClarification(verb + " a house").Contains("PlanBuild"), verb + " unsupported building");
 Check(Rules.Parse(verb + " a story") == null && !Rules.IsDirectOrder(verb + " a story"), verb + " creative conversation");
}
void Check(bool success, string label) { if (!success) throw new Exception(label); checks++; }
foreach (var (phrase, action) in new[] {
 ("stick with me", "follow"), ("stay with me", "follow"), ("come along", "follow"), ("guard me", "defend"), ("cover me", "defend"), ("watch my back", "defend"),
 ("halt", "stay"), ("stay put", "stay"), ("cancel current task", "stay"), ("hold your position", "stay"),
 ("pick everything up", "pickup_all"), ("loot everything", "pickup_all"), ("gather everything", "pickup_all"), ("stop looting", "stop_pickup"),
 ("stash your cargo", "store_cargo"), ("deliver your cargo", "return"), ("prepare food", "cook_food"), ("look after the base", "manage_base"),
 ("reset the crafting list", "clear_crafting"), ("report status", "status"), ("harvest ten wood", "gather_wood"), ("retrieve stone", "gather_stone"),
 ("grab resin", "gather_item"), ("scavenge for flint", "gather_item"), ("organise our chests", "sort_storage"), ("tidy up storage", "sort_storage"), ("skip resin", "exclude_item") }) {
 Check(Rules.Parse(phrase)?.action == action, phrase + " synonym");
 Check(Rules.Parse("Rune, could you please " + phrase + " please?")?.action == action, phrase + " polite synonym");
 Check(Rules.Parse("don't " + phrase) == null, phrase + " negation");
 Check(Rules.Parse("what if you " + phrase) == null, phrase + " hypothetical");
}
Check(Rules.Parse("gather a wood")?.action == "gather_wood", "Observed a wood phrase");
Check(Rules.Parse("get our wood")?.action == "gather_wood", "Observed our wood phrase");
Check(Rules.Parse("A Rune, get wood.")?.action == "gather_wood", "Observed spoken address");
Check(Rules.Parse("fight")?.action == "defend", "Fight activates defense");
Check(Rules.Parse("defend me")?.action == "defend", "Explicit defense");
Check(Rules.Parse("protect me")?.action == "defend", "Protect command");
Check(Rules.Parse("don't fight") == null, "Negated fight does not act");
Check(Rules.Parse("collect the ancient bark")?.item == "ancient bark", "Strip determiner without losing item name");
var inferred = new Command { action = "gather_item", item = "our wood", amount = 7 }; Rules.NormalizeGatherCommand(inferred);
Check(inferred.action == "gather_wood" && inferred.amount == 7, "Model item routing preserves quantity");
Check(Rules.Parse("gather sticks")?.action == "gather_wood", "Sticks mean wood");
Check(Rules.Parse("gather her sticks")?.action == "gather_wood", "Observed speech maps sticks correctly");
Check(Rules.Parse("gather ten branches")?.amount == 10, "Branches quantity preserved");
Check(Rules.Parse("punch trees")?.action == "gather_wood", "Unarmed gathering request");
Check(Rules.Parse("don't punch trees") == null, "Do not punch negation");
Check(Rules.Parse("go gather some wood")?.action == "gather_wood", "Spoken go prefix");
Check(Rules.Parse("Rune, could you go and get me twenty wood please?")?.amount == 20, "Natural spoken prefix and suffix");
Check(Rules.Parse("I want you to craft a stone axe")?.item == "stone axe", "Direct request phrasing");
Check(Rules.Parse("go make yourself a weapon") == null && Rules.OrderClarification("go make yourself a weapon").Contains("name the item"), "Vague weapon order asks for exact item");
foreach (var verb in new[] { "craft", "create", "make", "build", "place", "construct", "put down" }) foreach (var name in new[] { "workbench", "work bench", "working bench" }) {
 Check(Rules.Parse(verb + " a " + name + " near me")?.item == "piece_workbench", "Workbench placement alias " + verb + name);
 Check(Rules.Parse("don't " + verb + " a " + name) == null, "Workbench negation " + verb + name);
}
Check(Rules.Parse("can you build a working bench?")?.action == "craft_item", "Observed workbench request now builds");
Check(Rules.OrderClarification("build a house").Contains("PlanBuild"), "Unsupported house still explained");
Check(Rules.Parse("what do I need to build a workbench?") == null, "Workbench recipe question does not build");
Check(Rules.Parse("unsummon")?.action == "dismiss", "Dismiss speech command");
Check(Rules.Parse("go and don't gather wood") == null, "Go prefix never removes negation");
Check(!Rules.IsDirectOrder("what if you go gather wood"), "Hypothetical stays conversation");
Check(!Rules.IsDirectOrder("make me laugh"), "Casual talk remains conversational");
Check(Rules.Parse("clear crafting")?.action == "clear_crafting", "Clear crafting command");
Check(Rules.Parse("Rune, please clear crafting")?.action == "clear_crafting", "Addressed polite clear crafting");
Check(Rules.Parse("don't clear crafting") == null, "Negated clearing stays conversation");
Check(Rules.Parse("what if you clear crafting") == null, "Hypothetical clearing stays conversation");
Check(Rules.Parse("clear quests") == null, "Old clear quests phrase removed");
Check(Rules.Parse("craft a bronze axe")?.item == "bronze axe", "Craft article removed for exact recipe lookup");
Check(Rules.Parse("craft me a stone axe")?.item == "stone axe", "Personal crafting request");
Check(Rules.Parse("what materials do we need for a bronze axe") == null, "Recipe questions do not start work");
T WireRoundTrip<T>(T value) {
    using var stream = new MemoryStream(); var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(T));
    serializer.WriteObject(stream, value); stream.Position = 0; return (T)serializer.ReadObject(stream)!;
}
var catalog = WireRoundTrip(new RecipeBook { world = "test", recipes = new[] { new RecipeInfo { name = "Stone Axe", known = true, materials = new[] { new RecipeIngredient { name = "Wood", required = 5 } } } } });
Check(catalog.recipes[0].materials[0].required == 5 && catalog.recipes[0].known, "Game recipe catalog wire format");
var questState = WireRoundTrip(new CompanionState { quest = new CraftQuest { item = "Axe", status = "Blocked", materials = new[] { new MaterialNeed { name = "Wood", required = 5, carried = 2, stored = 1, missing = 2 } }, gather = new[] { new MaterialNeed { name = "Wood", missing = 2 } } } });
Check(questState.quest.materials[0].stored == 1 && questState.quest.gather[0].missing == 2, "Nested quest progress wire format");
Check(WireRoundTrip(new CompanionState()).quest == null, "Cleared quest survives wire format");
Check(Rules.Parse("Rune, gather twenty wood")?.action == "gather_wood", "Spoken gathering");
Check(Rules.Parse("Rune, gather twenty wood")?.amount == 20, "Spoken amount");
Check(Rules.Parse("could you get me 50 stones please")?.amount == 50, "Polite gathering");
Check(Rules.Parse("collect 0 stone")?.action == "invalid_amount", "No zero quantities");
Check(Rules.Parse("collect 99999999999999 wood")?.action == "invalid_amount", "No overflow");
Check(Rules.Parse("collect 101 wood")?.action == "invalid_amount", "Capacity limit");
Check(Rules.Parse("don't collect wood") == null, "Negated order");
Check(Rules.Parse("tell me about wood") == null, "Conversation is not an order");
Check(Rules.Parse("stop")?.action == "stay", "Stop remains immediate");
Check(Rules.Parse("return")?.action == "return", "Delivery command");
Check(Rules.Parse("collect one hundred stone")?.amount == 100, "Spoken hundred");
Check(Rules.Parse("could you please collect ten stone")?.amount == 10, "Repeated polite prefix");
Check(Rules.Parse("what if you gather 20 wood") == null, "Hypothetical remains conversation");
Check(Rules.Parse("Could you please find twelve stones for me?")?.amount == 12, "Twelve bypasses model inference");
Check(Rules.Parse("pick up all items")?.action == "pickup_all", "Continuous pickup");
Check(Rules.Parse("stop picking up all items")?.action == "stop_pickup", "Stop pickup independently");
Check(Rules.Parse("don't pick up resin")?.action == "exclude_item", "Explicit exclusion");
Check(Rules.Parse("don't pick up resin")?.item == "resin", "Exclusion item preserved");
Check(Rules.Parse("pick up resin again")?.action == "include_item", "Re-enable an item");
Check(Rules.Parse("exclude deer hide")?.item == "deer hide", "Multiword exclusion");
Check(Rules.Parse("collect 10 blueberries")?.action == "gather_item", "Named loot");
Check(Rules.Parse("sort my storage")?.action == "sort_storage", "Storage command");
Check(Rules.Parse("set base here")?.action == "set_base", "Base marker");
Check(Rules.Parse("manage my base")?.action == "manage_base", "Base shift");
Check(Rules.Parse("cook food")?.action == "cook_food", "Cooking");
Check(Rules.Parse("equip gear")?.action == "equip_gear", "Dwarf gear");
Check(Rules.Parse("gather materials for a boat")?.action == "gather_recipe", "Recipe plan");
Check(Rules.Parse("make a karve")?.action == "build_boat", "Boat build");
Check(Rules.Parse("craft bronze nails")?.action == "craft_item", "Learned recipe");
Check(Rules.Parse("make a task list for the base") == null, "Planning is not crafting");
Check(Rules.Parse("make me laugh") == null, "Conversation is not crafting");
Check(Rules.ValidatePlan(new[] { new PlanStep { action = "gather_wood", amount = 12 }, new PlanStep { action = "return" } }) == "", "Valid bounded plan");
Check(Rules.ValidatePlan(Array.Empty<PlanStep>()) != "", "No empty plan");
Check(Rules.ValidatePlan(new[] { new PlanStep { action = "run_plan" } }) != "", "No recursive plans");
Check(Rules.ValidatePlan(new[] { new PlanStep { action = "pickup_all" } }) != "", "No endless pickup step");
Check(Rules.ValidatePlan(new[] { new PlanStep { action = "craft_item", item = "" } }) != "", "Named crafting steps");
Check(Rules.ValidatePlan(Enumerable.Range(0, 9).Select(_ => new PlanStep { action = "return" }).ToArray()) != "", "Bounded step count");
var c = new Command { id = Guid.NewGuid().ToString(), world = "test", timestamp = 100, action = "gather_wood", amount = 20 };
Check(Rules.Validate(c, "test", 100) == "", "Valid request");
var profile = new Command { id = Guid.NewGuid().ToString(), world = "test", timestamp = 100, action = "update_profile", displayName = "Astrid Ironhand", gender = "female", combatStyle = "Balanced" };
Check(Rules.Validate(profile, "test", 100) == "", "Editable companion profile");
profile.companionId = "bot-a1b2c3d4"; profile.displayName = "Companion 4"; Check(Rules.Validate(profile, "test", 100) == "", "Dynamic numbered companion profile");
profile.action = "dismiss"; Check(Rules.Validate(profile, "test", 100) == "", "Validated companion removal");
profile.displayName = "<script>"; Check(Rules.Validate(profile, "test", 100) != "", "Safe companion name");
profile.displayName = "Astrid"; profile.gender = "unknown"; Check(Rules.Validate(profile, "test", 100) != "", "Voice gender validation");
profile.gender = "female"; profile.combatStyle = "reckless"; Check(Rules.Validate(profile, "test", 100) != "", "Combat style validation");
Check(Rules.Validate(c, "other", 100) != "", "World isolation");
Check(Rules.Validate(c, "test", 116) != "", "Expired request");
Check(Rules.Validate(c, "test", 90) != "", "Future timestamp");
c.action = "execute_code"; Check(Rules.Validate(c, "test", 100) != "", "Allowlisted actions");
c.action = "gather_stone"; c.amount = -1; Check(Rules.Validate(c, "test", 100) != "", "Negative quantity");
c.amount = 20; c.id = "bad"; Check(Rules.Validate(c, "test", 100) != "", "Request identity");
foreach (string phrase in new[] { "go get our items to make a stone axe.", "Hey, Rune, get our items to make a stone axe.", "gather materials to craft the stone axe", "get yourself materials for a stone axe" }) {
    var parsed = Rules.Parse(phrase);
    Check(parsed?.action == "gather_recipe" && parsed.item == "stone axe", "Session crafting request: " + phrase);
}
Check(Rules.Parse("Hey, Rune, go get yourself items to make a...") == null && Rules.OrderClarification("Hey, Rune, go get yourself items to make a...").Contains("Which item"), "Incomplete session request asks for recipe");
var equipment = Rules.Parse("pick up the stone axe and use it.");
Check(equipment?.action == "pickup_equip" && equipment.item == "stone axe" && equipment.amount == 1, "Session pickup and equip is one bounded action");
Check(Rules.Parse("get a stone axe then equip it")?.action == "pickup_equip", "Alternate equip wording");
Check(Rules.Parse("don't pick up the stone axe and use it")?.action != "pickup_equip", "Negated equipment request never equips");
Check(Rules.Parse("Hey, Rune, pick up the stone axe.")?.amount == 1, "Single equipment pickup does not search for twenty axes");
Check(Rules.Parse("pick up 3 stone axe")?.amount == 3, "Explicit pickup quantity preserved");
Check(Rules.Parse("gather loose stone")?.action == "gather_stone", "Loose stones use native resource pickup");
Check(Rules.Parse("get wood and then follow me") == null, "Compound task never becomes a literal item");
var invalidItem = new Command { id = Guid.NewGuid().ToString(), world = "test", timestamp = 100, action = "gather_item", item = "stone axe and use it" };
Check(Rules.Validate(invalidItem, "test", 100) != "", "Model and bridge reject sentence as item");
invalidItem.action = "pickup_equip"; invalidItem.item = "stone axe"; invalidItem.amount = 1;
Check(Rules.Validate(invalidItem, "test", 100) == "", "Equipment command accepted by protocol");
invalidItem.item = ""; Check(Rules.Validate(invalidItem, "test", 100) != "", "Equipment must name the item");
Check(Rules.ValidatePlan(new[] { new PlanStep { action = "pickup_equip", item = "stone axe", amount = 1 }, new PlanStep { action = "gather_wood", amount = 10 } }) == "", "Equipment can prepare a bounded gathering plan");
Check(Rules.Parse("craft yourself a stone axe")?.action == "craft_item" && Rules.Parse("craft yourself a stone axe")?.item == "stone axe", "Crafting for self uses retained-output flow");
Check(Rules.Parse("make yourself a stone axe")?.item == "stone axe", "Make self equipment request");
Check(Rules.OrderClarification("craft yourself a stone axe") == "", "No outdated self-crafting rejection");
Check(Rules.Parse("don't craft yourself a stone axe") == null, "Negated self crafting never acts");
foreach (var location in new[] { "on me", "at my location", "near my position", "on yourself", "at your position" }) {
    var bp = Rules.Parse("planbuild grand hall 2 " + location);
    Check(bp?.item == "grand hall 2" && bp.amount == 1 && bp.action == (location.Contains("your") ? "planbuild_self" : "planbuild_player"), "Blueprint anchor " + location);
}
Check(Rules.Parse("plan build cabin on me")?.action == "planbuild_player", "Split spoken PlanBuild");
Check(Rules.Parse("finish the plan")?.action == "finish_plan", "Finish native plan");
foreach (var order in new[] { "build this blueprint", "finish these plans", "complete the blueprint I placed", "build my blueprints nearby", "resume construction" })
    Check(Rules.Parse(order)?.action == "finish_plan" && Rules.IsDirectOrder(order), "Finish placed plans: " + order);
foreach (var order in new[] { "don't build this blueprint", "if I build this blueprint", "how do I build this blueprint?", "finish the blueprint tomorrow" })
    Check(Rules.Parse(order)?.action != "finish_plan", "Do not guess construction intent: " + order);
Check(Rules.Parse("planbuild cabin") == null && Rules.OrderClarification("planbuild cabin").Length > 0, "Missing blueprint reference clarifies");
Check(Rules.Parse("planbuild on me") == null, "Missing blueprint name");
Check(Rules.Parse("don't planbuild cabin on me") == null, "Negated blueprint does not dispatch");
Check(Rules.Parse("planbuild cabin over there") == null, "Unknown reference does not guess");
Console.WriteLine($"Passed {checks} command and protocol checks.");

RecorderChecks.Run();

CombatPolicyChecks.Run();
