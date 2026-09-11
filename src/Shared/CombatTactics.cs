using System;

namespace Rune.Shared
{
    // Uses observed attacks/resources, never enemy names or model inference in the combat loop.
    public static class CombatTactics
    {
        public enum Action { Engage, Guard, Evade, Recover, Separate }
        public struct Facts
        {
            public bool attacking, area, unblockable, ranged, facingUs, protectingPlayer;
            public bool slowed, wet, damageOverTime, wasRecovering;
            public float distance, reach, reactionSeconds, stamina, maxStamina, health, incomingDamage, blockPower;
            public int nearbyEnemies;
        }
        public static Action Decide(Facts f)
        {
            bool incoming = f.attacking && (f.area || f.facingUs) && f.distance <= f.reach;
            if (incoming && f.reactionSeconds >= .12f) {
                if (f.area || f.unblockable || f.ranged || f.stamina < 20 || f.blockPower <= 0 ||
                    f.incomingDamage > f.blockPower * .8f) return Action.Evade;
                return Action.Guard;
            }
            float reserve = (f.slowed || f.wet ? .30f : .22f) * Math.Max(1, f.maxStamina);
            if (!f.protectingPlayer && (f.stamina < reserve || f.wasRecovering && f.stamina < reserve + 15 ||
                f.damageOverTime && f.health < .5f)) return Action.Recover;
            if (f.nearbyEnemies >= 3 && !f.protectingPlayer) return Action.Separate;
            return Action.Engage;
        }
        public static bool ThreatVisible(bool alive, bool hostile, bool boss, bool bossesAllowed, float distance, bool visible) =>
            alive && hostile && (!boss || bossesAllowed) && distance <= 32 && visible;

        // Endpoints must be safe. While escaping a zone, allow a non-increasing exposure along
        // the route, but never enter a new zone from safety. Geometry is checked separately.
        public static bool ExposureStep(float previous, float next, float initial) =>
            next <= previous + .05f && (initial > 0 || next <= 0);
    }
}
