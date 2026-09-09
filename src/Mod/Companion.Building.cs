using System;
using System.Linq;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        internal static bool IsWorkbench(string name) => Normalize(name) == "workbench" || Normalize(name) == "workingbench" || Normalize(name) == "pieceworkbench";
        private ItemDrop.ItemData BuildingHammer() => Body.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == "Hammer" && (!i.m_shared.m_useDurability || i.m_durability > 0));
        private static bool WorkbenchSpace(Vector3 position, Quaternion rotation)
        {
            // Conservative footprint: horizontal support under the entire bench, free body
            // space and approach clearance. Never destroy/flatten anything to make it fit.
            int supportMask = LayerMask.GetMask("terrain", "piece", "static_solid", "Default");
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++) {
                Vector3 sample = position + rotation * new Vector3(x * 1.15f, 0, z * .85f);
                if (Location.IsInsideNoBuildLocation(sample) || !PrivateArea.CheckAccess(sample, 0, false, false)) return false;
                if (!Physics.Raycast(sample + Vector3.up * .45f, Vector3.down, out var hit, .8f, supportMask, QueryTriggerInteraction.Ignore)) return false;
                if (hit.normal.y < .94f || Math.Abs(hit.point.y - position.y) > .22f || hit.point.y < WaterAt(sample) + .15f) return false;
                if (!hit.collider.GetComponent<Heightmap>() && !hit.collider.GetComponentInParent<Piece>()) return false;
            }
            var overlaps = Physics.OverlapBox(position + Vector3.up * 1.05f, new Vector3(1.3f, .95f, 1.05f), rotation,
                LayerMask.GetMask("piece", "static_solid", "Default", "character", "character_net", "character_noenv"), QueryTriggerInteraction.Ignore);
            return !overlaps.Any(c => c && !c.isTrigger);
        }
        private static bool FindWorkbenchSpot(CraftPlan plan, out Vector3 position)
        {
            int mask = LayerMask.GetMask("terrain", "piece", "static_solid", "Default");
            foreach (float radius in new[] { 3f, 4.5f, 6f }) for (int i = 0; i < 12; i++) {
                Vector3 candidate = plan.BuildOrigin + plan.BuildRotation * Quaternion.Euler(0, i * 30, 0) * Vector3.forward * radius;
                if (!Physics.Raycast(candidate + Vector3.up * 2, Vector3.down, out var hit, 4, mask, QueryTriggerInteraction.Ignore)) continue;
                candidate.y = hit.point.y;
                if (!WorkbenchSpace(candidate, plan.BuildRotation)) continue;
                position = candidate; return true;
            }
            position = Vector3.zero; return false;
        }
        private void BuildWorkbench(float dt, Piece.Requirement[] requirements)
        {
            var plan = craftPlan;
            if (Vector3.Distance(Player.transform.position, plan.BuildOrigin) > 45) { BlockCraft("You moved too far from the workbench site. Ask again near the place you want it."); return; }
            var hammer = BuildingHammer();
            if (hammer == null) { BlockCraft("I need a usable hammer to place the workbench. My hammer is missing or broken."); return; }
            if (!BuildingKnowledge.Known(Player, plan.Boat.GetComponent<Piece>())) { BlockCraft("The workbench building piece is no longer known."); return; }
            bool close = Vector3.Distance(transform.position, plan.BuildPosition) <= 2.8f;
            if ((close || Time.time >= plan.BuildValidateAt) && !WorkbenchSpace(plan.BuildPosition, plan.BuildRotation)) {
                if (!FindWorkbenchSpot(plan, out plan.BuildPosition)) { BlockCraft("The workbench site is obstructed, unsafe or protected. Clear a spot and ask again."); return; }
                plan.BuildWalkStarted = 0;
            }
            if (close || Time.time >= plan.BuildValidateAt) plan.BuildValidateAt = Time.time + .5f;
            if (Vector3.Distance(transform.position, plan.BuildPosition) > 2.8f) {
                if (plan.BuildWalkStarted == 0) plan.BuildWalkStarted = Time.time;
                if (Time.time - plan.BuildWalkStarted > 30) { BlockCraft("I cannot reach the workbench site. My materials are still in my inventory."); return; }
                craftNote = "Walking to the workbench site"; Move(dt, plan.BuildPosition, 2.4f); return;
            }
            ai.StopMoving(); Body.EquipItem(hammer, false);
            var ingredients = ConsumeIngredients(requirements);
            if (ingredients == null) { BlockCraft("The workbench ingredients changed before placement."); return; }
            GameObject placed = null;
            try {
                placed = Instantiate(plan.Boat, plan.BuildPosition, plan.BuildRotation);
                if (!placed.GetComponent<ZNetView>().IsValid()) throw new InvalidOperationException("Building network object unavailable");
                placed.GetComponent<Piece>().AssignCreator(Owner);
                var wear = placed.GetComponent<WearNTear>(); if (wear) wear.OnPlaced();
                foreach (var callback in placed.GetComponents<IPlaced>()) callback.OnPlaced();
                var station = placed.GetComponent<CraftingStation>();
                if (!station) throw new InvalidOperationException("Workbench station missing");
                if (hammer.m_shared.m_useDurability) hammer.m_durability = Math.Max(0, hammer.m_durability - hammer.m_shared.m_useDurabilityDrain);
                if (hammer.m_shared.m_attack != null && !string.IsNullOrEmpty(hammer.m_shared.m_attack.m_attackAnimation)) Body.GetZAnim().SetTrigger(hammer.m_shared.m_attack.m_attackAnimation);
                plan.Boat.GetComponent<Piece>().m_placeEffect.Create(plan.BuildPosition, plan.BuildRotation);
                bool usable = station.CheckUsable(Player, false);
                SetQuestStatus("Complete", "Workbench placed near your requested location."); craftPlan = null; mode = "follow"; Save();
                Say("Built the workbench near where you asked, using my materials. " + (usable ? "It is sheltered and usable." : "It still needs shelter before you can craft or repair at it."));
            } catch (Exception e) {
                if (placed) ZNetScene.instance.Destroy(placed);
                RestoreIngredients(ingredients); BlockCraft("Workbench placement failed: " + e.Message);
            }
        }
    }
}
