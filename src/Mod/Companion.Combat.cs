using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        internal float CombatStamina = 100;
        private float staminaSpentAt, weaponChoiceAt, attackAt, drawStarted, windupAt, blockUntil, reactionAt;
        private bool observedWindup;
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
        internal string EquippedWeaponStatus
        {
            get {
                if (Appearance == "wolf") return "WEAPON · Teeth · no durability";
                var item = Body.GetCurrentWeapon();
                if (item == null || item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.None) return "WEAPON · Unarmed";
                string condition = !item.m_shared.m_useDurability ? "no durability" : item.m_durability <= 0 ? "BROKEN" :
                    Mathf.CeilToInt(item.m_durability) + "/" + Mathf.CeilToInt(item.GetMaxDurability()) + " · " + Mathf.RoundToInt(item.GetDurabilityPercentage() * 100) + "%";
                int ammo = IsBow(item) ? Body.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && i.m_shared.m_ammoType == item.m_shared.m_ammoType).Sum(i => i.m_stack) : 0;
                return "WEAPON · " + ItemName(item) + " · " + condition + (IsBow(item) ? ammo == 0 ? " · NO ARROWS" : " · " + ammo + " arrows" : "");
            }
        }
        private string RejectCombat(string reason) { LastOrderAccepted = false; return reason + " No combat order started."; }
        internal string SelectOwnEquipment(string query)
        {
            if (Appearance == "wolf") return RejectCombat("A wolf cannot equip player weapons or shields.");
            string key = Rune.Shared.Rules.BlueprintKey(query);
            var items = Body.GetInventory().GetAllItems().Where(i => i.IsEquipable() &&
                (Rune.Shared.Rules.BlueprintKey(ItemName(i)) == key || i.m_dropPrefab && Rune.Shared.Rules.BlueprintKey(i.m_dropPrefab.name) == key)).ToArray();
            if (items.Length == 0) items = Body.GetInventory().GetAllItems().Where(i => i.IsEquipable() &&
                (key == "bow" ? IsBow(i) : key == "shield" ? i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield : Rune.Shared.Rules.BlueprintKey(ItemName(i)).Contains(key))).ToArray();
            if (key.Length == 0 || items.Length == 0) return RejectCombat("I don't have " + query + " in my inventory.");
            var usable = items.Where(Usable).ToArray();
            if (usable.Length == 0) return RejectCombat("My " + query + " is broken and needs repair.");
            if (usable.Select(ItemName).Distinct().Count() > 1) return RejectCombat("Which one: " + string.Join(", ", usable.Select(ItemName).Distinct().Take(5)) + "?");
            var item = usable.OrderByDescending(i => i.m_quality).First();
            if (IsBow(item) && Appearance != "dwarf") return RejectCombat("Bow shooting requires the dwarf player rig.");
            if (IsBow(item) && !HasAmmo(item)) return RejectCombat("I have " + ItemName(item) + " but no matching arrows.");
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
            if (action == "shoot") {
                var bow = Body.GetInventory().GetAllItems().Where(i => IsBow(i) && Usable(i) && HasAmmo(i)).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();
                if (bow == null) return RejectCombat("I need a usable bow and matching arrows in my inventory.");
                if (!NearbyCombatEnemies().Any()) return RejectCombat("There is no safe hostile target in sight.");
                string selected = SelectOwnEquipment(ItemName(bow)); if (!LastOrderAccepted) return selected;
            } else {
                var shield = Body.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield && Usable(i) && (action != "parry" || i.m_shared.m_timedBlockBonus > 1)).OrderByDescending(i => i.GetBlockPower(0)).FirstOrDefault();
                if (shield != null && !Body.EquipItem(shield, false)) return RejectCombat("I cannot raise my shield with my current weapon.");
                var blocker = (ItemDrop.ItemData)AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker").Invoke(Body, null);
                if (!Usable(blocker) || (action == "parry" && blocker.m_shared.m_timedBlockBonus <= 1)) return RejectCombat(action == "parry" ? "I need a usable weapon or shield that can parry." : "I need a usable weapon or shield to block.");
            }
            Pause(); combatOrder = action; followingOrder = false;
            combatNote = action == "shoot" ? "Looking for a clear bow shot" : action == "parry" ? "Waiting to parry an attack" : "Holding guard";
            return action == "shoot" ? "Using my bow against nearby hostile targets. Each shot uses an arrow." : action == "parry" ? "Watching for attacks and attempting timed parries. Bad timing or low stamina can still break my guard." : "Holding my guard here. Say follow, defend, or stop blocking to change orders.";
        }
        private Character[] NearbyCombatEnemies() => Character.GetAllCharacters().Where(c => c && c != Body && !(c is Player) && !c.IsDead() && BaseAI.IsEnemy(Body, c) && (JoinBossFights || !c.IsBoss()) && Vector3.Distance(c.transform.position, transform.position) < 32 && SafeGround(c.transform.position)).ToArray();
        private bool IsRangedEnemy(Character enemy) => enemy is Humanoid h && h.GetInventory().GetAllItems().Any(i => IsBow(i) || i.m_shared.m_aiAttackRange > 6);
        private Character ChooseCombatEnemy()
        {
            return NearbyCombatEnemies().OrderByDescending(c => {
                float distance = Vector3.Distance(c.transform.position, transform.position);
                bool attackingPlayer = c.GetComponent<MonsterAI>() && c.GetComponent<MonsterAI>().GetTargetCreature() == Player;
                return (protectionTarget == c ? 200 : 0) + (focusName.Length > 0 && Localization.instance.Localize(c.m_name).IndexOf(focusName, StringComparison.OrdinalIgnoreCase) >= 0 ? 80 : 0) + (distance < 4 ? 40 : 0) + (attackingPlayer ? 30 : 0) + (IsRangedEnemy(c) ? 16 : 0) - distance;
            }).FirstOrDefault();
        }
        private void ChooseCombatWeapon(Character enemy)
        {
            if (Appearance != "dwarf" || Body.InAttack() || Time.time < weaponChoiceAt) return;
            weaponChoiceAt = Time.time + 2;
            var items = Body.GetInventory().GetAllItems().Where(i => Usable(i) && i.GetDamage().GetTotalDamage() > 0 && i.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo && HasAmmo(i)).ToArray();
            var selected = items.FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == preferredWeapon);
            if (selected == null && items.Length > 0) selected = items.OrderByDescending(i => {
                var hit = new HitData { m_damage = i.GetDamage() }; if (enemy) hit.ApplyResistance(enemy.GetDamageModifiers(default), out var _);
                float range = enemy ? Vector3.Distance(enemy.transform.position, transform.position) : 2;
                return hit.GetTotalDamage() * (IsBow(i) ? range < 5 ? .15f : 1.25f : 1) / Mathf.Max(.8f, i.m_shared.m_aiAttackInterval);
            }).First();
            if (selected != null) Body.EquipItem(selected, false);
        }
        private bool ClearShot(Character enemy)
        {
            Vector3 start = Body.GetEyePoint(), end = enemy.GetCenterPoint();
            return !Physics.Linecast(start, end, out var hit, LayerMask.GetMask("terrain", "static_solid", "Default", "piece", "character", "character_net", "character_ghost")) || hit.collider.GetComponentInParent<Character>() == enemy;
        }
        private bool TacticalCombat(float dt)
        {
            if (Appearance != "dwarf") return false;
            if (Time.time >= reactionAt) { combatEnemy = protectionTarget ? protectionTarget : ChooseCombatEnemy(); reactionAt = Time.time + .2f; }
            if (!combatEnemy || combatEnemy.IsDead()) { SetGuard(combatOrder == "block"); combatNote = combatOrder == "block" ? "Holding guard" : combatOrder == "parry" ? "Waiting to parry an attack" : combatOrder == "shoot" ? "Waiting for a hostile bow target" : ""; drawStarted = 0; observedWindup = false; if (combatOrder.Length > 0) ai.StopMoving(); return combatOrder.Length > 0; }
            diagnosticCombat = true; CancelMaintenance(); ai.SetFollowTarget(null); SetCombatTarget(combatEnemy);
            if (combatOrder == "shoot") {
                var selectedBow = Body.GetCurrentWeapon();
                if (!IsBow(selectedBow) || !Usable(selectedBow) || !HasAmmo(selectedBow)) {
                    combatNote = "Shooting blocked: need a usable bow and arrows";
                    SetGuard(false); ai.StopMoving(); SafetySay("I need a usable bow and arrows to keep shooting."); return true;
                }
            }
            if (combatOrder.Length == 0) ChooseCombatWeapon(combatEnemy);
            float distance = Vector3.Distance(transform.position, combatEnemy.transform.position);
            Body.SetLookDir((combatEnemy.GetCenterPoint() - Body.GetEyePoint()).normalized, 0);
            LookMethod.Invoke(ai, new object[] { combatEnemy.GetCenterPoint() });
            bool windup = combatEnemy.InAttack() && distance < 5;
            if (windup && !observedWindup) { windupAt = Time.time; blockUntil = 0; }
            observedWindup = windup;
            if (windup && Time.time - windupAt >= .12f && Time.time - windupAt < .4f && blockUntil == 0) blockUntil = Time.time + .3f;
            bool guard = combatOrder == "block" || Time.time < blockUntil;
            SetGuard(guard);
            if (guard || combatOrder == "parry") { ai.StopMoving(); combatNote = guard ? "Blocking incoming attack" : "Watching for a parry opening"; return true; }
            var weapon = Body.GetCurrentWeapon();
            if (!Usable(weapon)) { combatNote = "No usable combat weapon"; ai.StopMoving(); return true; }
            if (CombatStamina < 12) { combatNote = "Recovering stamina"; ai.StopMoving(); return true; }
            bool bow = IsBow(weapon);
            if (bow && !HasAmmo(weapon)) { combatNote = "Out of arrows"; if (combatOrder == "shoot") { SafetySay("I'm out of arrows. I cannot keep shooting."); ai.StopMoving(); return true; } preferredWeapon = ""; weaponChoiceAt = 0; return true; }
            if (bow && distance < 5) {
                Vector3 away = transform.position + (transform.position - combatEnemy.transform.position).normalized * 3;
                if (SafeGround(away)) Move(dt, away, .6f); combatNote = "Making room to shoot"; drawStarted = 0; return true;
            }
            if (distance > (bow ? 22 : 2.1f) || bow && !ClearShot(combatEnemy)) {
                Vector3 point = combatEnemy.transform.position;
                if (bow && distance < 22) point = transform.position + Vector3.Cross(Vector3.up, (point - transform.position).normalized) * 3;
                if (SafeGround(point)) Move(dt, point, bow ? 14 : 1.6f); else ai.StopMoving();
                combatNote = bow ? "Finding a clear shot" : "Closing on " + Localization.instance.Localize(combatEnemy.m_name); drawStarted = 0; return true;
            }
            ai.StopMoving(); combatNote = bow ? "Aiming at " + Localization.instance.Localize(combatEnemy.m_name) : "Fighting " + Localization.instance.Localize(combatEnemy.m_name);
            if (Body.InAttack() || Time.time < attackAt) return true;
            if (bow) {
                if (drawStarted == 0) drawStarted = Time.time;
                float duration = Mathf.Max(.5f, weapon.m_shared.m_attack.m_drawDurationMin);
                DrawField.SetValue(Body, Time.time - drawStarted); Body.GetZAnim().SetBool("bow_aim", true);
                if (Time.time - drawStarted < duration || !ClearShot(combatEnemy)) return true;
            }
            if (Body.StartAttack(combatEnemy, false)) { attackAt = Time.time + (bow ? 1.2f : .8f); drawStarted = 0; Body.GetZAnim().SetBool("bow_aim", false); }
            return true;
        }
        private void ResetCombatControl() { combatOrder = ""; focusName = ""; combatNote = ""; combatEnemy = null; drawStarted = 0; SetGuard(false); }
    }

    // Native NPC Humanoid stamina methods are unlimited. Give Rune a separate,
    // finite pool; native attack and block code still determines the actual cost.
    [HarmonyPatch(typeof(Character), nameof(Character.HaveStamina))]
    internal static class RuneHaveStamina { static bool Prefix(Character __instance, float __0, ref bool __result) { var c = __instance.GetComponent<Companion>(); if (!c) return true; __result = c.CombatStamina > Mathf.Max(0, __0); return false; } }
    [HarmonyPatch(typeof(Character), nameof(Character.UseStamina))]
    internal static class RuneUseStamina { static bool Prefix(Character __instance, float __0) { var c = __instance.GetComponent<Companion>(); if (!c) return true; c.SpendCombatStamina(__0); return false; } }
    [HarmonyPatch(typeof(Character), nameof(Character.AddStamina))]
    internal static class RuneAddStamina { static bool Prefix(Character __instance, float __0) { var c = __instance.GetComponent<Companion>(); if (!c) return true; c.CombatStamina = Mathf.Clamp(c.CombatStamina + __0, 0, 100); return false; } }
    [HarmonyPatch(typeof(Character), nameof(Character.GetStaminaPercentage))]
    internal static class RuneStaminaPercentage { static bool Prefix(Character __instance, ref float __result) { var c = __instance.GetComponent<Companion>(); if (!c) return true; __result = c.CombatStamina / 100; return false; } }
}
