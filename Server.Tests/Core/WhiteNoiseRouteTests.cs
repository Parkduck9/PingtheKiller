using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.Core;

// White Noise points start next to a terminal or within the first 4 m of a NavGrid route from a landmark (terminals, exits,
// survivor spawns), so they also start on the bent routes of a dense map, not only on straight lines between landmarks.
public sealed class WhiteNoiseRouteTests
{
    private readonly ITestOutputHelper _output;
    public WhiteNoiseRouteTests(ITestOutputHelper output) { _output = output; }

    private static float Length(List<MapPoint> path)
    {
        float length = 0;
        for (int i = 1; i < path.Count; i++)
            length += MathF.Sqrt((path[i].X - path[i - 1].X) * (path[i].X - path[i - 1].X) + (path[i].Z - path[i - 1].Z) * (path[i].Z - path[i - 1].Z));
        return length;
    }

    [Fact]
    public void WhiteNoisePointsStartWithinFourMetresAlongARouteFromALandmarkAndSpreadOverManyLandmarks()
    {
        using var m = RulesMatch.Start(1, -1, SkillIds.WhiteNoise);
        var map = new FixedMap();
        var nav = new NavGrid(map);
        var path = new List<MapPoint>();
        var landmarks = map.TerminalPositions.Concat(map.Exits).Concat(map.SpawnPoints.Skip(1)).ToList();
        var seen = new HashSet<int>();
        var used = new HashSet<int>();
        int points = 0;
        m.Tick(25);
        for (int cast = 0; cast < 12; cast++)
        {
            m[0].Send(new C2S_UseSkill { SkillId = SkillIds.WhiteNoise });
            m.Tick();
            var placed = m.F.Server.PhantomSnapshot().Where(p => !p.IsDummy && seen.Add(p.Serial)).ToList();
            Assert.Equal(SkillIds.WhiteNoiseSignalCount, placed.Count);
            foreach (var p in placed)
            {
                Assert.True(map.CanStand(p.X, p.Z), $"point {p.X},{p.Z}");
                float best = float.MaxValue;
                int nearest = -1;
                for (int i = 0; i < landmarks.Count; i++)
                {
                    if (!nav.FindPath(landmarks[i].X, landmarks[i].Z, p.X, p.Z, path)) continue;
                    float length = Length(path);
                    if (length < best) { best = length; nearest = i; }
                }
                Assert.True(best <= 4.05f, $"point {p.X},{p.Z} is {best:F2} m along the nearest landmark route");
                used.Add(nearest);
                ++points;
            }
            m.Tick((int)(GameConstants.SurvivorSkillCooldownSeconds * GameConstants.TickRate) + 1);
        }
        _output.WriteLine($"{points} points from {used.Count} of {landmarks.Count} landmarks");
        Assert.True(used.Count >= 8, $"{used.Count} landmarks used");
    }
}
