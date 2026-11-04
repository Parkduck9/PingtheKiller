using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using Xunit;

namespace PingTheKiller.Tests.Core;

// Post-merge bot-freeze fix: ThreatLevel 2+ from a killer that neither comes closer nor is seen moving (an idle human
// killer, camping) used to keep a survivor bot paused for good. A SurvivorBrain is driven here through its
// SurvivorBotView alone on the real map: one incomplete terminal (index 4, (41, 44)), the killer standing still out of
// sight at its spawn (34, 43.5), 7 m from that terminal.
public sealed class SurvivorCampTests
{
    private const float Dt = 0.05f;
    private const int Terminal = 4;

    private sealed class Sim
    {
        internal readonly FixedMap Map = new FixedMap();
        internal readonly NavGrid Nav;
        internal readonly SurvivorBrain Brain = new SurvivorBrain();
        internal readonly SurvivorBotView View = new SurvivorBotView();
        internal readonly System.Random Random;
        internal float KillerX = 34f, KillerZ = 43.5f, KillerYaw;
        internal bool KillerVisible;
        internal double Time;
        internal BotIntent Intent;

        internal Sim(int seed, float x, float z)
        {
            Nav = new NavGrid(Map);
            Random = new System.Random(seed);
            Assert.True(Map.CanStand(x, z));
            View.SelfId = 1;
            View.X = x;
            View.Z = z;
            View.Speed = GameConstants.SurvivorSpeed;
            View.Status = PlayerStatus.Normal;
            View.Skill = SkillIds.EmergencyRestore;
            View.Terminals = Map.CreateTerminals(1, out byte required);
            View.TerminalCount = View.Terminals.Length;
            View.TerminalsRequired = required;
            for (int i = 0; i < View.TerminalCount; i++)
                if (i != Terminal) View.Terminals[i].Progress = 100;
        }

        internal float TerminalDistance => Dist(View.X, View.Z, View.Terminals[Terminal].X, View.Terminals[Terminal].Z);
        internal bool Hacking => Intent.Interact && TerminalDistance <= SurvivorBrain.HackStopDistance + 0.1f;
        internal bool HackingAt(int index) => Intent.Interact &&
            Dist(View.X, View.Z, View.Terminals[index].X, View.Terminals[index].Z) <= SurvivorBrain.HackStopDistance + 0.1f;

        // One 20 Hz tick: fill the view like GameServer.FillSurvivorView, decide, then move the bot on the map.
        internal void Step()
        {
            Time += Dt;
            View.Time = Time;
            View.Threat = GameServer.ThreatForDistance(Dist(View.X, View.Z, KillerX, KillerZ));
            View.KillerVisible = KillerVisible;
            View.KillerX = KillerVisible ? KillerX : 0;
            View.KillerZ = KillerVisible ? KillerZ : 0;
            View.KillerYaw = KillerVisible ? KillerYaw : 0;
            Intent = Brain.Update(View, Nav, Map, Random);
            float step = View.Speed * Dt;
            var next = Map.Move(View.X, View.Z, Intent.DirX * step, Intent.DirZ * step);
            bool moved = Dist(next.X, next.Z, View.X, View.Z) > 0.001f;
            View.X = next.X;
            View.Z = next.Z;
            // Moving or hacking is Signal; the WorldState of the next tick carries it in this bot's own entry.
            View.Team.Clear();
            View.Team.Add(new PlayerState
            {
                Id = 1, Role = (byte)Role.Survivor, Status = (byte)PlayerStatus.Normal, X = View.X, Z = View.Z,
                Flags = (byte)(moved || Intent.Interact ? PlayerFlags.Signal : PlayerFlags.None)
            });
        }

        // Runs until the bot hacks at the terminal; returns false on time-out.
        internal bool RunUntilHacking(double seconds)
        {
            for (double end = Time + seconds; Time < end;)
            {
                Step();
                if (Hacking) return true;
            }
            return false;
        }
    }

    private static float Dist(float x, float z, float tx, float tz)
    {
        float dx = x - tx, dz = z - tz;
        return (float)System.Math.Sqrt(dx * dx + dz * dz);
    }

    // The idle unseen killer is within ThreatLevel 2 of the hacking spot of the last incomplete terminal: after the scare
    // the bot works it anyway (it used to pause there until the match timer ran out).
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void AnIdleUnseenKillerNearTheLastTerminalDoesNotFreezeTheBot(int seed)
    {
        var sim = new Sim(seed, 41f, 38f);
        double hacking = 0, maxThreatWhileHacking = 0;
        for (int i = 0; i < 180 * 20; i++)
        {
            sim.Step();
            if (!sim.Hacking) continue;
            hacking += Dt;
            maxThreatWhileHacking = System.Math.Max(maxThreatWhileHacking, sim.View.Threat);
        }
        // Hack bursts are 2-4 s with 8-16 s pauses, so 180 s of free work is well above 20 s at the terminal.
        Assert.True(hacking >= 20, $"seed {seed}: hacked {hacking:0.0} s in 180 s next to an idle unseen killer");
        Assert.True(maxThreatWhileHacking >= 2, "the hacking spot must be inside ThreatLevel 2 for this test to mean anything");
    }

    // The same idle killer in plain sight (standing still, facing the terminal) does not freeze the bot either.
    [Fact]
    public void AnIdleKillerInSightNearTheLastTerminalDoesNotFreezeTheBot()
    {
        var sim = new Sim(7, 41f, 38f) { KillerVisible = true, KillerYaw = 90f };
        Assert.True(sim.RunUntilHacking(120), "the bot never hacked next to an idle killer in sight");
    }

    // Once the bot works next to the camping killer, a rising ThreatLevel (the killer walks at it, still unseen) makes it
    // stop hacking and flee at once, as before the fix.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ARisingThreatStillInterruptsTheHackAndMakesTheBotFlee(int seed)
    {
        var sim = new Sim(seed, 41f, 38f);
        Assert.True(sim.RunUntilHacking(120), "the bot never hacked");
        Assert.True(sim.View.Threat == 2, $"threat at the hacking spot {sim.View.Threat}");
        float hackX = sim.View.X, hackZ = sim.View.Z;
        // The killer closes in to 4 m (ThreatLevel 3) while the bot is Signal at the terminal.
        sim.KillerX = hackX - 4f;
        sim.KillerZ = hackZ;
        sim.Step();
        Assert.Equal(3, sim.View.Threat);
        bool stopped = false;
        for (int i = 0; i < 20 && !stopped; i++) { sim.Step(); stopped = !sim.Intent.Interact; }
        Assert.True(stopped, "still hacking 1 s after ThreatLevel rose to 3");
        for (int i = 0; i < 5 * 20; i++) sim.Step();
        Assert.True(Dist(sim.View.X, sim.View.Z, hackX, hackZ) > 3f, "the bot did not flee from the rising threat");
    }

    // A killer seen walking at the bot (the existing coming rule) still interrupts work next to a former camp.
    [Fact]
    public void AKillerSeenComingStillInterruptsTheHack()
    {
        var sim = new Sim(11, 41f, 38f);
        Assert.True(sim.RunUntilHacking(120), "the bot never hacked");
        float hackX = sim.View.X, hackZ = sim.View.Z;
        // The killer appears 9 m away (ThreatLevel 2) and walks straight at the bot.
        sim.KillerVisible = true;
        sim.KillerX = hackX;
        sim.KillerZ = hackZ - 9f;
        sim.KillerYaw = 0f;
        bool stopped = false;
        for (int i = 0; i < 20 && !stopped; i++)
        {
            sim.KillerZ += 0.2f;
            sim.Step();
            stopped = !sim.Intent.Interact;
        }
        Assert.True(stopped, "still hacking 1 s after the killer was seen coming");
    }

    // Attempt 2: every stale phase notes the camp once, not only the first one. The bot works T4 next to the idle unseen
    // killer (its first camp phase; no other work left). Then T1 (58, 27) becomes workable again (regressed), and the
    // killer shows up in sight 11 m off - same ThreatLevel 2, more than TerminalDangerRadius from T4 - shuffles (acute, no
    // rise) and idles there. When that second phase turns stale the bot must leave T4 for T1; before the fix it stayed.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ASecondCampPhaseLeavesTheTerminalAgain(int seed)
    {
        const int other = 1;
        var sim = new Sim(seed, 41f, 38f);
        Assert.True(sim.RunUntilHacking(120), "the bot never hacked T4");
        float hackX = sim.View.X, hackZ = sim.View.Z;
        Assert.Equal(2, sim.View.Threat);
        sim.View.Terminals[other].Progress = 40;
        sim.KillerX = hackX - 11f;
        sim.KillerZ = hackZ - 1f;
        Assert.True(Dist(sim.KillerX, sim.KillerZ, sim.View.Terminals[Terminal].X, sim.View.Terminals[Terminal].Z) > 10f);
        // Facing half toward the bot: watched, not coming.
        float yawToBot = (float)(System.Math.Atan2(hackX - sim.KillerX, hackZ - sim.KillerZ) * 180 / System.Math.PI);
        sim.KillerYaw = yawToBot - 50f;
        sim.KillerVisible = true;
        for (int i = 0; i < 5; i++)
        {
            sim.KillerZ += 0.1f;
            sim.Step();
            Assert.Equal(2, sim.View.Threat);
        }
        bool atOther = false;
        for (int i = 0; i < 90 * 20 && !atOther; i++)
        {
            sim.Step();
            atOther = sim.HackingAt(other);
        }
        Assert.True(atOther, $"seed {seed}: not at T1 90 s after the second camp phase began (at {sim.View.X:0.0}, {sim.View.Z:0.0})");
    }

    // Attempt 2: a killer remembered (seen) right at the last terminal blocks it even on the fallback pass once it camps:
    // the bot never walks into it. The killer stands 1 m in front of T4 on the bot's side, in sight and idle.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void AKillerRememberedAtTheTerminalIsNeverWalkedInto(int seed)
    {
        var sim = new Sim(seed, 41f, 36f) { KillerX = 41f, KillerZ = 43f, KillerYaw = 180f, KillerVisible = true };
        float closest = float.MaxValue;
        for (int i = 0; i < 120 * 20; i++)
        {
            sim.Step();
            Assert.False(sim.Hacking, $"seed {seed}: hacked T4 next to the killer at t={sim.Time:0.0}");
            closest = System.Math.Min(closest, Dist(sim.View.X, sim.View.Z, sim.KillerX, sim.KillerZ));
        }
        Assert.True(closest > GameConstants.CaptureRadius + 1f, $"seed {seed}: came within {closest:0.00} m of the killer");
    }
}
