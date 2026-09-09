using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Jotunn.Managers;

namespace Rune.Mod
{
    // Optional at load time; construction fails explicitly if the installed API is incompatible.
    internal static class PlanBuildAdapter
    {
        private static readonly Lazy<Type> PlanClass = new Lazy<Type>(() => AccessTools.TypeByName("PlanBuild.Plans.PlanPiece"));
        internal static Type PlanType => PlanClass.Value;
        internal static object Field(object obj, string name) => AccessTools.Field(obj.GetType(), name).GetValue(obj);
        internal static object Call(object obj, string name, params object[] args) => AccessTools.Method(obj.GetType(), name).Invoke(obj, args);
        internal static Piece Original(Component plan) => (Piece)Field(plan, "originalPiece");
        internal static Dictionary<string, int> Remaining(Component plan) => (Dictionary<string, int>)Call(plan, "GetRemaining");
        internal static bool Supported(Component plan) => (bool)Call(plan, "HasSupport");
        internal static IEnumerable<object> Blueprints()
        {
            var type = AccessTools.TypeByName("PlanBuild.Blueprints.BlueprintManager");
            var entries = type == null ? null : AccessTools.Field(type, "LocalBlueprints")?.GetValue(null) as IDictionary;
            return entries == null ? Enumerable.Empty<object>() : entries.Values.Cast<object>();
        }
        internal static string Names() => string.Join(", ", Blueprints().Select(b => (string)Field(b, "Name")).OrderBy(n => n).Take(20));
        internal static string Key(string value) => Rune.Shared.Rules.BlueprintKey(value);
        internal sealed class Entry
        {
            internal GameObject Prefab;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal string Text;
        }
        internal static List<Entry> Resolve(string name, Vector3 origin, Quaternion rotation)
        {
            if (PlanType == null) throw new InvalidOperationException("PlanBuild is not loaded. Install PlanBuild and restart Valheim.");
            var matches = Blueprints().Where(b => Key((string)Field(b, "Name")) == Key(name)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException(matches.Length > 1 ? "Several blueprints have that name. Give them unique names in PlanBuild." : "No saved PlanBuild blueprint named '" + name + "'. Available: " + (Names().Length == 0 ? "none; save or import a blueprint in PlanBuild first." : Names()));
            var blueprint = matches[0];
            if (((Array)Field(blueprint, "TerrainMods")).Length > 0) throw new InvalidOperationException("This blueprint includes terrain changes. Place its plan yourself with PlanBuild, then ask me to finish the plan.");
            var entries = ((IEnumerable)Field(blueprint, "PieceEntries")).Cast<object>().ToArray();
            if (entries.Length == 0 || entries.Length > 256) throw new InvalidOperationException("Choose a blueprint with 1 to 256 pieces for this construction run.");
            var result = new List<Entry>();
            foreach (var entry in entries) {
                string prefabName = (string)Field(entry, "name");
                var blacklist = AccessTools.TypeByName("PlanBuild.Plans.PlanBlacklist");
                if (blacklist != null && (bool)AccessTools.Method(blacklist, "Contains", new[] { typeof(string) }).Invoke(null, new object[] { prefabName })) throw new InvalidOperationException("The blueprint contains a piece disabled by PlanBuild: " + prefabName);
                var prefab = PrefabManager.Instance.GetPrefab(prefabName + "_planned");
                if (!prefab || !prefab.GetComponent(PlanType)) throw new InvalidOperationException("Missing planned piece '" + prefabName + "'. Install its required mod before building.");
                if ((Vector3)Call(entry, "GetScale") != Vector3.one) throw new InvalidOperationException("Scaled blueprint pieces need manual placement in PlanBuild first.");
                Vector3 offset = (Vector3)Call(entry, "GetPosition");
                if (!Finite(offset) || offset.magnitude > 40) throw new InvalidOperationException("The blueprint extends beyond Rune's 40 metre construction area.");
                var pieceRotation = (Quaternion)Call(entry, "GetRotation");
                if (!Finite(new Vector3(pieceRotation.x, pieceRotation.y, pieceRotation.z)) || float.IsNaN(pieceRotation.w) || float.IsInfinity(pieceRotation.w)) throw new InvalidOperationException("Invalid blueprint rotation.");
                result.Add(new Entry { Prefab = prefab, Position = origin + rotation * offset, Rotation = rotation * pieceRotation, Text = (string)Field(entry, "additionalInfo") });
            }
            return result;
        }
        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsInfinity(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.z);
        internal static Component Place(Entry entry, long owner)
        {
            var obj = UnityEngine.Object.Instantiate(entry.Prefab, entry.Position, entry.Rotation);
            obj.GetComponent<Piece>().AssignCreator(owner);
            var view = obj.GetComponent<ZNetView>();
            if (!view || !view.IsValid()) throw new InvalidOperationException("PlanBuild could not create a networked plan.");
            view.GetZDO().Set("AdditionalText", entry.Text ?? "");
            var wear = obj.GetComponent<WearNTear>(); if (wear) wear.OnPlaced();
            return obj.GetComponent(PlanType);
        }
        internal static void Deposit(Component plan, Humanoid body)
        {
            // Bypass inventory-extension hooks that can withdraw directly from remote chests.
            // Rune fetches chest supplies physically through her own gathering executor.
            var type = PlanType.GetNestedType("StandardInventory", System.Reflection.BindingFlags.NonPublic);
            if (type == null) throw new InvalidOperationException("This PlanBuild version has an incompatible inventory API.");
            object inventory = Activator.CreateInstance(type, new object[] { body.GetInventory() });
            Call(plan, "AddAllMaterials", inventory);
        }
        internal static bool Funded(Component plan) => (bool)Call(plan, "HasAllResources");
        internal static void Build(Component plan, long owner) => Call(plan, "Build", owner);
    }
}
