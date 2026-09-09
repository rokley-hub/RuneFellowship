using Rune.Shared;

internal static class CombatPolicyChecks
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("Combat policy: " + name); count++; }
        CombatPolicy.Decision Decide(string style = "Balanced", float hp = .3f, float player = 1f, bool recovering = false,
            bool protecting = false, int enemies = 1, bool threat = true, bool boss = false, bool attack = false, bool allowed = true, bool far = false, bool recall = false) =>
            CombatPolicy.Decide(style, hp, player, recovering, protecting, enemies, threat, boss, attack, allowed, far, recall);
        foreach (var style in new[] { "Cautious", "Balanced", "Aggressive" }) {
            float retreat = CombatPolicy.RetreatAt(style), resume = CombatPolicy.ResumeAt(style);
            Check(resume > retreat && resume <= .65f, style + " recovery range");
            Check(Decide(style, retreat - .01f).retreat, style + " enters recovery");
            Check(!Decide(style, retreat).recovering, style + " strict retreat boundary");
            Check(Decide(style, resume - .01f, recovering: true).retreat, style + " stays recovering until lower resume target");
            Check(!Decide(style, resume, recovering: true).retreat, style + " resumes exactly at target");
            var protect = Decide(style, .01f, .2f, enemies: 8);
            Check(protect.protectPlayer && !protect.retreat, style + " injured player overrides even critical companion HP and crowds");
        }
        Check(!Decide(hp: 54.22f / 150, recovering: true, enemies: 0, threat: false).retreat, "Recorded low health no longer stops safe work");
        Check(!Decide(hp: 98.3254f / 150, recovering: true).recovering, "Recorded hammer-order HP exceeds new Balanced resume threshold");
        Check(!Decide("Aggressive", .32f).retreat, "No contradictory 35 percent global guard");
        Check(!Decide(player: .2f, enemies: 0, threat: false).protectPlayer, "No enemies means no invented rescue combat");
        Check(Decide(player: .399f).protectPlayer && !Decide(player: .4f).protectPlayer, "Player trigger boundary");
        Check(Decide(player: .54f, protecting: true).protectPlayer && !Decide(player: .55f, protecting: true).protectPlayer, "Player protection exit boundary");
        Check(!Decide(player: .2f, recall: true).protectPlayer && !Decide(player: .2f, recall: true).retreat, "Follow overrides combat and personal recovery");
        Check(Decide(player: .2f, boss: true, attack: true).protectPlayer, "Emergency protection overrides permitted boss attack avoidance");
        Check(!Decide(player: .2f, boss: true, allowed: false).protectPlayer, "Boss permission preserved");
        Check(Decide(hp: 1f, enemies: 4).retreat && !Decide(hp: 1f, enemies: 3).retreat, "Ordinary crowd avoidance preserved");
        Check(Decide(hp: 1f, boss: true, attack: true).retreat, "Healthy player's companion still respects boss windups");
        Console.WriteLine($"Passed {count} combat policy checks.");
    }
}
