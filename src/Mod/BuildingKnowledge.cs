namespace Rune.Mod
{
    internal static class BuildingKnowledge
    {
        internal static bool Known(Player player, Piece piece)
        {
            if (!player || !piece || !piece.m_enabled) return false;
            // The notification cache can be absent after tool/profile/mod changes.
            // Use the same discovery requirements as the native hammer menu; never
            // grant recipes, ignore undiscovered ingredients or bypass DLC/stations.
            return player.IsRecipeKnown(piece.m_name) || player.HaveRequirements(piece, Player.RequirementMode.IsKnown);
        }
    }
}
