using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        public bool HasBase;
        private Vector3 basePoint;
        private string baseNote = "";
        private float baseActionAt, baseStarted;
        private Container sourceChest, destinationChest;
        private ItemDrop.ItemData transferItem;
        private bool fetched;
        private readonly Dictionary<int, string> chestCategories = new Dictionary<int, string>();
        private readonly HashSet<int> skippedBaseTargets = new HashSet<int>();
        private int walkTarget;
        private float walkStarted;
        private bool IsBaseMode => mode == "sort_storage" || mode == "store_cargo" || mode == "cook_food" || mode == "manage_base";
        private string BaseTaskLabel => baseNote.Length > 0 ? baseNote : "Working at base";
        private static readonly System.Reflection.MethodInfo ChestSave = AccessTools.Method(typeof(Container), "Save");
        private static readonly System.Reflection.MethodInfo ChestAccess = AccessTools.Method(typeof(Container), "CheckAccess");
        private static readonly System.Reflection.MethodInfo SlotMethod = AccessTools.Method(typeof(CookingStation), "GetSlot");
        private void LoadBase() { HasBase = view.GetZDO().GetBool("rune.hasBase", false); basePoint = view.GetZDO().GetVec3("rune.base", transform.position); }
        private void SaveBase() { view.GetZDO().Set("rune.hasBase", HasBase); view.GetZDO().Set("rune.base", basePoint); }
        private string BaseOrder(string action)
        {
            if (!UseStoredMaterials && action == "sort_storage") { LastOrderAccepted = false; return "Chest access is disabled. Enable Use stored materials before asking me to rearrange chest contents."; }
            if (action == "set_base") { HasBase = true; basePoint = Player.transform.position; Save(); return "Base remembered. I can use your own accessible chests and cooking stations within twenty metres of here."; }
            if (action != "sort_storage" && action != "store_cargo" && action != "cook_food" && action != "manage_base") return null;
            if (Appearance == "wolf") return "Wolves can fetch loose items and defend you, but cannot cook or sort chests.";
            if (!HasBase) return "Stand in your base and say set base here first.";
            if (Vector3.Distance(Player.transform.position, basePoint) > 40) return "Come back to base before asking me to manage storage or cook.";
            if (Instances.Any(c => c && c != this && c.Owner == Owner && c.IsBaseMode && Vector3.Distance(c.basePoint, basePoint) < 40)) return "Another companion is managing this base. Ask them to stop first so we do not rearrange each other's work.";
            mode = action; target = null; transferItem = null; sourceChest = destinationChest = null; fetched = false;
            chestCategories.Clear(); skippedBaseTargets.Clear(); walkTarget = 0;
            baseStarted = Time.time; baseActionAt = 0;
            foreach (var chest in Chests()) {
                var dominant = chest.GetInventory().GetAllItems().GroupBy(Category).OrderByDescending(g => g.Count()).FirstOrDefault();
                chestCategories[chest.GetInstanceID()] = dominant == null ? "" : dominant.Key;
            }
            baseNote = action == "sort_storage" ? "Sorting chests by item type" : action == "store_cargo" ? "Putting cargo away" : "Tending cooking stations";
            ai.SetFollowTarget(null); SetTargetMethod.Invoke(ai, new object[] { null }); Save();
            return baseNote + ". I'll work nearby for up to five minutes. Say stop at any time. Cooking needs a lit fire or a fueled oven.";
        }
        private bool OwnPiece(Component component)
        {
            if (!component || Vector3.Distance(component.transform.position, basePoint) > 20) return false;
            var piece = component.GetComponentInParent<Piece>();
            var nv = component.GetComponent<ZNetView>();
            return piece && piece.IsPlacedByPlayer() && piece.GetCreator() == Owner && nv && nv.IsValid() && nv.IsOwner();
        }
        private bool CanChest(Container chest) => chest && OwnPiece(chest) && !chest.IsInUse() && chest.IsOwner() && (!chest.m_checkGuardStone || PrivateArea.CheckAccess(chest.transform.position, 0, false, false)) && (bool)ChestAccess.Invoke(chest, new object[] { Owner });
        private Container[] Chests() => UnityEngine.Object.FindObjectsOfType<Container>().Where(CanChest).Where(c => !skippedBaseTargets.Contains(c.GetInstanceID())).OrderBy(c => c.transform.position.x).ThenBy(c => c.transform.position.z).ToArray();
        private static string Category(ItemDrop.ItemData item) => item.m_shared.m_food > 0 || item.m_shared.m_foodStamina > 0 ? "Food" : item.m_shared.m_itemType.ToString();
        private Container Destination(ItemDrop.ItemData item, Container source = null)
        {
            var chests = Chests();
            // A stable category assignment lasts for the whole order, preventing oscillation.
            string category = Category(item);
            var targetChest = chests.Where(c => c != source && c.GetInventory().CanAddItem(item, item.m_stack))
                .OrderByDescending(c => c.GetInventory().GetAllItems().Any(i => i.m_shared.m_name == item.m_shared.m_name))
                .FirstOrDefault(c => chestCategories.TryGetValue(c.GetInstanceID(), out var type) && type == category);
            if (targetChest) return targetChest;
            targetChest = chests.FirstOrDefault(c => c != source && c.GetInventory().NrOfItems() == 0 && c.GetInventory().CanAddItem(item, item.m_stack) && (!chestCategories.TryGetValue(c.GetInstanceID(), out var type) || type.Length == 0));
            if (targetChest) chestCategories[targetChest.GetInstanceID()] = category;
            return targetChest;
        }
        private bool WalkBase(float dt, Component component)
        {
            if (!component) return false;
            int id = component.GetInstanceID();
            if (walkTarget != id) { walkTarget = id; walkStarted = Time.time; }
            if (Vector3.Distance(transform.position, component.transform.position) <= 2.8f) { ai.StopMoving(); walkTarget = 0; return true; }
            if (Time.time - walkStarted > 25) { skippedBaseTargets.Add(id); transferItem = null; sourceChest = destinationChest = null; baseNote = "Skipping an unreachable station or chest"; return false; }
            Move(dt, component.transform.position, 2.5f); return false;
        }
        // Every transfer removes the source first, preserves metadata, and rolls back on failure.
        private bool Transfer(Inventory from, Inventory to, ItemDrop.ItemData item, int amount, Container fromChest = null, Container toChest = null)
        {
            if (fromChest && to == Body.GetInventory() && !UseStoredMaterials) return false;
            if (fromChest && !CanChest(fromChest) || toChest && !CanChest(toChest)) return false;
            if (!from.GetAllItems().Contains(item) || amount < 1 || amount > item.m_stack) return false;
            var copy = item.Clone(); copy.m_stack = amount; copy.m_equipped = false;
            if (!to.CanAddItem(copy, amount)) return false;
            if (!from.RemoveItem(item, amount)) return false;
            if (!to.AddItem(copy)) { from.AddItem(copy); return false; }
            if (fromChest) ChestSave.Invoke(fromChest, null);
            if (toChest) ChestSave.Invoke(toChest, null);
            Save(); return true;
        }
        private void EndBase(string message) { mode = "stay"; anchor = transform.position; ai.StopMoving(); transferItem = null; Save(); Say(DisplayName + ": " + message); }
        private void TickBase(float dt)
        {
            if (!HasBase || Vector3.Distance(Player.transform.position, basePoint) > 45 || Time.time - baseStarted > 300) { EndBase("Base shift finished. Anything still cooking needs watching; any undeposited items remain in my inventory."); return; }
            if (Time.time < baseActionAt) { ai.StopMoving(); return; }
            if (transferItem != null)
            {
                if (!fetched) {
                    if (!CanChest(sourceChest) || !CanChest(destinationChest)) { transferItem = null; return; }
                    if (!WalkBase(dt, sourceChest)) return;
                    if (!Transfer(sourceChest.GetInventory(), Body.GetInventory(), transferItem, transferItem.m_stack, sourceChest)) { transferItem = null; return; }
                    fetched = true; transferItem = null; // The next pass locates cargo by real inventory contents.
                }
            }
            // Cook first so a sorting job never lets food burn while moving stacks.
            if (mode == "cook_food" || mode == "manage_base") if (CookStep(dt)) return;
            var cargo = Body.GetInventory().GetAllItems().FirstOrDefault(i => IsStorable(i) && Destination(i));
            if (cargo != null) {
                var dest = Destination(cargo); baseNote = "Putting " + Localization.instance.Localize(cargo.m_shared.m_name) + " away";
                if (!WalkBase(dt, dest)) return;
                Transfer(Body.GetInventory(), dest.GetInventory(), cargo, cargo.m_stack, null, dest); baseActionAt = Time.time + .6f; return;
            }
            if (UseStoredMaterials && (mode == "sort_storage" || mode == "manage_base")) {
                foreach (var chest in Chests()) foreach (var item in chest.GetInventory().GetAllItems().Where(IsStorable).ToArray()) {
                    if (chestCategories.TryGetValue(chest.GetInstanceID(), out var category) && category == Category(item)) continue;
                    var dest = Destination(item, chest); if (!dest || !Body.GetInventory().CanAddItem(item, item.m_stack)) continue;
                    sourceChest = chest; destinationChest = dest; transferItem = item; fetched = false; baseNote = "Sorting " + Localization.instance.Localize(item.m_shared.m_name); return;
                }
            }
            if (mode == "cook_food" || mode == "manage_base") { baseNote = "Watching cooking stations • waiting for food, fuel or space"; baseActionAt = Time.time + 1; ai.StopMoving(); }
            else EndBase("Storage pass finished. Items with no suitable space stay where they are. Empty chests let me separate more item types.");
        }
        private bool CookStep(float dt)
        {
            var stations = UnityEngine.Object.FindObjectsOfType<CookingStation>().Where(OwnPiece).Where(c => !skippedBaseTargets.Contains(c.GetInstanceID())).OrderBy(c => Vector3.Distance(transform.position, c.transform.position)).ToArray();
            // Collect cooked drops before they accumulate. Only cooking outputs near a station qualify.
            var outputs = new HashSet<string>(stations.SelectMany(s => s.m_conversion).Where(c => c.m_to).Select(c => c.m_to.name));
            foreach (var collider in Physics.OverlapSphere(transform.position, 2.5f)) {
                var drop = collider.GetComponentInParent<ItemDrop>();
                if (!drop || !drop.m_itemData.m_dropPrefab || !outputs.Contains(drop.m_itemData.m_dropPrefab.name) || !stations.Any(s => Vector3.Distance(s.transform.position, drop.transform.position) < 4)) continue;
                gathered = 0; goal = 100; TakeDrop(drop); baseActionAt = Time.time + .3f; return true;
            }
            foreach (var station in stations) {
                bool done = false, busy = false;
                for (int i = 0; i < station.m_slots.Length; i++) {
                    object[] args = { i, "", 0f, Enum.ToObject(SlotMethod.GetParameters()[3].ParameterType.GetElementType(), 0) }; SlotMethod.Invoke(station, args);
                    if (((string)args[1]).Length == 0) continue;
                    if (Convert.ToInt32(args[3]) != 0) done = true; else busy = true;
                }
                if (done) {
                    baseNote = "Taking food off the cooking station"; if (!WalkBase(dt, station)) return true;
                    station.GetComponent<ZNetView>().InvokeRPC("RPC_RemoveDoneItem", new object[] { transform.position, 1 }); baseActionAt = Time.time + .4f; return true;
                }
                bool lit = !station.m_requireFire || (bool)AccessTools.Method(typeof(CookingStation), "IsFireLit").Invoke(station, null);
                bool fueled = !station.m_useFuel || (float)AccessTools.Method(typeof(CookingStation), "GetFuel").Invoke(station, null) > 0;
                if (!lit || !fueled || (int)AccessTools.Method(typeof(CookingStation), "GetFreeSlot").Invoke(station, null) < 0) continue;
                // Keep one batch at a time: stay nearby until ready, then store its output.
                if (busy) { baseNote = "Watching food cook"; WalkBase(dt, station); return true; }
                var valid = new HashSet<string>(station.m_conversion.Where(c => c.m_from).Select(c => c.m_from.name));
                var raw = Body.GetInventory().GetAllItems().FirstOrDefault(i => IsStorable(i) && i.m_dropPrefab && valid.Contains(i.m_dropPrefab.name));
                if (raw != null) { baseNote = "Adding food to the cooking station"; if (!WalkBase(dt, station)) return true; station.UseItem(Body, raw); Save(); baseActionAt = Time.time + .5f; return true; }
                // Fetch exactly one item, preserving the remainder in storage.
                if (UseStoredMaterials) foreach (var chest in Chests()) {
                    raw = chest.GetInventory().GetAllItems().FirstOrDefault(i => IsStorable(i) && i.m_dropPrefab && valid.Contains(i.m_dropPrefab.name));
                    if (raw == null || !Body.GetInventory().CanAddItem(raw, 1)) continue;
                    baseNote = "Fetching cooking ingredients"; if (!WalkBase(dt, chest)) return true;
                    Transfer(chest.GetInventory(), Body.GetInventory(), raw, 1, chest); baseActionAt = Time.time + .3f; return true;
                }
            }
            return false;
        }
    }
}
