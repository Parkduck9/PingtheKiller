using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.Killer
{
    public enum ExecutionPrompt : byte { None, Grace, StepBack, Ready }

    // HUD_SPEC 6.4: execution guidance mirrors the server's re-arm rule. The target is the nearest Lagging instance within
    // 3 m. armed = false whenever an instance becomes the target; each snapshot: farther than 1.2 m -> armed, otherwise
    // an in-grace touch disarms (it is spent). Hidden while S2C_SelfState is not available (pending U2c).
    public sealed class ExecutionGuidance
    {
        public const float TargetRange = 3f;
        public const float Contact = GameConstants.CaptureRadius;
        public int Target { get; private set; } = -1;
        public bool Armed { get; private set; }
        public ExecutionPrompt Prompt { get; private set; }

        public void Reset() { Target = -1; Armed = false; Prompt = ExecutionPrompt.None; }

        // Once per snapshot. nearest = handle of the nearest Lagging instance (-1 when none), distance from the Killer.
        public ExecutionPrompt Update(int nearest, float distance, bool selfStateAvailable, float graceLeft)
        {
            if (nearest < 0 || !(distance <= TargetRange)) { Reset(); return Prompt; }
            if (nearest != Target) { Target = nearest; Armed = false; }
            if (!(graceLeft > 0)) graceLeft = 0;
            if (distance > Contact) Armed = true;
            else if (graceLeft > 0) Armed = false;
            Prompt = !selfStateAvailable ? ExecutionPrompt.None
                : graceLeft > 0 ? ExecutionPrompt.Grace
                : Armed ? ExecutionPrompt.Ready : ExecutionPrompt.StepBack;
            return Prompt;
        }
    }
}
