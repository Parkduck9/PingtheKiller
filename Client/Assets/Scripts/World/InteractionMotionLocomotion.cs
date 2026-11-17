namespace PingTheKiller.Client.World
{
    // U10 (scope add, 11/17): the speed-driven locomotion blend MotionSetup writes into Motion.controller. Survivors move
    // at GameConstants.SurvivorSpeed 4.0 m/s and now run there instead of playing a sped-up walk; the Killer (4.6 m/s,
    // 6.9 with Overclock) uses the same tree and runs too. Each child's time scale is the speed it stands for divided by
    // the clip's own ground speed (the planted foot's backward speed at rate 1, measured on Rio), so the feet do not
    // slide at that speed. Between children the 1D blend interpolates both the pose and the rate.
    public static class LocomotionBlend
    {
        // Ground speed of each in-place clip at rate 1 (m/s): the median planted-foot speed on Rio (InteractionMotionTests
        // PlantedSpeed, measured on the pre-U10 tree: Walk 1.715, Run 3.401, InjuredWalk 1.092).
        public const float WalkNatural = 1.72f, RunNatural = 3.40f, InjuredWalkNatural = 1.09f;
        // Thresholds (m/s): below IdleBelow idle; WalkSpeed a brisk walk; RunSpeed the full run (Survivor speed);
        // FastSpeed the Overclocked Killer (the run, sped up to its speed).
        public const float IdleBelow = .49f, WalkFrom = .5f, WalkSpeed = 1.6f, RunSpeed = 4.0f, FastSpeed = 6.9f;
        // Injured: the limp holds up to InjuredLimpSpeed, then blends into the same run (a sped-up limp read wrong).
        public const float InjuredLimpSpeed = 1.6f;

        public static float Rate(float speed, float natural) => natural > 0 ? speed / natural : 1;
    }
}
