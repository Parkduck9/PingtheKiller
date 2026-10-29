using PingTheKiller.Shared.Game;

namespace PingTheKiller.Client.Killer
{
    public enum ExecutionPrompt : byte { None, Grace, StepBack, Ready }

    // HUD_SPEC 6.4: execution guidance mirrors the server's re-arm rule. The target is the nearest Lagging instance within
    // 3 m. armed = false whenever an instance becomes the target; each snapshot: farther than 1.2 m -> armed, otherwise
    // an in-grace touch disarms (it is spent). The grace seconds are S2C_SelfState.ExecutionGraceLeft; before the first
    // SelfState of a match the grace is unknown, so the prompt waits for it (the armed flag still tracks).
    public sealed class ExecutionGuidance
    {
        public const float TargetRange = 3f;
        public const float Contact = GameConstants.CaptureRadius;
        public int Target { get; private set; } = -1;
        public bool Armed { get; private set; }
        public ExecutionPrompt Prompt { get; private set; }

        public void Reset() { Target = -1; Armed = false; Prompt = ExecutionPrompt.None; }

        // Once per snapshot (and again for the same snapshot when a SelfState arrives after it). nearest = handle of the
        // nearest Lagging instance (-1 when none), distance from the Killer.
        public ExecutionPrompt Update(int nearest, float distance, bool selfStateAvailable, float graceLeft)
        {
            if (nearest < 0 || !(distance <= TargetRange)) { Reset(); return Prompt; }
            if (nearest != Target) { Target = nearest; Armed = false; }
            if (!(graceLeft > 0)) graceLeft = 0;
            if (distance > Contact) Armed = true;
            else if (graceLeft > 0) Armed = false;
            Prompt = !selfStateAvailable ? ExecutionPrompt.None : Line(graceLeft);
            return Prompt;
        }

        // The prompt for this frame: the grace counts down between snapshots, so Grace turns into Step back / Ready as
        // soon as the local countdown reaches 0. No target (or no SelfState yet): None.
        public ExecutionPrompt Display(float graceLeft) => Prompt == ExecutionPrompt.None ? ExecutionPrompt.None : Line(graceLeft > 0 ? graceLeft : 0);

        private ExecutionPrompt Line(float graceLeft) => graceLeft > 0 ? ExecutionPrompt.Grace : Armed ? ExecutionPrompt.Ready : ExecutionPrompt.StepBack;
    }
}
