using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Rune.Mod
{
    public partial class Companion
    {
        private static readonly System.Reflection.FieldInfo AreaOwnerField = AccessTools.Field(typeof(Aoe), "m_owner");
        private static readonly System.Reflection.FieldInfo AreaHitField = AccessTools.Field(typeof(Aoe), "m_hitData");
        private static readonly System.Reflection.MethodInfo AreaDamageMethod = AccessTools.Method(typeof(Aoe), "GetDamage", System.Type.EmptyTypes);
        private readonly List<RuneAreaWatch> nearbyHarmfulAreas = new List<RuneAreaWatch>();
        private float environmentCheckAt;
        private bool inGroundHazard;
        private Vector3 groundDanger;

        private bool AreaCanHurt(Aoe area)
        {
            if (!area || !area.isActiveAndEnabled || !area.m_hitCharacters) return false;
            var owner = AreaOwnerField.GetValue(area) as Character;
            if (owner == Body && !area.m_hitOwner) return false;
            if (owner && owner != Body && (BaseAI.IsEnemy(owner, Body) ? !area.m_hitEnemy : !area.m_hitFriendly)) return false;
            if (!area.m_hitSame && owner && owner.name == Body.name) return false;
            var damage = (HitData.DamageTypes)AreaDamageMethod.Invoke(area, null);
            if (area.m_useAttackSettings && AreaHitField.GetValue(area) is HitData source) damage = source.m_damage;
            var hit = new HitData { m_damage = damage };
            hit.ApplyResistance(Body.GetDamageModifiers(default), out var _);
            string effect = area.m_statusEffect;
            return hit.GetTotalDamage() > 0 || area.m_launchCharacters || effect == "Burning" || effect == "Poison" ||
                effect == "Frost" || effect == "Tared" || effect == "Smoked";
        }
        private float GroundExposure(Vector3 point)
        {
            float exposure = 0;
            var terrain = Heightmap.FindHeightmap(point);
            if (terrain && terrain.IsLava(point, .1f)) exposure += 5;
            if (EffectArea.IsPointInsideArea(point + Vector3.up * .25f, EffectArea.Type.Burning, .4f)) exposure += 3;
            foreach (var watch in nearbyHarmfulAreas) {
                if (!watch || !watch.Area) continue;
                float penetration = watch.Exposure(point + Vector3.up * .8f);
                if (penetration > 0) exposure += penetration;
            }
            return exposure;
        }
        private bool HazardStep(Vector3 from, Vector3 to)
        {
            bool Lava(Vector3 p) { var map = Heightmap.FindHeightmap(p); return map && map.IsLava(p, .1f); }
            bool Fire(Vector3 p) => EffectArea.IsPointInsideArea(p + Vector3.up * .25f, EffectArea.Type.Burning, .4f);
            if (Lava(to) && !Lava(from) || Fire(to) && !Fire(from)) return false;
            foreach (var watch in nearbyHarmfulAreas) if (watch && watch.Area) {
                float before = watch.Exposure(from + Vector3.up * .8f), after = watch.Exposure(to + Vector3.up * .8f);
                if (!Rune.Shared.CombatTactics.ExposureStep(before, after, before)) return false;
            }
            return true;
        }
        private bool AvoidGroundHazards(float dt)
        {
            if (Time.time >= environmentCheckAt) {
                environmentCheckAt = Time.time + .25f;
                nearbyHarmfulAreas.Clear();
                foreach (var watch in RuneAreaWatch.Active) if (watch && watch.Area &&
                    Vector3.Distance(watch.Area.transform.position, transform.position) < Mathf.Max(48, watch.Area.m_radius + 12) && AreaCanHurt(watch.Area)) nearbyHarmfulAreas.Add(watch);
                inGroundHazard = GroundExposure(transform.position) > 0;
                if (inGroundHazard) {
                    groundDanger = transform.position - transform.forward;
                    foreach (var watch in nearbyHarmfulAreas) if (watch && watch.Area && watch.Exposure(Body.GetCenterPoint()) > 0) {
                        groundDanger = watch.Area.transform.position; break;
                    }
                }
            }
            if (!inGroundHazard) return false;
            CancelMaintenance(); SetCombatTarget(null); ai.SetFollowTarget(null);
            TacticalReposition(dt, groundDanger, "Leaving fire, lava or an active damage zone", false);
            safetyNote = combatNote; lastProgress = Time.time; return true;
        }
    }

    // Track native zones once, including zones without physical hit colliders. No scene-wide
    // searches per companion and no prefab/name catalogue. Lifecycle and count are bounded.
    internal sealed class RuneAreaWatch : MonoBehaviour
    {
        internal static readonly HashSet<RuneAreaWatch> Active = new HashSet<RuneAreaWatch>();
        internal Aoe Area;
        private Collider[] shapes;
        internal void Track(Aoe area) { Area = area; shapes = area.GetComponentsInChildren<Collider>(); OnEnable(); }
        private void OnEnable() { if (Area && Area.isActiveAndEnabled && Active.Count < 256) Active.Add(this); }
        private void OnDisable() { Active.Remove(this); }
        internal float Exposure(Vector3 point)
        {
            if (!Area || !Area.isActiveAndEnabled) return 0;
            float result = 0;
            if (Area.m_useTriggers || Area.m_useCollider) {
                foreach (var shape in shapes) {
                    if (!shape || !shape.enabled || !shape.gameObject.activeInHierarchy || Area.m_useCollider && shape != Area.m_useCollider) continue;
                    var bounds = shape.bounds; bounds.Expand(1.2f);
                    if (!bounds.Contains(point)) continue;
                    var offset = point - bounds.center;
                    result = Mathf.Max(result, .1f + Mathf.Min(bounds.extents.x - Mathf.Abs(offset.x), bounds.extents.z - Mathf.Abs(offset.z)));
                }
                return result;
            }
            return Mathf.Max(0, Area.m_radius + .6f - Vector3.Distance(point, Area.transform.position));
        }
        private void OnDestroy() { Active.Remove(this); }
    }
    [HarmonyPatch(typeof(Aoe), "Awake")]
    internal static class ObserveRuneAreas
    {
        static void Postfix(Aoe __instance) {
            if (!__instance.GetComponent<RuneAreaWatch>()) __instance.gameObject.AddComponent<RuneAreaWatch>().Track(__instance);
        }
    }
}
