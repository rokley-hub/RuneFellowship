namespace Rune.Shared
{
    // Pure decisions; native movement, attacks and healing remain in the game executor.
    public static class CombatPolicy
    {
        public static float RetreatAt(string style) => style == "Aggressive" ? .28f : style == "Balanced" ? .40f : .52f;
        public static float ResumeAt(string style) => style == "Aggressive" ? .45f : style == "Balanced" ? .55f : .65f;
        public static int CrowdLimit(string style) => style == "Aggressive" ? 6 : style == "Balanced" ? 4 : 3;
        public struct Decision { public bool recovering, protectPlayer, retreat; }
        public static Decision Decide(string style, float health, float playerHealth, bool wasRecovering, bool wasProtecting,
            int threats, bool playerThreat, bool bossNearby, bool bossAttacking, bool joinBossFights, bool playerFarFromBoss, bool manualRecall)
        {
            bool recovering = health < RetreatAt(style) || wasRecovering && health < ResumeAt(style);
            bool protection = !manualRecall && playerThreat && (playerHealth < .40f || wasProtecting && playerHealth < .55f);
            // Explicit boss permissions remain authoritative. Player protection overrides personal
            // HP, crowd and boss-health retreat preferences; it never grants extra health or damage.
            bool forbiddenBoss = bossNearby && !joinBossFights;
            bool danger = threats > 0 && (recovering || threats >= CrowdLimit(style) ||
                bossNearby && (forbiddenBoss || bossAttacking || health < ResumeAt(style) || playerFarFromBoss));
            return new Decision { recovering = recovering, protectPlayer = protection && !forbiddenBoss,
                retreat = !manualRecall && danger && (!protection || forbiddenBoss) };
        }
    }
}
