using System;
using System.IO;
using System.Linq;
using Rune.Shared;
using UnityEngine;
using Jotunn.Managers;

namespace Rune.Mod
{
    public partial class Plugin
    {
        private float recipeCatalogAt;
        private string recipeCatalogWorld = "";
        internal static MaterialNeed[] RecipeMaterials(Piece.Requirement[] requirements) => requirements
            .Where(r => r.m_resItem && r.GetAmount(1) > 0).Select(r => new MaterialNeed {
                prefab = r.m_resItem.name, name = Localization.instance.Localize(r.m_resItem.m_itemData.m_shared.m_name), required = r.GetAmount(1)
            }).ToArray();
        private static RecipeIngredient[] CatalogMaterials(Piece.Requirement[] requirements) => RecipeMaterials(requirements)
            .Select(n => new RecipeIngredient { prefab = n.prefab, name = n.name, required = n.required }).ToArray();
        private void WriteRecipeCatalog()
        {
            if (!Player.m_localPlayer || !ObjectDB.instance) return;
            if (recipeCatalogWorld == World && Time.unscaledTime < recipeCatalogAt) return;
            recipeCatalogWorld = World; recipeCatalogAt = Time.unscaledTime + 15;
            var player = Player.m_localPlayer;
            var recipes = ObjectDB.instance.m_recipes.Where(r => r && r.m_enabled && r.m_item).Select(r => {
                var station = r.GetRequiredStation(1);
                return new RecipeInfo {
                    prefab = r.m_item.name, name = Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name),
                    known = player.IsRecipeKnown(r.m_item.m_itemData.m_shared.m_name), output = r.m_amount,
                    station = station ? Localization.instance.Localize(station.m_name) : "By hand", stationLevel = r.GetRequiredStationLevel(1),
                    anyIngredient = r.m_requireOnlyOneIngredient, materials = CatalogMaterials(r.m_resources)
                };
            }).ToList();
            foreach (var id in new[] { "Raft", "Karve", "VikingShip", "piece_workbench" }) {
                var prefab = PrefabManager.Instance.GetPrefab(id); var piece = prefab ? prefab.GetComponent<Piece>() : null;
                if (!piece) continue;
                recipes.Add(new RecipeInfo { prefab = id, name = Localization.instance.Localize(piece.m_name),
                    known = (id != "piece_workbench" || BuildingKnowledge.Known(player, piece)) && piece.m_resources.All(r => !r.m_resItem || player.IsMaterialKnown(r.m_resItem.m_itemData.m_shared.m_name)),
                    station = id == "piece_workbench" ? "Hammer and clear building site" : piece.m_craftingStation ? Localization.instance.Localize(piece.m_craftingStation.m_name) : "By hand",
                    materials = CatalogMaterials(piece.m_resources) });
            }
            AtomicWrite(Path.Combine(BridgePath, "recipes.json"), WireJson.Write(new RecipeBook { timestamp = Rules.Now, world = World, recipes = recipes.ToArray() }));
        }
    }
}
