namespace Rune.Direwolf
{
    internal static class MountRules
    {
        internal const float JumpCost = 20;
        internal const float StopInput = -1;
        internal static bool NextAutoRun(bool current, bool toggle, bool manualMovement, bool brake) =>
            !brake && (toggle ? !current : current && !manualMovement);
        internal static bool ShouldStop(float forward, bool brake) => brake || forward <= .5f;
        internal static bool CanJump(bool riderControls, bool grounded, bool swimming, bool blocked, float stamina, float cooldown) =>
            riderControls && grounded && !swimming && !blocked && stamina > JumpCost && cooldown <= 0;
    }
}
