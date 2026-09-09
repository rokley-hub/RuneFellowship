using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace Rune.Mod
{
    // Entry is guarded by IntegrationSmoke's verified isolated save paths.
    internal static class CombatSmoke
    {
        internal static IEnumerator Run(Plugin plugin, Player player, string output)
        {
            var report = new List<string>(); Companion npc = null; Character enemy = null;
            void Check(bool ok, string note) { report.Add((ok ? "PASS: " : "FAILED: ") + note); File.WriteAllLines(output, report); }
            void ClearEnemies() { foreach (var c in Character.GetAllCharacters().ToArray()) if (c && !(c is Player) && !c.GetComponent<Companion>()) UnityEngine.Object.Destroy(c.gameObject); }
            ClearEnemies();
            try { var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), player.transform.position + Vector3.right * 2, Quaternion.identity); npc = go.GetComponent<Companion>(); npc.Bind(player, "bot-combat-test", "dwarf", "Combat test", "female"); }
            catch (Exception e) { Check(false, e.ToString()); }
            yield return new WaitForSecondsRealtime(3);
            if (!npc || !npc.Ready) { Check(false, "Companion ready"); yield break; }
            ItemDrop.ItemData Add(string prefab, int amount = 1) {
                var go = ZNetScene.instance.GetPrefab(prefab); var item = go.GetComponent<ItemDrop>().m_itemData.Clone(); item.m_dropPrefab = go; item.m_stack = amount; item.m_durability = item.GetMaxDurability(); npc.Body.GetInventory().AddItem(item); return item;
            }
            string Order(string action, string item = "") => npc.Order(action, 20, item);
            try {
                Order("stay"); npc.Body.UnequipAllItems(); npc.Body.GetInventory().RemoveAll();
                var club = Add("Club"); var bow = Add("Bow"); Add("ShieldWood"); Add("ArrowWood", 30);
                int playerItems = player.GetInventory().GetAllItems().Sum(i => i.m_stack);
                string reply = Order("equip_weapon", "club");
                Check(npc.LastOrderAccepted && npc.Body.GetCurrentWeapon() == club && player.GetInventory().GetAllItems().Sum(i => i.m_stack) == playerItems, "Own club equipped without borrowing: " + reply);
                club.m_durability = club.GetMaxDurability() * .25f;
                Check(npc.EquippedWeaponStatus.Contains("25%"), "HUD real durability: " + npc.EquippedWeaponStatus);
                club.m_durability = 0; Check(npc.EquippedWeaponStatus.Contains("BROKEN"), "Broken display"); club.m_durability = club.GetMaxDurability();
                Order("equip_weapon", "bow"); Check(npc.Body.GetCurrentWeapon() == bow, "HUD follows equipment change: " + npc.EquippedWeaponStatus);
                foreach (var i in npc.Body.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo).ToArray()) npc.Body.GetInventory().RemoveItem(i);
                Check(npc.EquippedWeaponStatus.Contains("NO ARROWS"), "No arrows display");
                Order("shoot"); Check(!npc.LastOrderAccepted, "Shoot without ammunition rejected"); Add("ArrowWood", 30);
                Order("equip_weapon", "club"); Order("block"); npc.Tick(.1f);
                Check(npc.Body.IsBlocking(), "Native blocking state from command");
                npc.CombatStamina = 100;
                var hit = new HitData { m_dir = -npc.transform.forward, m_point = npc.Body.GetCenterPoint(), m_blockable = true, m_damage = new HitData.DamageTypes { m_blunt = 5 } };
                AccessTools.Field(typeof(Humanoid), "m_blockTimer").SetValue(npc.Body, 1f);
                bool blocked = (bool)AccessTools.Method(typeof(Humanoid), "BlockAttack").Invoke(npc.Body, new object[] { hit, null });
                Check(blocked && hit.GetTotalDamage() < 5 && npc.CombatStamina < 100, "Native block reduces damage and spends stamina: blocked=" + blocked + "; damage=" + hit.GetTotalDamage() + "; stamina=" + npc.CombatStamina + "; drain=" + npc.Body.m_blockStaminaDrain + "; parryDrain=" + npc.Body.m_perfectBlockStaminaDrain);
                npc.Body.UseStamina(7); Check(npc.CombatStamina <= 93, "Native stamina calls spend the companion pool");
                npc.CombatStamina = 0; Check(!npc.Body.HaveStamina(1), "Exhaustion rejects native stamina request"); npc.CombatStamina = 100;
                Order("follow"); npc.Tick(.1f); Check(!npc.Body.IsBlocking() && npc.TaskLabel.Contains("Following"), "Follow cancels guard"); Order("stay");
                var pins = (System.Collections.IDictionary)AccessTools.Field(typeof(Plugin), "companionPins").GetValue(plugin);
                Check(pins.Contains(npc) && !((Minimap.PinData)pins[npc]).m_save, "Live companion has unsaved map pin");
                npc.Body.SetHealth(npc.Body.GetMaxHealth());
                var pos = npc.transform.position + npc.transform.forward * 12; if (Heightmap.GetHeight(pos, out var h)) pos.y = h;
                enemy = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Greydwarf"), pos, Quaternion.identity).GetComponent<Character>(); enemy.SetMaxHealth(1000); enemy.SetHealth(1000);
            } catch (Exception e) { Check(false, e.ToString()); }
            yield return new WaitForSecondsRealtime(1);
            int before = npc.Count("ArrowWood"); float beforeHealth = enemy ? enemy.GetHealth() : 0; bool projectileSeen = false;
            var testBow = npc.Body.GetInventory().GetAllItems().First(i => i.m_dropPrefab && i.m_dropPrefab.name == "Bow"); float bowDurability = testBow.m_durability;
            try { string reply = Order("shoot"); Check(npc.LastOrderAccepted, "Shoot accepted: " + reply); } catch (Exception e) { Check(false, e.ToString()); }
            float end = Time.realtimeSinceStartup + 35;
            while (Time.realtimeSinceStartup < end && enemy && !enemy.IsDead()) {
                yield return new WaitForSecondsRealtime(.1f); npc.Body.Heal(5, false); player.Heal(5, false);
                projectileSeen |= UnityEngine.Object.FindObjectsOfType<Projectile>().Any(p => AccessTools.Field(typeof(Projectile), "m_owner").GetValue(p) == npc.Body);
                if (npc.Count("ArrowWood") < before && enemy.GetHealth() < beforeHealth) break;
            }
            Check(npc.Count("ArrowWood") < before, "Native arrows consumed: " + before + " -> " + npc.Count("ArrowWood") + "; " + npc.TaskLabel);
            Check(testBow.m_durability < bowDurability, "Native bow wear: " + bowDurability + " -> " + testBow.m_durability);
            Check(projectileSeen || enemy && enemy.GetHealth() < beforeHealth, "Native projectile / hostile hit: observed=" + projectileSeen + "; targetHP=" + (enemy ? enemy.GetHealth().ToString() : "gone"));
            Order("follow"); Check(npc.TaskLabel.Contains("Following"), "Follow cancels shooting");
            try {
                Order("stay"); npc.Body.SetHealth(20); player.Heal(500, false);
                enemy.transform.position = npc.transform.position + Vector3.forward * 8;
                var safety = AccessTools.Method(typeof(Companion), "TerrainOrCombatSafety");
                bool retreat = (bool)safety.Invoke(npc, new object[] { .1f });
                var destination = (Vector3)AccessTools.Field(typeof(Companion), "retreatDestination").GetValue(npc);
                enemy.transform.position = npc.transform.position + Vector3.forward * 28;
                bool held = (bool)safety.Invoke(npc, new object[] { .1f });
                Check(retreat && held && destination == (Vector3)AccessTools.Field(typeof(Companion), "retreatDestination").GetValue(npc), "Recovery holds one safe destination after threat leaves radius");
                npc.Body.Heal(500, false);
                var projectilePrefab = ZNetScene.instance.GetPrefab("ArrowWood").GetComponent<ItemDrop>().m_itemData.m_shared.m_attack.m_attackProjectile;
                var shot = UnityEngine.Object.Instantiate(projectilePrefab, npc.Body.GetCenterPoint() + Vector3.forward * 8, Quaternion.identity).GetComponent<Projectile>();
                shot.Setup(enemy, Vector3.back * 12, 0, new HitData(), null, null);
                Check(RuneProjectileWatch.Active.Contains(shot) && (bool)AccessTools.Method(typeof(Companion), "AvoidProjectile").Invoke(npc, new object[] { shot }), "Incoming hostile native arrow selects a safe sidestep even without a collider");
                UnityEngine.Object.DestroyImmediate(shot.gameObject);
                var treePrefab = ZNetScene.instance.m_prefabs.First(p => p && p.GetComponent<TreeLog>() && p.GetComponent<Rigidbody>());
                var log = UnityEngine.Object.Instantiate(treePrefab, npc.transform.position + Vector3.forward, Quaternion.identity);
                var solid = log.GetComponentsInChildren<Collider>().First(c => c.enabled && !c.isTrigger);
                log.transform.position += npc.Body.GetCenterPoint() + Vector3.forward - solid.bounds.center;
                log.GetComponent<Rigidbody>().isKinematic = false; log.GetComponent<Rigidbody>().velocity = Vector3.back * 3;
                Physics.SyncTransforms(); AccessTools.Field(typeof(Companion), "hazardScanAt").SetValue(npc, 0f); AccessTools.Field(typeof(Companion), "hazardUntil").SetValue(npc, 0f);
                bool avoids = (bool)AccessTools.Method(typeof(Companion), "AvoidMovingHazards").Invoke(npc, new object[] { .1f });
                Check(avoids && npc.TaskLabel.Contains("tree"), "Moving native tree log selects an escape destination: prefab=" + treePrefab.name + "; bounds=" + solid.bounds + "; nearbyColliders=" + Physics.OverlapSphere(npc.transform.position, 18).Length + "; status=" + npc.TaskLabel); UnityEngine.Object.Destroy(log);
                foreach (var i in npc.Body.GetInventory().GetAllItems()) i.m_customData["rune.personal"] = "1";
                var wood = ZNetScene.instance.GetPrefab("Wood"); player.GetInventory().AddItem(wood, 35);
                int startWood = player.GetInventory().CountItems(wood.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
                npc.Body.GetInventory().AddItem(wood, 50); npc.Body.GetInventory().AddItem(wood, 8); AccessTools.Method(typeof(Companion), "Deliver").Invoke(npc, null);
                Check(npc.Count("Wood") == 0 && player.GetInventory().CountItems(wood.GetComponent<ItemDrop>().m_itemData.m_shared.m_name) == startWood + 58 && plugin.Note.Contains("58 wood"), "Stack merging preserves all 58 wood and reports the same transferred count");
            } catch (Exception e) { Check(false, e.ToString()); }
            ClearEnemies(); File.WriteAllLines(output, report);
        }
    }
}
