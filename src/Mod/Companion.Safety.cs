using System;
using System.Linq;
using UnityEngine;
using Rune.Shared;

namespace Rune.Mod
{
    public partial class Companion
    {
        private Vector3 lastDryGround;
        private bool haveDryGround;
        private float safetyMessageAt;
        private bool recovering;
        private bool protectingPlayer;
        private Character protectionTarget;
        private string safetyNote = "";
        private bool retreatHeld;
        private Vector3 retreatDestination;
        private static float WaterAt(Vector3 point) => ZoneSystem.instance ? ZoneSystem.instance.m_waterLevel : 30;
        private static bool SafeGround(Vector3 point)
        {
            if (!Heightmap.GetHeight(point, out float height)) return false;
            if (height < WaterAt(point) - .4f) return false;
            int mask = LayerMask.GetMask("terrain", "static_solid", "Default", "piece");
            if (Physics.Raycast(point + Vector3.up * 4, Vector3.down, out var hit, 12, mask) && Vector3.Angle(hit.normal, Vector3.up) > 42) return false;
            return true;
        }
        private void SafetySay(string message) { if (Time.time < safetyMessageAt) return; safetyMessageAt = Time.time + 18; Say(DisplayName + ": " + message); }
        private bool TerrainOrCombatSafety(float dt)
        {
            safetyNote = ""; protectionTarget = null;
            if (!Body.IsSwimming() && SafeGround(transform.position)) { lastDryGround = transform.position; haveDryGround = true; }
            if (Body.IsSwimming()) {
                safetyNote = "Paused: returning to dry ground"; protectingPlayer = false;
                SetCombatTarget(null); ai.SetFollowTarget(null);
                if (haveDryGround) MoveMethod.Invoke(ai, new object[] { dt, lastDryGround, 1f, true }); else ai.StopMoving();
                SafetySay("Heading back to shore. I won't chase targets into deep water."); return true;
            }
            if ((mode == "follow" || mode == "return") && !SafeGround(Player.transform.position)) {
                safetyNote = "Paused: unsafe terrain toward player"; protectingPlayer = false;
                SetCombatTarget(null); ai.SetFollowTarget(null); ai.StopMoving();
                SafetySay("I'll wait on safe ground. I cannot sail after you or safely follow over that drop."); return true;
            }
            var threats = Character.GetAllCharacters().Where(c => c && !c.IsDead() && c != Body && BaseAI.IsEnemy(Body, c) && Vector3.Distance(transform.position, c.transform.position) < 22).ToArray();
            var boss = threats.FirstOrDefault(c => c.IsBoss());
            var playerThreat = Player.GetHealthPercentage() < .55f ? threats.Where(c => (JoinBossFights || !c.IsBoss()) && SafeGround(c.transform.position) &&
                (Vector3.Distance(Player.transform.position, c.transform.position) < 14 || (c.GetComponent<MonsterAI>() && c.GetComponent<MonsterAI>().GetTargetCreature() == Player)))
                .OrderBy(c => Vector3.Distance(Player.transform.position, c.transform.position)).FirstOrDefault() : null;
            var decision = CombatPolicy.Decide(CombatStyle, Body.GetHealthPercentage(), Player.GetHealthPercentage(), recovering, protectingPlayer,
                threats.Length, playerThreat && mode != "stay", boss, boss && boss.InAttack(), JoinBossFights,
                boss && Vector3.Distance(Player.transform.position, boss.transform.position) > 14, followingOrder && mode == "follow");
            recovering = decision.recovering; protectingPlayer = decision.protectPlayer;
            string recovery = recovering ? Body.GetHealth().ToString("F0") + "/" + Body.GetMaxHealth().ToString("F0") + " HP; combat ready at " + (Body.GetMaxHealth() * CombatPolicy.ResumeAt(CombatStyle)).ToString("F1") + " HP" : "";
            if (protectingPlayer) {
                retreatHeld = false;
                protectionTarget = playerThreat;
                safetyNote = "Covering you: your HP " + (Player.GetHealthPercentage() * 100).ToString("F0") + "%";
                SafetySay("You're hurt. I'm covering you; get to safety."); return false;
            }
            if (recovering && threats.Length == 0) {
                Body.Heal(Body.GetMaxHealth() / 600f * dt, false);
                safetyNote = "Recovering while safe work continues (" + recovery + ")";
            }
            // Once recovery forced a retreat, stay at one safe destination until
            // ready. Do not resume work merely by stepping outside a threat radius.
            if (!recovering || followingOrder) retreatHeld = false;
            if (recovering && !followingOrder && (retreatHeld || decision.retreat)) {
                if (!retreatHeld || !SafeGround(retreatDestination) || threats.Any(c => Vector3.Distance(c.transform.position, retreatDestination) < 5)) {
                    var danger = threats.OrderBy(c => Vector3.Distance(c.transform.position, transform.position)).FirstOrDefault();
                    var away = danger ? transform.position - danger.transform.position : -transform.forward; away.y = 0;
                    var candidate = transform.position + away.normalized * 8;
                    retreatDestination = SafeGround(candidate) ? candidate : haveDryGround ? lastDryGround : transform.position;
                    retreatHeld = true;
                }
                safetyNote = "Recovering at safe ground (" + recovery + ")";
                SetGuard(false); SetCombatTarget(null); ai.SetFollowTarget(null);
                if (Vector3.Distance(transform.position, retreatDestination) > 1.2f && SafeGround(retreatDestination)) Move(dt, retreatDestination, .8f); else ai.StopMoving();
                SafetySay("I'm recovering here before I return to danger."); return true;
            }
            if (!decision.retreat) return false;
            safetyNote = recovering ? "Paused for combat recovery (" + recovery + ")" : "Paused: " + (boss && !JoinBossFights ? "boss fights disabled" : threats.Length >= CombatPolicy.CrowdLimit(CombatStyle) ? "surrounded by enemies" : "avoiding a boss attack or unsafe engagement");
            SetCombatTarget(null); ai.SetFollowTarget(null);
            var nearest = boss ? boss : threats.OrderBy(c => Vector3.Distance(c.transform.position, transform.position)).FirstOrDefault();
            if (nearest) {
                var away = transform.position - nearest.transform.position; away.y = 0; away = away.sqrMagnitude > .1f ? away.normalized : -transform.forward;
                var destination = transform.position + away * (boss ? 8 : 5);
                if (SafeGround(destination)) Move(dt, destination, 1); else if (haveDryGround && Vector3.Distance(lastDryGround, nearest.transform.position) > Vector3.Distance(transform.position, nearest.transform.position)) Move(dt, lastDryGround, 1); else ai.StopMoving();
            }
            SafetySay(recovering ? "I'm hurt. Pulling back until I've recovered." : "Giving that attack some room. I'll rejoin when there is an opening."); return true;
        }
    }
}
