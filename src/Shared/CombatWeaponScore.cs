using System;
namespace Rune.Shared
{
    public static class CombatWeaponScore
    {
        public static float Evaluate(float effectiveDamage, int level, bool bow, float distance, float interval, float staminaCost, float stamina, float durability)
        {
            if (float.IsNaN(effectiveDamage) || float.IsInfinity(effectiveDamage) || effectiveDamage <= 0 || durability <= 0) return 0;
            float mastery = CombatExperience.DamageFactor(level, .5f);
            float positioning = bow ? distance < 5 ? .15f : 1.25f : 1;
            float reserve = Math.Max(0, staminaCost) * (1 - Math.Max(0, Math.Min(100, level)) * .0033f);
            float readiness = stamina < reserve ? .1f : stamina - reserve < 12 ? .65f : 1;
            float wear = durability < .1f ? .6f : 1;
            return effectiveDamage * mastery * positioning * readiness * wear / Math.Max(.8f, interval);
        }
    }
}
