namespace PingTheKiller.Client.World
{
    // Own-only server state (HUD_SPEC 11 Q5/Q6, N5): skill cooldown, rescue progress, post-rescue protection and the
    // Killer's execution grace, from S2C_SelfState (protocol v0.3). Available stays false until the first packet of a
    // match; until then every element that needs it shows its fallback (no cooldown ring, no execution prompt).
    public interface ISelfState
    {
        bool Available { get; }
        float SkillCooldown { get; }
        float RescueProgress { get; }
        float InvulnerableLeft { get; }
        float ExecutionGraceLeft { get; }
    }

    // The holder and its local clock, pure (no engine types). The server sends S2C_SelfState on every change plus a
    // 1 Hz refresh, so between packets the three timers count down by real time, floored at 0, and every received value
    // replaces the local one (snap). RescueProgress is shown exactly as received: no extrapolation (HUD_SPEC 4.4).
    public sealed class SelfStateHolder : ISelfState
    {
        // A cooldown cycle opens when a value above this arrives (a new use, or the first packet mid-cooldown). The
        // skill_ready cue fires once when the opened cycle reaches 0; a late refresh of a few hundredths never re-opens it.
        public const float ReadyRearmSeconds = .5f;
        private float cooldownAt, invulnerableAt, graceAt;
        private double receivedAt, now;
        private bool cycleOpen;
        private int readyEdges;
        public bool Available { get; private set; }
        public float SkillCooldown { get; private set; }
        public float RescueProgress { get; private set; }
        public float InvulnerableLeft { get; private set; }
        public float ExecutionGraceLeft { get; private set; }
        // Number of S2C_SelfState packets fed since the last Reset (dev log and tests).
        public int Received { get; private set; }

        // One S2C_SelfState. at = its arrival time (LinkClock seconds); NaN = the clock's current time. Values are
        // cleaned (NaN or negative -> 0, rescue clamped to 0..1, timers to an hour) and replace the counted-down ones.
        public void Feed(float skillCooldown, float rescueProgress, float invulnerableLeft, float executionGraceLeft, double at = double.NaN)
        {
            if (double.IsNaN(at) || double.IsInfinity(at)) at = now;
            cooldownAt = Clean(skillCooldown, 3600);
            RescueProgress = Clean(rescueProgress, 1);
            invulnerableAt = Clean(invulnerableLeft, 3600);
            graceAt = Clean(executionGraceLeft, 3600);
            receivedAt = at;
            if (at > now) now = at;
            Available = true;
            Received++;
            if (cooldownAt > ReadyRearmSeconds) cycleOpen = true;
            Count();
        }

        // Every frame: the timers as of now. Time never runs backwards; values never go below 0.
        public void Advance(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time) || time < now) return;
            now = time;
            Count();
        }

        // True once per cooldown cycle, when the opened cycle reached 0 (HUD_SPEC 4.8 / 6.5 skill_ready).
        public bool TakeSkillReady()
        {
            if (readyEdges == 0) return false;
            readyEdges = 0;
            return true;
        }

        // New match, match end, leaving, new link or disconnect.
        public void Reset()
        {
            Available = cycleOpen = false;
            SkillCooldown = RescueProgress = InvulnerableLeft = ExecutionGraceLeft = 0;
            cooldownAt = invulnerableAt = graceAt = 0;
            readyEdges = Received = 0;
        }

        private void Count()
        {
            if (!Available) return;
            float elapsed = (float)(now - receivedAt);
            if (!(elapsed > 0)) elapsed = 0;
            SkillCooldown = Down(cooldownAt, elapsed);
            InvulnerableLeft = Down(invulnerableAt, elapsed);
            ExecutionGraceLeft = Down(graceAt, elapsed);
            if (cycleOpen && SkillCooldown <= 0) { cycleOpen = false; readyEdges++; }
        }

        private static float Down(float value, float elapsed) => value > elapsed ? value - elapsed : 0;
        private static float Clean(float value, float max) => float.IsNaN(value) || value < 0 ? 0 : value > max ? max : value;
    }
}
