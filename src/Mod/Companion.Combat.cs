using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        internal float CombatStamina = 100;
        private float staminaSpentAt, weaponChoiceAt, attackAt, drawStarted, reactionAt;
        private string preferredWeapon = "", combatOrder = "", focusName = "", combatNote = "";
        private Character combatEnemy;
        private static readonly System.Reflection.FieldInfo BlockingField = AccessTools.Field(typeof(Character), "m_blocking");
        private static readonly System.Reflection.FieldInfo DrawField = AccessTools.Field(typeof(Humanoid), "m_attackDrawTime");
        internal void SpendCombatStamina(float amount) { CombatStamina = Mathf.Max(0, CombatStamina - Mathf.Max(0, amount)); staminaSpentAt = Time.time; }
        private void SetGuard(bool guard) { BlockingField.SetValue(Body, guard && CombatStamina > 5); }
        private static bool Usable(ItemDrop.ItemData item) => item != null && (!item.m_shared.m_useDurability || item.m_durability > 0);
        private static bool IsBow(ItemDrop.ItemData item) => item?.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow;
        private static string ItemName(ItemDrop.ItemData item) => Localization.instance.Localize(item.m_shared.m_name);
        private bool HasAmmo(ItemDrop.ItemData weapon) => string.IsNullOrEmpty(weapon.m_shared.m_ammoType) || Body.GetInventory().GetAmmoItem(weapon.m_shared.m_ammoType, null) != null;
        private static bool SupportedCombatWeapon(ItemDrop.ItemData item) => item != null && item.m_shared.m_attack != null &&
            (IsMagic(item) ? SupportedMagic(item) : !item.m_shared.m_attack.m_requiresReload || IsCrossbow(item));
        internal string EquippedWeaponStatus
        {
            get {
                if (Rune.Shared.Rules.IsWolf(Appearance)) return "WEAPON \u00b7 Teeth";
                var item = Body.GetCurrentWeapon();
                if (item == null || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.None) return "WEAPON \u00b7 Unarmed";
                string condition = !item.m_shared.m_useDurability ? "no durability" : item.m_durability <= 0 ? "BROKEN" :
                    Mathf.CeilToInt(item.m_durability) + "/" + Mathf.CeilToInt(item.GetMaxDurability()) + " \u00b7 " + Mathf.RoundToInt(item.GetDurabilityPercentage() * 100) + "%";
                int ammo = UsesAmmunition(item) ? Body.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && i.m_shared.m_ammoType == item.m_shared.m_ammoType).Sum(i => i.m_stack) : 0;
                return "WEAPON \u00b7 " + ItemName(item) + " \u00b7 " + condition + (UsesAmmunition(item) ? ammo == 0 ? " \u00b7 NO AMMUNITION" : " \u00b7 " + ammo + (IsCrossbow(item) ? " bolts" : " arrows") : "") + RangedResourceStatus(item);
            }
        }
        private string RejectCombat(string reason) { LastOrderAccepted = false; return reason + " No combat order started."; }
        internal string SelectOwnEquipment(string query)
        {
            if (Rune.Shared.Rules.IsWolf(Appearance)) return RejectCombat("A wolf cannot equip player weapons or shields.");
            string key = Rune.Shared.Rules.BlueprintKey(query);
            var items = Body.GetInventory().GetAllItems().Where(i => i.IsEquipable() &&
                (Rune.Shared.Rules.BlueprintKey(ItemName(i)) == key || i.m_dropPrefab && Rune.Shared.Rules.BlueprintKey(i.m_dropPrefab.name) == key)).ToArray();
            if (items.Length == 0) items = Body.GetInventory().GetAllItems().Where(i => i.IsEquipable() &&
                (key == "bow" ? IsBow(i) : key == "crossbow" ? IsCrossbow(i) : key == "shield" ? i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield : Rune.Shared.Rules.BlueprintKey(ItemName(i)).Contains(key))).ToArray();
            if (key.Length == 0 || items.Length == 0) return RejectCombat("I don't have " + query + " in my inventory.");
            var usable = items.Where(Usable).ToArray();
            if (usable.Length == 0) return RejectCombat("My " + query + " is broken and needs repair.");
            if (usable.Select(ItemName).Distinct().Count() > 1) return RejectCombat("Which one: " + string.Join(", ", usable.Select(ItemName).Distinct().Take(5)) + "?");
            var item = usable.OrderByDescending(i => i.m_quality).First();
            if ((IsMagic(item) || item.GetDamage().GetTotalDamage() > 0) && !SupportedCombatWeapon(item))
                return RejectCombat("This staff needs a summoning, support or special-attack controller. Offensive elemental projectile staves are supported.");
            if (IsRangedWeapon(item) && Appearance != "dwarf") return RejectCombat("Ranged weapons and magic require the dwarf player rig.");
            if (UsesAmmunition(item) && !HasAmmo(item)) return RejectCombat("I have " + ItemName(item) + " but no matching ammunition.");
            if (Body.InAttack()) return RejectCombat("I am mid-swing. Ask again when the swing finishes.");
            if (!Body.EquipItem(item, false)) return RejectCombat("This body cannot equip " + ItemName(item) + " right now.");
            if (!item.m_customData.ContainsKey("rune.loan")) item.m_customData["rune.personal"] = "1";
            if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield && item.GetDamage().GetTotalDamage() > 0) preferredWeapon = item.m_dropPrefab ? item.m_dropPrefab.name : ItemName(item);
            Save(); return "Equipped my " + ItemName(item) + ". Your inventory is unchanged.";
        }
        private string SetCombatOrder(string action, string item)
        {
            if (action == "equip_weapon") return SelectOwnEquipment(item);
            if (action == "combat_auto") { combatOrder = ""; focusName = ""; preferredWeapon = ""; SetGuard(false); weaponChoiceAt = 0; Save(); return "Choosing my own combat equipment and tactics again."; }
            if (action == "focus_enemy") {
                var match = NearbyCombatEnemies().Where(c => Localization.instance.Localize(c.m_name).IndexOf(item, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(c => Vector3.Distance(c.transform.position, transform.position)).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(item) || !match) return RejectCombat("I cannot see a reachable hostile " + item + " nearby.");
                focusName = item; combatEnemy = match; followingOrder = false; return "Focusing on " + Localization.instance.Localize(match.m_name) + ".";
            }
            if (Appearance != "dwarf") return RejectCombat("Player-style blocking, parrying and bow shooting require a dwarf body.");
            if (action == "shoot" || action == "cast") {
                var bow = Body.GetInventory().GetAllItems().Where(i => (action == "cast" ? IsMagic(i) && SupportedMagic(i) : IsBow(i) || IsCrossbow(i)) && Usable(i) && HasAmmo(i))
                    .OrderByDescending(i => i == Body.GetCurrentWeapon() ? float.MaxValue : i.GetDamage().GetTotalDamage()).FirstOrDefault();
                if (bow == null) return RejectCombat("I need a supported ranged weapon and its supplies in my inventory.");
                if (!NearbyCombatEnemies().Any()) return RejectCombat("There is no safe hostile target in sight.");
                string selected = SelectOwnEquipment(ItemName(bow)); if (!LastOrderAccepted) return selected;
            } else {
                var shield = Body.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield && Usable(i) && (action != "parry" || i.m_shared.m_timedBlockBonus > 1)).OrderByDescending(i => i.GetBlockPower(0)).FirstOrDefault();
                if (shield != null && !Body.EquipItem(shield, false)) return RejectCombat("I cannot raise my shield with my current weapon.");
                var blocker = (ItemDrop.ItemData)AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker").Invoke(Body, null);
                if (!Usable(blocker) || (action == "parry" && blocker.m_shared.m_timedBlockBonus <= 1)) return RejectCombat(action == "parry" ? "I need a usable weapon or shield that can parry." : "I need a usable weapon or shield to block.");
            }
            Pause(); combatOrder = action; followingOrder = false;
            combatNote = action == "cast" ? "Preparing elemental magic" : action == "shoot" ? "Looking for a clear bow shot" : action == "parry" ? "Waiting to parry an attack" : "Holding guard";
            return action == "cast" ? "Using my elemental staff. I need carried eitr food and time to recover between casts." : action == "shoot" ? "Using my bow or crossbow against nearby hostile targets. Each shot uses matching ammunition." : action == "parry" ? "Watching for attacks and attempting timed parries. Bad timing or low stamina can still break my guard." : "Holding my guard here. Say follow, defend, or stop blocking to change orders.";
        }
        private Character[] NearbyCombatEnemies() => Character.GetAllCharacters().Where(CanObserveEnemy).ToArray();
        private bool IsRangedEnemy(Character enemy) => enemy is Humanoid h && h.GetInventory().GetAllItems().Any(i => IsBow(i) || i.m_shared.m_aiAttackRange > 6);
        private Character ChooseCombatEnemy()
        {
            return observedEnemies.OrderByDescending(c => {
                float distance = Vector3.Distance(c.transform.position, transform.position);
                bool attackingPlayer = c.GetComponent<MonsterAI>() && c.GetComponent<MonsterAI>().GetTargetCreature() == Player;
                return (protectionTarget == c ? 200 : 0) + (focusName.Length > 0 && Localization.instance.Localize(c.m_name).IndexOf(focusName, StringComparison.OrdinalIgnoreCase) >= 0 ? 80 : 0) + (distance < 4 ? 40 : 0) + (attackingPlayer ? 30 : 0) + (IsRangedEnemy(c) ? 16 : 0) - distance;
            }).FirstOrDefault();
        }
        private void ChooseCombatWeapon(Character enemy)
        {
            if (Appearance != "dwarf" || Body.InAttack() || Time.time < weaponChoiceAt) return;
            weaponChoiceAt = Time.time + 2;
            var items = Body.GetInventory().GetAllItems().Where(i => Usable(i) && SupportedCombatWeapon(i) && i.GetDamage().GetTotalDamage() > 0 && i.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo && HasAmmo(i)).ToArray();
            var selected = items.FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == preferredWeapon);
            if (selected == null && items.Length > 0) selected = items.OrderByDescending(i => {
                if (enemy && (enemy.IsFlying() || Mathf.Abs(enemy.transform.position.y - transform.position.y) > 3) && !IsRangedWeapon(i)) return -1f;
                if (IsMagic(i) && !magic.Have(i.m_shared.m_attack.GetAttackEitr(Body, i) + .1f)) return -1f;
                var damage = i.GetDamage();
                var ammo = string.IsNullOrEmpty(i.m_shared.m_ammoType) ? null : Body.GetInventory().GetAmmoItem(i.m_shared.m_ammoType, null);
                if (ammo != null) damage.Add(ammo.GetDamage());
                var hit = new HitData { m_damage = damage }; if (enemy) hit.ApplyResistance(enemy.GetDamageModifiers(default), out var _);
                float range = enemy ? Vector3.Distance(enemy.transform.position, transform.position) : 2;
                return Rune.Shared.CombatWeaponScore.Evaluate(hit.GetTotalDamage(), WeaponLevel(i.m_shared.m_skillType),
                    IsRangedWeapon(i), range, i.m_shared.m_aiAttackInterval, i.m_shared.m_attack.m_attackStamina,
                    CombatStamina, i.m_shared.m_useDurability ? i.GetDurabilityPercentage() : 1);
            }).First();
            if (selected != null && (Body.GetCurrentWeapon() == selected || Body.EquipItem(selected, false)) && selected.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon) {
                var shield = Body.GetInventory().GetAllItems().Where(i => Usable(i) && i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
                    .OrderByDescending(i => i.GetBlockPower(WeaponLevel(Skills.SkillType.Blocking) / 100f)).FirstOrDefault();
                if (shield != null) Body.EquipItem(shield, false);
            }
        }
        private bool ClearShot(Character enemy)
        {
            Vector3 start = Body.GetEyePoint(), end = enemy.GetCenterPoint();
            return !Physics.Linecast(start, end, out var hit, LayerMask.GetMask("terrain", "static_solid", "Default", "piece", "character", "character_net", "character_ghost")) || hit.collider.GetComponentInParent<Character>() == enemy;
        }
        private bool TacticalCombat(float dt)
        {
            if (Appearance != "dwarf") return false;
            if (Time.time >= reactionAt) { observedEnemies = NearbyCombatEnemies(); combatEnemy = protectionTarget && CanObserveEnemy(protectionTarget) ? protectionTarget : ChooseCombatEnemy(); reactionAt = Time.time + .2f; }
            if (!combatEnemy || combatEnemy.IsDead()) { SetGuard(combatOrder == "block"); combatNote = combatOrder == "block" ? "Holding guard" : combatOrder == "parry" ? "Waiting to parry an attack" : (combatOrder == "shoot" || combatOrder == "cast") ? "Waiting for a visible hostile target" : ""; CancelBowDraw(); defenseEnemy = null; defenseAttack = null; tacticalRecovering = false; if (combatOrder.Length > 0) ai.StopMoving(); return combatOrder.Length > 0; }
            diagnosticCombat = true; CancelMaintenance(); ai.SetFollowTarget(null); SetCombatTarget(combatEnemy);
            if (combatOrder == "shoot") {
                var selectedBow = Body.GetCurrentWeapon();
                if (!(IsBow(selectedBow) || IsCrossbow(selectedBow)) || !Usable(selectedBow) || !HasAmmo(selectedBow)) {
                    combatNote = "Shooting blocked: need a usable bow/crossbow and ammunition";
                    SetGuard(false); ai.StopMoving(); SafetySay("I need a usable bow/crossbow and ammunition to keep shooting."); return true;
                }
            }
            if (combatOrder.Length == 0) ChooseCombatWeapon(combatEnemy);
            float distance = Vector3.Distance(transform.position, combatEnemy.transform.position);
            Body.SetLookDir((combatEnemy.GetCenterPoint() - Body.GetEyePoint()).normalized, 0);
            LookMethod.Invoke(ai, new object[] { combatEnemy.GetCenterPoint() });
            if (RespondToCombatTactics(dt)) return true;
            bool guard = combatOrder == "block";
            SetGuard(guard);
            if (guard || combatOrder == "parry") { CancelBowDraw(); ai.StopMoving(); combatNote = guard ? "Holding guard" : "Watching for a parry opening"; return true; }
            var weapon = Body.GetCurrentWeapon();
            if (!Usable(weapon)) { combatNote = "No usable combat weapon"; ai.StopMoving(); return true; }
            if (!SupportedCombatWeapon(weapon)) { combatNote = "Weapon needs a summoning, support or special-attack controller"; ai.StopMoving(); return true; }
            if (combatOrder == "cast" && !IsMagic(weapon)) { combatNote = "Casting blocked: equip an elemental staff"; ai.StopMoving(); return true; }
            if (IsMagic(weapon) && !PrepareMagic(weapon)) { CancelBowDraw(); ai.StopMoving(); return true; }
            var possibleHit = new HitData { m_damage = weapon.GetDamage() };
            var possibleAmmo = string.IsNullOrEmpty(weapon.m_shared.m_ammoType) ? null : Body.GetInventory().GetAmmoItem(weapon.m_shared.m_ammoType, null);
            if (possibleAmmo != null) possibleHit.m_damage.Add(possibleAmmo.GetDamage());
            possibleHit.ApplyResistance(combatEnemy.GetDamageModifiers(default), out var _);
            if (possibleHit.GetTotalDamage() <= 0) return TacticalReposition(dt, combatEnemy.transform.position, "This weapon cannot damage the target; keeping clear", false);
            if (CombatStamina < 12) { CancelBowDraw(); combatNote = "Recovering stamina"; ai.StopMoving(); return true; }
            bool bow = IsRangedWeapon(weapon);
            if (bow && !HasAmmo(weapon)) { combatNote = "Out of ammunition"; if (combatOrder == "shoot") { SafetySay("I'm out of ammunition. I cannot keep shooting."); ai.StopMoving(); return true; } preferredWeapon = ""; weaponChoiceAt = 0; return true; }
            if (bow && distance < 5) return TacticalReposition(dt, combatEnemy.transform.position, "Making room to shoot", false);
            if (!bow && (combatEnemy.IsFlying() || Mathf.Abs(combatEnemy.transform.position.y - transform.position.y) > 3)) {
                CancelBowDraw(); ai.StopMoving(); combatNote = "Target is out of melee reach; need a usable bow/crossbow and ammunition"; return true;
            }
            float meleeReach = Mathf.Clamp(weapon.m_shared.m_attack.m_attackRange, 1.2f, 4);
            if (distance > (bow ? 22 : meleeReach) || bow && !ClearShot(combatEnemy)) {
                if (bow && distance < 22) return TacticalReposition(dt, combatEnemy.transform.position, "Finding a clear shot", true);
                Vector3 toward = combatEnemy.transform.position - transform.position; toward.y = 0;
                float step = Mathf.Min(3, toward.magnitude - (bow ? 14 : Mathf.Max(.8f, meleeReach - .5f)));
                if (step > .2f && TacticalRoute(transform.position + toward.normalized * step, out var point)) {
                    Move(dt, point, .5f); combatNote = "Approaching " + Localization.instance.Localize(combatEnemy.m_name) + " over safe ground";
                } else { ai.StopMoving(); combatNote = "Target approach blocked by unsafe terrain or obstacles"; }
                CancelBowDraw(); return true;
            }
            ai.StopMoving(); combatNote = bow ? "Aiming at " + Localization.instance.Localize(combatEnemy.m_name) : "Fighting " + Localization.instance.Localize(combatEnemy.m_name);
            if (IsMagic(weapon) && !MagicImpactClear(weapon, combatEnemy)) {
                StopMagicChannel(); combatNote = "Holding magic: an ally is too close to the impact"; return true;
            }
            if (Body.InAttack()) {
                if (IsMagic(weapon) && Time.time - channelStarted > 2) StopMagicChannel();
                return true;
            }
            if (Time.time < attackAt) return true;
            if (IsCrossbow(weapon) && PrepareCrossbow(dt, weapon)) return true;
            if (IsBow(weapon)) {
                if (drawStarted == 0) drawStarted = Time.time;
                float duration = Mathf.Max(.5f, weapon.m_shared.m_attack.m_drawDurationMin);
                DrawField.SetValue(Body, Time.time - drawStarted); Body.GetZAnim().SetBool("bow_aim", true);
                if (Time.time - drawStarted < duration || !ClearShot(combatEnemy)) return true;
            }
            if (Body.StartAttack(combatEnemy, false)) { channelStarted = Time.time; attackAt = Time.time + (bow ? 1.2f : .8f); drawStarted = 0; Body.GetZAnim().SetBool("bow_aim", false); }
            return true;
        }
        private void ResetCombatControl() { combatOrder = ""; focusName = ""; combatNote = ""; combatEnemy = null; CancelBowDraw(); defenseEnemy = null; defenseAttack = null; haveTacticalPosition = false; tacticalRecovering = false; SetGuard(false); }
    }

    // Native NPC Humanoid stamina methods are unlimited. Give Rune a separate,
    // finite pool; native attack and block code still determines the actual cost.
    [HarmonyPatch(typeof(Character), nameof(Character.HaveStamina))]
    internal static class RuneHaveStamina { static bool Prefix(Character __instance, float __0, ref bool __result) { var c = __instance.GetComponent<Companion>(); if (!c) return true; __result = c.CombatStamina > Mathf.Max(0, __0); return false; } }
    [HarmonyPatch(typeof(Character), nameof(Character.UseStamina))]
    internal static class RuneUseStamina { static bool Prefix(Character __instance, float __0) { var c = __instance.GetComponent<Companion>(); if (!c) return true; c.SpendCombatStamina(__0); return false; } }
    [HarmonyPatch(typeof(Character), nameof(Character.AddStamina))]
    internal static class RuneAddStamina { static bool Prefix(Character __instance, float __0) { var c = __instance.GetComponent<Companion>(); if (!c) return true; c.CombatStamina = Mathf.Clamp(c.CombatStamina + __0, 0, c.MaxCombatStamina); return false; } }
    [HarmonyPatch(typeof(Character), nameof(Character.GetStaminaPercentage))]
    internal static class RuneStaminaPercentage { static bool Prefix(Character __instance, ref float __result) { var c = __instance.GetComponent<Companion>(); if (!c) return true; __result = c.CombatStamina / c.MaxCombatStamina; return false; } }
}
