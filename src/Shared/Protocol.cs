using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Rune.Shared
{
    public static class Release
    {
        public const string Gameplay = "0.3.27", Desktop = "0.4.41";
        public const int Protocol = 2;
    }
    [Serializable] public class GameState
    {
        [System.Runtime.Serialization.OptionalField] public int protocolVersion;
        [System.Runtime.Serialization.OptionalField] public string gameVersion = "";
        public long timestamp;
        public string world = "";
        public bool ready;
        public bool simulationPaused;
        public bool companion;
        public string task = "Not summoned";
        public float health;
        public float playerHealth;
        public int wood;
        public int stone;
        public int enemies;
        public string note = "";
        public string noteCompanion = "";
        public string error = "";
        public CompanionState[] roster = Array.Empty<CompanionState>();
        public RecipeInfo[] recipes = Array.Empty<RecipeInfo>();
    }
    [Serializable] public class CompanionState
    {
        public string id = "rune", name = "Rune", appearance = "skeleton", task = "";
        public float health, maxHealth;
        public int cargo;
        public bool hasBase;
        public string plan = "", objective = "", next = "";
        public string tools = "";
        public int diagnosticsVersion;
        public string commandId = "", workPhase = "", workProgress = "", outcome = "";
        public float x, y, z;
        public bool safetyPaused, inCombat;
        public CraftQuest quest = null!;
    }
    [Serializable] public class RecipeBook { public long timestamp; public string world = ""; public RecipeInfo[] recipes = Array.Empty<RecipeInfo>(); }
    [Serializable] public class RecipeInfo {
        public string prefab = "", name = "", station = "";
        public bool known, anyIngredient;
        public int output = 1, stationLevel = 1;
        public RecipeIngredient[] materials = Array.Empty<RecipeIngredient>();
    }
    [Serializable] public class RecipeIngredient { public string prefab = "", name = ""; public int required; }
    [Serializable] public class MaterialNeed { public string prefab = "", name = ""; public int required, carried, stored, missing; }
    [Serializable] public class CraftQuest {
        public string item = "", action = "", status = "", note = "";
        public MaterialNeed[] materials = Array.Empty<MaterialNeed>();
        public MaterialNeed[] gather = Array.Empty<MaterialNeed>();
    }
    [Serializable] public class PlanStep { public string action = "status", item = ""; public int amount = 20; }
    [Serializable] public class PlanDocument { public string objective = ""; public PlanStep[] steps = Array.Empty<PlanStep>(); }
    [Serializable] public class Command
    {
        public string id = "";
        public string world = "";
        public long timestamp;
        public string action = "none";
        public int amount = 20;
        public string companionId = "rune";
        public string appearance = "skeleton";
        public string displayName = "Rune";
        public string gender = "male";
        public string combatStyle = "Cautious";
        public bool joinBossFights = true;
        public string item = "";
        [System.Runtime.Serialization.OptionalField] public string objective = "";
        [System.Runtime.Serialization.OptionalField] public bool useStoredMaterials = true;
        [System.Runtime.Serialization.OptionalField] public int protocolVersion = Release.Protocol;
        [System.Runtime.Serialization.OptionalField] public bool allowCrafting = true, allowBaseWork = true;
        public PlanStep[] steps = Array.Empty<PlanStep>();
    }
    [Serializable] public class Reply
    {
        public string id = "";
        public bool accepted;
        public string message = "";
        public string outcome = "";
    }
    [Serializable] public sealed class Capability
    {
        public string action = "", group = "", example = "", description = "";
        public bool plan, hidden;
    }
    public static class Rules
    {
        public static readonly string[] Appearances = { "skeleton", "draugr", "elite", "dwarf", "wolf", "direwolf" };
        public static bool IsWolf(string appearance) => appearance == "wolf" || appearance == "direwolf";
        public static bool CanChangeRiddenBody(string action, bool hasRider, string current, string next) =>
            !hasRider || !(action == "dismiss" || ((action == "summon" || action == "update_profile") && current != next));
        public static string BlueprintKey(string value) => Regex.Replace((value ?? "").ToLowerInvariant(), @"[\s_\-]+", "").Trim('"');
        public const int MaxAmount = 100;
        public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // One capability registry drives model grounding, validation, plans and the
        // Commands page. Natural wording is interpreted by the model; adding a game
        // ability does not require enumerating every phrase that might express it.
        public static readonly Capability[] Capabilities = {
            new Capability { action="equip_weapon", group="Combat & equipment", example="switch to club", description="Equip a named item already in the companion inventory. Never borrows player gear; checks body, durability and bow ammunition." },
            new Capability { action="block", group="Combat & equipment", example="hold your shield up", description="Dwarf holds a native guard with finite stamina. Requires usable blocking equipment." },
            new Capability { action="parry", group="Combat & equipment", example="parry their attacks", description="Dwarf attempts timed native parries against visible windups. Equipment, facing, stamina and timing can cause failure." },
            new Capability { action="cast", group="Combat & equipment", example="use elemental magic", description="Dwarf uses a carried offensive elemental projectile staff. Consumes carried eitr food and finite eitr. Summoning and support staves are not yet supported." },
            new Capability { action="shoot", group="Combat & equipment", example="shoot arrows", description="Dwarf uses a carried bow or crossbow with matching ammunition. Crossbows require timed stamina-consuming reloads. No player targeting." },
            new Capability { action="focus_enemy", group="Combat & equipment", example="focus on the skeleton", description="Prioritize a named nearby hostile creature while respecting terrain, boss permissions and safety." },
            new Capability { action="combat_auto", group="Combat & equipment", example="choose your weapons", description="Release a requested weapon or guard preference and resume automatic combat choices." },

            new Capability { action="summon", group="Movement & fellowship", example="join me", description="Summon the selected companion beside the player." },
            new Capability { action="update_profile", group="Internal", example="", description="Synchronize saved identity and combat settings.", hidden=true },
            new Capability { action="dismiss", group="Movement & fellowship", example="leave the fellowship", description="Remove this companion after their cargo and borrowed gear are returned." },
            new Capability { action="follow", group="Movement & fellowship", example="come with me", description="Immediately abort current work and recall the companion to follow the player." },
            new Capability { action="defend", group="Combat & equipment", example="watch my back", description="Follow and engage nearby hostile creatures using the selected combat policy." },
            new Capability { action="stay", group="Movement & fellowship", example="wait here", description="Abort current work and hold the current position." },
            new Capability { action="return", group="Movement & fellowship", example="bring your cargo back", description="Return to the player and deliver cargo and borrowed items; retain personal equipment.", plan=true },
            new Capability { action="gather_wood", group="Gathering & loot", example="find about 20 wood", description="Collect nearby loose wood, branches and reachable wood sources; use an axe when available or punch eligible small sources.", plan=true },
            new Capability { action="gather_stone", group="Gathering & loot", example="collect 10 stone", description="Collect nearby loose stone; mine eligible rocks only with a usable pickaxe.", plan=true },
            new Capability { action="status", group="Movement & fellowship", example="what are you working on?", description="Report current work, cargo, tools, occupied inventory slots and health." },
            new Capability { action="lend_tools", group="Combat & equipment", example="borrow my tools", description="Take one suitable axe and pickaxe from the nearby player as borrowed work tools." },
            new Capability { action="pickup_all", group="Gathering & loot", example="loot everything nearby", description="Continuously collect nearby loose items, except named exclusions, until stopped or full." },
            new Capability { action="pickup_equip", group="Combat & equipment", example="take that stone axe and use it", description="Pick up one named loose tool or armour item, equip it when possible and retain it as personal equipment.", plan=true },
            new Capability { action="gather_item", group="Gathering & loot", example="pick up 5 resin", description="Collect a bounded quantity of one named nearby loose item and return it to the player.", plan=true },
            new Capability { action="set_base", group="Base management", example="remember this as our base", description="Remember the player's current location as the companion's base work area." },
            new Capability { action="store_cargo", group="Base management", example="put your cargo in storage", description="Deposit carried cargo into accessible owner chests in the remembered base.", plan=true },
            new Capability { action="sort_storage", group="Base management", example="organise the chests", description="Sort items between accessible owner chests in the remembered base.", plan=true },
            new Capability { action="cook_food", group="Base management", example="prepare some food", description="Tend supported lit cooking stations and fueled ovens in the remembered base.", plan=true },
            new Capability { action="manage_base", group="Base management", example="take care of the base", description="Run one bounded base shift that cooks food and sorts storage.", plan=true },
            new Capability { action="stop_pickup", group="Gathering & loot", example="stop looting", description="Turn off continuous loose-item pickup without discarding collected items." },
            new Capability { action="exclude_item", group="Gathering & loot", example="leave resin alone", description="Add one named item to the continuous-pickup exclusion list." },
            new Capability { action="include_item", group="Gathering & loot", example="start collecting resin again", description="Remove one named item from the continuous-pickup exclusion list." },
            new Capability { action="equip_gear", group="Combat & equipment", example="use some of my armour", description="Borrow and equip suitable gear from the nearby player." },
            new Capability { action="craft_item", group="Crafting & task plans", example="make a bronze axe", description="For one learned inventory recipe, calculate missing materials, gather reachable inputs, use its required station and craft the item. Workbench requests place one workbench nearby.", plan=true },
            new Capability { action="gather_recipe", group="Crafting & task plans", example="get what you need for a bronze axe", description="Create a checklist for one known recipe and gather its missing ingredients without crafting the final item.", plan=true },
            new Capability { action="build_boat", group="Building & boats", example="build a karve", description="Gather materials and place one raft, karve or longship at a reachable valid shore; sailing is unavailable.", plan=true },
            new Capability { action="planbuild_player", group="Building & boats", example="planbuild cabin on me", description="Place and complete one exact saved PlanBuild design anchored at the player's order location.", plan=true },
            new Capability { action="planbuild_self", group="Building & boats", example="planbuild cabin on yourself", description="Place and complete one exact saved PlanBuild design anchored at the companion's order location.", plan=true },
            new Capability { action="finish_plan", group="Building & boats", example="finish the blueprints I placed", description="Supply materials to and finish accessible player-owned PlanBuild pieces within 25 metres.", plan=true },
            new Capability { action="resume_task", group="Crafting & task plans", example="resume task", description="Resume the saved unfinished job. Recheck current permissions, tools and access; construction continues existing pieces at the original site." },
            new Capability { action="clear_crafting", group="Crafting & task plans", example="clear crafting", description="Stop crafting-related work and clear its checklist while keeping collected items." }
        };
        public static readonly string[] Actions = Capabilities.Select(c => c.action).ToArray();
        public static bool IsAction(string action) => action == "run_plan" || Array.IndexOf(Actions, action) >= 0;
        public static readonly string[] PlanActions = Capabilities.Where(c => c.plan).Select(c => c.action).ToArray();
        public static string DescribeAction(string action, int amount = 20, string item = "")
        {
            string words = Regex.Replace(action ?? "", @"[_-]+", " ").Trim();
            if (words.Length == 0 || words == "none" || words == "status") return "";
            if (words.StartsWith("gather ") && amount > 0) words = "gather " + amount + " " + (item.Length > 0 ? item : words.Substring(7));
            else if (item.Length > 0) words += " " + item.Trim();
            return char.ToUpperInvariant(words[0]) + words.Substring(1);
        }
        public static string NormalizeObjective(string value, string action = "", int amount = 20, string item = "")
        {
            string text = Regex.Replace(value ?? "", @"[\r\n\t]+", " ");
            text = Regex.Replace(text, @"[`*_#>\[\]{}]", "");
            text = Regex.Replace(text, @"^\s*(?:goal|objective)\s*[:\-]\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"^\s*(?:I(?:'m| am) going to|I will|I'll)\s+", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s+", " ").Trim(' ', '"', '\'', '.', ',', ';', ':', '!', '?');
            if (text.Length == 0) text = DescribeAction(action, amount, item);
            if (text.Length > 96) {
                text = text.Substring(0, 96).TrimEnd(); int split = text.LastIndexOf(' ');
                if (split >= 64) text = text.Substring(0, split);
            }
            return text;
        }
        public static string ValidatePlan(PlanStep[] steps)
        {
            if (steps == null || steps.Length < 1 || steps.Length > 8) return "A plan must have one to eight steps.";
            foreach (var step in steps) {
                if (step == null || Array.IndexOf(PlanActions, step.action) < 0) return "That plan contains an unsupported step.";
                if (step.amount < 1 || step.amount > 100) return "Plan quantities must be between one and one hundred.";
                if ((step.action == "pickup_equip" || step.action.StartsWith("planbuild_") || step.action == "finish_plan") && step.amount != 1) return "Pick up and equip one item at a time.";
                if (step.item == null || step.item.Length > 64) return "A plan item name is invalid.";
                if (step.action == "gather_item" && LooksLikeTaskPhrase(step.item)) return "A plan item must be an item name, not a sentence.";
                if ((step.action == "pickup_equip" || step.action == "gather_item" || step.action == "gather_recipe" || step.action == "craft_item" || step.action == "build_boat" || step.action == "planbuild_player" || step.action == "planbuild_self") && string.IsNullOrWhiteSpace(step.item)) return "A plan step needs an item name.";
            }
            return "";
        }
        public static bool IsCompanion(string id) => !string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id, @"^[a-z][a-z0-9-]{2,39}$");
        public static string Name(string id) => id == "eira" ? "Eira" : id == "bjorn" ? "Bjorn" : "Rune";
        public static string Validate(Command command, string world, long now)
        {
            if (command == null || !Guid.TryParse(command.id, out _)) return "Invalid command identifier.";
            if (string.IsNullOrEmpty(world) || command.world != world) return "The world changed. Please give the order again.";
            if (now - command.timestamp > 15 || command.timestamp > now + 3) return "That order expired. Please repeat it.";
            if (!IsAction(command.action)) return "I cannot perform that action.";
            if (command.objective != null && command.objective.Length > 96) return "The task description is too long.";
            if (command.action == "run_plan") { string planError = ValidatePlan(command.steps); if (planError.Length > 0) return planError; }
            if (!IsCompanion(command.companionId)) return "Choose a valid companion profile.";
            if (!Appearances.Contains(command.appearance)) return "Choose an available appearance.";
            if (string.IsNullOrWhiteSpace(command.displayName) || command.displayName.Length > 24 || !Regex.IsMatch(command.displayName, @"^[\p{L}\p{M}][\p{L}\p{M}0-9' -]{0,23}$")) return "Choose a companion name using letters, numbers, spaces, apostrophes, or hyphens.";
            if (command.gender != "male" && command.gender != "female") return "Choose a male or female companion voice.";
            if (command.combatStyle != "Cautious" && command.combatStyle != "Balanced" && command.combatStyle != "Aggressive") return "Choose a cautious, balanced, or aggressive combat style.";
            if ((command.action == "pickup_equip" || command.action == "gather_item" || command.action == "exclude_item" || command.action == "include_item" || command.action == "craft_item" || command.action == "gather_recipe" || command.action == "build_boat" || command.action == "planbuild_player" || command.action == "planbuild_self") && (string.IsNullOrWhiteSpace(command.item) || command.item.Length > 64)) return "Name an item to collect.";
            if (command.action == "gather_item" && LooksLikeTaskPhrase(command.item)) return "That sounds like a task rather than an item name. Please give an item name or a crafting request.";
            if (command.amount < 1 || command.amount > MaxAmount) return "Ask for between 1 and 100 materials per trip.";
            if ((command.action == "pickup_equip" || command.action.StartsWith("planbuild_") || command.action == "finish_plan") && command.amount != 1) return "Pick up and equip one item at a time.";
            return "";
        }
        #nullable enable
        public static string NormalizeOrder(string input)
        {
            var s = Regex.Replace((input ?? "").ToLowerInvariant().Replace('’', '\''), @"[.!?,]", "").Trim();
            s = Regex.Replace(s, @"\s+", " ");
            s = Regex.Replace(s, @"^(?:(?:hey|a|ah|okay) )?(?:rune|roon|ruin)\s+", "");
            s = Regex.Replace(s, @"^(?:(?:please|can you|could you|would you|will you|go ahead and|go and|go|i want you to|i need you to)\s+)+", "");
            s = Regex.Replace(s, @"\s+please$", "");
            // Whole requests/prefixes only: never rewrite words inside a question or negation.
            switch (s) {
                case "stick with me": case "stay with me": case "keep up with me": case "come along": case "tag along": case "follow along": return "follow me";
                case "guard me": case "cover me": case "watch my back": case "help me fight": case "fight back": case "protect us": return "defend me";
                case "halt": case "hold here": case "hold your position": case "stay put": case "wait for me": case "cancel current task": case "abort current task": case "stop what you are doing": return "stay here";
                case "collect everything": case "gather everything": case "grab everything": case "pick everything up": case "loot everything": case "pick up everything": return "pick up all items";
                case "stop looting": case "stop collecting everything": case "stop auto collection": case "stop picking everything up": return "stop auto pickup";
                case "put away your cargo": case "stash your cargo": case "deposit your items": case "store your items": return "store cargo";
                case "deliver your cargo": case "bring me your items": case "give me your cargo": return "return";
                case "prepare food": case "cook a meal": case "cook meals": return "cook food";
                case "look after the base": case "take care of base": return "manage base";
                case "cancel crafting": case "clear the crafting list": case "reset the crafting list": return "clear crafting";
                case "report status": case "give me an update": case "what is your current task": return "status";
            }
            s = Regex.Replace(s, @"^(?:harvest|retrieve|grab|scavenge for) ", "gather ");
            s = Regex.Replace(s, @"^(?:organise|tidy up|organize|sort out) (?:(?:my|our|the) )?(storage|chests)$", "sort $1");
            s = Regex.Replace(s, @"^(?:skip|leave out) ", "exclude ");
            s = Regex.Replace(s, @"^stop pickup$", "stop auto pickup");
            return s;
        }
        public static bool IsDirectOrder(string input)
        {
            string s = NormalizeOrder(input);
            if (Regex.IsMatch(s, @"\b(don't|dont|do not|never|not|if|hypothetically|shouldn't)\b")) return false;
            if (IsCreativeConversation(s)) return false;
            if (Regex.IsMatch(s, @"^make (?:me|us) (?:laugh|smile|happy|a joke|a story)\b")) return false;
            if (Regex.IsMatch(s, @"^(?:(?:finish|complete|resume)(?: the| this| these| my| our| nearby)? (?:plan|plans|blueprint|blueprints|construction)\b|plan ?build\b)")) return true;
            if (Regex.IsMatch(s, @"^(?:resume|continue)(?: my| the| our)? (?:task|job|work)$")) return true;
            return Regex.IsMatch(s, @"^(?:fight|defend|protect|attack|gather|collect|fetch|get|bring|chop|cut|punch|mine|find|pick|set|sort|organize|tidy|store|deposit|put|cook|tend|manage|equip|craft|clear|reset|cancel|build|make|create|follow|wait|stay|return|stop|come|lend|take|summon|unsummon|dismiss)\b");
        }
        public static string OrderClarification(string input)
        {
            string s = NormalizeOrder(input);
            if (Regex.IsMatch(s, @"^plan ?build(?: |$)") && !Regex.IsMatch(s, @"^plan ?build [\p{L}0-9][\p{L}0-9 '""_-]{0,63}? (?:on|at|near) (?:me|my location|my position|yourself|you|your location|your position)$"))
                return "Name a saved PlanBuild blueprint and its location, for example: planbuild cabin on me, or planbuild cabin on yourself. No order was started.";
            if (!IsDirectOrder(s)) return "";
            if (Regex.IsMatch(s, @"^(?:gather|collect|fetch|get)(?: me| us| yourself)?(?: the| our| my| some)? (?:items|materials|ingredients|resources) (?:for|to (?:make|craft|build|create))(?: a| an| the)?$"))
                return "Which item should I gather materials for? Your request ended before the item name. No order was started.";
            if (Regex.IsMatch(s, @"^(?:make|craft|build|create)(?: (?:me|us|yourself))? (?:a |an |some )?(?:weapon|weapons|armor|armour|gear|equipment|tool|tools)$"))
                return "No crafting order started yet: name the item you want, for example craft a club or craft a stone axe. I can check its recipe and gather the missing materials.";
            if (Regex.IsMatch(s, @"^(?:make|craft|build|create)(?: (?:me|us))? (?:a |an |the )?(?:house|wall|roof|base)$"))
                return "Use a saved PlanBuild design: planbuild cabin on me, or planbuild cabin on yourself. Replace cabin with its saved blueprint name. No building order was started.";
            return "";
        }
        public static Command? Parse(string input)
        {
            var s = NormalizeOrder(input);
            if (Regex.IsMatch(s, @"^(stop (picking up|collecting)( all)?( items)?|stop auto ?pickup)$")) return new Command { action = "stop_pickup" };
            var exclusion = Regex.Match(s, @"^(?:exclude|ignore|don't pick up|dont pick up|do not pick up|stop picking up) (?<item>[a-z][a-z ]{0,63})$");
            if (exclusion.Success) return new Command { action = "exclude_item", item = exclusion.Groups["item"].Value };
            var inclusion = Regex.Match(s, @"^(?:include|allow|pick up) (?<item>[a-z][a-z ]{0,63}?)(?: again)$|^(?:include|allow) (?<item>[a-z][a-z ]{0,63})$");
            if (inclusion.Success) return new Command { action = "include_item", item = inclusion.Groups["item"].Value };
            if (Regex.IsMatch(s, @"\b(don't|dont|do not|never|shouldn't|not|if|hypothetically)\b")) return null;
            if (OrderClarification(s).Length > 0 || IsCreativeConversation(s)) return null;
            if (Regex.IsMatch(s, @"^(?:resume|continue)(?: my| the| our)? (?:task|job|work)$")) return new Command { action = "resume_task" };
            if (Regex.IsMatch(s, @"^(?:clear|reset|cancel)(?: all| my| the| our)? crafting$")) return new Command { action = "clear_crafting" };
            var blueprint = Regex.Match(s, @"^plan ?build (?<name>[\p{L}0-9][\p{L}0-9 '""_-]{0,63}?) (?:on|at|near) (?<where>me|my location|my position|yourself|you|your location|your position)$");
            if (blueprint.Success) return new Command { action = blueprint.Groups["where"].Value.StartsWith("m") ? "planbuild_player" : "planbuild_self", item = blueprint.Groups["name"].Value.Trim('"'), amount = 1 };
            if (Regex.IsMatch(s, @"^(?:finish|complete|resume|build)(?: the| this| these| my| our| nearby)? (?:plan|plans|blueprint|blueprints|construction)(?: (?:here|nearby|I placed|i placed|I put down|i put down))?$")) return new Command { action = "finish_plan", amount = 1 };
            if (Regex.IsMatch(s, @"\b(task ?list|plan)\b") || Regex.IsMatch(s, @"^make (me|us) (laugh|smile|happy|a joke|a story)\b")) return null;
            if (Regex.IsMatch(s, @"^(?:block|hold (?:your |the )?(?:shield|guard)(?: up)?|raise (?:your |the )?shield)$")) return new Command { action = "block" };
            if (Regex.IsMatch(s, @"^(?:parry|parry (?:their |the |incoming )?attacks)$")) return new Command { action = "parry" };
            if (Regex.IsMatch(s, @"^(?:cast|cast spells|use magic|use elemental magic|cast elemental spells)$")) return new Command { action = "cast" };
            if (Regex.IsMatch(s, @"^(?:shoot|shoot bolts|fire bolts|shoot crossbow|shoot arrows|fire arrows|shoot (?:at )?(?:the )?enemies)$")) return new Command { action = "shoot" };
            if (Regex.IsMatch(s, @"^(?:stop blocking|lower (?:your )?shield|choose your weapons|automatic combat)$")) return new Command { action = "combat_auto" };
            var focus = Regex.Match(s, @"^(?:focus on|target|attack) (?:the |a )?(?<enemy>[a-z][a-z ]{0,40})$");
            if (focus.Success) return new Command { action = "focus_enemy", item = focus.Groups["enemy"].Value };
            var equip = Regex.Match(s, @"^(?:equip|switch to|use) (?:your |the |a )?(?<item>[a-z][a-z ]{0,40})$");
            if (equip.Success && !Regex.IsMatch(equip.Groups["item"].Value, @"^(?:my |gear$|tools$)")) return new Command { action = "equip_weapon", item = equip.Groups["item"].Value };
            if (Regex.IsMatch(s, @"^(equip gear|equip my gear|take my gear|borrow my gear)$")) return new Command { action = "equip_gear" };
            var useItem = Regex.Match(s, @"^(?:pick up|take|get)(?: the| a| an)? (?<item>[a-z][a-z ]{0,63}?) (?:and|then|and then) (?:use|equip|keep)(?: it| that)?(?: for yourself)?$");
            if (useItem.Success) return new Command { action = "pickup_equip", item = useItem.Groups["item"].Value, amount = 1 };
            var recipe = Regex.Match(s, @"^(?:gather|collect|fetch|get)(?: me| us| yourself)?(?: the| our| my| some)? (?:items|materials|ingredients|resources) (?:for|to (?:make|craft|build|create)) (?:a |an |the )?(?<item>[a-z][a-z ]{0,63})$");
            if (recipe.Success) return new Command { action = "gather_recipe", item = recipe.Groups["item"].Value };
            if (Regex.IsMatch(s, @"^(?:craft|create|make|build|place|construct|put down) (?:me |us |yourself )?(?:a |an |the )?(?:workbench|work bench|working bench)(?: here| near me| nearby| near my location| next to me)?$")) return new Command { action = "craft_item", item = "piece_workbench", amount = 1 };
            var boat = Regex.Match(s, @"^(?:build|make|craft|create) (?:a |the )?(?<item>boat|raft|karve|longship)$");
            if (boat.Success) return new Command { action = "build_boat", item = boat.Groups["item"].Value };
            var craft = Regex.Match(s, @"^(?:craft|create|make|build) (?:me |us |yourself )?(?:a |an |the )?(?<item>[a-z][a-z ]{0,63})$");
            if (craft.Success) return new Command { action = "craft_item", item = craft.Groups["item"].Value };
            if (Regex.IsMatch(s, @"^(pick up|collect|gather)( all)? (items|everything|loot|nearby items)$")) return new Command { action = "pickup_all", amount = 100 };
            if (Regex.IsMatch(s, @"^(set base|set base here|this is (our|my) base|remember this base)$")) return new Command { action = "set_base" };
            if (Regex.IsMatch(s, @"^(sort|organize|tidy)( (my|our|the))? (storage|chests)$")) return new Command { action = "sort_storage" };
            if (Regex.IsMatch(s, @"^(store cargo|put (it|everything) away|store (the )?items|deposit cargo)$")) return new Command { action = "store_cargo" };
            if (Regex.IsMatch(s, @"^(cook|cook food|cook some food|tend (the )?(oven|cooking stations))$")) return new Command { action = "cook_food" };
            if (Regex.IsMatch(s, @"^(manage base|manage (the|our|my) base|take care of (the|our|my) base)$")) return new Command { action = "manage_base" };
            if (Regex.IsMatch(s, @"^(stop|stop working|cancel|stay|stay here|wait|wait here|hold position)$")) return new Command { action = "stay" };
            if (Regex.IsMatch(s, @"^(?:fight|attack)(?: (?:the |nearby )?enemies)?$|^(?:defend|protect)(?: me| us| yourself)?$|^help me (?:fight|defend)$")) return new Command { action = "defend" };
            if (Regex.IsMatch(s, @"^(follow|follow me|come with me|let's go|lets go)$")) return new Command { action = "follow" };
            if (Regex.IsMatch(s, @"^(return|come back|come here|bring it back|bring them back|return materials)$")) return new Command { action = "return" };
            if (Regex.IsMatch(s, @"^(summon|summon rune|spawn companion|join me)$")) return new Command { action = "summon" };
            if (Regex.IsMatch(s, @"^(unsummon|dismiss)(?: companion| yourself)?$")) return new Command { action = "dismiss" };
            if (Regex.IsMatch(s, @"^(status|what are you doing|how much are you carrying)$")) return new Command { action = "status" };
            if (Regex.IsMatch(s, @"^(lend tools|take my tools|borrow my tools)$")) return new Command { action = "lend_tools" };
            s = Regex.Replace(s, @"\b(a hundred|one hundred|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety)\b", m => WordNumber(m.Value));
            if (Regex.IsMatch(s, @"^(?:punch|chop|cut)(?: down)?(?: some)? trees$")) return new Command { action = "gather_wood", amount = 20 };
            var gather = Regex.Match(s, @"^(?:gather|collect|fetch|get|bring|chop(?: down)?|cut(?: down)?|mine|find)(?: me| us| her)?(?: some| a| an| the| our| my)?(?: (?<amount>\d+))? (?<resource>wood|sticks|branches|stone|stones)(?: please| for me| for us)?$");
            if (!gather.Success) {
                var item = Regex.Match(s, @"^(?:pick up|collect|fetch|get|gather)(?: me)?(?: some)?(?: (?<amount>\d+))? (?<item>[a-z][a-z ]{0,63}?)(?: please| for me)?$");
                if (!item.Success) return null;
                if (LooksLikeTaskPhrase(item.Groups["item"].Value)) return null;
                int count = Regex.IsMatch(s, @"^pick up (?:the|a|an) ") && !item.Groups["item"].Value.EndsWith("s") ? 1 : 20;
                if (item.Groups["amount"].Success && (!int.TryParse(item.Groups["amount"].Value, out count) || count < 1 || count > MaxAmount)) return new Command { action = "invalid_amount", amount = 0 };
                var command = new Command { action = "gather_item", amount = count, item = item.Groups["item"].Value }; NormalizeGatherCommand(command); return command;
            }
            int amount = 20;
            if (gather.Groups["amount"].Success && (!int.TryParse(gather.Groups["amount"].Value, out amount) || amount < 1 || amount > MaxAmount))
                return new Command { action = "invalid_amount", amount = 0 };
            return new Command { action = (gather.Groups["resource"].Value == "wood" || gather.Groups["resource"].Value == "sticks" || gather.Groups["resource"].Value == "branches") ? "gather_wood" : "gather_stone", amount = amount };
        }
        public static void NormalizeGatherCommand(Command command)
        {
            if (command.action != "gather_item") return;
            string item = Regex.Replace(command.item.ToLowerInvariant().Trim(), @"^(?:(?:a|an|the|our|my|some|loose)\s+)+", "");
            if (item == "wood" || item == "sticks" || item == "branches") { command.action = "gather_wood"; command.item = ""; }
            else if (item == "stone" || item == "stones") { command.action = "gather_stone"; command.item = ""; }
            else command.item = item;
        }
        private static bool IsCreativeConversation(string s) => Regex.IsMatch(s, @"^(?:craft|create|make|build) (?:me |us )?(?:a |an |the )?(?:joke|story|poem|song|laugh|smile|happy|friendship|conversation)\b");
        public static bool LooksLikeTaskPhrase(string item) => Regex.IsMatch(item ?? "", @"\b(and|then|yourself|to make|to craft|to build|to create)\b", RegexOptions.IgnoreCase);
        private static string WordNumber(string n)
        {
            switch (n) { case "two": return "2"; case "three": return "3"; case "four": return "4"; case "six": return "6"; case "seven": return "7"; case "eight": return "8"; case "nine": return "9"; case "eleven": return "11"; case "twelve": return "12"; case "thirteen": return "13"; case "fourteen": return "14"; case "fifteen": return "15"; case "sixteen": return "16"; case "seventeen": return "17"; case "eighteen": return "18"; case "nineteen": return "19"; case "one": return "1"; case "five": return "5"; case "ten": return "10"; case "twenty": return "20"; case "thirty": return "30"; case "forty": return "40"; case "fifty": return "50"; case "sixty": return "60"; case "seventy": return "70"; case "eighty": return "80"; case "ninety": return "90"; default: return "100"; }
        }
    }
}



