using System;

namespace Rune.Shared
{
    // In-game buttons and the desktop bridge use the same validated command executor.
    public static class LocalCompanions
    {
        public const int SlotCount = 3;
        public static readonly string[] Actions = { "summon", "follow", "stay", "defend", "return", "gather_wood", "gather_stone", "lend_tools", "dismiss" };

        public static string SlotId(int slot)
        {
            if (slot < 0 || slot >= SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            return "ingame-" + (slot + 1);
        }

        public static Command DefaultProfile(int slot) => new Command {
            companionId = SlotId(slot),
            displayName = slot == 0 ? "Rune" : slot == 1 ? "Odin" : "Eira",
            appearance = slot == 1 ? "direwolf" : "dwarf",
            gender = slot == 2 ? "female" : "male"
        };

        public static Command Order(Command profile, string action, string world, long timestamp)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (Array.IndexOf(Actions, action) < 0) throw new ArgumentException("Unsupported in-game order.", nameof(action));
            return new Command {
                id = Guid.NewGuid().ToString(), world = world, timestamp = timestamp, action = action,
                companionId = profile.companionId, displayName = profile.displayName,
                appearance = profile.appearance, gender = profile.gender,
                combatStyle = profile.combatStyle, joinBossFights = profile.joinBossFights,
                useStoredMaterials = profile.useStoredMaterials, allowCrafting = profile.allowCrafting,
                allowBaseWork = profile.allowBaseWork, amount = 20
            };
        }
    }
}
