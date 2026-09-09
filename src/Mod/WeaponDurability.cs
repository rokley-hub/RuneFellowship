using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace Rune.Mod
{
    [HarmonyPatch]
    internal static class RuneWeaponDurability
    {
        static IEnumerable<MethodBase> TargetMethods() => new[] { "ProjectileAttackTriggered", "DoAreaAttack", "DoMeleeAttack" }.Select(n => (MethodBase)AccessTools.Method(typeof(Attack), n));
        static bool UsesWeaponDurability(Character character) => character.IsPlayer() || character.GetComponent<Companion>();
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var code = instructions.ToList(); int matches = 0;
            var isPlayer = AccessTools.Method(typeof(Character), nameof(Character.IsPlayer));
            var durability = AccessTools.Field(typeof(ItemDrop.ItemData), "m_durability");
            for (int i = 0; i < code.Count; i++) {
                // Change only the native durability eligibility check. Do not
                // make a creature count as Player for combat, skills or damage.
                if (!code[i].Calls(isPlayer) || !code.Skip(i + 1).Take(6).Any(c => c.LoadsField(durability))) continue;
                code[i].opcode = OpCodes.Call; code[i].operand = AccessTools.Method(typeof(RuneWeaponDurability), nameof(UsesWeaponDurability)); matches++;
            }
            if (matches != 1) throw new InvalidOperationException("Unsupported native weapon durability code in " + original.Name);
            return code;
        }
    }
}
