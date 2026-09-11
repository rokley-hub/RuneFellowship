using Rune.Shared;
using System.Globalization;

internal static class CombatExperienceChecks
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("Combat experience: " + name); checks++; }
        var xp = new CombatExperience();
        Check(xp.Mastery == 0 && CombatExperience.Required(0) == 1, "native first level requirement");
        Check(Math.Abs(CombatExperience.Required(49) - (Math.Pow(50, 1.5) * .5 + .5)) < 1e-9, "native later level curve");
        xp.Add(7, .5); Check(xp.Level(7) == 0, "partial action progress");
        xp.Add(7, .5); Check(xp.Level(7) == 1 && xp.Level(1) == 0, "independent weapon skills");
        xp.Add(6, 10000); Check(xp.Level(6) == 1 && xp.Mastery == 1, "one level per event; block has no overall stat growth");
        double before = xp.Xp(7);
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity, 0 }) xp.Add(7, invalid);
        xp.Add(999, 500); xp.Add(13, 500);
        Check(xp.Xp(7) == before && xp.Xp(999) == 0 && xp.Xp(13) == 0, "invalid grants and unsupported skills rejected");
        for (int family = 1; family <= 14; family++) {
            if (!CombatExperience.Supported(family)) continue;
            var learner = new CombatExperience();
            for (int level = 1; level <= 100; level++) {
                learner.Add(family, double.MaxValue);
                Check(learner.Level(family) == level, "native single-level cap and rounding family " + family + " level " + level);
            }
            learner.Add(family, double.MaxValue);
            Check(learner.Level(family) == 100 && CombatExperience.Parse(learner.Serialize()).Level(family) == 100, "saved maximum and cap family " + family);
        }
        double legacyTotal = 0;
        for (int level = 0; level <= 100; level++) {
            double requirement = 8 + 2 * level + level * level * .05;
            var migrated = CombatExperience.Parse("1|7:" + (legacyTotal + (level < 100 ? requirement * .4 : 0)).ToString("R", CultureInfo.InvariantCulture));
            Check(migrated.Level(7) == level, "legacy earned level retained " + level);
            double converted = Enumerable.Range(0, level).Sum(CombatExperience.Required) + (level < 100 ? CombatExperience.Required(level) * .4 : 0);
            Check(Math.Abs(migrated.Xp(7) - converted) < 1e-7, "partial legacy progress retained " + level);
            legacyTotal += requirement;
        }
        Check(Math.Abs(CombatExperience.DamageFactor(0, 0) - .25) < .00001 && Math.Abs(CombatExperience.DamageFactor(0, 1) - .55) < .00001, "level zero native damage range");
        Check(Math.Abs(CombatExperience.DamageFactor(100, 0) - .85) < .00001 && CombatExperience.DamageFactor(100, 1) == 1, "level hundred native damage range");
        string saved = xp.Serialize();
        var culture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nl-NL"); Check(CombatExperience.Parse(saved).Serialize() == saved, "save/reload independent of Dutch locale"); }
        finally { CultureInfo.CurrentCulture = culture; }
        var restored = CombatExperience.Parse(saved);
        Check(restored.Xp(7) == xp.Xp(7), "owner backup restoration retains complete skill progress without death reduction");
        restored.Add(1, 1);
        Check(xp.Level(1) == 0 && restored.Level(1) == 1, "companion ledgers remain independent");
        Check(CombatExperience.Parse("1|7:8;7:9999;1:NaN;2:Infinity;999:123;3:-7").Level(7) == 1, "corrupt duplicate legacy rows cannot inflate skill");
        Check(CombatExperience.Parse("2|7:1;7:9999;1:NaN;2:Infinity;999:123;3:-7").Level(7) == 1, "corrupt duplicate current rows cannot inflate skill");
        Check(CombatExperience.Parse("future|7:1000").Mastery == 0 && CombatExperience.Parse(new string('x', 3000)).Mastery == 0, "unknown oversized saves bounded");
        float Score(float damage, bool bow = false, float distance = 8, float stamina = 100, float cost = 10, int level = 0, float wear = 1) => CombatWeaponScore.Evaluate(damage, level, bow, distance, 1, cost, stamina, wear);
        Check(Score(60) > Score(20, level: 100) && Score(40, level: 100) > Score(40), "gear and native skill both affect expected damage");
        Check(Score(40, bow: true, distance: 3) < Score(20), "close threat favors melee");
        Check(Score(40, bow: true) > Score(40), "clear ranged distance favors bow");
        Check(Score(50, stamina: 10, cost: 40) < Score(25, stamina: 10, cost: 5), "exhaustion favors affordable weapon");
        Check(Score(40, wear: .05f) < Score(40) && Score(40, wear: 0) == 0, "damaged and broken weapons penalized");
        Check(Score(0, level: 100) == 0, "mastery never defeats damage immunity");
        Console.WriteLine($"PASS: {checks} combat progression/scoring checks (simulation, not live gameplay)");
    }
}
