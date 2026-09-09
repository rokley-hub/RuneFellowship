using System;
using System.Linq;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        private ItemDrop.ItemData repairItem;
        private CraftingStation repairStation;
        private float equipmentCheckAt, repairStarted, repairNoticeAt;
        private readonly System.Collections.Generic.Dictionary<int, float> failedRepairStations = new System.Collections.Generic.Dictionary<int, float>();
        private string maintenanceNote = "";
        private bool deliverKept;
        private static bool IsPersonalEquipment(ItemDrop.ItemData item) => item.IsEquipable() && item.m_shared.m_maxStackSize == 1;
        private string ToolCondition(bool pickaxe)
        {
            var item = GetTool(pickaxe) ?? Body.GetInventory().GetAllItems().FirstOrDefault(i => pickaxe ? i.GetDamage().m_pickaxe > 0 : i.GetDamage().m_chop > 0);
            return item == null ? "missing" : !item.m_shared.m_useDurability ? "usable" : Mathf.RoundToInt(item.GetDurabilityPercentage() * 100) + "% durability";
        }

        private string KeepCraftedItem(ItemDrop.ItemData item)
        {
            item.m_customData.Remove("rune.kept"); item.m_customData.Remove("rune.personal");
            if (!IsPersonalEquipment(item)) { item.m_customData["rune.kept"] = "1"; return "Kept in my inventory for later. Say return to receive it."; }
            item.m_customData["rune.personal"] = "1";
            if (Appearance == "wolf") return "Kept, but this body cannot equip it.";
            var current = Body.GetInventory().GetAllItems().Where(i => i != item && i.m_equipped && i.m_shared.m_itemType == item.m_shared.m_itemType).FirstOrDefault();
            float Score(ItemDrop.ItemData i) => i.m_shared.m_useDurability && i.m_durability <= 0 ? -1 : IsArmour(i) ? i.GetArmor() : i.GetDamage().GetTotalDamage() + i.m_shared.m_toolTier * 10;
            if (current == null || Score(item) > Score(current) || Score(item) == Score(current) && current.m_shared.m_useDurability && current.GetDurabilityPercentage() < item.GetDurabilityPercentage()) {
                if (Body.EquipItem(item, false)) return "Equipped for my own use. I'll keep it when delivering cargo.";
            }
            return "Kept as spare equipment; my current equipment is at least as useful or this body cannot equip it.";
        }

        private void CancelMaintenance()
        {
            if (repairItem != null) {
                float elapsed = Mathf.Max(0, Time.time - repairStarted);
                orderTime += elapsed; if (craftPlan != null) craftPlan.Started += elapsed;
                lastProgress = Time.time; closestDistance = float.MaxValue;
            }
            repairItem = null; repairStation = null; maintenanceNote = ""; equipmentCheckAt = Time.time + 5;
        }
        private void MaintenanceNotice(string message)
        {
            if (Time.time < repairNoticeAt) return;
            repairNoticeAt = Time.time + 60; Say(DisplayName + ": " + message);
        }
        private static bool StationRepairs(CraftingStation station, Recipe recipe)
        {
            return station && recipe && ((recipe.m_repairStation && recipe.m_repairStation.m_name == station.m_name) || (recipe.m_craftingStation && recipe.m_craftingStation.m_name == station.m_name))
                && Mathf.Min(station.GetLevel(), 4) >= recipe.m_minStationLevel;
        }
        private bool StationAccessible(CraftingStation station) => station && SafeGround(station.transform.position)
            && Vector3.Distance(station.transform.position, Player.transform.position) <= 35
            && PrivateArea.CheckAccess(station.transform.position, 0, false, true)
            && station.CheckUsable(Player, false);

        // Separate from the work mode: repairs suspend a task without consuming or replacing it.
        private bool TickEquipmentCare(float dt)
        {
            if (Time.time >= supplyCheckAt) { supplyCheckAt = Time.time + 5; RefreshSupplies(); }
            if (Appearance == "wolf" || followingOrder || mode == "stay" || mode == "return" || Body.InAttack()) { CancelMaintenance(); return false; }
            if (repairItem == null) {
                if (Time.time < equipmentCheckAt) return false;
                equipmentCheckAt = Time.time + 5;
                foreach (int id in failedRepairStations.Where(p => p.Value <= Time.time).Select(p => p.Key).ToArray()) failedRepairStations.Remove(id);
                var candidates = Body.GetInventory().GetAllItems().Where(i => i.m_shared.m_useDurability && i.m_shared.m_canBeReparied && i.GetDurabilityPercentage() <= .2f
                    && (i.m_equipped || ReservedSupply(i) || i.m_customData.ContainsKey("rune.personal") || i.m_customData.ContainsKey("rune.loan")))
                    .OrderBy(i => i.GetDurabilityPercentage()).ToArray();
                if (candidates.Length == 0) return false;
                var worn = candidates[0];
                CraftingStation station = null;
                foreach (var candidate in candidates) {
                    var candidateRecipe = ObjectDB.instance.GetRecipe(candidate);
                    station = CraftingStation.Instances.OfType<CraftingStation>().Where(s => StationRepairs(s, candidateRecipe)
                        && !failedRepairStations.ContainsKey(s.GetInstanceID()) && Vector3.Distance(transform.position, s.transform.position) <= 30 && StationAccessible(s))
                        .OrderBy(s => Vector3.Distance(transform.position, s.transform.position)).FirstOrDefault();
                    if (station) { worn = candidate; break; }
                }
                var recipe = ObjectDB.instance.GetRecipe(worn);
                string name = Localization.instance.Localize(worn.m_shared.m_name);
                if (!station) {
                    string needed = recipe && (recipe.m_repairStation || recipe.m_craftingStation) ? Localization.instance.Localize((recipe.m_repairStation ? recipe.m_repairStation : recipe.m_craftingStation).m_name) + " (level " + recipe.m_minStationLevel + ")" : "a compatible repair station";
                    MaintenanceNotice("My " + name + " is at " + Mathf.RoundToInt(worn.GetDurabilityPercentage() * 100) + "% durability. I need a nearby usable " + needed + ", with access, shelter and fire where required. I cannot repair it here.");
                    return false;
                }
                repairItem = worn; repairStation = station; repairStarted = Time.time;
                maintenanceNote = "Repairing " + name + " at " + Localization.instance.Localize(station.m_name);
                Say(DisplayName + ": " + maintenanceNote + ". I'll resume my task afterward.");
            }
            if (!Body.GetInventory().GetAllItems().Contains(repairItem) || !StationRepairs(repairStation, ObjectDB.instance.GetRecipe(repairItem)) || !StationAccessible(repairStation) || Time.time - repairStarted > 25) {
                if (repairStation && failedRepairStations.Count < 64) failedRepairStations[repairStation.GetInstanceID()] = Time.time + 60;
                MaintenanceNotice("I couldn't reach or use the repair station. Resuming my task with the equipment I have.");
                CancelMaintenance(); return false;
            }
            ai.SetFollowTarget(null); SetTargetMethod.Invoke(ai, new object[] { null });
            float reach = Mathf.Min(2.5f, repairStation.m_useDistance);
            if (Vector3.Distance(transform.position, repairStation.transform.position) > reach) { Move(dt, repairStation.transform.position, Mathf.Max(.5f, reach - .3f)); return true; }
            ai.StopMoving(); repairStation.PokeInUse();
            repairItem.m_durability = repairItem.GetMaxDurability();
            repairStation.m_repairItemDoneEffects.Create(repairStation.transform.position, Quaternion.identity);
            string repaired = Localization.instance.Localize(repairItem.m_shared.m_name);
            // Pause progress timeouts too, so a successful detour cannot fail the old path immediately.
            CancelMaintenance(); Save(); Say(DisplayName + ": Repaired my " + repaired + ". Resuming work."); return true;
        }
    }
}
