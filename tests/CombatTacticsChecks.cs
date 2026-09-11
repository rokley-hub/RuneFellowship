using Rune.Shared;
using A = Rune.Shared.CombatTactics.Action;

internal static class CombatTacticsChecks
{
    internal static void Run()
    {
        int count = 0;
        void Check(bool ok, string scenario) { if (!ok) throw new Exception("Combat tactics: " + scenario); count++; }
        CombatTactics.Facts Ready() => new() { maxStamina = 100, stamina = 100, health = 1, blockPower = 50,
            incomingDamage = 20, distance = 3, reach = 5, reactionSeconds = .2f, facingUs = true, nearbyEnemies = 1 };
        var f = Ready(); Check(CombatTactics.Decide(f) == A.Engage, "healthy supported encounter engages");
        f.attacking = true; Check(CombatTactics.Decide(f) == A.Guard, "ordinary visible melee windup guards");
        f.reactionSeconds = .05f; Check(CombatTactics.Decide(f) == A.Engage, "no instant perfect reaction");
        f.reactionSeconds = .2f; f.area = true; Check(CombatTactics.Decide(f) == A.Evade, "area slam moves away instead of trying to parry");
        f.facingUs = false; Check(CombatTactics.Decide(f) == A.Evade, "radial attack remains dangerous from behind");
        f.area = false; Check(CombatTactics.Decide(f) == A.Engage, "attack directed away does not interrupt offense");
        f.facingUs = true; f.unblockable = true; Check(CombatTactics.Decide(f) == A.Evade, "unblockable attack sidesteps");
        f.unblockable = false; f.ranged = true; Check(CombatTactics.Decide(f) == A.Evade, "ranged volley moves laterally");
        f.ranged = false; f.incomingDamage = 100; Check(CombatTactics.Decide(f) == A.Evade, "strong attack exceeds small shield");
        f.incomingDamage = 20; f.blockPower = 0; Check(CombatTactics.Decide(f) == A.Evade, "broken/missing blocker cannot guard");
        f.blockPower = 50; f.stamina = 10; Check(CombatTactics.Decide(f) == A.Evade, "exhausted guard avoids breaking");
        f = Ready(); f.attacking = true; f.distance = 10; Check(CombatTactics.Decide(f) == A.Engage, "distant melee windup is not an incoming hit");
        f = Ready(); f.stamina = 18; Check(CombatTactics.Decide(f) == A.Recover, "reserve stamina before empty");
        f.stamina = 28; Check(CombatTactics.Decide(f) == A.Engage, "healthy movement can use normal reserve");
        f.wet = true; Check(CombatTactics.Decide(f) == A.Recover, "wet increases reserve");
        f.wet = false; f.slowed = true; Check(CombatTactics.Decide(f) == A.Recover, "frost/tar increase reserve");
        f = Ready(); f.wasRecovering = true; f.stamina = 28; Check(CombatTactics.Decide(f) == A.Recover, "recovery hysteresis prevents oscillation");
        f.stamina = 40; Check(CombatTactics.Decide(f) == A.Engage, "recovery resumes below full stamina");
        f = Ready(); f.damageOverTime = true; f.health = .4f; Check(CombatTactics.Decide(f) == A.Recover, "poisoned/burning wounded fighter creates room");
        f.health = .9f; Check(CombatTactics.Decide(f) == A.Engage, "healthy poison does not cause permanent retreat");
        f = Ready(); f.nearbyEnemies = 3; Check(CombatTactics.Decide(f) == A.Separate, "nearby group triggers separation");
        f.nearbyEnemies = 2; Check(CombatTactics.Decide(f) == A.Engage, "two manageable enemies still engage");
        f.nearbyEnemies = 6; f.stamina = 10; f.damageOverTime = true; f.health = .2f; f.protectingPlayer = true;
        Check(CombatTactics.Decide(f) == A.Engage, "injured-player protection overrides personal retreat preference");
        f.attacking = true; f.area = true; Check(CombatTactics.Decide(f) == A.Evade, "protecting player still avoids area damage");
        f = Ready(); f.maxStamina = 180; f.stamina = 30; Check(CombatTactics.Decide(f) == A.Recover, "mastery growth scales reserve");
        Check(!CombatTactics.ThreatVisible(true, true, false, true, 10, false), "no through-wall target acquisition");
        Check(!CombatTactics.ThreatVisible(true, true, true, false, 10, true), "boss permission remains authoritative");
        Check(!CombatTactics.ThreatVisible(true, false, false, true, 10, true), "friendlies excluded");
        Check(!CombatTactics.ThreatVisible(false, true, false, true, 10, true), "dead enemies excluded");
        Check(!CombatTactics.ThreatVisible(true, true, false, true, 50, true), "bounded perception");
        Check(CombatTactics.ThreatVisible(true, true, true, true, 10, true), "visible permitted boss supported by generic tactics");
        Check(CombatTactics.ExposureStep(0, 0, 0), "safe ground remains safe");
        Check(!CombatTactics.ExposureStep(0, .01f, 0), "cannot enter a new damage zone");
        Check(CombatTactics.ExposureStep(5, 3, 5), "can move out of a damage zone");
        Check(!CombatTactics.ExposureStep(2, 3, 5), "cannot deepen exposure during escape");
        Check(CombatTactics.ExposureStep(2, 0, 5), "can finish escape");
        Check(!CombatTactics.ExposureStep(0, 2, 0), "each overlapping zone must independently avoid new exposure");
        Console.WriteLine($"PASS: {count} combat tactics scenarios (pure policy; native movement remains untested)");
    }
}
