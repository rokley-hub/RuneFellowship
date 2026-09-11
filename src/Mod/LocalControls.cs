using System;
using System.Linq;
using BepInEx.Configuration;
using Jotunn.Managers;
using Rune.Shared;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Plugin
    {
        private bool localControlsVisible;
        private Rect localWindow = new Rect(24, 80, 490, 510);
        private Vector2 localScroll;
        private ConfigEntry<string> localControlsKey;
        private readonly ConfigEntry<string>[] localNames = new ConfigEntry<string>[LocalCompanions.SlotCount];
        private readonly ConfigEntry<string>[] localBodies = new ConfigEntry<string>[LocalCompanions.SlotCount];
        private readonly ConfigEntry<string>[] localGenders = new ConfigEntry<string>[LocalCompanions.SlotCount];
        private string localSelection = LocalCompanions.SlotId(0);
        private Command localDraft = LocalCompanions.DefaultProfile(0);
        private string localMessage = "Choose a companion and select Summon. No desktop app is required.";
        private GUIStyle localText;
        private static readonly string[] BodyLabels = { "Skeleton", "Draugr", "Elite", "Dwarf", "Wolf", "Direwolf" };

        private void InitializeLocalControls()
        {
            localControlsKey = Config.Bind("Interface", "CompanionControlsKey", "F6", "Open basic in-game companion setup and orders. Voice and AI dialogue are optional desktop features.");
            for (int i = 0; i < LocalCompanions.SlotCount; i++) {
                var profile = LocalCompanions.DefaultProfile(i);
                string section = "In-game companion " + (i + 1);
                localNames[i] = Config.Bind(section, "Name", profile.displayName, "Companion name, saved when summoned from the in-game menu.");
                localBodies[i] = Config.Bind(section, "Appearance", profile.appearance, "skeleton, draugr, elite, dwarf, wolf or direwolf.");
                localGenders[i] = Config.Bind(section, "Gender", profile.gender, "male or female; changes the dwarf appearance.");
            }
            SelectLocalCompanion(localSelection);
        }

        private int SelectedLocalSlot {
            get {
                for (int i = 0; i < LocalCompanions.SlotCount; i++) if (LocalCompanions.SlotId(i) == localSelection) return i;
                return -1;
            }
        }

        private void SelectLocalCompanion(string id)
        {
            localSelection = id;
            int slot = SelectedLocalSlot;
            if (slot >= 0) {
                localDraft = LocalCompanions.DefaultProfile(slot);
                localDraft.displayName = localNames[slot].Value;
                localDraft.appearance = Rules.Appearances.Contains(localBodies[slot].Value) ? localBodies[slot].Value : localDraft.appearance;
                localDraft.gender = localGenders[slot].Value == "female" ? "female" : "male";
            }
            localMessage = "";
        }

        private void SetLocalControlsVisible(bool visible)
        {
            localControlsVisible = visible;
            if (visible) { show = false; SaveOverlayPosition(); }
            GUIManager.BlockInput(show || localControlsVisible);
        }

        private void UpdateLocalControls()
        {
            if (Player.m_localPlayer && OverlayKeyDown(localControlsKey.Value)) SetLocalControlsVisible(!localControlsVisible);
            if (!Player.m_localPlayer && localControlsVisible) SetLocalControlsVisible(false);
        }

        private Command LocalProfile(Companion companion) => companion ? new Command {
            companionId = companion.Id, displayName = companion.DisplayName, appearance = companion.Appearance,
            gender = companion.Gender, combatStyle = companion.CombatStyle, joinBossFights = companion.JoinBossFights,
            useStoredMaterials = companion.UseStoredMaterials, allowCrafting = companion.AllowCrafting,
            allowBaseWork = companion.AllowBaseWork
        } : localDraft;

        private void SendLocalOrder(string action)
        {
            try {
                var companion = Find(localSelection);
                if (!companion && SelectedLocalSlot < 0) { localMessage = "This companion is no longer nearby."; return; }
                var command = LocalCompanions.Order(LocalProfile(companion), action, World, Rules.Now);
                var reply = Execute(command);
                localMessage = reply.message;
                Note = reply.message; NoteCompanion = command.companionId;
                // Save only after an accepted summon. Invalid drafts never replace saved choices.
                if (reply.accepted && action == "summon" && !companion && SelectedLocalSlot >= 0) {
                    int slot = SelectedLocalSlot;
                    localNames[slot].Value = command.displayName;
                    localBodies[slot].Value = command.appearance;
                    localGenders[slot].Value = command.gender;
                }
            } catch (Exception e) { localMessage = "Could not complete that order: " + e.Message; ReportError("In-game companion controls", e); }
        }

        private void DrawLocalControls()
        {
            if (!localControlsVisible || !Player.m_localPlayer) return;
            localWindow.width = Mathf.Min(490, Mathf.Max(240, Screen.width - 32));
            localWindow = KeepOnScreen(GUILayout.Window(778125, KeepOnScreen(localWindow), DrawLocalControlsWindow, "RUNE FELLOWSHIP — companion controls"));
        }

        private void DrawLocalControlsWindow(int id)
        {
            if (localText == null) localText = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14 };
            localScroll = GUILayout.BeginScrollView(localScroll, GUILayout.Height(Mathf.Clamp(Screen.height - 230, 180, 410)));
            GUILayout.Label("Create and command companions here. The desktop app is optional for voice and AI dialogue.", localText);
            GUILayout.Label("YOUR IN-GAME COMPANIONS");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < LocalCompanions.SlotCount; i++) {
                string key = LocalCompanions.SlotId(i);
                var member = Find(key);
                if (GUILayout.Button((localSelection == key ? "• " : "") + (member ? member.DisplayName : localNames[i].Value), GUILayout.Height(30))) SelectLocalCompanion(key);
            }
            GUILayout.EndHorizontal();
            var others = Companion.Instances.Where(c => c && c.Ready && c.Owner == Player.m_localPlayer.GetPlayerID() && !Enumerable.Range(0, LocalCompanions.SlotCount).Any(i => LocalCompanions.SlotId(i) == c.Id)).ToArray();
            if (others.Length > 0) {
                GUILayout.Label("OTHER NEARBY COMPANIONS");
                foreach (var member in others) if (GUILayout.Button((localSelection == member.Id ? "• " : "") + member.DisplayName)) SelectLocalCompanion(member.Id);
            }
            var selected = Find(localSelection);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && Solo && Player.m_localPlayer && !Player.m_localPlayer.IsDead() && registered;
            if (!selected && SelectedLocalSlot >= 0) {
                GUILayout.Label("Name"); localDraft.displayName = GUILayout.TextField(localDraft.displayName ?? "", 24);
                GUILayout.Label("Appearance");
                int body = Array.IndexOf(Rules.Appearances, localDraft.appearance);
                localDraft.appearance = Rules.Appearances[GUILayout.SelectionGrid(Mathf.Max(0, body), BodyLabels, 3)];
                if (localDraft.appearance == "dwarf") localDraft.gender = GUILayout.SelectionGrid(localDraft.gender == "female" ? 1 : 0, new[] { "Male", "Female" }, 2) == 1 ? "female" : "male";
                if (GUILayout.Button("Summon", GUILayout.Height(32))) SendLocalOrder("summon");
                GUILayout.Label("Companions are saved in your world. Summon will report the location if this companion already exists farther away.", localText);
            } else if (selected) {
                GUILayout.Label(selected.DisplayName + " · " + selected.Appearance + " · " + selected.TaskLabel, localText);
                GUI.enabled = GUI.enabled && selected.Ready;
                LocalOrderRow(new[] { "Follow", "Stay", "Defend" }, new[] { "follow", "stay", "defend" });
                LocalOrderRow(new[] { "Gather 20 wood", "Gather 20 stone" }, new[] { "gather_wood", "gather_stone" });
                if (!Rules.IsWolf(selected.Appearance) && GUILayout.Button("Lend tools")) SendLocalOrder("lend_tools");
                if (GUILayout.Button("Return cargo and borrowed gear")) SendLocalOrder("return");
                if (GUILayout.Button("Unsummon")) SendLocalOrder("dismiss");
                GUILayout.Label("Lend tools transfers a suitable axe and pickaxe. Return brings borrowed tools and cargo back. Unsummon is refused while carrying cargo, borrowed gear, or a rider.", localText);
                if (selected.Appearance == "direwolf") GUILayout.Label("Interact with the saddle to ride. Use movement, Run, Jump and Attack. Backward or Block brakes; secondary attack or dodge dismounts.", localText);
            } else GUILayout.Label("Choose another companion or an in-game slot.", localText);
            GUI.enabled = wasEnabled;
            if (!Solo) GUILayout.Label("Companion orders currently require a solo world.", localText);
            if (!string.IsNullOrEmpty(localMessage)) GUILayout.Label(localMessage, localText);
            GUILayout.EndScrollView();
            GUILayout.Label(localControlsKey.Value + " closes controls. " + overlay.controlsKey + " opens companion information.", localText);
            if (GUILayout.Button("Close")) SetLocalControlsVisible(false);
            GUI.DragWindow(new Rect(0, 0, localWindow.width, 24));
        }

        private void LocalOrderRow(string[] labels, string[] actions)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++) if (GUILayout.Button(labels[i], GUILayout.Height(30))) SendLocalOrder(actions[i]);
            GUILayout.EndHorizontal();
        }
    }
}
