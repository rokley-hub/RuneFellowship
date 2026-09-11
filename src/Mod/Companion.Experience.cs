using System;
using HarmonyLib;
using System.Linq;
using Rune.Shared;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        private CombatExperience experience = new CombatExperience();
        private float baseCombatHealth, baseCombatStamina = 75, baseStaminaRegen = 5, staminaRegenMultiplier = 1, staminaRegenDelay = 1;
        private string ExperienceKey => "rune.combat." + ZNet.instance.GetWorldUID() + "." + Id;
        internal float MaxCombatStamina => Rune.Shared.Rules.IsWolf(Appearance) ? 100 : baseCombatStamina + magic.Stamina;
        internal Skills.SkillType ProgressionSkill(Skills.SkillType skill) => skill == Skills.SkillType.None && Rune.Shared.Rules.IsWolf(Appearance) ? Skills.SkillType.Unarmed : skill;
        internal int WeaponLevel(Skills.SkillType skill) => experience.Level((int)ProgressionSkill(skill));
        private void LoadExperience()
        {
            string saved = view.GetZDO().GetString("rune.combatXp", "");
            if (saved.Length == 0 && Player && Player.GetPlayerID() == Owner) Player.m_customData.TryGetValue(ExperienceKey, out saved);
            if (!string.IsNullOrEmpty(saved) && saved.StartsWith("1|", StringComparison.Ordinal) && Player && Player.GetPlayerID() == Owner &&
                !Player.m_customData.ContainsKey(ExperienceKey + ".legacy-v1")) Player.m_customData[ExperienceKey + ".legacy-v1"] = saved;
            experience = CombatExperience.Parse(saved);
            // Read the loaded player's base configuration, not food buffs, current pools or learned skills.
            baseCombatHealth = Rune.Shared.Rules.IsWolf(Appearance) ? Body.m_health : Player ? Player.m_baseHP : 25;
            if (Player) {
                baseCombatStamina = Mathf.Max(1, Player.m_baseStamina);
                baseStaminaRegen = Mathf.Max(0, Player.m_staminaRegen);
                staminaRegenMultiplier = Mathf.Max(0, Player.m_staminaRegenTimeMultiplier);
                staminaRegenDelay = Mathf.Max(0, Player.m_staminaRegenDelay);
            }
            ApplyFoodCapacity();
        }
        private void SaveExperience()
        {
            string saved = experience.Serialize();
            view.GetZDO().Set("rune.combatXp", saved);
            // Owner's character backup survives unsummoning and death, scoped by world/id.
            if (Player && Player == global::Player.m_localPlayer && Player.GetPlayerID() == Owner)
                Player.m_customData[ExperienceKey] = saved;
        }
        private void ApplyFoodCapacity()
        {
            if (baseCombatHealth <= 0) return;
            float health = Body.GetHealth();
            Body.SetMaxHealth(baseCombatHealth + (Rune.Shared.Rules.IsWolf(Appearance) ? 0 : magic.Health));
            Body.SetHealth(Mathf.Min(health, Body.GetMaxHealth())); // A level never heals a wound.
            CombatStamina = Mathf.Min(CombatStamina, MaxCombatStamina);
        }
        internal void EarnCombatExperience(Skills.SkillType skill, double reward)
        {
            if (!Ready || !view || !view.IsValid() || !view.IsOwner() || Body.IsDead()) return;
            skill = ProgressionSkill(skill);
            // Definitions only: never copy the owner's learned skill values.
            var definition = Player ? Player.GetSkills().m_skills.FirstOrDefault(d => d.m_skill == skill) : null;
            if (definition == null || !CombatExperience.Supported((int)skill)) return;
            float modifier = 1;
            Body.GetSEMan().ModifyRaiseSkill(skill, ref modifier);
            experience.Add((int)skill, reward * definition.m_increseStep * modifier * Game.m_skillGainRate);
            SaveExperience(); // ZDO + in-memory owner backup; no per-hit file writes, no death loss.
        }
    }

    // Native hit/projectile/block events decide whether practice qualifies.
    // Suppress the tameable-owner forwarding path only for Rune companions.
    [HarmonyPatch(typeof(Character), nameof(Character.RaiseSkill))]
    internal static class RunePracticeExperience
    {
        static bool Prefix(Character __instance, Skills.SkillType __0, float __1)
        {
            var companion = __instance.GetComponent<Companion>();
            if (!companion) return true;
            companion.EarnCombatExperience(__0, __1);
            return false;
        }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.GetSkillLevel))]
    internal static class RuneCombatSkillLevel
    {
        static bool Prefix(Character __instance, Skills.SkillType __0, ref float __result) {
            var companion = __instance.GetComponent<Companion>();
            if (!companion || !CombatExperience.Supported((int)companion.ProgressionSkill(__0))) return true;
            __result = companion.WeaponLevel(__0); return false;
        }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.GetSkillFactor))]
    internal static class RuneCombatSkillFactor
    {
        static bool Prefix(Character __instance, Skills.SkillType __0, ref float __result) {
            var companion = __instance.GetComponent<Companion>();
            if (!companion || !CombatExperience.Supported((int)companion.ProgressionSkill(__0))) return true;
            __result = companion.WeaponLevel(__0) / 100f; return false;
        }
    }
    [HarmonyPatch(typeof(Character), nameof(Character.GetRandomSkillFactor))]
    internal static class RuneCombatDamageProgression
    {
        static bool Prefix(Character __instance, Skills.SkillType __0, ref float __result) {
            var companion = __instance.GetComponent<Companion>();
            if (!companion || !CombatExperience.Supported((int)companion.ProgressionSkill(__0))) return true;
            __result = CombatExperience.DamageFactor(companion.WeaponLevel(__0), UnityEngine.Random.value);
            return false;
        }
    }
}
