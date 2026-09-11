using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        private float supplyCheckAt, gatherRecoveryDeadline;
        private string gatherRecoveryNote = "";
        private static bool ReservedSupply(ItemDrop.ItemData item) => item.m_customData.ContainsKey("rune.supply");

        // Keep one existing stack per ammunition type and the best carried axe/pickaxe.
        // Never borrow items, split stacks, invent supplies or relabel loaned equipment.
        private void RefreshSupplies()
        {
            var items = Body.GetInventory().GetAllItems();
            var keep = new HashSet<ItemDrop.ItemData>();
            if (!Rune.Shared.Rules.IsWolf(Appearance)) {
                var own = items.Where(i => IsOwnedItem(i) && !i.m_customData.ContainsKey("rune.loan")).ToArray();
                foreach (bool pick in new[] { false, true }) {
                    var tool = own.Where(i => IsPersonalEquipment(i) && (pick ? i.GetDamage().m_pickaxe : i.GetDamage().m_chop) > 0)
                        .OrderByDescending(i => !i.m_shared.m_useDurability || i.m_durability > 0)
                        .ThenByDescending(i => i.m_shared.m_toolTier).ThenByDescending(i => pick ? i.GetDamage().m_pickaxe : i.GetDamage().m_chop).FirstOrDefault();
                    if (tool != null) keep.Add(tool);
                }
                foreach (string type in own.Where(i => IsPersonalEquipment(i) && (i.m_equipped || i.m_customData.ContainsKey("rune.personal")) && !string.IsNullOrEmpty(i.m_shared.m_ammoType)).Select(i => i.m_shared.m_ammoType).Distinct()) {
                    var ammo = Body.GetInventory().GetAmmoItem(type, null);
                    if (ammo != null && own.Contains(ammo)) keep.Add(ammo);
                }
                {
                    foreach (var food in own.Where(i => i.m_shared.m_food > 0 && i.m_shared.m_foodBurnTime > 0)
                        .OrderByDescending(i => FoodScore(i, false)).GroupBy(i => i.m_shared.m_name).Take(3).Select(g => g.First())) keep.Add(food);
                }
            }
            bool changed = false;
            foreach (var item in items) {
                if (keep.Contains(item)) { if (!ReservedSupply(item)) { item.m_customData["rune.supply"] = "1"; changed = true; } }
                else if (item.m_customData.Remove("rune.supply")) changed = true;
            }
            if (changed) Save();
        }

        private void ResetGatherRecovery() { gatherRecoveryDeadline = 0; gatherRecoveryNote = ""; }
        private bool WaitForGatherRecovery()
        {
            if (gatherRecoveryDeadline == 0) gatherRecoveryDeadline = Time.time + 12;
            if (Time.time >= gatherRecoveryDeadline) { gatherRecoveryNote = ""; return false; }
            gatherRecoveryNote = "Checking for another reachable " + (resource == "items" ? "item" : resource.ToLowerInvariant()) + " source";
            ai.StopMoving(); return true;
        }
        private bool HasGatherCapacity()
        {
            if (Body.GetInventory().GetEmptySlots() > 0) return true;
            var prefab = ObjectDB.instance.GetItemPrefab(resource == "items" ? itemFilter : resource);
            var item = prefab ? prefab.GetComponent<ItemDrop>() : null;
            // Unknown/localized pickup names are checked against the actual drop in TakeDrop.
            return !item || Body.GetInventory().CanAddItem(item.m_itemData, 1);
        }
    }
}
