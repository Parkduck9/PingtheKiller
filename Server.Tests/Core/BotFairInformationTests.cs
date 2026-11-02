using System.Reflection;
using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;
using Xunit;

namespace PingTheKiller.Tests.Core;

// U2d fair-information rule: a bot decides only from what a human in its role receives. Two all-bot servers with the
// same private seed run in lock step; on one of them, right before each bot's view is filled, everything that bot must
// not know is scrambled and restored right after its decision (GameServer.BotProbeForTests). Identical matches prove
// that no decision read the scrambled state. Negative controls scramble legitimate inputs instead and must diverge.
public sealed class BotFairInformationTests
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const int Ticks = 3600;

    private enum Scramble { Hidden, LegitKillerView, LegitThreat }

    [Theory]
    [InlineData(3, 7)]
    [InlineData(11, 7)]
    [InlineData(5, 3)]
    [InlineData(8, 1)]
    public void BotsDecideIdenticallyWhenHiddenStateIsScrambled(int seed, int survivors)
    {
        var (plain, probed, scrambles, divergedAt) = RunPair(seed, survivors, Scramble.Hidden);
        Assert.True(scrambles.Killer > 500 && scrambles.Survivor > 500, $"scrambles killer {scrambles.Killer} survivor {scrambles.Survivor}");
        Assert.True(divergedAt < 0, $"seed {seed} 1v{survivors}: diverged at tick {divergedAt}\nplain  {plain}\nprobed {probed}");
    }

    // Negative controls: the probe is not vacuous. Shifting the killer bot's anonymous view, or moving the killer into a
    // different ThreatLevel band for a survivor bot, changes the match.
    [Theory]
    [InlineData(Scramble.LegitKillerView)]
    [InlineData(Scramble.LegitThreat)]
    public void ScramblingLegitimateInputsChangesTheMatch(object scramble)
    {
        var (_, _, _, divergedAt) = RunPair(3, 7, (Scramble)scramble);
        Assert.True(divergedAt >= 0, "scrambling a legitimate input changed nothing");
    }

    // Brains are separate classes that cannot reach the server or any player: no field, parameter or return value of a
    // brain class is a GameServer, Player or a collection of them.
    [Fact]
    public void BrainClassesHoldNoReferenceToTheServerOrPlayers()
    {
        var forbidden = new[] { typeof(GameServer), typeof(Player) };
        foreach (var type in new[] { typeof(BotBrain), typeof(SurvivorBrain), typeof(KillerBrain), typeof(SurvivorBotView), typeof(KillerBotView) })
        {
            foreach (var field in type.GetFields(All | BindingFlags.Static | BindingFlags.DeclaredOnly))
                Assert.False(Mentions(field.FieldType, forbidden), $"{type.Name}.{field.Name} is a {field.FieldType.Name}");
            foreach (var method in type.GetMethods(All | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.False(Mentions(method.ReturnType, forbidden), $"{type.Name}.{method.Name} returns {method.ReturnType.Name}");
                foreach (var parameter in method.GetParameters())
                    Assert.False(Mentions(parameter.ParameterType, forbidden), $"{type.Name}.{method.Name}({parameter.Name})");
            }
        }
    }

    private static bool Mentions(Type type, Type[] forbidden)
    {
        if (type.HasElementType) return Mentions(type.GetElementType()!, forbidden);
        if (forbidden.Contains(type)) return true;
        return type.IsGenericType && type.GetGenericArguments().Any(a => Mentions(a, forbidden));
    }

    private static (string Plain, string Probed, (int Killer, int Survivor) Scrambles, int DivergedAt) RunPair(int seed, int survivors, Scramble mode)
    {
        using var plain = Server(seed, survivors);
        using var probed = Server(seed, survivors);
        var probe = new Probe(probed, seed, mode);
        probed.BotProbeForTests = probe.Call;
        var plainLog = new List<string>();
        var probedLog = new List<string>();
        plain.Log += plainLog.Add;
        probed.Log += probedLog.Add;
        for (int tick = 0; tick < Ticks; tick++)
        {
            plain.Tick(0.05f);
            probed.Tick(0.05f);
            string a = State(plain), b = State(probed);
            if (a != b || plainLog.Count != probedLog.Count) return (a, b, (probe.KillerScrambles, probe.SurvivorScrambles), tick);
            if (plainLog.Count > 0 && plainLog[^1].StartsWith("match end")) break;
        }
        // The match-start line names the cosmetic wire seed, which differs per server instance by design.
        Assert.Equal(plainLog.Where(l => !l.StartsWith("match start")), probedLog.Where(l => !l.StartsWith("match start")));
        return ("", "", (probe.KillerScrambles, probe.SurvivorScrambles), -1);
    }

    private static GameServer Server(int seed, int survivors) =>
        new GameServer(new ServerOptions { IsOffline = true, Seed = seed, BotCount = survivors + 1, AutoStart = true });

    private static string State(GameServer server) => string.Join(" ", server.PlayerSnapshot()
        .Select(p => $"{p.Id}:{p.Status}:{p.X:R},{p.Z:R}:{(p.Hacking ? 1 : 0)}{(p.Visible ? 1 : 0)}{(p.Rescuing ? 1 : 0)}"));

    // Scrambles and restores state around one bot's decision. Its own System.Random never touches the server's streams.
    private sealed class Probe
    {
        private readonly GameServer _server;
        private readonly Random _random;
        private readonly Scramble _mode;
        private readonly List<Player> _players;
        private readonly List<PlayerState> _killerView;
        private readonly MethodInfo _threatFor, _inSight;
        private readonly List<Action> _restore = new();
        internal int KillerScrambles, SurvivorScrambles;

        internal Probe(GameServer server, int seed, Scramble mode)
        {
            _server = server;
            _random = new Random(seed * 7919);
            _mode = mode;
            _players = (List<Player>)typeof(GameServer).GetField("_players", All)!.GetValue(server)!;
            _killerView = (List<PlayerState>)typeof(GameServer).GetField("_killerView", All)!.GetValue(server)!;
            _threatFor = typeof(GameServer).GetMethod("ThreatFor", All)!;
            _inSight = typeof(GameServer).GetMethod("KillerInSightOf", All)!;
        }

        private TerminalState[] Terminals => (TerminalState[])typeof(GameServer).GetField("_terminals", All)!.GetValue(_server)!;
        private Player? Killer => _players.FirstOrDefault(p => p.Role == Role.Killer);

        internal void Call(int botId, bool before)
        {
            if (!before)
            {
                for (int i = _restore.Count - 1; i >= 0; i--) _restore[i]();
                _restore.Clear();
                return;
            }
            var bot = _players.First(p => p.Id == botId);
            if (bot.Role == Role.Killer) ScrambleForKiller();
            else ScrambleForSurvivor(bot);
        }

        private void ScrambleForKiller()
        {
            if (_mode == Scramble.LegitThreat) return;
            if (_mode == Scramble.LegitKillerView)
            {
                // A legitimate input: the anonymous entries the killer client receives.
                var saved = _killerView.ToArray();
                for (int i = 0; i < _killerView.Count; i++)
                    if (_killerView[i].Role == (byte)Role.Survivor) { var e = _killerView[i]; e.X += 3f; _killerView[i] = e; }
                _restore.Add(() => { _killerView.Clear(); _killerView.AddRange(saved); });
                ++KillerScrambles;
                return;
            }
            foreach (var p in _players)
            {
                if (p.Role != Role.Survivor) continue;
                var (x, z, skill, ready) = (p.X, p.Z, p.Skill, p.SkillReadyAt);
                // A Silent survivor is absent from the killer's view: its position is anywhere standable.
                if (p.Status == PlayerStatus.Normal && !p.Visible)
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        float nx = (float)(_random.NextDouble() * 60), nz = (float)(_random.NextDouble() * 60);
                        if (!_server.Map.CanStand(nx, nz)) continue;
                        p.X = nx;
                        p.Z = nz;
                        break;
                    }
                p.Skill = (byte)_random.Next(SkillIds.SurvivorSkillCount);
                p.SkillReadyAt = ready + _random.NextDouble() * 30 - 15;
                _restore.Add(() => { p.X = x; p.Z = z; p.Skill = skill; p.SkillReadyAt = ready; });
            }
            // Exact terminal progress: the killer only learns done or not done.
            var terminals = Terminals;
            var progress = terminals.Select(t => t.Progress).ToArray();
            for (int i = 0; i < terminals.Length; i++)
                if (terminals[i].Progress < 100) terminals[i].Progress = (byte)_random.Next(100);
            _restore.Add(() => { for (int i = 0; i < terminals.Length; i++) terminals[i].Progress = progress[i]; });
            ++KillerScrambles;
        }

        private void ScrambleForSurvivor(Player bot)
        {
            var killer = Killer;
            if (killer == null || _mode == Scramble.LegitKillerView) return;
            var (x, z, yaw, skill, ready) = (killer.X, killer.Z, killer.Yaw, killer.Skill, killer.SkillReadyAt);
            byte band = (byte)_threatFor.Invoke(_server, new object[] { bot })!;
            bool sight = (bool)_inSight.Invoke(_server, new object[] { bot })!;
            if (sight && _mode == Scramble.Hidden) return; // a killer in line of sight is legitimately known
            killer.Skill = (byte)_random.Next(SkillIds.KillerSkillCount);
            killer.SkillReadyAt = ready + _random.NextDouble() * 20 - 10;
            if (!sight) killer.Yaw = (float)(_random.NextDouble() * 360);
            _restore.Add(() => { killer.X = x; killer.Z = z; killer.Yaw = yaw; killer.Skill = skill; killer.SkillReadyAt = ready; });
            // Hidden: anywhere else with the same ThreatLevel and still out of sight. Legit control: a different band.
            for (int attempt = 0; attempt < 40; attempt++)
            {
                double angle = _random.NextDouble() * 2 * Math.PI, reach = _random.NextDouble() * 40;
                float nx = bot.X + (float)(Math.Sin(angle) * reach), nz = bot.Z + (float)(Math.Cos(angle) * reach);
                if (!_server.Map.CanStand(nx, nz)) continue;
                killer.X = nx;
                killer.Z = nz;
                byte newBand = (byte)_threatFor.Invoke(_server, new object[] { bot })!;
                bool newSight = (bool)_inSight.Invoke(_server, new object[] { bot })!;
                bool ok = _mode == Scramble.Hidden ? newBand == band && !newSight : newBand != band;
                if (ok) { ++SurvivorScrambles; return; }
            }
            killer.X = x;
            killer.Z = z;
        }
    }
}
