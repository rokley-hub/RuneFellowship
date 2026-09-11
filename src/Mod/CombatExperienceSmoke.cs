using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    // Only dispatched after IntegrationSmoke verifies a separate world/character save root.
    internal static class CombatExperienceSmoke
    {
        internal static IEnumerator Run(Player player, string output)
        {
            var results = new List<string>();
            void Check(bool ok, string note) { results.Add((ok ? "PASS: " : "FAILED: ") + note); File.WriteAllLines(output, results); }
            var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(Plugin.PrefabFor("dwarf")), player.transform.position + Vector3.right * 3, Quaternion.identity);
            var companion = go.GetComponent<Companion>(); companion.Bind(player, "xp-fixture", "dwarf", "XP fixture");
            yield return new WaitForSecondsRealtime(3);
            Character enemy = null;
            try {
                Check(companion.Ready, "fixture companion initialized");
                companion.Order("stay", 1);
                enemy = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("Greydwarf"), player.transform.position + Vector3.forward * 4, Quaternion.identity).GetComponent<Character>();
                enemy.GetComponent<MonsterAI>().enabled = false;
                enemy.SetMaxHealth(10000); enemy.SetHealth(10000);
                var hit = new HitData { m_skill = Skills.SkillType.Axes, m_damage = new HitData.DamageTypes { m_blunt = 60 } };
                hit.SetAttacker(companion.Body);
                // Raw damage no longer grants XP; native qualifying practice events do.
                var apply = AccessTools.Method(typeof(Character), "ApplyDamage");
                apply.Invoke(enemy, new object[] { hit, false, false, HitData.DamageModifier.Normal });
                Check(companion.Body.GetSkillLevel(Skills.SkillType.Axes) == 0, "raw health loss cannot double-award practice XP");
                companion.Body.RaiseSkill(Skills.SkillType.Axes, 1000);
                Check(companion.Body.GetSkillLevel(Skills.SkillType.Axes) == 1, "native practice hook awards one own skill level");
                Check(companion.Body.GetSkillLevel(Skills.SkillType.Swords) == 0, "no cross-training to swords");
                int before = companion.WeaponLevel(Skills.SkillType.Axes);
                enemy.SetTamed(true);
                apply.Invoke(enemy, new object[] { hit, false, false, HitData.DamageModifier.Normal });
                Check(companion.WeaponLevel(Skills.SkillType.Axes) == before, "tamed creature damage awards no experience");
                companion.Body.SetHealth(10);
                for (int i = 0; i < 100; i++) companion.Body.RaiseSkill(Skills.SkillType.Axes, 1000000);
                Check(companion.Body.GetHealth() <= 10 && companion.Body.GetMaxHealth() == player.m_baseHP, "skill cannot grant health or healing");
                companion.Body.AddStamina(1000);
                Check(companion.CombatStamina == player.m_baseStamina && companion.Body.GetStaminaPercentage() == 1, "native stamina addition respects unfed player base capacity");
                var snapshot = companion.SnapshotForReplacement();
                Check(Rune.Shared.CombatExperience.Parse(snapshot.CombatXp).Level((int)Skills.SkillType.Axes) == 100, "body replacement snapshot includes experience");
                AccessTools.Method(typeof(Companion), "LoadExperience").Invoke(companion, null);
                Check(companion.WeaponLevel(Skills.SkillType.Axes) == 100, "ZDO save/load retains mastery");
                Check(player.m_customData.ContainsKey("rune.combat." + ZNet.instance.GetWorldUID() + ".xp-fixture"), "owner backup is scoped to companion and world");
            } catch (Exception ex) { Check(false, ex.ToString()); }
            finally {
                if (enemy) UnityEngine.Object.Destroy(enemy.gameObject);
                if (go) UnityEngine.Object.Destroy(go);
            }
        }
    }
}
