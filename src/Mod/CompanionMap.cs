using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Plugin
    {
        private readonly Dictionary<Companion, Minimap.PinData> companionPins = new Dictionary<Companion, Minimap.PinData>();
        private Minimap pinMap;
        private float pinUpdateAt;
        private void LateUpdate()
        {
            if (Time.unscaledTime < pinUpdateAt) return;
            pinUpdateAt = Time.unscaledTime + .3f;
            if (pinMap != Minimap.instance) { RemoveCompanionPins(); pinMap = Minimap.instance; }
            if (!pinMap) return;
            var visible = Player.m_localPlayer && Solo ? Companion.Instances.Where(c => c && c.Ready && !c.Body.IsDead() && c.Owner == Player.m_localPlayer.GetPlayerID()).ToArray() : new Companion[0];
            foreach (var old in companionPins.Keys.ToArray()) if (!visible.Contains(old)) { pinMap.RemovePin(companionPins[old]); companionPins.Remove(old); }
            foreach (var c in visible) {
                if (!companionPins.TryGetValue(c, out var pin)) { pin = pinMap.AddPin(c.transform.position, Minimap.PinType.Player, c.DisplayName, false, false, 0, default); companionPins[c] = pin; }
                pin.m_pos = c.transform.position; pin.m_name = c.DisplayName; pin.m_save = false;
            }
        }
        private void RemoveCompanionPins() { if (pinMap) foreach (var pin in companionPins.Values) pinMap.RemovePin(pin); companionPins.Clear(); }
    }
}
