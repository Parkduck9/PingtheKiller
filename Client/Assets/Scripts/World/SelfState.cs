namespace PingTheKiller.Client.World
{
    // Own-only server state (HUD_SPEC 11 Q5/Q6, N5): skill cooldown, rescue progress, post-rescue protection and the
    // Killer's execution grace. S2C_SelfState arrives with protocol v0.3; until then Available stays false and every
    // element that needs it shows its fallback (no cooldown ring, no execution prompt).
    public interface ISelfState
    {
        bool Available { get; }
        float SkillCooldown { get; }
        float RescueProgress { get; }
        float InvulnerableLeft { get; }
        float ExecutionGraceLeft { get; }
    }

    public sealed class SelfStateHolder : ISelfState
    {
        public bool Available { get; private set; }
        public float SkillCooldown { get; private set; }
        public float RescueProgress { get; private set; }
        public float InvulnerableLeft { get; private set; }
        public float ExecutionGraceLeft { get; private set; }

        // U2c: feed from S2C_SelfState
        // The single adapter point: ClientApp calls this from its S2C_SelfState case once the packet exists in Shared.
        public void Feed(float skillCooldown, float rescueProgress, float invulnerableLeft, float executionGraceLeft)
        {
            SkillCooldown = Clean(skillCooldown, 3600);
            RescueProgress = Clean(rescueProgress, 1);
            InvulnerableLeft = Clean(invulnerableLeft, 3600);
            ExecutionGraceLeft = Clean(executionGraceLeft, 3600);
            Available = true;
        }

        // New match, new link or disconnect.
        public void Reset()
        {
            Available = false;
            SkillCooldown = RescueProgress = InvulnerableLeft = ExecutionGraceLeft = 0;
        }

        private static float Clean(float value, float max) => float.IsNaN(value) || value < 0 ? 0 : value > max ? max : value;
    }
}
