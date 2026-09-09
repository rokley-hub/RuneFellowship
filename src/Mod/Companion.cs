using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion : MonoBehaviour
    {
        public static readonly List<Companion> Instances = new List<Companion>();
        private static readonly MethodInfo MoveMethod = AccessTools.Method(typeof(BaseAI), "MoveTo", new[] { typeof(float), typeof(Vector3), typeof(float), typeof(bool) });
        private static readonly MethodInfo SetTargetMethod = AccessTools.Method(typeof(MonsterAI), "SetTarget");
        private static readonly FieldInfo CreatureTargetField = AccessTools.Field(typeof(MonsterAI), "m_targetCreature");
        private static readonly FieldInfo StaticTargetField = AccessTools.Field(typeof(MonsterAI), "m_targetStatic");
        private void SetCombatTarget(Character enemy)
        {
            // Native SetTarget ignores null and cannot replace an existing creature target.
            // Clear the native fields explicitly, then let it initialize a new target normally.
            if (!enemy || ai.GetTargetCreature() != enemy) { CreatureTargetField.SetValue(ai, null); StaticTargetField.SetValue(ai, null); }
            if (enemy) SetTargetMethod.Invoke(ai, new object[] { enemy });
        }
        private static readonly MethodInfo PatrolMethod = AccessTools.Method(typeof(BaseAI), "SetPatrolPoint", new[] { typeof(Vector3) });
        private static readonly MethodInfo LookMethod = AccessTools.Method(typeof(BaseAI), "LookAt", new[] { typeof(Vector3) });
        private static readonly MethodInfo DropSaveMethod = AccessTools.Method(typeof(ItemDrop), "Save");
        public Humanoid Body;
        private MonsterAI ai;
        private ZNetView view;
        public long Owner;
        public string Id = "rune", Appearance = "skeleton", DisplayName = "Rune", Gender = "male", CombatStyle = "Cautious";
        public bool JoinBossFights = true;
        public int CargoCount => Body.GetInventory().GetAllItems().Where(IsCargo).Sum(i => i.m_stack);
        private static bool IsOwnedItem(ItemDrop.ItemData i) => !i.m_customData.ContainsKey("rune.starter");
        private static bool IsCargo(ItemDrop.ItemData i) => IsOwnedItem(i) && !ReservedSupply(i) && !i.m_customData.ContainsKey("rune.personal") && !i.m_customData.ContainsKey("rune.kept");
        private static bool IsStorable(ItemDrop.ItemData i) => IsOwnedItem(i) && !ReservedSupply(i) && !i.m_customData.ContainsKey("rune.personal") && !i.m_customData.ContainsKey("rune.loan") && !i.m_equipped;
        private string itemFilter = "";
        private int gathered;
        private bool autoPickup;
        private bool equipPickup;
        private readonly HashSet<string> pickupExclusions = new HashSet<string>();
        public bool Ready;
        private string mode = "follow";
        private string resource = "Wood";
        private int goal = 20;
        private Vector3 anchor;
        private ResourceTarget target;
        private float scanAt;
        private float orderTime;
        private float lastAttack;
        private float lastProgress;
        private float closestDistance = float.MaxValue;
        private float saveAt;
        private bool dropping;
        private bool followingOrder;
        private string orderFailure = "", searchFailure = "";
        public string Objective { get; private set; } = "";
        public bool LastOrderAccepted { get; private set; } = true;
        public bool LastOrderStoppedWork;
        public string ToolStatus => "Axe: " + ToolCondition(false) + ". Pickaxe: " + ToolCondition(true) + ". Kept items: " + string.Join(", ", Body.GetInventory().GetAllItems().Where(i => ReservedSupply(i) || i.m_customData.ContainsKey("rune.personal") || i.m_customData.ContainsKey("rune.kept")).Take(8).Select(i => Localization.instance.Localize(i.m_shared.m_name) + " x" + i.m_stack)) + ".";
        private readonly Dictionary<int, float> excluded = new Dictionary<int, float>();
        private string WorkTaskLabel => combatNote.Length > 0 ? combatNote : maintenanceNote.Length > 0 ? maintenanceNote : gatherRecoveryNote.Length > 0 ? gatherRecoveryNote : orderFailure.Length > 0 ? "Blocked: " + orderFailure : mode == "craft_plan" ? craftNote : IsBaseMode ? BaseTaskLabel : mode == "gather" ? "Gathering " + resource.ToLowerInvariant() + " (" + gathered + "/" + goal + ")" : mode == "return" ? "Returning materials and borrowed tools" : mode == "stay" ? "Holding position" : followingOrder ? "Following you" : "Following and defending";
        public string TaskLabel => safetyNote.Length > 0 ? safetyNote + " · " + WorkTaskLabel : WorkTaskLabel;
        public bool ObjectiveOngoing {
            get {
                if (Objective.Length == 0 || PlanLabel.StartsWith("Plan complete")) return false;
                if (PlanLabel.StartsWith("Plan blocked") || orderFailure.Length > 0) return true;
                if (DiagnosticAction == "run_plan") return activeStep != null;
                if (DiagnosticAction == "follow") return mode == "follow" && followingOrder;
                if (DiagnosticAction == "defend") return mode == "follow";
                if (DiagnosticAction == "stay") return mode == "stay";
                if (DiagnosticAction == "return") return mode == "return";
                if (DiagnosticAction == "pickup_all") return autoPickup;
                if (DiagnosticAction == "pickup_equip" || DiagnosticAction.StartsWith("gather_")) return mode == "gather" || mode == "return";
                if (DiagnosticAction is "craft_item" or "build_boat" or "planbuild_player" or "planbuild_self" or "finish_plan") return craftingQuest != null && (craftingQuest.status == "Active" || craftingQuest.status == "Blocked");
                if (DiagnosticAction is "store_cargo" or "sort_storage" or "cook_food" or "manage_base") return IsBaseMode;
                return false;
            }
        }
        public void SetObjective(string objective, string action, int amount, string item) { Objective = Rune.Shared.Rules.NormalizeObjective(objective, action, amount, item); }

        private void Awake()
        {
            Body = GetComponent<Humanoid>(); ai = GetComponent<MonsterAI>(); view = GetComponent<ZNetView>();
        }
        private System.Collections.IEnumerator Start()
        {
            // Humanoid.Start creates default equipment. Restore saved inventory after it finishes.
            yield return null;
            if (!view || !view.IsValid()) yield break;
            if (!Instances.Contains(this)) Instances.Add(this);
            Owner = view.GetZDO().GetLong("rune.owner", Owner);
            if (Owner == 0) yield break;
            Initialize();
        }
        public void Bind(Player player, string id = "rune", string appearance = "skeleton", string displayName = "Rune", string gender = "male", string combatStyle = "Cautious", bool joinBossFights = true, ReplacementState replacement = null)
        {
            Owner = player.GetPlayerID(); Id = id; Appearance = appearance; DisplayName = displayName; Gender = gender; CombatStyle = combatStyle; JoinBossFights = joinBossFights;
            view.GetZDO().Set("rune.id", Id); view.GetZDO().Set("rune.appearance", Appearance); view.GetZDO().Set("rune.name", DisplayName); view.GetZDO().Set("rune.gender", Gender); view.GetZDO().Set("rune.combatStyle", CombatStyle); view.GetZDO().Set("rune.joinBossFights", JoinBossFights);
            view.GetZDO().Set("rune.owner", Owner);
            if (replacement != null) { view.GetZDO().Set("rune.inventory", replacement.Inventory); view.GetZDO().Set("rune.hasBase", replacement.HasBase); view.GetZDO().Set("rune.base", replacement.Base); view.GetZDO().Set("rune.exclusions", replacement.Exclusions); view.GetZDO().Set("rune.mode", "stay"); view.GetZDO().Set("rune.anchor", replacement.Position); view.GetZDO().Set("rune.health", replacement.Health); }
            if (!Instances.Contains(this)) Instances.Add(this);
        }
        private void Initialize()
        {
            if (Ready) return;
            Id = view.GetZDO().GetString("rune.id", "rune"); Appearance = view.GetZDO().GetString("rune.appearance", "skeleton"); DisplayName = view.GetZDO().GetString("rune.name", Rune.Shared.Rules.Name(Id)); Gender = view.GetZDO().GetString("rune.gender", "male"); CombatStyle = view.GetZDO().GetString("rune.combatStyle", "Cautious"); JoinBossFights = view.GetZDO().GetBool("rune.joinBossFights", true);
            Body.m_name = DisplayName;
            Body.SetTamed(true);
            Body.m_faction = Character.Faction.Players;
            var saved = view.GetZDO().GetString("rune.inventory", "");
            if (saved.Length > 0) { Body.UnequipAllItems(); Body.GetInventory().Load(new ZPackage(saved)); }
            // Mark only the original starter axe. Existing saves migrate once.
            if (!view.GetZDO().GetBool("rune.starterTagged", false)) {
                foreach (var prefab in Body.m_defaultItems.Where(p => p && p.name != "AxeStone")) if (!Body.GetInventory().GetAllItems().Any(i => i.m_dropPrefab && i.m_dropPrefab.name == prefab.name)) Body.GetInventory().AddItem(prefab, 1);
                var starter = Body.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == "AxeStone" && !i.m_customData.ContainsKey("rune.loan") && !i.m_customData.ContainsKey("rune.personal") && !i.m_customData.ContainsKey("rune.kept"));
                if (starter != null) starter.m_customData["rune.starter"] = "1";
                foreach (var defaultItem in Body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab && Body.m_defaultItems.Any(d => d && d.name == i.m_dropPrefab.name) && !i.m_customData.ContainsKey("rune.loan") && !i.m_customData.ContainsKey("rune.personal") && !i.m_customData.ContainsKey("rune.kept"))) defaultItem.m_customData["rune.starter"] = "1";
                view.GetZDO().Set("rune.starterTagged", true);
            }
            LoadBase();
            UseStoredMaterials = view.GetZDO().GetBool("rune.useStoredMaterials", true);
            AllowCrafting = view.GetZDO().GetBool("rune.allowCrafting", true); AllowBaseWork = view.GetZDO().GetBool("rune.allowBaseWork", true);
            followingOrder = view.GetZDO().GetBool("rune.followingOrder", false);
            preferredWeapon = view.GetZDO().GetString("rune.preferredWeapon", "");
            orderFailure = view.GetZDO().GetString("rune.orderFailure", "");
            LoadCraftingQuest();
            LoadJob();
            foreach (var name in view.GetZDO().GetString("rune.exclusions", "").Split('|')) if (name.Length > 0) pickupExclusions.Add(name);
            autoPickup = false; // Automatic work requires a fresh order after reload.
            mode = view.GetZDO().GetString("rune.mode", "follow");
            // Interrupted harvesting resumes as a return, avoiding unattended harvesting on load.
            if (mode == "gather" || mode == "craft_plan" || IsBaseMode) mode = "return";
            anchor = view.GetZDO().GetVec3("rune.anchor", transform.position);
            Body.m_onDeath += OnDeath;
            Ready = true;
            float replacementHealth = view.GetZDO().GetFloat("rune.health", -1); if (replacementHealth > 0) { Body.SetHealth(Math.Min(Body.GetMaxHealth(), replacementHealth)); view.GetZDO().Set("rune.health", -1f); }
            if (Appearance == "dwarf") DwarfAppearance.SetGender(gameObject, Gender);
            EquipTool(false);
            Save();
        }
        public void Pause() { ResetCombatControl(); CancelMaintenance(); equipPickup = false; followingOrder = false; ClearPlan("Job paused • say resume task", true); autoPickup = false; craftPlan = null; SetQuestStatus("Paused", "Crafting paused. Ask to craft the item again to resume."); mode = "stay"; anchor = transform.position; target = null; ai.StopMoving(); Save(); }
        private Player Player => global::Player.m_localPlayer && global::Player.m_localPlayer.GetPlayerID() == Owner ? global::Player.m_localPlayer : null;
        public int Count(string prefab) => Body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab && i.m_dropPrefab.name == prefab).Sum(i => i.m_stack);
        public int ThreatCount() => Character.GetAllCharacters().Count(c => c && !c.IsDead() && c != Body && Vector3.Distance(c.transform.position, transform.position) < 12 && BaseAI.IsEnemy(Body, c));
        public string Order(string action, int amount, string item = "")
        {
            LastOrderAccepted = true; LastOrderStoppedWork = false;
            if (action == "equip_weapon" || action == "block" || action == "parry" || action == "shoot" || action == "focus_enemy" || action == "combat_auto") return SetCombatOrder(action, item);
            if (action != "status" && action != "exclude_item" && action != "include_item" && action != "equip_gear" && action != "lend_tools") ResetCombatControl();
            if (!AllowCrafting && IsCraftStep(new Rune.Shared.PlanStep { action = action })) { LastOrderAccepted = false; return "Crafting and building are disabled for this companion."; }
            if (!AllowBaseWork && (action == "sort_storage" || action == "store_cargo" || action == "cook_food" || action == "manage_base")) { LastOrderAccepted = false; return "Base work is disabled for this companion."; }
            if (action != "status" && action != "exclude_item" && action != "include_item") { CancelMaintenance(); safetyNote = ""; protectionTarget = null; equipmentCheckAt = 0; deliverKept = action == "return"; }
            if (action != "status" && action != "exclude_item" && action != "include_item") equipPickup = action == "pickup_equip";
            if (equipPickup && Appearance == "wolf") { equipPickup = false; LastOrderAccepted = false; return "A wolf cannot equip tools or armour. Ask me to pick up the item for you instead."; }
            if (action != "status" && action != "exclude_item" && action != "include_item") followingOrder = action == "follow";
            if (action != "status") { orderFailure = ""; ResetGatherRecovery(); }
            if (action == "follow") {
                ClearPlan("Job paused • say resume task", true); autoPickup = false; craftPlan = null; target = null; excluded.Clear();
                transferItem = null; sourceChest = destinationChest = null; fetched = false; skippedBaseTargets.Clear(); walkTarget = 0;
                SetQuestStatus("Paused", "Stopped to follow you. Ask to craft again to resume.");
                mode = "follow"; anchor = Player.transform.position; ai.StopMoving(); ai.ResetPatrolPoint();
                SetCombatTarget(null); ai.SetFollowTarget(Player.gameObject); Save();
                return "Stopped my previous job. Following you now; I kept everything I was carrying.";
            }
            if (action == "resume_task") return ResumeJob();
            if (action == "clear_crafting") return ClearCrafting();
            if (action == "status") return TaskLabel + ". Cargo for delivery: " + CargoCount + " items. " + ToolStatus + " Inventory slots: " + Body.GetInventory().NrOfItems() + "/32. Health " + Body.GetHealth().ToString("F0") + "/" + Body.GetMaxHealth().ToString("F0") + ".";
            if (!executingStep && action != "status" && action != "exclude_item" && action != "include_item") ClearPlan();
            if (action == "stop_pickup") { autoPickup = false; if (mode == "gather" && resource == "items") Pause(); Save(); return "Automatic pickup stopped. I kept everything already collected."; }
            if (action == "exclude_item" || action == "include_item") {
                string key = Normalize(item);
                if (key.Length == 0) return "Name an item to exclude or include.";
                if (action == "exclude_item") pickupExclusions.Add(key); else pickupExclusions.Remove(key);
                target = null; Save(); return action == "exclude_item" ? "I'll skip " + item + " during pickup from now on." : "I'll pick up " + item + " again.";
            }
            if (action == "equip_gear") return LendGear();
            if (action == "lend_tools") return Appearance == "wolf" ? "Wolves cannot use axes or pickaxes. I can collect loose materials." : LendTools();
            if (action == "planbuild_player" || action == "planbuild_self" || action == "finish_plan") return StartConstruction(action, item);
            if (action == "gather_recipe" || action == "craft_item" || action == "build_boat") return StartCraftPlan(action, item);
            if (craftPlan != null) SetQuestStatus("Paused", "Another order interrupted crafting. Ask to craft this again to resume.");
            craftPlan = null;
            autoPickup = action == "pickup_all";
            string baseReply = BaseOrder(action); if (baseReply != null) return baseReply;
            target = null; excluded.Clear(); ai.SetFollowTarget(null); SetCombatTarget(null);
            if (action.StartsWith("gather_") || action == "pickup_all" || equipPickup)
            {
                resource = action == "gather_wood" ? "Wood" : action == "gather_stone" ? "Stone" : "items";
                itemFilter = action == "gather_item" || equipPickup ? item : ""; goal = equipPickup ? 1 : amount; gathered = 0; mode = "gather";
                anchor = Player.transform.position; orderTime = Time.time; scanAt = 0;
                if (!autoPickup) {
                    target = FindTarget(); closestDistance = float.MaxValue; lastProgress = Time.time;
                    if (target == null) return RejectGather(searchFailure);
                }
                Save();
                if (equipPickup) return "I'll pick up one " + itemFilter + ", equip it if this body can use it, and keep it for work.";
                if (resource == "items") return "I'll pick up nearby " + (itemFilter.Length > 0 ? itemFilter : "loose items") + " and bring them to you. Up to " + goal + " items this trip.";
                return (resource == "Wood" && Appearance != "wolf" && GetTool(false) == null ? "I have no axe, so I will collect loose wood and punch wood sources my fists can damage. " : "") + "I'll gather up to " + goal + " " + resource.ToLowerInvariant() + " nearby and bring it back.";
            }
            mode = action == "stay" ? "stay" : action == "return" ? "return" : "follow";
            anchor = transform.position; Save();
            if (action == "defend") return ThreatCount() > 0 ? "Defending you. I will engage nearby enemies while following your combat settings." : "I will defend you and engage nearby enemies. There is no enemy within my combat range right now.";
            return mode == "stay" ? "Holding position. My previous task is stopped." : mode == "return" ? "Bringing back what I have, including your tools." : "Following you.";
        }
        public bool Tick(float dt)
        {
            if (!view.IsOwner()) return false;
            if (!Plugin.Solo || !Player || Player.IsDead() || Body.IsDead()) { ai.StopMoving(); return false; }
            diagnosticSafety = diagnosticCombat = false;
            if (Time.time - staminaSpentAt > 1.5f && !Body.InAttack() && !Body.IsBlocking()) CombatStamina = Mathf.Min(100, CombatStamina + 12 * dt);
            if (AvoidMovingHazards(dt)) { diagnosticSafety = true; return false; }
            AdvancePlan();
            if (Time.time > saveAt) { Save(); saveAt = Time.time + 2; }
            if (TerrainOrCombatSafety(dt)) { diagnosticSafety = true; CancelMaintenance(); return false; }
            // A manual follow order recalls the NPC instead of allowing native combat to keep chasing.
            if (followingOrder && mode == "follow") {
                SetCombatTarget(null); ai.SetFollowTarget(null); ai.ResetPatrolPoint();
                if (Vector3.Distance(transform.position, Player.transform.position) > 2.5f) Move(dt, Player.transform.position, 2); else ai.StopMoving();
                return false;
            }
            if (TacticalCombat(dt)) return false;
            // Native combat remains responsible for creature bodies without a player rig.
            if (Appearance != "dwarf" && protectionTarget) {
                diagnosticCombat = true; CancelMaintenance(); EquipCombat();
                ai.SetFollowTarget(Player.gameObject); SetCombatTarget(protectionTarget);
                return true;
            }
            if (Appearance != "dwarf" && ThreatCount() > 0)
            {
                diagnosticCombat = true;
                CancelMaintenance();
                EquipCombat();
                ai.SetFollowTarget(mode == "stay" ? null : Player.gameObject);
                if (mode == "stay") PatrolMethod.Invoke(ai, new object[] { anchor });
                return true;
            }
            SetCombatTarget(null);
            if (TickEquipmentCare(dt)) return false;
            if (mode == "follow") {
                ai.ResetPatrolPoint(); EquipCombat();
                if (Appearance == "dwarf") { ai.SetFollowTarget(null); if (Vector3.Distance(transform.position, Player.transform.position) > 2.5f) Move(dt, Player.transform.position, 2); else ai.StopMoving(); return false; }
                ai.SetFollowTarget(Player.gameObject); return true;
            }
            if (mode == "stay") { if (Vector3.Distance(transform.position, anchor) > 2) Move(dt, anchor, 2); else ai.StopMoving(); return false; }
            ai.SetFollowTarget(null);
            if (mode == "craft_plan") { TickCraftPlan(dt); return false; }
            if (IsBaseMode) { TickBase(dt); return false; }
            if (mode == "return")
            {
                if (Vector3.Distance(transform.position, Player.transform.position) > 3) { Move(dt, Player.transform.position, 2.5f); return false; }
                ai.StopMoving(); Deliver(); return false;
            }
            if (gathered < goal && (!HasGatherCapacity() || Time.time - orderTime > 180 || Vector3.Distance(Player.transform.position, anchor) > 65)) {
                StopGather(!HasGatherCapacity() ? "My inventory has no room for more of this material." : Time.time - orderTime > 180 ? "This gathering trip reached its three-minute limit." : "You moved too far from the gathering area."); return false;
            }
            if (gathered >= goal)
            {
                if (craftPlan != null) mode = "craft_plan"; else mode = "return"; target = null; Save(); return false;
            }
            if (target == null || !target.Valid())
            {
                target = null;
                if (Time.time < scanAt) { ai.StopMoving(); return false; }
                scanAt = Time.time + 2;
                target = FindTarget();
                closestDistance = float.MaxValue; lastProgress = Time.time;
                if (target == null)
                {
                    if (autoPickup) { anchor = Player.transform.position; if (CargoCount > 0) mode = "return"; else { Move(dt, Player.transform.position, 3); scanAt = Time.time + 3; } return false; }
                    if (WaitForGatherRecovery()) return false;
                    StopGather(searchFailure); return false;
                }
                gatherRecoveryNote = "";
            }
            if (target.IsHarvest && Protected(target.Position)) { ExcludeTarget(); return false; }
            Vector3 point = target.Approach(transform.position);
            float distance = Vector3.Distance(transform.position, point);
            if (distance < closestDistance - .25f) { closestDistance = distance; lastProgress = Time.time; }
            if (Time.time - lastProgress > (target.IsHarvest ? 40 : 12)) { ExcludeTarget(); return false; }
            float reach = target.StandingPoint.HasValue ? .25f : target.IsHarvest ? 1.7f : 1.8f;
            if (distance > reach) { Move(dt, point, Mathf.Max(.15f, reach - .2f)); return false; }
            if (target.StandingPoint.HasValue && Vector3.Distance(transform.position + Vector3.up * .5f, target.ContactPoint(transform.position)) > 1.8f) { ExcludeTarget(); return false; }
            ai.StopMoving(); LookMethod.Invoke(ai, new object[] { point + Vector3.up * .3f });
            if (target.Drop)
            {
                if (ExcludedItem(target.Drop.m_itemData)) { ExcludeTarget(); return false; }
                TakeDrop(target.Drop); target = null;
            }
            else if (target.Pickable)
            {
                target.Pickable.Interact(Body, false, false); target = null; scanAt = Time.time + .5f;
            }
            else
            {
                var tool = GetHarvestTool(NeedsPickaxe);
                if (tool != null && tool == GetTool(NeedsPickaxe)) EquipTool(NeedsPickaxe);
                else if (Body.GetCurrentWeapon() != null) Body.UnequipItem(Body.GetCurrentWeapon(), false);
                if (tool == null || tool.m_shared.m_toolTier < target.ToolTier || (tool.m_shared.m_useDurability && tool.m_durability <= 0)) { ExcludeTarget(); return false; }
                // Apply tool damage to the selected resource collider. Creature rigs do not
                // reliably provide player tool attack events; never damage nearby buildings.
                if (Time.time - lastAttack > 1.4f) {
                    var destructible = target.Component as IDestructible;
                    if (destructible == null) { ExcludeTarget(); return false; }
                    // Trigger a visible rig-compatible swing without running a second damage attack.
                    var animatedTool = Appearance == "dwarf" ? tool : Body.GetInventory().GetAllItems().FirstOrDefault(i => i.m_customData.ContainsKey("rune.starter") && i.m_shared.m_attack != null && !string.IsNullOrEmpty(i.m_shared.m_attack.m_attackAnimation) && i != tool);
                    if (animatedTool?.m_shared.m_attack != null && !string.IsNullOrEmpty(animatedTool.m_shared.m_attack.m_attackAnimation)) Body.GetZAnim().SetTrigger(animatedTool.m_shared.m_attack.m_attackAnimation);
                    var hit = new HitData { m_point = point, m_hitCollider = target.Collider, m_toolTier = (short)tool.m_shared.m_toolTier };
                    var damage = tool.GetDamage();
                    hit.m_damage = damage;
                    hit.SetAttacker(Body); destructible.Damage(hit);
                    if (tool.m_shared.m_useDurability) tool.m_durability = Math.Max(0, tool.m_durability - tool.m_shared.m_durabilityDrain);
                    lastAttack = Time.time; Save();
                }
            }
            return false;
        }
        private void Move(float dt, Vector3 point, float range)
        {
            MoveMethod.Invoke(ai, new object[] { dt, point, range, true });
        }
        private void ExcludeTarget()
        {
            if (target != null && target.Component) excluded[target.Component.GetInstanceID()] = Time.time + 60;
            target = null; scanAt = 0; ai.StopMoving();
        }
        private ResourceTarget FindTarget()
        {
            var candidates = new List<ResourceTarget>(); var seen = new HashSet<int>();
            bool protectedSource = false, unsafeSource = false, failedPath = false;
            var tool = GetHarvestTool(NeedsPickaxe);
            foreach (var collider in Physics.OverlapSphere(anchor, 35))
            {
                if (!collider || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
                ResourceTarget t = null;
                var drop = collider.GetComponentInParent<ItemDrop>();
                if (drop && drop.m_itemData.m_dropPrefab && Matches(drop.m_itemData) && !ExcludedItem(drop.m_itemData))
                    t = new ResourceTarget { Component = drop, Drop = drop, Collider = collider };
                if (t == null)
                {
                    var pickable = collider.GetComponentInParent<Pickable>();
                    if (pickable && pickable.m_itemPrefab && pickable.m_itemPrefab.GetComponent<ItemDrop>() && Matches(pickable.m_itemPrefab.GetComponent<ItemDrop>().m_itemData, pickable.m_itemPrefab.name) && !ExcludedItem(pickable.m_itemPrefab.GetComponent<ItemDrop>().m_itemData) && pickable.CanBePicked())
                        t = new ResourceTarget { Component = pickable, Pickable = pickable, Collider = collider };
                }
                if (t == null && Appearance != "wolf" && resource != "items" && tool != null && (!tool.m_shared.m_useDurability || tool.m_durability > 0))
                {
                    if (!NeedsPickaxe)
                    {
                        var tree = collider.GetComponentInParent<TreeBase>(); var log = collider.GetComponentInParent<TreeLog>();
                        var small = collider.GetComponentInParent<Destructible>(); var drops = small ? small.GetComponent<DropOnDestroyed>() : null;
                        if (tree && tree.m_minToolTier <= tool.m_shared.m_toolTier && (ContainsResource(tree.m_dropWhenDestroyed, resource) || LogContains(tree.m_logPrefab, resource, 0)))
                            t = new ResourceTarget { Component = tree, Collider = collider, ToolTier = tree.m_minToolTier };
                        else if (log && log.m_minToolTier <= tool.m_shared.m_toolTier && LogContains(log.gameObject, resource, 0))
                            t = new ResourceTarget { Component = log, Collider = collider, ToolTier = log.m_minToolTier };
                        else if (small && !small.GetComponent<Piece>() && drops && small.m_minToolTier <= tool.m_shared.m_toolTier && ContainsResource(drops.m_dropWhenDestroyed, resource))
                            t = new ResourceTarget { Component = small, Collider = collider, ToolTier = small.m_minToolTier };
                        if (t != null && !CanHarvestDamage(t.Component, tool)) t = null;
                    }
                    else
                    {
                        var rock = collider.GetComponentInParent<MineRock>(); var rock5 = collider.GetComponentInParent<MineRock5>();
                        if (rock && rock.m_minToolTier <= tool.m_shared.m_toolTier && (resource == "Stone" ? HasOnly(rock.m_dropItems, "Stone") : ContainsResource(rock.m_dropItems, resource)))
                            t = new ResourceTarget { Component = rock, Collider = collider, ToolTier = rock.m_minToolTier };
                        else if (rock5 && rock5.m_minToolTier <= tool.m_shared.m_toolTier && (resource == "Stone" ? HasOnly(rock5.m_dropItems, "Stone") : ContainsResource(rock5.m_dropItems, resource)))
                            t = new ResourceTarget { Component = rock5, Collider = collider, ToolTier = rock5.m_minToolTier };
                    }
                }
                if (t != null && !SafeGround(t.Position)) {
                    if (t.IsHarvest || !TrySafePickupPosition(t)) { unsafeSource = true; continue; }
                }
                if (t == null || !seen.Add(t.Component.GetInstanceID()) || !t.Valid()) continue;
                if (t.IsHarvest && Protected(t.Position)) { protectedSource = true; continue; }
                if (excluded.TryGetValue(t.Component.GetInstanceID(), out float until) && until > Time.time) { failedPath = true; continue; }
                candidates.Add(t);
            }
            string material = resource == "items" ? (itemFilter.Length > 0 ? itemFilter : "loose items") : resource.ToLowerInvariant();
            searchFailure = Appearance == "wolf" && resource != "items" ? "A wolf cannot use tools. I found no loose " + material + " to collect nearby."
                : GetTool(NeedsPickaxe) == null && resource != "items" ? "I don't have a usable " + (NeedsPickaxe ? "pickaxe" : "axe") + ", and I found no loose " + material + (NeedsPickaxe ? " nearby." : " or safe wood source I can punch nearby.") + " Come close and say lend tools."
                : protectedSource ? "The remaining " + material + " is within twelve metres of buildings, where I won't harvest. Move to an unbuilt area and ask again."
                : unsafeSource ? "The remaining " + material + " is on unsafe terrain. Move closer to a safe source and ask again."
                : failedPath ? "I could not reach the remaining " + material + ". Move near an accessible source and ask again."
                : "I found no reachable " + material + " within thirty-five metres that my current tools can gather. Move closer to a suitable source and ask again.";
            return candidates.OrderBy(t => (t.IsHarvest ? 100 : 0) + Vector3.Distance(transform.position, t.Approach(transform.position))).FirstOrDefault();
        }
        private static bool TrySafePickupPosition(ResourceTarget item)
        {
            // Reach from a dry nearby foothold; never treat a submerged item as a swimming order.
            for (int i = 0; i < 12; i++) {
                float angle = i * Mathf.PI / 6;
                var point = item.Position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.15f;
                if (!Heightmap.GetHeight(point, out float ground)) continue;
                point.y = ground + .1f;
                if (!SafeGround(point) || Vector3.Distance(point + Vector3.up * .5f, item.ContactPoint(point)) > 1.7f) continue;
                item.StandingPoint = point; return true;
            }
            return false;
        }
        private string RejectGather(string reason) { LastOrderAccepted = false; LastOrderStoppedWork = true; StopGather(reason, false); return reason + " No gathering order started."; }
        private void StopGather(string reason, bool announce = true)
        {
            if (craftPlan != null) { BlockCraft(reason); return; }
            ClearPlan("Plan blocked: " + reason); autoPickup = false; target = null; orderFailure = reason;
            mode = CargoCount > 0 && announce ? "return" : "stay"; anchor = transform.position;
            ai.SetFollowTarget(null); ai.StopMoving(); Save();
            if (announce) Say(DisplayName + ": " + reason + (CargoCount > 0 ? " Bringing back what I collected." : " I stopped without collecting anything."));
        }
        private bool NeedsPickaxe => resource != "Wood" && resource != "FineWood" && resource != "RoundLog";
        private static bool ContainsResource(DropTable table, string item) => table != null && table.m_drops.Any(d => d.m_item && d.m_item.name == item);
        private static bool LogContains(GameObject prefab, string item, int depth) { if (!prefab || depth > 3) return false; var log = prefab.GetComponent<TreeLog>(); return log && (ContainsResource(log.m_dropWhenDestroyed, item) || LogContains(log.m_subLogPrefab, item, depth + 1)); }
        private static bool HasOnly(DropTable table, string prefab) => table != null && table.m_drops.Count > 0 && table.m_drops.All(d => d.m_item && d.m_item.name == prefab);
        private static bool Protected(Vector3 position)
        {
            var pieces = new List<Piece>(); Piece.GetAllPiecesInRadius(position, 12, pieces);
            return pieces.Any(p => p && p.IsPlacedByPlayer());
        }
        private void TakeDrop(ItemDrop drop)
        {
            var nv = drop.GetComponent<ZNetView>();
            if (!nv || !nv.IsValid()) return;
            if (!nv.IsOwner() && Plugin.Solo) nv.ClaimOwnership();
            if (!nv.IsOwner() || !drop.CanPickup(false)) return;
            int take = Math.Min(drop.m_itemData.m_stack, Math.Max(0, goal - gathered));
            if (take <= 0) return;
            var copy = drop.m_itemData.Clone(); copy.m_stack = take;
            var inv = Body.GetInventory();
            int low = 0, high = take;
            while (low < high) { int middle = low + (high - low + 1) / 2; if (inv.CanAddItem(copy, middle)) low = middle; else high = middle - 1; }
            take = low;
            copy.m_stack = take;
            if (take == 0 || !inv.AddItem(copy)) { StopGather("There is no inventory space for more of this item. Free space, then ask me to resume task."); return; }
            drop.m_itemData.m_stack -= take; gathered += take;
            ResetGatherRecovery();
            if (drop.m_itemData.m_stack <= 0) ZNetScene.instance.Destroy(drop.gameObject); else DropSaveMethod.Invoke(drop, null);
            if (equipPickup) {
                if (Body.EquipItem(copy, false)) {
                    copy.m_customData["rune.personal"] = "1"; orderFailure = ""; mode = "follow";
                    Say(DisplayName + ": Equipped " + Localization.instance.Localize(copy.m_shared.m_name) + ". I'll keep it for work; returning cargo will not give it away.");
                } else StopGather("I picked up " + itemFilter + ", but this body cannot equip it.");
                equipPickup = false;
            }
            Save();
        }
        private bool ExcludedItem(ItemDrop.ItemData item) => pickupExclusions.Contains(Normalize(item.m_dropPrefab ? item.m_dropPrefab.name : "")) || pickupExclusions.Contains(Normalize(Localization.instance.Localize(item.m_shared.m_name)));
        private bool Matches(ItemDrop.ItemData item, string sourcePrefab = "") {
            string actualPrefab = sourcePrefab.Length > 0 ? sourcePrefab : item.m_dropPrefab ? item.m_dropPrefab.name : "";
            if (resource != "items") return Normalize(actualPrefab) == Normalize(resource);
            if (itemFilter.Length == 0) return true;
            string wanted = Normalize(itemFilter);
            return Normalize(actualPrefab) == wanted || Normalize(Localization.instance.Localize(item.m_shared.m_name)) == wanted;
        }
        private static string Normalize(string name) => new string(name.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray()).TrimEnd('s');
        private ItemDrop.ItemData GetTool(bool pickaxe)
        {
            return Body.GetInventory().GetAllItems()
                .Where(i => pickaxe ? i.GetDamage().m_pickaxe > 0 : i.GetDamage().m_chop > 0)
                .Where(i => !i.m_shared.m_useDurability || i.m_durability > 0)
                .OrderByDescending(i => i.m_shared.m_toolTier).ThenByDescending(i => i.GetDurabilityPercentage()).FirstOrDefault();
        }
        private ItemDrop.ItemData GetHarvestTool(bool pickaxe)
        {
            var tool = GetTool(pickaxe);
            if (tool != null || pickaxe || Appearance == "wolf") return tool;
            var playerPrefab = ZNetScene.instance.GetPrefab("Player");
            var fists = playerPrefab ? playerPrefab.GetComponent<Humanoid>().m_unarmedWeapon : null;
            return fists ? fists.m_itemData : null;
        }
        private static bool CanHarvestDamage(Component source, ItemDrop.ItemData tool)
        {
            var hit = new HitData { m_damage = tool.GetDamage() };
            var tree = source as TreeBase; var log = source as TreeLog; var small = source as Destructible;
            hit.ApplyResistance(tree ? tree.m_damageModifiers : log ? log.m_damages : small.m_damages, out var _);
            return hit.GetTotalDamage() > (small ? small.m_minDamageTreshold : 0);
        }
        private ItemDrop.ItemData EquipTool(bool pickaxe)
        {
            var item = GetTool(pickaxe);
            if (item != null && !Body.InAttack() && Body.GetCurrentWeapon() != item) Body.EquipItem(item, false);
            return item;
        }
        private string LendTools()
        {
            if (!Player || Vector3.Distance(transform.position, Player.transform.position) > 4) return "Come within four metres so I can take the tools.";
            int moved = 0;
            foreach (bool pick in new[] { false, true })
            {
                if (Body.GetInventory().GetAllItems().Any(i => i.m_customData.ContainsKey("rune.loan") && (!i.m_shared.m_useDurability || i.m_durability > 0) && (pick ? i.GetDamage().m_pickaxe > 0 : i.GetDamage().m_chop > 0))) continue;
                var tool = Player.GetInventory().GetAllItems().Where(i => pick ? i.GetDamage().m_pickaxe > 0 : i.GetDamage().m_chop > 0).Where(i => !i.m_shared.m_useDurability || i.m_durability > 0).OrderByDescending(i => i.m_shared.m_toolTier).ThenByDescending(i => i.GetDurabilityPercentage()).FirstOrDefault();
                if (tool == null) continue;
                var clone = tool.Clone(); clone.m_equipped = false; clone.m_customData["rune.loan"] = "1";
                if (!Body.GetInventory().CanAddItem(clone, clone.m_stack)) continue;
                Player.UnequipItem(tool, false);
                if (!Player.GetInventory().RemoveItem(tool)) continue;
                if (Body.GetInventory().AddItem(clone)) moved++; else Player.GetInventory().AddItem(tool);
            }
            Save();
            return moved > 0 ? "Borrowed " + moved + " tool(s). Say return when you want them back. I promise not to chew them." : "No spare tool transferred. Bring an axe or pickaxe close to me.";
        }
        private void Deliver()
        {
            RefreshSupplies();
            int wood = 0, stone = 0, tools = 0;
            foreach (var item in Body.GetInventory().GetAllItems().ToArray())
            {
                string prefab = item.m_dropPrefab ? item.m_dropPrefab.name : "";
                bool loan = item.m_customData.ContainsKey("rune.loan");
                if (ReservedSupply(item) || (!IsCargo(item) && !(deliverKept && item.m_customData.ContainsKey("rune.kept")))) continue;
                var copy = item.Clone(); copy.m_equipped = false; copy.m_customData.Remove("rune.loan"); copy.m_customData.Remove("rune.kept");
                if (!Player.GetInventory().CanAddItem(copy, copy.m_stack)) continue;
                Body.UnequipItem(item, false);
                if (!Body.GetInventory().RemoveItem(item)) continue;
                int transferredCount = copy.m_stack;
                if (!Player.GetInventory().AddItem(copy)) { Body.GetInventory().AddItem(item); continue; }
                if (prefab == "Wood") wood += transferredCount; else if (prefab == "Stone") stone += transferredCount; else tools += transferredCount;
            }
            int remainingDelivery = CargoCount + (deliverKept ? Body.GetInventory().GetAllItems().Where(i => i.m_customData.ContainsKey("rune.kept") && !ReservedSupply(i)).Sum(i => i.m_stack) : 0);
            mode = orderFailure.Length > 0 || remainingDelivery > 0 ? "stay" : "follow";
            if (autoPickup && CargoCount == 0) { mode = "gather"; resource = "items"; itemFilter = ""; goal = 100; gathered = 0; anchor = Player.transform.position; orderTime = Time.time; scanAt = Time.time + 2; }
            else if (autoPickup && CargoCount > 0) { autoPickup = false; Say("Automatic pickup paused: make room in your inventory, then ask again."); }
            Save();
            string supplies = string.Join(", ", Body.GetInventory().GetAllItems().Where(ReservedSupply).Take(4).Select(i => Localization.instance.Localize(i.m_shared.m_name) + " x" + i.m_stack));
            Say("Delivered " + wood + " wood, " + stone + " stone and " + tools + " other item(s)." + (supplies.Length > 0 ? " Keeping my working supplies: " + supplies + "." : "") + (orderFailure.Length > 0 ? " Stopped: " + orderFailure : remainingDelivery > 0 ? " Make inventory space and ask me to return again for the rest." : ""));
        }
        public bool UseStoredMaterials = true, AllowCrafting = true, AllowBaseWork = true;
        private void Save()
        {
            if (!Ready || !view || !view.IsValid() || !view.IsOwner()) return;
            var package = new ZPackage(); Body.GetInventory().Save(package);
            view.GetZDO().Set("rune.inventory", Convert.ToBase64String(package.GetArray()));
            SaveBase();
            SaveJob();
            view.GetZDO().Set("rune.useStoredMaterials", UseStoredMaterials);
            view.GetZDO().Set("rune.allowCrafting", AllowCrafting); view.GetZDO().Set("rune.allowBaseWork", AllowBaseWork);
            view.GetZDO().Set("rune.followingOrder", followingOrder);
            view.GetZDO().Set("rune.preferredWeapon", preferredWeapon);
            view.GetZDO().Set("rune.orderFailure", orderFailure);
            view.GetZDO().Set("rune.craftingQuest", craftingQuest == null ? "" : WireJson.Write(craftingQuest));
            view.GetZDO().Set("rune.exclusions", string.Join("|", pickupExclusions));
            view.GetZDO().Set("rune.mode", mode); view.GetZDO().Set("rune.anchor", anchor);
        }
        private void OnDeath()
        {
            if (dropping || !view.IsOwner()) return;
            dropping = true;
            foreach (var item in Body.GetInventory().GetAllItems().ToArray())
            {
                string prefab = item.m_dropPrefab ? item.m_dropPrefab.name : "";
                if (!IsOwnedItem(item)) continue;
                var copy = item.Clone(); copy.m_equipped = false; copy.m_customData.Remove("rune.loan"); copy.m_customData.Remove("rune.kept"); copy.m_customData.Remove("rune.personal");
                ItemDrop.DropItem(copy, copy.m_stack, transform.position + Vector3.up, Quaternion.identity);
                Body.GetInventory().RemoveItem(item);
            }
            Save(); Say(DisplayName + " fell. Their cargo and borrowed tools are on the ground where they died.");
        }
        private void OnDestroy() { Save(); Instances.Remove(this); }

        public bool HasBorrowedEquipment => Body.GetInventory().GetAllItems().Any(i => i.m_customData.ContainsKey("rune.loan"));
        public ReplacementState SnapshotForReplacement()
        {
            Save(); var transferable = new Inventory("rune-body-change", null, 8, 4);
            foreach (var item in Body.GetInventory().GetAllItems().Where(IsOwnedItem)) transferable.AddItem(item.Clone());
            var package = new ZPackage(); transferable.Save(package);
            return new ReplacementState { Inventory = Convert.ToBase64String(package.GetArray()), HasBase = HasBase, Base = basePoint, Exclusions = string.Join("|", pickupExclusions), Health = Body.GetHealth(), Position = transform.position };
        }
        public void UpdateIdentity(string displayName, string gender, string combatStyle, bool joinBossFights)
        {
            DisplayName = displayName; Gender = gender; CombatStyle = combatStyle; JoinBossFights = joinBossFights; Body.m_name = displayName; view.GetZDO().Set("rune.name", displayName); view.GetZDO().Set("rune.gender", gender); view.GetZDO().Set("rune.combatStyle", combatStyle); view.GetZDO().Set("rune.joinBossFights", joinBossFights); if (Appearance == "dwarf") DwarfAppearance.SetGender(gameObject, gender);
        }
        public void DropPersonalEquipment()
        {
            foreach (var item in Body.GetInventory().GetAllItems().Where(i => i.m_customData.ContainsKey("rune.personal") || i.m_customData.ContainsKey("rune.kept")).ToArray()) {
                var copy = item.Clone(); copy.m_equipped = false; copy.m_customData.Remove("rune.personal"); copy.m_customData.Remove("rune.kept");
                ItemDrop.DropItem(copy, copy.m_stack, transform.position + Vector3.up, Quaternion.identity);
                Body.UnequipItem(item, false); Body.GetInventory().RemoveItem(item);
            }
            Save();
        }
    }
    public sealed class ReplacementState { public string Inventory = "", Exclusions = ""; public bool HasBase; public Vector3 Base, Position; public float Health; }
    internal class ResourceTarget
    {
        public Component Component;
        public Collider Collider;
        public ItemDrop Drop;
        public Pickable Pickable;
        public int ToolTier;
        public Vector3? StandingPoint;
        public bool IsHarvest => !Drop && !Pickable;
        public Vector3 Position => Component.transform.position;
        public bool Valid() => Component && Component.gameObject.activeInHierarchy && Collider && Collider.enabled && (!Pickable || Pickable.CanBePicked());
        public Vector3 Approach(Vector3 from) => StandingPoint ?? ContactPoint(from);
        public Vector3 ContactPoint(Vector3 from)
        {
            Vector3 origin = from + Vector3.up * .5f;
            if (!(Collider is MeshCollider mesh) || mesh.convex) return Collider.ClosestPoint(origin);
            // Collider.Raycast supports non-convex meshes; never call ClosestPoint on them.
            Vector3 aim = Collider.bounds.center; aim.y = Mathf.Clamp(origin.y, Collider.bounds.min.y + .05f, Collider.bounds.max.y - .05f);
            Vector3 direction = aim - origin;
            if (direction.sqrMagnitude > .001f && Collider.Raycast(new Ray(origin, direction.normalized), out var hit, direction.magnitude + Collider.bounds.size.magnitude)) return hit.point;
            // A conservative centre fallback cannot make a distant tree seem within reach.
            return aim;
        }
    }
}




