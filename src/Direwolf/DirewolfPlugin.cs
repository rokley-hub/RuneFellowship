using System;
using System.Linq;
using BepInEx;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace Rune.Direwolf
{
    [BepInPlugin(Guid, "Rune Direwolf", "0.1.0")]
    [BepInDependency("local.rune.companion")]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public sealed class DirewolfPlugin : BaseUnityPlugin
    {
        public const string Guid = "local.rune.direwolf";
        public const string PrefabName = "RuneDirewolf";
        private Harmony harmony;
        private bool registered;

        private void Awake()
        {
            harmony = new Harmony(Guid); harmony.PatchAll(typeof(DirewolfPlugin).Assembly);
            CreatureManager.OnVanillaCreaturesAvailable += Register;
        }
        private void OnDestroy()
        {
            CreatureManager.OnVanillaCreaturesAvailable -= Register;
            harmony?.UnpatchSelf();
        }
        private void Register()
        {
            if (registered) return;
            try
            {
                var skin = DirewolfSkin.Load();
                var creature = new CustomCreature(PrefabName, "Wolf", new CreatureConfig { Name = "Direwolf" });
                var prefab = creature.Prefab;
                DirewolfSetup.Configure(prefab);
                if (!CreatureManager.Instance.AddCreature(creature)) throw new InvalidOperationException("Direwolf registration failed");
                registered = true;
                CreatureManager.OnVanillaCreaturesAvailable -= Register;
                Logger.LogInfo("Direwolf candidate registered. Native wolf combat and animation controller retained. No world spawns added.");
            }
            catch (Exception e) { Logger.LogError("Direwolf registration failed: " + e); }
        }
    }

}
