using System;
using System.Linq;
using HarmonyLib;
using Rune.Shared;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        private static readonly System.Reflection.FieldInfo CurrentAttackField = AccessTools.Field(typeof(Humanoid), "m_currentAttack");
        private static readonly System.Reflection.MethodInfo CurrentBlockerMethod = AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker");
        private Character[] observedEnemies = Array.Empty<Character>();
        private Character defenseEnemy;
        private Attack defenseAttack;
        private float defenseObservedAt, tacticalPositionAt;
        private bool tacticalRecovering, haveTacticalPosition;
        private Vector3 tacticalPosition;
        private string tacticalMovementReason = "";
        private static readonly int TacticalMask = LayerMask.GetMask("terrain", "static_solid", "Default", "piece");
        private static readonly float[] TacticalRadii = { 3f, 6f, 9f };

        private void CancelBowDraw()
        {
            InterruptReload(); StopMagicChannel();
            drawStarted = 0; DrawField.SetValue(Body, 0f); Body.GetZAnim().SetBool("bow_aim", false);
        }
        private bool CanObserveEnemy(Character enemy) => enemy && CombatTactics.ThreatVisible(!enemy.IsDead(),
            enemy != Body && !(enemy is Player) && !enemy.IsTamed() && BaseAI.IsEnemy(Body, enemy), enemy.IsBoss(), JoinBossFights,
            Vector3.Distance(transform.position, enemy.transform.position), ai.CanSeeTarget(enemy));

        private bool HasEffect(int effect) => Body.GetSEMan().HaveStatusEffect(effect);
        private bool ImpairedMovement => HasEffect(SEMan.s_statusEffectFrost) || HasEffect(SEMan.s_statusEffectFreezing) || HasEffect(SEMan.s_statusEffectTared);
        private bool OngoingDamage => HasEffect(SEMan.s_statusEffectBurning) || HasEffect(SEMan.s_statusEffectPoison) || HasEffect(SEMan.s_statusEffectSmoked);

        private static bool WideAttack(Attack attack) => attack != null && (attack.m_attackType == Attack.AttackType.Area || attack.m_attackAngle >= 150 ||
            attack.m_spawnOnTrigger && attack.m_spawnOnTrigger.GetComponent<Aoe>());
        private float IncomingPriority(Character enemy)
        {
            var attack = enemy is Humanoid humanoid ? CurrentAttackField.GetValue(humanoid) as Attack : null;
            float distance = Vector3.Distance(transform.position, enemy.transform.position);
            bool ranged = attack?.m_attackProjectile || attack?.m_attackType == Attack.AttackType.Projectile;
            float reach = ranged ? 32 : Mathf.Clamp(Mathf.Max(attack?.m_attackRange ?? 3, (enemy as Humanoid)?.GetCurrentWeapon()?.m_shared.m_aiAttackRange ?? 3) + 1.5f, 3, 15);
            bool area = WideAttack(attack);
            bool aimed = area || Vector3.Dot(enemy.transform.forward, (transform.position - enemy.transform.position).normalized) > .2f;
            return aimed && distance <= reach ? (area ? 200 : 100) - distance : -1000;
        }

        private bool RespondToCombatTactics(float dt)
        {
            // Consider every visible attacker, including one behind our selected target.
            var attacker = observedEnemies.Where(c => c && !c.IsDead() && c.InAttack())
                .OrderByDescending(IncomingPriority).FirstOrDefault();
            var weapon = (attacker as Humanoid)?.GetCurrentWeapon();
            var attack = attacker is Humanoid humanoid ? CurrentAttackField.GetValue(humanoid) as Attack : null;
            if (defenseEnemy != attacker || defenseAttack != attack) {
                defenseEnemy = attacker; defenseAttack = attack; defenseObservedAt = Time.time;
            }
            var blocker = CurrentBlockerMethod.Invoke(Body, null) as ItemDrop.ItemData;
            bool area = WideAttack(attack);
            float incoming = 0;
            if (weapon != null) {
                var hit = new HitData { m_damage = weapon.GetDamage() };
                hit.m_damage.Modify(Mathf.Max(1, attack?.m_damageMultiplier ?? 1) * Mathf.Max(1, attacker.GetLevel()));
                hit.ApplyResistance(Body.GetDamageModifiers(default), out var _); incoming = hit.GetTotalDamage();
            }
            bool ranged = attack?.m_attackProjectile || attack?.m_attackType == Attack.AttackType.Projectile;
            float reach = ranged ? 32 : Mathf.Clamp(Mathf.Max(attack?.m_attackRange ?? 3, weapon?.m_shared.m_aiAttackRange ?? 3) + 1.5f, 3, 15);
            var facts = new CombatTactics.Facts {
                attacking = attacker, area = area, unblockable = weapon == null || !weapon.m_shared.m_blockable,
                ranged = ranged, facingUs = attacker && Vector3.Dot(attacker.transform.forward, (transform.position - attacker.transform.position).normalized) > .2f,
                distance = attacker ? Vector3.Distance(transform.position, attacker.transform.position) : 100,
                reach = reach, reactionSeconds = Time.time - defenseObservedAt, incomingDamage = incoming,
                blockPower = Usable(blocker) ? blocker.GetBlockPower(WeaponLevel(Skills.SkillType.Blocking) / 100f) : 0,
                stamina = CombatStamina, maxStamina = MaxCombatStamina, health = Body.GetHealthPercentage(),
                slowed = ImpairedMovement, wet = HasEffect(SEMan.s_statusEffectWet), damageOverTime = OngoingDamage,
                nearbyEnemies = observedEnemies.Count(c => c && !c.IsDead() && Vector3.Distance(transform.position, c.transform.position) < 5),
                protectingPlayer = protectingPlayer, wasRecovering = tacticalRecovering
            };
            var decision = CombatTactics.Decide(facts);
            tacticalRecovering = decision == CombatTactics.Action.Recover;
            if (decision == CombatTactics.Action.Guard) {
                CancelBowDraw(); Body.SetLookDir((attacker.GetCenterPoint() - Body.GetEyePoint()).normalized, 0);
                SetGuard(true); ai.StopMoving(); combatNote = "Guarding a visible attack; watching stamina"; return true;
            }
            if (decision == CombatTactics.Action.Evade) {
                return TacticalReposition(dt, attacker.transform.position, area ? "Leaving a wide attack" : ranged ? "Moving across the incoming shot" : "Avoiding an attack my guard may not stop", ranged);
            }
            if (decision == CombatTactics.Action.Recover || decision == CombatTactics.Action.Separate) {
                string reason = decision == CombatTactics.Action.Separate ? "Moving out of the surrounding group" :
                    OngoingDamage ? "Creating distance while taking ongoing damage" : ImpairedMovement ? "Making room while slowed" : "Making room to recover stamina";
                return TacticalReposition(dt, combatEnemy.transform.position, reason, false);
            }
            return false;
        }

        // Short local paths are sampled for ground, height changes, walls and hazard exposure.
        // This is deliberately conservative; it does not teleport or grant dodge invulnerability.
        private bool TacticalRoute(Vector3 candidate, out Vector3 destination)
        {
            destination = candidate; Vector3 start = transform.position, previous = start;
            float distance = Vector3.Distance(start, candidate);
            if (distance > 12 || distance < .2f) return false;
            float initialExposure = GroundExposure(start), exposure = initialExposure;
            int steps = Mathf.CeilToInt(distance / .65f);
            for (int i = 1; i <= steps; i++) {
                Vector3 point = Vector3.Lerp(start, candidate, (float)i / steps); point.y = previous.y;
                if (!Physics.Raycast(point + Vector3.up * 1.2f, Vector3.down, out var ground, 2.4f, TacticalMask, QueryTriggerInteraction.Ignore)) return false;
                point.y = ground.point.y;
                if (Mathf.Abs(point.y - previous.y) > .65f || Vector3.Angle(ground.normal, Vector3.up) > 40 || point.y < WaterAt(point) - .3f) return false;
                Vector3 step = point - previous;
                if (Physics.CapsuleCast(previous + Vector3.up * .55f, previous + Vector3.up * 1.45f, .3f,
                    step.normalized, step.magnitude, TacticalMask, QueryTriggerInteraction.Ignore)) return false;
                float next = GroundExposure(point);
                if (!CombatTactics.ExposureStep(exposure, next, initialExposure) || !HazardStep(previous, point)) return false;
                exposure = next; previous = point;
            }
            if (exposure > 0) return false;
            destination = previous; return true;
        }
        private bool FindTacticalPosition(Vector3 danger, bool lateral, out Vector3 best, bool seekShot = false)
        {
            best = transform.position; float bestScore = float.NegativeInfinity;
            Vector3 away = transform.position - danger; away.y = 0;
            if (away.sqrMagnitude < .1f) away = transform.forward;
            away.Normalize(); Vector3 side = Vector3.Cross(Vector3.up, away);
            foreach (float radius in TacticalRadii) for (int i = 0; i < 8; i++) {
                Vector3 direction = Quaternion.AngleAxis(i * 45, Vector3.up) * away;
                if (!TacticalRoute(transform.position + direction * radius, out var point)) continue;
                float nearest = 12;
                foreach (var c in observedEnemies) if (c && !c.IsDead()) nearest = Mathf.Min(nearest, Vector3.Distance(point, c.transform.position));
                if (nearest < 2) continue;
                float playerDistance = Player ? Vector3.Distance(point, Player.transform.position) : 0;
                if (protectingPlayer && playerDistance > Mathf.Max(8, Vector3.Distance(transform.position, Player.transform.position))) continue;
                float score = nearest * 2 + Vector3.Dot(direction, away) * 2 - radius * .35f - playerDistance * .1f;
                if (lateral) score += Mathf.Abs(Vector3.Dot(direction, side)) * 5;
                if (seekShot && combatEnemy) {
                    float range = Vector3.Distance(point, combatEnemy.transform.position);
                    if (range < 5 || range > 22 || Physics.Linecast(point + Vector3.up * 1.5f, combatEnemy.GetCenterPoint(), TacticalMask, QueryTriggerInteraction.Ignore)) continue;
                    score += 20;
                } else if (combatEnemy && IsRangedEnemy(combatEnemy) && Physics.Linecast(combatEnemy.GetEyePoint(), point + Vector3.up, TacticalMask, QueryTriggerInteraction.Ignore)) score += 4;
                if (score > bestScore) { bestScore = score; best = point; }
            }
            return !float.IsNegativeInfinity(bestScore);
        }
        private bool TacticalReposition(float dt, Vector3 danger, string reason, bool lateral)
        {
            CancelBowDraw(); SetGuard(false);
            if (Time.time >= tacticalPositionAt || tacticalMovementReason != reason) {
                tacticalPositionAt = Time.time + .6f; tacticalMovementReason = reason;
                haveTacticalPosition = FindTacticalPosition(danger, lateral, out tacticalPosition, reason == "Finding a clear shot");
            }
            if (haveTacticalPosition && GroundExposure(tacticalPosition) <= 0) {
                // Stop below ordinary pursuit speed while exhausted; movement is still native.
                MoveMethod.Invoke(ai, new object[] { dt, tacticalPosition, .6f, CombatStamina >= 20 });
                combatNote = reason;
            } else { ai.StopMoving(); combatNote = reason + ": no safe local escape"; }
            return true;
        }
    }
}
