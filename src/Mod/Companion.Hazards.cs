using System;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        private readonly Collider[] hazardColliders = new Collider[256];
        private float hazardScanAt, hazardUntil;
        private Vector3 hazardDestination;
        private string hazardReason = "";
        private static readonly System.Reflection.FieldInfo ProjectileOwner = AccessTools.Field(typeof(Projectile), "m_owner");
        private bool AvoidProjectile(Projectile projectile)
        {
            if (!projectile || (projectile.transform.position - transform.position).sqrMagnitude > 32 * 32) return false;
            var owner = ProjectileOwner.GetValue(projectile) as Character; var velocity = projectile.GetVelocity();
            if (!owner || !BaseAI.IsEnemy(Body, owner) || velocity.sqrMagnitude < 4) return false;
            var relative = Body.GetCenterPoint() - projectile.transform.position;
            float arrival = Vector3.Dot(relative, velocity) / velocity.sqrMagnitude;
            if (arrival < 0 || arrival > .9f || (relative - velocity * arrival).sqrMagnitude > 2.25f) return false;
            Vector3 side = Vector3.Cross(Vector3.up, velocity).normalized * 3;
            var escape = transform.position + side;
            if (!SafeGround(escape)) escape = transform.position - side;
            if (!SafeGround(escape)) return false;
            hazardDestination = escape; hazardUntil = Time.time + .7f; hazardReason = "Sidestepping an incoming projectile"; return true;
        }
        private bool AvoidMovingHazards(float dt)
        {
            if (Time.time >= hazardScanAt) {
                hazardScanAt = Time.time + .2f;
                // Native arrows use raycasts and need not have a Collider.
                foreach (var shot in RuneProjectileWatch.Active) if (AvoidProjectile(shot)) break;
                int count = Physics.OverlapSphereNonAlloc(transform.position, 18, hazardColliders);
                // In crowded bases, prioritize the immediate collision area
                // instead of silently dropping a newly spawned falling log.
                if (count == hazardColliders.Length) count = Physics.OverlapSphereNonAlloc(transform.position, 6, hazardColliders);
                for (int i = 0; i < count; i++) {
                    var collider = hazardColliders[i]; if (!collider) continue;
                    var log = collider.GetComponentInParent<TreeLog>();
                    var tree = collider.GetComponentInParent<TreeBase>();
                    var body = collider.attachedRigidbody;
                    if ((!log && !tree) || !body || body.isKinematic || body.velocity.sqrMagnitude + body.angularVelocity.sqrMagnitude < .8f) continue;
                    var bounds = collider.bounds; bounds.Expand(3); var predicted = bounds; predicted.center += body.velocity * .6f;
                    if (!bounds.Contains(transform.position + Vector3.up) && !predicted.Contains(transform.position + Vector3.up)) continue;
                    Vector3 away = transform.position - bounds.center; away.y = 0;
                    if (away.sqrMagnitude < 1) away = Vector3.Cross(Vector3.up, collider.transform.up);
                    if (away.sqrMagnitude < .1f) away = transform.right;
                    var destination = transform.position + away.normalized * 5;
                    if (!SafeGround(destination)) destination = transform.position - away.normalized * 5;
                    if (SafeGround(destination)) { hazardDestination = destination; hazardUntil = Time.time + 1.3f; hazardReason = "Moving clear of a falling tree or rolling log"; break; }
                }
            }
            if (Time.time >= hazardUntil) return false;
            SetGuard(false); SetCombatTarget(null); ai.SetFollowTarget(null);
            safetyNote = hazardReason;
            Move(dt, hazardDestination, .6f); lastProgress = Time.time;
            return true;
        }
    }
    internal sealed class RuneProjectileWatch : MonoBehaviour
    {
        internal static readonly System.Collections.Generic.HashSet<Projectile> Active = new System.Collections.Generic.HashSet<Projectile>();
        private Projectile projectile;
        internal void Track(Projectile value) { projectile = value; Active.Add(value); }
        private void OnDestroy() { if (!ReferenceEquals(projectile, null)) Active.Remove(projectile); }
    }
    [HarmonyPatch(typeof(Projectile), "Awake")]
    internal static class ObserveRuneProjectiles
    {
        static void Postfix(Projectile __instance) {
            if (RuneProjectileWatch.Active.Count < 512) __instance.gameObject.AddComponent<RuneProjectileWatch>().Track(__instance);
        }
    }
}
