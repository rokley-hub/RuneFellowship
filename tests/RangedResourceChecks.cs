using Rune.Shared;

internal static class RangedResourceChecks
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("Ranged resources: " + name); checks++; }
        var load = new CrossbowLoad();
        Check(!load.Loaded, "fresh crossbow is unloaded");
        for (int i = 0; i < 7; i++) load.Advance(.25f, 2, true, true);
        Check(!load.Loaded && load.Progress > .8f, "time cannot be skipped");
        load.Interrupt(); Check(load.Progress == 0, "evasion loses incomplete reload progress");
        load.Advance(.25f, 2, true, false); Check(load.Progress == 0, "unpaid resource drain cannot advance");
        load.Advance(.25f, 2, false, true); Check(load.Progress == 0, "unsafe reload cannot advance");
        for (int i = 0; i < 8; i++) load.Advance(.25f, 2, true, true);
        Check(load.Loaded, "completed reload enables one shot");
        load.Interrupt(); Check(load.Loaded, "completed load survives evasion");
        load.Reset(); Check(!load.Loaded, "native shot or weapon switch invalidates load");
        load.Advance(100, 2, true, true); Check(!load.Loaded, "frame hitch does not instantly reload");
        Check(CrossbowLoad.Duration(4, 0) == 4 && CrossbowLoad.Duration(4, 100) == 2, "own crossbow skill halves maximum loading time");
        Check(CrossbowLoad.Duration(4, 999) == 2, "reload skill bounded");
        var mana = new MagicResources();
        Check(mana.Maximum == 0 && !mana.Have(1), "no free magic without food");
        Check(mana.Have(0), "zero eitr attacks remain allowed");
        Check(mana.Eat("meal", 80, 100), "first eitr food accepted");
        Check(mana.Current == 0, "eating creates capacity rather than a free refill");
        Check(!mana.Eat("meal", 80, 100), "duplicate foods rejected");
        mana.Tick(1, 2); Check(mana.Current == 2, "bounded regeneration");
        mana.Add(1000); Check(mana.Current == mana.Maximum, "restoration capped to real capacity");
        var old = mana.Current; mana.Spend(20); Check(mana.Current == old - 20, "casting spends finite eitr");
        mana.Spend(float.NaN); mana.Add(float.PositiveInfinity); Check(mana.Current == old - 20, "invalid resource changes rejected");
        Check(!mana.Have(float.NaN) && !mana.Have(-1), "invalid resource checks fail");
        Check(mana.Eat("meal2", 50, 100) && mana.Eat("meal3", 20, 100), "three different foods supported");
        Check(!mana.Eat("meal4", 20, 100), "fourth food cannot bypass slot limit");
        var restored = MagicResources.Parse(mana.Serialize()); Check(restored.Serialize() == mana.Serialize(), "save reload preserves food time and energy");
        mana.Tick(100, 0); Check(mana.Maximum == 0 && mana.Current == 0, "food expiry removes capacity and excess energy");
        Check(mana.CanEat("meal"), "expired food slot reusable");
        Check(MagicResources.Parse("1|NaN|bad").Maximum == 0, "corrupt food record fails closed");
        Check(MagicResources.Parse("2|500|").Current == 0, "unknown save version does not grant energy");
        Check(!mana.Eat("invalid", float.NaN, 100) && !mana.Eat("invalid", 50, -1), "invalid food stats rejected");
        Check(restored.Maximum > 0, "body transfer snapshot independent");
        Check(Rules.Parse("use elemental magic")?.action == "cast", "elemental magic command");
        Check(Rules.Parse("shoot bolts")?.action == "shoot", "crossbow command");
        Check(Rules.Parse("don't cast spells") == null, "negated spell request does not cast");
        var meal = new MagicResources();
        Check(meal.Eat("stew", 40, 30, 0, 2, 100), "non-magic food supplies health and stamina");
        Check(meal.Health == 40 && meal.Stamina == 30 && meal.Healing == 2 && meal.Current == 0, "food capacity is separate from resource refill");
        meal.Tick(50, 0);
        Check(Math.Abs(meal.Health - 40 * Math.Pow(.5, .3)) < .001 && !meal.CanEat("stew"), "native food decay and half-duration refresh gate");
        meal.Tick(1, 0);
        Check(meal.CanEat("stew") && meal.Eat("stew", 40, 30, 0, 2, 100) && meal.Count == 1, "refresh consumes same food slot");
        meal.Tick(20, 0, 2);
        Check(Math.Abs(meal.Stamina - 30 * Math.Pow(.6, .3)) < .001, "world food rate controls duration");
        string nutrition = meal.Serialize();
        Check(MagicResources.Parse(nutrition).Serialize() == nutrition, "all nutrition and timers survive serialization");
        var oldFood = MagicResources.Parse("1|7|c3Rldw==,40,100,40");
        oldFood.RestoreFoodValues("stew", 25, 20, 2);
        Check(oldFood.Current == 7 && Math.Abs(oldFood.Health - 25 * Math.Pow(.4, .3)) < .001, "legacy food migration does not refill or renew duration");
        var invalidMeal = MagicResources.Parse("2|10|c3Rldw==,20,100,90,NaN,30,2,0");
        Check(invalidMeal.Count == 0 && invalidMeal.Current == 0, "corrupt nutrition rejected");
        meal.Tick(1000, 0);
        Check(meal.Health == 0 && meal.Stamina == 0 && meal.Healing == 0, "expired food cannot heal or provide capacity");
        Console.WriteLine($"PASS: {checks} reload/eitr/food/command checks (simulation, not native firing)");
    }
}
