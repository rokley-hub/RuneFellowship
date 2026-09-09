using System;
using System.Reflection;
using System.Linq;
using UnityEngine;
using HarmonyLib;
using Jotunn.Managers;

namespace Rune.Mod
{
    internal static class DwarfAppearance
    {
        public static void Configure(GameObject npc)
        {
            var player = PrefabManager.Instance.GetPrefab("Player");
            if (!player) throw new InvalidOperationException("Player appearance is unavailable.");
            var sourceVisual = player.transform.Find("Visual");
            if (!sourceVisual) throw new InvalidOperationException("Player Visual hierarchy changed.");
            var oldVisual = npc.transform.Find("Visual");
            if (oldVisual) UnityEngine.Object.DestroyImmediate(oldVisual.gameObject);
            var visual = UnityEngine.Object.Instantiate(sourceVisual.gameObject, npc.transform); visual.name = "Visual";
            visual.transform.localPosition = Vector3.zero; visual.transform.localRotation = Quaternion.identity; visual.transform.localScale = Vector3.one;
            var source = player.GetComponent<VisEquipment>();
            var equipment = npc.GetComponent<VisEquipment>() ?? npc.AddComponent<VisEquipment>();
            foreach (var field in typeof(VisEquipment).GetFields(BindingFlags.Instance | BindingFlags.Public)) {
                var value = field.GetValue(source);
                if (value is Component component && component.transform.IsChildOf(player.transform)) value = Map(component.transform, player.transform, npc.transform)?.GetComponent(component.GetType());
                else if (value is CapsuleCollider[] colliders) value = colliders.Select(c => c ? Map(c.transform, player.transform, npc.transform)?.GetComponent<CapsuleCollider>() : null).Where(c => c).ToArray();
                field.SetValue(equipment, value);
            }
            equipment.m_isPlayer = true;
            var sourceSync = player.GetComponent<ZSyncAnimation>(); var sync = npc.GetComponent<ZSyncAnimation>();
            sync.m_syncBools = new System.Collections.Generic.List<string>(sourceSync.m_syncBools);
            sync.m_syncFloats = new System.Collections.Generic.List<string>(sourceSync.m_syncFloats);
            sync.m_syncInts = new System.Collections.Generic.List<string>(sourceSync.m_syncInts);
            var h = npc.GetComponent<Humanoid>(); var playerBody = player.GetComponent<Humanoid>();
            h.m_blockStaminaDrain = playerBody.m_blockStaminaDrain;
            h.m_perfectBlockStaminaDrain = playerBody.m_perfectBlockStaminaDrain;
            h.m_eye = Map(playerBody.m_eye, player.transform, npc.transform);
            h.m_defaultItems = new[] { PrefabManager.Instance.GetPrefab("AxeStone"), PrefabManager.Instance.GetPrefab("ArmorRagsChest"), PrefabManager.Instance.GetPrefab("ArmorRagsLegs") }.Where(g => g).ToArray();
            h.m_walkSpeed = 2; h.m_speed = 3; h.m_runSpeed = 6;
            npc.transform.localScale = new Vector3(.88f, .72f, .88f);
        }
        public static void SetGender(GameObject npc, string gender)
        {
            var equipment = npc.GetComponent<VisEquipment>();
            if (equipment) equipment.SetModel(gender == "female" ? 1 : 0);
        }
        private static Transform Map(Transform value, Transform sourceRoot, Transform destination)
        {
            if (!value) return null; if (value == sourceRoot) return destination;
            string path = value.name; for (var parent = value.parent; parent && parent != sourceRoot; parent = parent.parent) path = parent.name + "/" + path;
            return destination.Find(path);
        }
    }
    public partial class Companion
    {
        private string LendGear()
        {
            if (Appearance != "dwarf") return "Choose a dwarf companion for visible player armour and gear.";
            if (!Player || Vector3.Distance(transform.position, Player.transform.position) > 4) return "Come within four metres to lend your equipped gear.";
            int moved = 0;
            foreach (var item in Player.GetInventory().GetAllItems().Where(i => i.m_equipped).ToArray()) {
                var type = item.m_shared.m_itemType;
                bool suitable = type == ItemDrop.ItemData.ItemType.Helmet || type == ItemDrop.ItemData.ItemType.Chest || type == ItemDrop.ItemData.ItemType.Legs || type == ItemDrop.ItemData.ItemType.Shoulder || type == ItemDrop.ItemData.ItemType.Shield || type == ItemDrop.ItemData.ItemType.OneHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeapon || type == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;
                if (!suitable || Body.GetInventory().GetAllItems().Any(i => i.m_customData.ContainsKey("rune.loan") && i.m_shared.m_itemType == type)) continue;
                var copy = item.Clone(); copy.m_equipped = false; copy.m_customData["rune.loan"] = "1";
                if (!Body.GetInventory().CanAddItem(copy, copy.m_stack)) continue;
                Player.UnequipItem(item, false); if (!Player.GetInventory().RemoveItem(item)) continue;
                if (!Body.GetInventory().AddItem(copy)) { Player.GetInventory().AddItem(item); Player.EquipItem(item, false); continue; }
                Body.EquipItem(copy, false); moved++;
            }
            Save(); return "Borrowed " + moved + " equipped item(s). Say return to get your gear back. I use real armour values; I do not inherit your skills or food buffs.";
        }
        private void EquipCombat()
        {
            if (Appearance == "wolf") return;
            if (Appearance == "dwarf") { ChooseCombatWeapon(combatEnemy); return; }
            if (Appearance != "dwarf") {
                var native = Body.GetInventory().GetAllItems().FirstOrDefault(i => i.m_customData.ContainsKey("rune.starter") && i.m_dropPrefab && i.m_dropPrefab.name != "AxeStone" && i.GetDamage().GetTotalDamage() > 0);
                if (native != null && !Body.InAttack()) { Body.EquipItem(native, false); return; }
            }
            var weapon = Body.GetInventory().GetAllItems().Where(i => (i.m_customData.ContainsKey("rune.loan") || i.m_customData.ContainsKey("rune.personal")) && i.GetDamage().GetTotalDamage() > 0 && (!i.m_shared.m_useDurability || i.m_durability > 0)).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();
            if (weapon != null && !Body.InAttack()) Body.EquipItem(weapon, false); else EquipTool(false);
        }
        private static bool IsArmour(ItemDrop.ItemData i) => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Legs || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shoulder;
        public float GearArmor => Body.GetInventory().GetAllItems().Where(i => IsArmour(i) && i.m_equipped && (!i.m_shared.m_useDurability || i.m_durability > 0)).Sum(i => i.GetArmor());
        public void ApplyGearModifiers(ref HitData.DamageModifiers mods) { foreach (var item in Body.GetInventory().GetAllItems().Where(i => i.m_equipped && (!i.m_shared.m_useDurability || i.m_durability > 0))) mods.Apply(item.m_shared.m_damageModifiers); }
        public void WearArmor() { foreach (var item in Body.GetInventory().GetAllItems().Where(i => IsArmour(i) && i.m_equipped && i.GetArmor() > 0 && i.m_shared.m_useDurability)) item.m_durability = Math.Max(0, item.m_durability - item.m_shared.m_durabilityDrain); }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.GetBodyArmor))]
    internal static class DwarfArmor { static void Postfix(Character __instance, ref float __result) { var c = __instance.GetComponent<Companion>(); if (c && c.Ready && c.Appearance == "dwarf") __result += c.GearArmor; } }
    [HarmonyPatch(typeof(Character), "ApplyArmorDamageMods")]
    internal static class DwarfResistances { static void Postfix(Character __instance, ref HitData.DamageModifiers __0) { var c = __instance.GetComponent<Companion>(); if (c && c.Ready && c.Appearance == "dwarf") c.ApplyGearModifiers(ref __0); } }
    [HarmonyPatch(typeof(Character), "DamageArmorDurability")]
    internal static class DwarfWear { static void Postfix(Character __instance) { var c = __instance.GetComponent<Companion>(); if (c && c.Ready && c.Appearance == "dwarf") c.WearArmor(); } }
}


