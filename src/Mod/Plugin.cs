using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using Rune.Shared;
using UnityEngine;

namespace Rune.Mod
{
    [BepInPlugin(Guid, "Rune Companion", Rune.Shared.Release.Gameplay)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInDependency("marcopogo.PlanBuild", BepInDependency.DependencyFlags.SoftDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.rune.companion";
        public const string PrefabName = "RuneCompanionNPC";
        public static Plugin Instance;
        public static string BridgePath;
        private Harmony harmony;
        private float pollAt;
        private string lastId = "";
        private string lastError = "";
        private bool registered;
        private bool show;
        private Rect window = new Rect(25, 80, 490, 420);
        private bool resetControlsPosition;
        private bool controlsPositioned;
        private string controlCompanion = "rune";
        private int controlAppearance;
        private string input = "";
        public string NoteCompanion = "";
        public string Note = "Press F8 for Rune's controls. Use the voice app for push-to-talk or always-on chat.";
        public static bool Solo => ZNet.instance && ZNet.instance.IsServer() && !ZNet.instance.IsDedicated() && ZNet.instance.GetPeers().Count == 0;
        public static string World => ZNet.instance ? ZNet.instance.GetWorldUID().ToString() : "";
        public static Companion Current => Find("rune");
        public static Companion Find(string id) => Companion.Instances.FirstOrDefault(c => c && c.Id == id && c.Owner == (Player.m_localPlayer ? Player.m_localPlayer.GetPlayerID() : 0));
        public static string PrefabFor(string appearance) => appearance == "draugr" ? "RuneCompanionDraugr" : appearance == "elite" ? "RuneCompanionElite" : appearance == "dwarf" ? "RuneCompanionDwarf" : appearance == "wolf" ? "RuneCompanionWolf" : PrefabName;

        private void Awake()
        {
            Instance = this;
            InitializeOverlayPosition();
            BridgePath = Config.Bind("General", "BridgeFolder", Path.Combine(BepInEx.Paths.ConfigPath, "RuneBridge"), "Folder shared with Rune Voice.").Value;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RUNE_INTEGRATION_SMOKE"))) BridgePath = Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("RUNE_INTEGRATION_SMOKE")), "isolated-test-bridge");
            Directory.CreateDirectory(BridgePath);
            CreatureManager.OnVanillaCreaturesAvailable += RegisterCreature;
            harmony = new Harmony(Guid);
            harmony.PatchAll();
            Logger.LogInfo("Rune loaded. Solo prototype; F8 controls, voice app supports always-on chat.");
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RUNE_INTEGRATION_SMOKE"))) StartCoroutine(IntegrationSmoke.Run(this));
            if (Environment.GetEnvironmentVariable("RUNE_STARTUP_SMOKE") == "1") StartCoroutine(StartupSmoke());
        }
        private void RegisterCreature()
        {
            try
            {
                foreach (var skin in new[] { "skeleton", "draugr", "elite", "dwarf", "wolf" }) {
                var creature = new CustomCreature(PrefabFor(skin), skin == "wolf" ? "Wolf" : skin == "draugr" ? "Draugr" : skin == "elite" ? "Draugr_Elite" : "Skeleton_NoArcher", new CreatureConfig { Name = "Rune", Faction = Character.Faction.Players });
                var h = creature.Prefab.GetComponent<Humanoid>();
                var axe = PrefabManager.Instance.GetPrefab("AxeStone");
                if (!axe) throw new InvalidOperationException("AxeStone prefab unavailable.");
                h.m_name = "Rune";
                h.m_faction = Character.Faction.Players;
                h.m_health = 150;
                h.m_damageModifiers = new HitData.DamageModifiers();
                if (skin != "wolf") {
                    var nativeWeapon = h.m_defaultItems.Concat(h.m_randomWeapon).FirstOrDefault(g => g && g.GetComponent<ItemDrop>() && g.GetComponent<ItemDrop>().m_itemData.GetDamage().GetTotalDamage() > 0);
                    h.m_defaultItems = nativeWeapon && skin != "dwarf" ? new[] { axe, nativeWeapon } : new[] { axe };
                }
                h.m_randomWeapon = Array.Empty<GameObject>();
                h.m_randomArmor = Array.Empty<GameObject>();
                h.m_randomShield = Array.Empty<GameObject>();
                h.m_randomSets = Array.Empty<Humanoid.ItemSet>();
                h.m_randomItems = Array.Empty<Humanoid.RandomItem>();
                if (skin == "dwarf") DwarfAppearance.Configure(creature.Prefab);
                var ai = creature.Prefab.GetComponent<MonsterAI>();
                ai.m_attackPlayerObjects = false;
                // This is an inactive prefab, without a live ZNetView/ZDO.
                // SetDespawnInDay writes network state and can only run on a spawned creature.
                var despawnField = AccessTools.Field(typeof(MonsterAI), "m_despawnInDay");
                if (despawnField == null) throw new MissingFieldException("MonsterAI.m_despawnInDay is missing in this game version.");
                despawnField.SetValue(ai, false);
                ai.m_randomMoveRange = 0; ai.m_avoidWater = true; ai.m_avoidFire = true; ai.m_avoidLava = true;
                ai.m_viewRange = 18;
                ai.m_consumeItems = new List<ItemDrop>();
                var drops = creature.Prefab.GetComponent<CharacterDrop>();
                if (drops) drops.m_drops = new List<CharacterDrop.Drop>();
                creature.Prefab.GetComponent<ZNetView>().m_persistent = true;
                creature.Prefab.AddComponent<Companion>();
                if (!CreatureManager.Instance.AddCreature(creature)) throw new InvalidOperationException("Creature registration failed.");
                }
                registered = true;
                CreatureManager.OnVanillaCreaturesAvailable -= RegisterCreature;
                Logger.LogInfo("Rune companion prefab registered.");
            }
            catch (Exception e) { ReportError("Could not register Rune", e); }
        }
        private System.Collections.IEnumerator StartupSmoke()
        {
            yield return new WaitForSecondsRealtime(25);
            string output = Environment.GetEnvironmentVariable("RUNE_SMOKE_RESULT");
            if (!string.IsNullOrEmpty(output)) File.WriteAllText(output, "Plugin loaded; prefab registered=" + registered + "; last error=" + lastError);
            Application.Quit();
        }
        private void Update()
        {
            PollOverlayState();
            if (Player.m_localPlayer && OverlayKeyDown(overlay.overlayKey)) overlayVisible = !overlayVisible;
            if (Player.m_localPlayer && OverlayKeyDown(overlay.controlsKey)) { show = !show; if (!show) SaveOverlayPosition(); GUIManager.BlockInput(show); }
            if (!Player.m_localPlayer && show) { show = false; GUIManager.BlockInput(false); }
            if (Time.unscaledTime < pollAt) return;
            pollAt = Time.unscaledTime + .3f;
            try
            {
                WriteState();
                string path = Path.Combine(BridgePath, "command.json");
                if (!File.Exists(path) || new FileInfo(path).Length > 4096) return;
                var command = WireJson.Read<Command>(File.ReadAllText(path));
                if (command == null || command.id == lastId) return;
                lastId = command.id;
                var reply = Execute(command);
                AtomicWrite(Path.Combine(BridgePath, "reply.json"), WireJson.Write(reply));
                if (lastError.StartsWith("Bridge error", StringComparison.Ordinal)) lastError = "";
            }
            catch (IOException) { /* An atomic writer can briefly hold the destination. Retry next tick. */ }
            catch (Exception e) { ReportError("Bridge error", e); }
        }
        public Reply Execute(Command c)
        {
            NoteCompanion = c?.companionId ?? "";
            var reply = new Reply { id = c.id };
            string error = Rules.Validate(c, World, Rules.Now);
            if (!Solo || !Player.m_localPlayer || Player.m_localPlayer.IsDead()) error = "Open a solo world with a living character first.";
            if (error.Length > 0) { reply.message = error; return reply; }
            if (Find(c.companionId)) ApplyPermissions(Find(c.companionId), c);
            if (c.action == "summon")
            {
                var existing = Find(c.companionId);
                if (existing) { existing.UpdateIdentity(c.displayName, c.gender, c.combatStyle, c.joinBossFights); if (existing.Appearance != c.appearance) return ReplaceAppearance(existing, c, reply); reply.message = c.displayName + " is already here and their profile is up to date."; reply.accepted = true; return reply; }
                var zdos = new List<ZDO>(); int index = 0;
                foreach (string skin in new[] { "skeleton", "draugr", "elite", "dwarf", "wolf" }) {
                    index = 0; while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(PrefabFor(skin), zdos, ref index)) { }
                }
                var saved = zdos.FirstOrDefault(z => z.GetLong("rune.owner", 0) == Player.m_localPlayer.GetPlayerID() && z.GetString("rune.id", "rune") == c.companionId);
                if (saved != null) { saved.Set("rune.name", c.displayName); saved.Set("rune.gender", c.gender); reply.message = c.displayName + " already exists in this world near " + saved.GetPosition().ToString("F0") + ". Return there to meet them before changing body type."; return reply; }
                var prefab = ZNetScene.instance.GetPrefab(PrefabFor(c.appearance));
                if (!prefab) { reply.message = lastError.Length > 0 ? "Rune could not load during startup. " + lastError : "Rune's creature model has not loaded yet. Fully restart Valheim after installing an update."; return reply; }
                var go = Instantiate(prefab, Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * 3 + Vector3.up, Quaternion.identity);
                go.GetComponent<Companion>().Bind(Player.m_localPlayer, c.companionId, c.appearance, c.displayName, c.gender, c.combatStyle, c.joinBossFights);
                reply.accepted = true;
                reply.message = c.displayName + " reporting for duty. Ready when you are.";
            }
            else if (c.action == "dismiss") return DismissCompanion(c, reply);
            else if (!Find(c.companionId)) reply.message = "Summon " + c.displayName + " first, or return to where you left them.";
            else if (!Find(c.companionId).Ready) reply.message = "Still getting ready. Try that order again in a moment.";
            else if (c.action == "update_profile")
            {
                var existing = Find(c.companionId); existing.LastOrderStoppedWork = false; existing.UpdateIdentity(c.displayName, c.gender, c.combatStyle, c.joinBossFights);
                if (existing.Appearance != c.appearance) return ReplaceAppearance(existing, c, reply);
                reply.accepted = true; reply.message = c.displayName + "'s profile is now active in this world.";
            }
            else { var existing = Find(c.companionId); existing.LastOrderStoppedWork = false; existing.UpdateIdentity(c.displayName, c.gender, c.combatStyle, c.joinBossFights); reply.message = c.action == "run_plan" ? existing.QueuePlan(c.steps, c.objective) : existing.Order(c.action, c.amount, c.item); reply.accepted = existing.LastOrderAccepted; if (!reply.accepted && existing.LastOrderStoppedWork) { existing.DiagnosticCommand = c.id; existing.DiagnosticAction = c.action; existing.DiagnosticRejected = true; } if (reply.accepted && c.action != "status" && c.action != "exclude_item" && c.action != "include_item" && c.action != "set_base" && c.action != "lend_tools" && c.action != "equip_gear" && c.action != "equip_weapon" && c.action != "focus_enemy" && c.action != "combat_auto") { if (c.action != "resume_task") existing.SetObjective(c.objective, c.action, c.amount, c.item); existing.DiagnosticRejected = false; existing.DiagnosticCommand = c.id; existing.DiagnosticAction = c.action == "resume_task" ? "run_plan" : c.action; } }
            if (Find(c.companionId)) ApplyPermissions(Find(c.companionId), c);
            Logger.LogInfo("Order " + c.id + " / " + c.companionId + " / " + c.action + " / " + c.amount + ": " + reply.message);
            Note = reply.message;
            return reply;
        }
        private static void ApplyPermissions(Companion companion, Command command) {
            companion.UseStoredMaterials = command.useStoredMaterials;
            companion.AllowCrafting = command.allowCrafting;
            companion.AllowBaseWork = command.allowBaseWork;
        }
        private Reply ReplaceAppearance(Companion existing, Command command, Reply reply)
        {
            if (existing.HasBorrowedEquipment) { reply.message = "Tell " + existing.DisplayName + " to return borrowed gear before changing body type."; return reply; }
            var prefab = ZNetScene.instance.GetPrefab(PrefabFor(command.appearance));
            if (!prefab) { reply.message = "The " + command.appearance + " body has not loaded. Restart Valheim after installing the update."; return reply; }
            var snapshot = existing.SnapshotForReplacement(); var position = existing.transform.position; var rotation = existing.transform.rotation;
            var replacement = Instantiate(prefab, position, rotation); replacement.GetComponent<Companion>().Bind(Player.m_localPlayer, command.companionId, command.appearance, command.displayName, command.gender, command.combatStyle, command.joinBossFights, snapshot);
            ApplyPermissions(replacement.GetComponent<Companion>(), command);
            ZNetScene.instance.Destroy(existing.gameObject); reply.accepted = true; reply.message = command.displayName + " now has a " + command.appearance.Replace("elite", "elite draugr") + " body. Their active task was stopped."; Note = reply.message; return reply;
        }
        private Reply DismissCompanion(Command command, Reply reply)
        {
            var existing = Find(command.companionId);
            if (existing)
            {
                if (existing.CargoCount > 0 || existing.HasBorrowedEquipment) { reply.message = "Tell " + existing.DisplayName + " to return all cargo and borrowed gear before unsummoning them."; return reply; }
                string name = existing.DisplayName; existing.DropPersonalEquipment(); ZNetScene.instance.Destroy(existing.gameObject); reply.accepted = true; reply.message = name + " was unsummoned from this world. Any personal equipment was left on the ground."; Note = reply.message; return reply;
            }
            var zdos = new List<ZDO>(); int index = 0;
            foreach (string skin in new[] { "skeleton", "draugr", "elite", "dwarf", "wolf" }) { index = 0; while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(PrefabFor(skin), zdos, ref index)) { } }
            var saved = zdos.FirstOrDefault(z => z.GetLong("rune.owner", 0) == Player.m_localPlayer.GetPlayerID() && z.GetString("rune.id", "") == command.companionId);
            if (saved == null) { reply.accepted = true; reply.message = command.displayName + " is already unsummoned in this world."; return reply; }
            try {
                string encoded = saved.GetString("rune.inventory", ""); var inventory = new Inventory("rune-remove-check", null, 8, 4); if (encoded.Length > 0) inventory.Load(new ZPackage(Convert.FromBase64String(encoded)));
                if (inventory.GetAllItems().Any(i => !i.m_customData.ContainsKey("rune.starter"))) { reply.message = command.displayName + " is carrying items at " + saved.GetPosition().ToString("F0") + ". Return there and ask them to return before unsummoning."; return reply; }
            } catch { reply.message = "Could not safely inspect " + command.displayName + "'s saved inventory. Return to them before unsummoning."; return reply; }
            ZDOMan.instance.DestroyZDO(saved); reply.accepted = true; reply.message = command.displayName + " was unsummoned from this world. Any personal equipment was left on the ground."; Note = reply.message; return reply;
        }
        private void WriteState()
        {
            WriteRecipeCatalog();
            var c = Current;
            var state = new GameState {
                protocolVersion = Rune.Shared.Release.Protocol, gameVersion = Rune.Shared.Release.Gameplay,
                timestamp = Rules.Now, world = World, ready = Solo && Player.m_localPlayer && !Player.m_localPlayer.IsDead(), simulationPaused = Time.timeScale < .1f, companion = c,
                task = c ? c.TaskLabel : "Not summoned", health = c ? c.Body.GetHealth() : 0,
                playerHealth = Player.m_localPlayer ? Player.m_localPlayer.GetHealth() : 0,
                wood = c ? c.Count("Wood") : 0, stone = c ? c.Count("Stone") : 0, enemies = c ? c.ThreatCount() : 0, note = Note, noteCompanion = NoteCompanion, error = lastError,
                roster = Companion.Instances.Where(n => n && n.Ready && Player.m_localPlayer && n.Owner == Player.m_localPlayer.GetPlayerID()).Select(n => n.DiagnosticState()).ToArray()
            };
            AtomicWrite(Path.Combine(BridgePath, "state.json"), WireJson.Write(state));
        }
        public static void AtomicWrite(string path, string text)
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        public void Say(string text, string companionId = "") { NoteCompanion = companionId; Note = text; Logger.LogInfo(text); }
        public void ReportError(string context, Exception e)
        {
            string error = context + ": " + e.Message;
            if (error != lastError) { lastError = error; Logger.LogError(error + "\n" + e); NoteCompanion = ""; Note = error; }
        }
        private void OnGUI()
        {
            DrawOverlay();
            if (show && Player.m_localPlayer) {
                if (!controlsPositioned) { window.position = new Vector2(Mathf.Max(8, Screen.width - window.width - 24), 80); controlsPositioned = true; }
                window = KeepOnScreen(GUILayout.Window(778123, KeepOnScreen(window), DrawWindow, "RUNE FELLOWSHIP — companions & commands"));
                if (resetControlsPosition) { window.position = new Vector2(Mathf.Max(8, Screen.width - window.width - 24), 80); resetControlsPosition = false; }
            }
        }
        private void DrawWindow(int id)
        {
            GUILayout.Label("CHOOSE COMPANION");
            GUILayout.BeginHorizontal();
            var members = Companion.Instances.Where(c => c && c.Ready && Player.m_localPlayer && c.Owner == Player.m_localPlayer.GetPlayerID()).Select(c => c.Id).Distinct().ToList(); if (members.Count == 0) members.AddRange(new[] { "rune", "eira", "bjorn" }); if (!members.Contains(controlCompanion)) controlCompanion = members[0];
            foreach (string member in members) if (GUILayout.Button((controlCompanion == member ? "● " : "") + (Find(member) ? Find(member).DisplayName : Rules.Name(member)), GUILayout.Height(34))) controlCompanion = member;
            GUILayout.EndHorizontal();
            var selected = Find(controlCompanion);
            GUILayout.Label(selected ? selected.DisplayName + " · " + selected.Appearance + " · " + selected.Body.GetHealth().ToString("F0") + " HP\n" + selected.TaskLabel : "Choose an appearance before first summon:");
            controlAppearance = GUILayout.SelectionGrid(selected ? Array.IndexOf(new[] { "skeleton", "draugr", "elite", "dwarf", "wolf" }, selected.Appearance) : controlAppearance, new[] { "Skeleton", "Draugr", "Elite", "Dwarf", "Wolf" }, 5);
            GUILayout.Label(Note, new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUILayout.BeginHorizontal();
            foreach (string action in new[] { "summon", "follow", "stay", "return", "dismiss" }) if (GUILayout.Button(action == "dismiss" ? "Unsummon" : action)) LocalOrder(action);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Gather 20 wood")) LocalOrder("gather_wood");
            if (GUILayout.Button("Gather 20 stone")) LocalOrder("gather_stone");
            if (GUILayout.Button("Lend tools")) LocalOrder("lend_tools");
            GUILayout.EndHorizontal();
            input = GUILayout.TextField(input, 300);
            if (GUILayout.Button("Send command")) { var c = Rules.Parse(input); if (c == null) Note = "Use Rune Voice for conversation. Try: gather 20 wood."; else LocalOrder(c.action, c.amount, c.item); input = ""; }
            GUILayout.Label("Drag the Fellowship overlay by its header while these controls are open. F8 locks it in place.\nLend tools transfers one axe and one pickaxe. Say return to get them back.");
            if (GUILayout.Button("Reset UI positions")) { overlayPosition = new Vector2(-1, -1); overlayPositionDirty = true; SaveOverlayPosition(); resetControlsPosition = true; }
            if (GUILayout.Button("Close")) { show = false; SaveOverlayPosition(); GUIManager.BlockInput(false); }
            GUI.DragWindow(new Rect(0, 0, window.width, 24));
        }
        private void LocalOrder(string action, int amount = 20, string item = "")
        {
            var selected = Find(controlCompanion); var reply = Execute(new Command { id = System.Guid.NewGuid().ToString(), world = World, timestamp = Rules.Now, action = action, amount = amount, item = item, companionId = controlCompanion, displayName = selected ? selected.DisplayName : Rules.Name(controlCompanion), gender = selected ? selected.Gender : controlCompanion == "eira" ? "female" : "male", combatStyle = selected ? selected.CombatStyle : "Cautious", joinBossFights = selected ? selected.JoinBossFights : true, appearance = new[] { "skeleton", "draugr", "elite", "dwarf", "wolf" }[controlAppearance] });
            Note = reply.message;
        }
        private void OnDestroy() { RemoveCompanionPins(); if (show) GUIManager.BlockInput(false); harmony?.UnpatchSelf(); CreatureManager.OnVanillaCreaturesAvailable -= RegisterCreature; }
    }
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    public static class CompanionTick
    {
        public static bool Prefix(MonsterAI __instance, float dt, ref bool __result)
        {
            var c = __instance.GetComponent<Companion>();
            if (!c || !c.Ready) return true;
            try { if (c.Tick(dt)) return true; }
            catch (Exception e) { Plugin.Instance.ReportError("Rune paused", e); c.Pause(); }
            __result = true;
            return false;
        }
    }
}




