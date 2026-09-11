using Rune.Shared;

static class LocalCompanionChecks
{
    public static void Run()
    {
        int count = 0;
        void Check(bool value, string why) { if (!value) throw new Exception(why); count++; }
        const string world = "standalone-fixture";
        long now = Rules.Now;
        var ids = new HashSet<string>();
        for (int slot = 0; slot < LocalCompanions.SlotCount; slot++) {
            var profile = LocalCompanions.DefaultProfile(slot);
            Check(ids.Add(profile.companionId), "In-game slots need distinct saved identities.");
            Check(profile.companionId != "rune", "A fresh local slot must not reuse the default desktop companion ID.");
            foreach (var action in LocalCompanions.Actions) {
                var command = LocalCompanions.Order(profile, action, world, now);
                Check(Rules.Validate(command, world, now) == "", "Fresh install default must produce a valid " + action);
                Check(command.amount == 20 && command.steps.Length == 0, "Local buttons are bounded single orders, not implicit plans.");
            }
        }
        var existing = new Command { companionId = "custom-companion", displayName = "Freya", appearance = "direwolf", gender = "female", combatStyle = "Aggressive", joinBossFights = false, useStoredMaterials = false, allowCrafting = false, allowBaseWork = false };
        var order = LocalCompanions.Order(existing, "stay", world, now);
        Check(order.companionId == existing.companionId && order.displayName == existing.displayName && order.appearance == existing.appearance && order.gender == existing.gender, "Manual orders preserve the existing companion identity.");
        Check(order.combatStyle == existing.combatStyle && !order.joinBossFights && !order.useStoredMaterials && !order.allowCrafting && !order.allowBaseWork, "Manual orders must not reset existing combat settings or grant work permissions.");
        Check(existing.action == "none" && existing.id == "", "Creating a command does not mutate a stored profile.");
        Check(order.id != LocalCompanions.Order(existing, "stay", world, now).id, "Repeated clicks have separate command identities.");
        Check(Rules.Validate(order, "another-world", now).Length > 0, "The existing world guard also protects local orders.");
        Check(Rules.Validate(order, world, now + 20).Length > 0, "Local orders retain expiry validation.");
        foreach (string badName in new[] { "", "<color=red>Rune</color>", new string('x', 25) }) {
            var draft = LocalCompanions.DefaultProfile(0); draft.displayName = badName;
            Check(Rules.Validate(LocalCompanions.Order(draft, "summon", world, now), world, now).Length > 0, "Unsafe or invalid local names must fail the same executor validation.");
        }
        bool rejected = false;
        try { LocalCompanions.Order(existing, "run_plan", world, now); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Local controls reject actions outside their supported button set.");
        rejected = false;
        try { LocalCompanions.DefaultProfile(3); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "No unbounded generation of local companion slots.");
        Check(!Rules.CanChangeRiddenBody("dismiss", true, "direwolf", "direwolf"), "The shared executor must still refuse occupied-mount dismissal.");
        Console.WriteLine($"Passed {count} standalone companion checks.");
    }
}
