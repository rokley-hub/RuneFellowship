using System;
using System.IO;
using System.Linq;
using Rune.Shared;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Plugin
    {
        private bool overlayVisible = true;
        private Texture2D overlayTexture;
        private GUIStyle overlayPanel, overlayTitle, overlayText, overlayMuted, overlayWrap;
        private Vector2 overlayScroll;
        private float overlayPoll;
        private string pendingVoiceControl = "";
        private float pendingVoiceAt;
        private VoiceOverlay overlay = new VoiceOverlay();
        private BepInEx.Configuration.ConfigEntry<float> overlayX, overlayY, overlayWidth;
        private Vector2 overlayPosition = new Vector2(-1, -1);
        private bool overlayPositionDirty;
        private void InitializeOverlayPosition()
        {
            trackCompanionTeam = Config.Bind("Interface", "TrackCompanionTeam", true, "Show owned companion map pins and direction/distance in the fellowship overlay. Rune app tracking preference overrides while connected.");
            trackedCompanionIds = Config.Bind("Interface", "TrackedCompanionIds", "*", "Companion IDs shown on the map. Managed from each companion's Show on map toggle in Rune; * shows all until configured.");
            overlayWidth = Config.Bind("Interface", "OverlayWidth", 480f, "Overlay width in pixels; status text wraps and height grows with content.");
            overlayX = Config.Bind("Interface", "OverlayX", -1f, "Horizontal overlay position, 0 to 1; -1 uses the default.");
            overlayY = Config.Bind("Interface", "OverlayY", -1f, "Vertical overlay position, 0 to 1; -1 uses the default.");
            overlayPosition = new Vector2(overlayX.Value, overlayY.Value);
        }
        private static Rect KeepOnScreen(Rect rect)
        {
            rect.x = Mathf.Clamp(rect.x, 8, Mathf.Max(8, Screen.width - rect.width - 8));
            rect.y = Mathf.Clamp(rect.y, 8, Mathf.Max(8, Screen.height - rect.height - 8));
            return rect;
        }
        private void SaveOverlayPosition()
        {
            if (!overlayPositionDirty || overlayX == null) return;
            overlayX.Value = overlayPosition.x; overlayY.Value = overlayPosition.y; overlayPositionDirty = false;
        }
        [Serializable] private class VoiceOverlay { public long timestamp; public bool alwaysOn, muted; public bool trackTeam = true; public string[] trackedCompanions; public string session = "", hotkey = "", recorder = "", controlAck = ""; public string companionName = "Rune", switchKey = "Ctrl + Alt + C", overlayKey = "F7", controlsKey = "F8"; public string companion = "rune", provider = "Local AI", state = "Mic off", caption = "", activity = ""; }
        private void PollOverlayState()
        {
            if (Time.unscaledTime > overlayPoll) {
                overlayPoll = Time.unscaledTime + .7f;
                try { string path = Path.Combine(BridgePath, "voice-state.json"); if (File.Exists(path) && new FileInfo(path).Length < 4096) overlay = WireJson.Read<VoiceOverlay>(File.ReadAllText(path)) ?? new VoiceOverlay(); } catch { }
            }
        }
        private void DrawOverlay()
        {
            if (!overlayVisible || !Player.m_localPlayer) return;
            if (overlayPanel == null) {
                overlayTexture = new Texture2D(24, 24, TextureFormat.RGBA32, false);
                for (int y = 0; y < 24; y++) for (int x = 0; x < 24; x++) {
                    float dx = Mathf.Max(6 - x, x - 17), dy = Mathf.Max(6 - y, y - 17);
                    bool corner = dx > 0 && dy > 0 && dx * dx + dy * dy > 36;
                    overlayTexture.SetPixel(x, y, corner ? Color.clear : new Color(.055f, .095f, .08f, .88f));
                }
                overlayTexture.Apply();
                overlayPanel = new GUIStyle { normal = { background = overlayTexture }, border = new RectOffset(7, 7, 7, 7), padding = new RectOffset(16, 16, 12, 12) };
                overlayTitle = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = new Color(.89f, .85f, .68f) }, clipping = TextClipping.Clip };
                overlayText = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(.92f, .94f, .9f) }, clipping = TextClipping.Clip };
                overlayMuted = new GUIStyle(overlayText) { fontSize = 13, normal = { textColor = new Color(.62f, .72f, .66f) } };
                overlayWrap = new GUIStyle(overlayMuted) { wordWrap = true };
            }
            var companions = Companion.Instances.Where(c => c && c.Ready && c.Owner == Player.m_localPlayer.GetPlayerID()).OrderBy(c => c.Id == overlay.companion ? 0 : 1).ToArray();
            float width = Mathf.Min(Mathf.Clamp(overlayWidth.Value, 380, 800), Mathf.Max(200, Screen.width - 32));
            float height = Mathf.Min(Mathf.Max(124, Screen.height - 96), Mathf.Max(220, (show ? 144 : 108) + OverlayContentHeight(companions, width - 50)));
            float rangeX = Mathf.Max(1, Screen.width - width - 16), rangeY = Mathf.Max(1, Screen.height - height - 16);
            var rect = KeepOnScreen(new Rect(overlayPosition.x < 0 ? 24 : 8 + Mathf.Clamp01(overlayPosition.x) * rangeX,
                overlayPosition.y < 0 ? Screen.height - height - 28 : 8 + Mathf.Clamp01(overlayPosition.y) * rangeY, width, height));
            if (show) {
                bool released = Event.current.rawType == EventType.MouseUp;
                var moved = KeepOnScreen(GUI.Window(778124, rect, _ => {
                    DrawOverlayContents(new Rect(0, 0, rect.width, rect.height), companions);
                    GUI.DragWindow(new Rect(0, 0, rect.width, 36));
                }, GUIContent.none, GUIStyle.none));
                if (moved.position != rect.position) {
                    overlayPosition = new Vector2(Mathf.Clamp01((moved.x - 8) / rangeX), Mathf.Clamp01((moved.y - 8) / rangeY));
                    overlayPositionDirty = true;
                }
                if (released) SaveOverlayPosition();
            } else DrawOverlayContents(rect, companions);
        }
        private void DrawOverlayContents(Rect rect, Companion[] companions)
        {
            GUI.Box(rect, GUIContent.none, overlayPanel);
            GUI.Label(new Rect(rect.x + 16, rect.y + 11, 185, 22), "F E L L O W S H I P", overlayTitle);
            GUI.Label(new Rect(rect.xMax - 126, rect.y + 12, 110, 22), show ? "Drag to move" : overlay.controlsKey + " controls", overlayMuted);
            float textWidth = rect.width - 50;
            float contentHeight = OverlayContentHeight(companions, textWidth), viewportHeight = Mathf.Max(40, rect.height - (show ? 132 : 96));
            overlayScroll.y = Mathf.Clamp(overlayScroll.y, 0, Mathf.Max(0, contentHeight - viewportHeight));
            var viewport = new Rect(rect.x, rect.y + 40, rect.width, viewportHeight);
            if (show) overlayScroll = GUI.BeginScrollView(viewport, overlayScroll, new Rect(0, 0, rect.width - 18, contentHeight));
            else { GUI.BeginGroup(viewport); GUI.BeginGroup(new Rect(0, -overlayScroll.y, rect.width - 18, contentHeight)); }
            float row = 0;
            if (companions.Length == 0) GUI.Label(new Rect(16, row, textWidth, 35), "Summon a companion in Rune Voice.", overlayMuted);
            foreach (var companion in companions) {
                string name = companion.DisplayName;
                GUI.Label(new Rect(16, row, textWidth - 80, 22), (companion.Id == overlay.companion ? "• " : "") + name, overlayText);
                GUI.Label(new Rect(textWidth - 56, row, 72, 22), companion.Body.GetHealth().ToString("F0") + " HP", overlayMuted);
                var oldColor = GUI.color; GUI.color = new Color(.22f, .30f, .25f, 1);
                GUI.DrawTexture(new Rect(16, row + 26, textWidth, 3), Texture2D.whiteTexture);
                GUI.color = companion.Body.GetHealthPercentage() < .4f ? new Color(.88f, .52f, .39f) : new Color(.54f, .74f, .57f);
                GUI.DrawTexture(new Rect(16, row + 26, textWidth * Mathf.Clamp01(companion.Body.GetHealthPercentage()), 3), Texture2D.whiteTexture); GUI.color = oldColor;
                row += 36;
                foreach (var line in StatusLines(companion)) {
                    float lineHeight = Mathf.Max(18, overlayWrap.CalcHeight(new GUIContent(line), textWidth));
                    GUI.Label(new Rect(16, row, textWidth, lineHeight), line, line.StartsWith("GOAL · ") ? overlayText : overlayWrap); row += lineHeight + 4;
                }
                row += 6;
                foreach (var line in QuestLines(companion)) {
                    float lineHeight = Mathf.Max(18, overlayWrap.CalcHeight(new GUIContent(line), textWidth));
                    GUI.Label(new Rect(16, row, textWidth, lineHeight), line, overlayWrap); row += lineHeight + 4;
                }
            }
            if (show) GUI.EndScrollView(); else { GUI.EndGroup(); GUI.EndGroup(); }
            if (Rules.Now - overlay.timestamp < 5 && overlay.activity.Length > 0) GUI.Label(new Rect(rect.x + 16, rect.yMax - (show ? 90 : 54), rect.width - 32, 22), overlay.activity, overlayText);
            string footer = Rules.Now - overlay.timestamp < 5 ? overlay.companionName + " · " + overlay.state + (overlay.alwaysOn ? " · " + overlay.hotkey + " toggles mute" : "  ·  " + overlay.provider) : "Rune Voice  ·  F7 hide";
            if (Rules.Now - overlay.timestamp < 5 && overlay.caption.Length > 0) footer = overlay.state + " · " + overlay.caption;
            GUI.Label(new Rect(rect.x + 16, rect.yMax - 28, rect.width - 32, 22), footer, overlayMuted);
            if (show) {
                bool enabled = GUI.enabled; GUI.enabled = Rules.Now - overlay.timestamp < 5 && (pendingVoiceControl.Length == 0 || pendingVoiceControl == overlay.controlAck || Time.unscaledTime - pendingVoiceAt > 5);
                float buttonWidth = (rect.width - 40) / 3;
                bool connected = GUI.enabled; GUI.enabled = connected && overlay.alwaysOn;
                if (GUI.Button(new Rect(rect.x + 16, rect.yMax - 62, buttonWidth, 28), overlay.muted ? "Unmute mic" : "Mute mic")) SendVoiceControl("toggle-mic");
                GUI.enabled = connected;
                if (GUI.Button(new Rect(rect.x + 20 + buttonWidth, rect.yMax - 62, buttonWidth, 28), "Report bug")) SendVoiceControl("report-bug");
                if (GUI.Button(new Rect(rect.x + 24 + buttonWidth * 2, rect.yMax - 62, buttonWidth, 28), "Note idea")) SendVoiceControl("note-improvement");
                GUI.enabled = enabled;
            }
        }
        [Serializable] private class VoiceControl { public string id = "", session = "", action = ""; public long timestamp; }
        private void SendVoiceControl(string action)
        {
            try { pendingVoiceControl = System.Guid.NewGuid().ToString(); pendingVoiceAt = Time.unscaledTime; AtomicWrite(Path.Combine(BridgePath, "voice-control.json"), WireJson.Write(new VoiceControl { id = pendingVoiceControl, session = overlay.session, timestamp = Rules.Now, action = action })); }
            catch (Exception e) { ReportError("Rune app control unavailable", e); }
        }
        private string[] QuestLines(Companion companion)
        {
            var quest = companion.QuestSnapshot();
            if (quest == null || quest.status != "Active") return Array.Empty<string>();
            var lines = new System.Collections.Generic.List<string> { "CRAFTING · " + quest.item + " · " + quest.status, quest.note };
            if (quest.status != "Complete") {
                lines.Add("Recipe · carried / required · in base");
                lines.AddRange(quest.materials.Select(n => n.name + "  " + Math.Min(n.carried, n.required) + "/" + n.required + " · " + n.stored));
                if (quest.gather.Length > 0) { lines.Add("Still needed · including ingredient recipes"); lines.AddRange(quest.gather.Select(n => n.missing + " " + n.name)); }
                else lines.Add("Materials available in cargo or accessible base.");
            }
            lines.Add("Say clear crafting to clear this checklist.");
            return lines.Where(s => s.Length > 0).ToArray();
        }
        private string[] StatusLines(Companion companion)
        {
            var lines = new System.Collections.Generic.List<string>();
            if (IsCompanionTracked(companion.Id)) lines.Add("TEAM · " + CompanionLocation(companion));
            lines.Add(companion.EquippedWeaponStatus);
            if (companion.ObjectiveOngoing) lines.Add("GOAL · " + companion.Objective);
            lines.Add("NOW · " + companion.TaskLabel);
            if (companion.PlanLabel.StartsWith("Step ")) lines.Add("PLAN · " + companion.PlanLabel.Substring(5));
            if (companion.NextPlanLabel.Length > 0) lines.Add("NEXT · " + companion.NextPlanLabel);
            if (companion.PlanLabel.StartsWith("Plan blocked: ")) lines.Add("BLOCKED · " + companion.PlanLabel.Substring(14));
            return lines.ToArray();
        }
        private float OverlayContentHeight(Companion[] companions, float textWidth) => companions.Length == 0 ? 48 : companions.Sum(c => 42 + StatusLines(c).Sum(line => Mathf.Max(18, overlayWrap.CalcHeight(new GUIContent(line), textWidth)) + 4) + QuestLines(c).Sum(line => Mathf.Max(18, overlayWrap.CalcHeight(new GUIContent(line), textWidth)) + 4));
    }
}
