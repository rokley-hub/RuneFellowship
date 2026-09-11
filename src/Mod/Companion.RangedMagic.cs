using System;
using System.Linq;
using HarmonyLib;
using Rune.Shared;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        private readonly CrossbowLoad crossbowLoad = new CrossbowLoad();
        private ItemDrop.ItemData loadingWeapon;
        private string reloadAnimation = "";
        private float reloadBlockedUntil, reloadTickAt, eitrSpentAt, magicFoodAt, channelStarted;
        private MagicResources magic = new MagicResources();
        private static bool IsCrossbow(ItemDrop.ItemData item) => item?.m_shared.m_skillType == Skills.SkillType.Crossbows && item.m_shared.m_attack.m_requiresReload;
        private static bool IsMagic(ItemDrop.ItemData item) => item != null && (item.m_shared.m_skillType == Skills.SkillType.ElementalMagic || item.m_shared.m_skillType == Skills.SkillType.BloodMagic);
        private static bool IsRangedWeapon(ItemDrop.ItemData item) => IsBow(item) || IsCrossbow(item) || IsMagic(item) && item.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile;
        private static bool UsesAmmunition(ItemDrop.ItemData item) => item != null && !string.IsNullOrEmpty(item.m_shared.m_ammoType);
        private static bool SpawnsCreature(GameObject prefab, int depth = 0)
        {
            if (!prefab) return false;
            if (depth > 3) return true; // Unknown/cyclic spawn chains need a dedicated controller.
            if (prefab.GetComponent<Character>() || prefab.GetComponent<SpawnAbility>()) return true;
            var projectile = prefab.GetComponent<Projectile>();
            return projectile && (SpawnsCreature(projectile.m_spawnOnHit, depth + 1) || projectile.m_randomSpawnOnHit != null && projectile.m_randomSpawnOnHit.Any(p => SpawnsCreature(p, depth + 1)));
        }
        // Offensive elemental projectile attacks are supported. Summons and support rituals
        // are not treated as ordinary damage casts; their ownership/lifetime needs separate work.
        private static bool SupportedMagic(ItemDrop.ItemData item) => item.m_shared.m_skillType == Skills.SkillType.ElementalMagic &&
            item.m_shared.m_attack.m_attackType == Attack.AttackType.Projectile && item.m_shared.m_attack.m_attackProjectile &&
            !SpawnsCreature(item.m_shared.m_attack.m_attackProjectile) && !SpawnsCreature(item.m_shared.m_attack.m_spawnOnTrigger) &&
            item.m_shared.m_attack.m_attackHealth <= 0 && item.m_shared.m_attack.m_attackHealthPercentage <= 0;
        private void StopMagicChannel()
        {
            if (IsMagic(Body.GetCurrentWeapon()) && CurrentAttackField.GetValue(Body) is Attack attack && attack.m_loopingAttack) attack.Stop();
        }
        internal bool CrossbowIsLoaded => loadingWeapon == Body.GetCurrentWeapon() && crossbowLoad.Loaded;
        internal void ResetCrossbow()
        {
            InterruptReload(); crossbowLoad.Reset();
            reloadBlockedUntil = Time.time + Mathf.Max(0, loadingWeapon?.m_shared.m_attack.m_blockReloadTime ?? 0);
        }
        private void InterruptReload()
        {
            crossbowLoad.Interrupt();
            if (reloadAnimation.Length > 0) { Body.GetZAnim().SetBool(reloadAnimation, false); reloadAnimation = ""; }
        }
        private bool PrepareCrossbow(float dt, ItemDrop.ItemData weapon)
        {
            if (loadingWeapon != weapon) { InterruptReload(); crossbowLoad.Reset(); loadingWeapon = weapon; }
            if (crossbowLoad.Loaded) return false;
            if (Body.InAttack() || Body.IsStaggering() || Body.IsBlocking() || Time.time < reloadBlockedUntil) { InterruptReload(); combatNote = "Waiting for a safe crossbow reload"; return true; }
            if (observedEnemies.Any(c => c && !c.IsDead() && Vector3.Distance(c.transform.position, transform.position) < 5))
                return TacticalReposition(dt, combatEnemy.transform.position, "Making room to reload the crossbow", false);
            ai.StopMoving(); dt = Mathf.Clamp(dt, 0, .25f);
            var attack = weapon.m_shared.m_attack;
            float stamina = Mathf.Max(0, attack.m_reloadStaminaDrain) * dt, eitr = Mathf.Max(0, attack.m_reloadEitrDrain) * dt;
            if (!Body.HaveStamina(stamina + 5) || !magic.Have(eitr)) {
                InterruptReload(); combatNote = "Recovering resources before reloading"; return true;
            }
            SpendCombatStamina(stamina); SpendEitr(eitr); reloadTickAt = Time.time;
            if (reloadAnimation.Length == 0 && !string.IsNullOrEmpty(attack.m_reloadAnimation)) {
                reloadAnimation = attack.m_reloadAnimation; Body.GetZAnim().SetBool(reloadAnimation, true);
            }
            bool ready = crossbowLoad.Advance(dt, CrossbowLoad.Duration(attack.m_reloadTime, WeaponLevel(weapon.m_shared.m_skillType)), true, true);
            combatNote = "Reloading crossbow · " + Mathf.RoundToInt(crossbowLoad.Progress * 100) + "%";
            if (ready) {
                string done = reloadAnimation; InterruptReload();
                if (done.Length > 0) Body.GetZAnim().SetTrigger(done + "_done");
                combatNote = "Crossbow loaded";
            }
            return true;
        }
        internal void SpendEitr(float amount) { magic.Spend(amount); if (amount > 0) eitrSpentAt = Time.time; }
        private float foodHealingTimer;
        private void TickMagic(float dt)
        {
            float regen = Time.time - eitrSpentAt >= 1 && !Body.InAttack() ? 2 : 0;
            float eitrModifier = 1; Body.GetSEMan().ModifyEitrRegen(ref eitrModifier);
            regen *= eitrModifier;
            float gear = Body.GetInventory().GetAllItems().Where(i => i.m_equipped).Sum(i => i.m_shared.m_eitrRegenModifier);
            magic.Tick(dt, Mathf.Max(0, regen * (1 + gear)), Game.m_foodRate);
            if (!Rune.Shared.Rules.IsWolf(Appearance)) {
                EatCarriedFood(false);
                ApplyFoodCapacity();
                foodHealingTimer += dt;
                if (foodHealingTimer >= 10) {
                    foodHealingTimer = 0;
                    float modifier = 1; Body.GetSEMan().ModifyHealthRegen(ref modifier);
                    if (magic.Healing > 0 && modifier > 0) Body.Heal(magic.Healing * modifier, false);
                }
            }
            if (reloadAnimation.Length > 0 && (Time.time - reloadTickAt > .15f || loadingWeapon != Body.GetCurrentWeapon())) InterruptReload();
            if (loadingWeapon != null && loadingWeapon != Body.GetCurrentWeapon()) { crossbowLoad.Reset(); loadingWeapon = null; }
        }
        private bool PrepareMagic(ItemDrop.ItemData weapon)
        {
            float cost = weapon.m_shared.m_attack.GetAttackEitr(Body, weapon);
            if (magic.Maximum < cost + 1) EatCarriedFood(true);
            if (magic.Maximum <= cost) { combatNote = "Magic needs more carried eitr food"; return false; }
            if (!magic.Have(cost + .1f)) { combatNote = "Recovering eitr · " + Mathf.FloorToInt(magic.Current) + "/" + Mathf.FloorToInt(magic.Maximum); return false; }
            return true;
        }
        private bool MagicImpactClear(ItemDrop.ItemData weapon, Character enemy)
        {
            var projectile = weapon.m_shared.m_attack.m_attackProjectile.GetComponent<Projectile>();
            var zone = projectile && projectile.m_spawnOnHit ? projectile.m_spawnOnHit.GetComponent<Aoe>() : null;
            float radius = zone ? Mathf.Clamp(zone.m_radius + 1, 2, 15) : 2;
            return !Character.GetAllCharacters().Any(c => c && c != enemy && !c.IsDead() && !BaseAI.IsEnemy(Body, c) &&
                Vector3.Distance(c.GetCenterPoint(), enemy.GetCenterPoint()) < radius);
        }
        private float FoodScore(ItemDrop.ItemData food, bool eitrOnly)
        {
            bool caster = eitrOnly || Body.GetInventory().GetAllItems().Any(i => IsMagic(i) && SupportedMagic(i));
            return food.m_shared.m_food + food.m_shared.m_foodStamina + (caster ? food.m_shared.m_foodEitr * 3 : 0);
        }
        private void EatCarriedFood(bool eitrOnly)
        {
            if (Rune.Shared.Rules.IsWolf(Appearance) || Time.time < magicFoodAt || Body.InAttack()) return;
            magicFoodAt = Time.time + 2;
            var food = Body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab && i.m_stack > 0 && IsOwnedItem(i) &&
                !i.m_customData.ContainsKey("rune.loan") && i.m_shared.m_food > 0 && i.m_shared.m_foodBurnTime > 0 &&
                (!eitrOnly || i.m_shared.m_foodEitr > 0) && magic.CanEat(i.m_shared.m_name))
                .OrderByDescending(i => FoodScore(i, eitrOnly)).FirstOrDefault();
            if (food == null) return;
            var f = food.m_shared;
            var next = MagicResources.Parse(magic.Serialize());
            if (next.Eat(f.m_name, f.m_food, f.m_foodStamina, f.m_foodEitr, f.m_foodRegen, f.m_foodBurnTime) && Body.GetInventory().RemoveItem(food, 1)) {
                magic = next; ApplyFoodCapacity(); Save();
            }
        }
        private void LoadMagic()
        {
            magic = MagicResources.Parse(view.GetZDO().GetString("rune.magic", ""));
            // Upgrade consumed legacy food using definitions, without renewing its timer or resources.
            foreach (string name in magic.LegacyNames) {
                var item = ObjectDB.instance.m_items.Select(p => p ? p.GetComponent<ItemDrop>() : null)
                    .FirstOrDefault(i => i && i.m_itemData.m_shared.m_name == name);
                if (item) { var f = item.m_itemData.m_shared; magic.RestoreFoodValues(name, f.m_food, f.m_foodStamina, f.m_foodRegen); }
            }
        }
        private void SaveMagic() { view.GetZDO().Set("rune.magic", magic.Serialize()); }
        private string RangedResourceStatus(ItemDrop.ItemData item) => IsMagic(item) ? " · eitr " + Mathf.FloorToInt(magic.Current) + "/" + Mathf.FloorToInt(magic.Maximum) :
            IsCrossbow(item) ? CrossbowIsLoaded ? " · loaded" : " · unloaded" : "";
    }
    [HarmonyPatch(typeof(Humanoid), "IsWeaponLoaded")]
    internal static class RuneWeaponLoaded { static bool Prefix(Humanoid __instance, ref bool __result) { var c = __instance.GetComponent<Companion>(); if (!c) return true; __result = c.CrossbowIsLoaded; return false; } }
    [HarmonyPatch(typeof(Humanoid), "ResetLoadedWeapon")]
    internal static class RuneResetLoaded { static void Postfix(Humanoid __instance) { var c = __instance.GetComponent<Companion>(); if (c) c.ResetCrossbow(); } }
    [HarmonyPatch(typeof(Character), nameof(Character.GetMaxEitr))]
    internal static class RuneMaxEitr { static bool Prefix(Character __instance, ref float __result) { var c = __instance.GetComponent<Companion>(); if (!c) return true; __result = c.MagicMaximum; return false; } }
    [HarmonyPatch(typeof(Character), nameof(Character.HaveEitr))]
    internal static class RuneHaveEitr { static bool Prefix(Character __instance, float __0, ref bool __result) { var c = __instance.GetComponent<Companion>(); if (!c) return true; __result = c.HasEitr(__0); return false; } }
    [HarmonyPatch(typeof(Character), nameof(Character.UseEitr))]
    internal static class RuneUseEitr { static bool Prefix(Character __instance, float __0) { var c = __instance.GetComponent<Companion>(); if (!c) return true; c.SpendEitr(__0); return false; } }
    [HarmonyPatch(typeof(Character), nameof(Character.AddEitr))]
    internal static class RuneAddEitr { static bool Prefix(Character __instance, float __0) { var c = __instance.GetComponent<Companion>(); if (!c) return true; c.AddMagicEitr(__0); return false; } }
    [HarmonyPatch(typeof(Character), nameof(Character.GetEitrPercentage))]
    internal static class RuneEitrPercent { static bool Prefix(Character __instance, ref float __result) { var c = __instance.GetComponent<Companion>(); if (!c) return true; __result = c.MagicFraction; return false; } }
    public partial class Companion
    {
        internal float MagicMaximum => magic.Maximum;
        internal float MagicFraction => magic.Maximum > 0 ? magic.Current / magic.Maximum : 0;
        internal bool HasEitr(float amount) => magic.Have(amount);
        internal void AddMagicEitr(float amount) => magic.Add(amount);
    }
}
