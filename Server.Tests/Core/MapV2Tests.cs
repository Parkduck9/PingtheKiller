using System.Diagnostics;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;
using Xunit;
using Xunit.Abstractions;

namespace PingTheKiller.Tests.Core;

public sealed class MapV2Tests(ITestOutputHelper output)
{
    private readonly FixedMap map = new();
    private static double Distance(MapPoint a, MapPoint b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
    private MapPoint RandomPoint(Random r)
    {
        while (true)
        {
            var p = new MapPoint((float)r.NextDouble()*FixedMap.Size, (float)r.NextDouble()*FixedMap.Size);
            if (map.CanStand(p.X, p.Z)) return p;
        }
    }

    [Fact]
    public void GridIsConnectedAndAllLandmarksAreInItsOnlyComponent()
    {
        var nav = new NavGrid(map);
        int start = Enumerable.Range(0, NavGrid.NodeCount).First(nav.IsWalkable);
        var seen = new HashSet<int> { start }; var queue = new Queue<int>(); queue.Enqueue(start);
        while (queue.TryDequeue(out int cell))
            for (int k=0;k<8;k++)
                if(nav.CanStep(cell,k,out int next) && seen.Add(next)) queue.Enqueue(next);
        Assert.Equal(Enumerable.Range(0,NavGrid.NodeCount).Count(nav.IsWalkable), seen.Count);
        var path = new List<MapPoint>(128);
        foreach(var p in map.TerminalPositions.Concat(map.Exits).Concat(map.HatchPoints).Concat(map.SpawnPoints))
        {
            Assert.True(map.CanStand(p.X,p.Z), $"landmark {p.X},{p.Z}");
            Assert.Contains(NavGrid.Cell(p.X,p.Z),seen);
            Assert.True(nav.FindPath(p.X,p.Z,46,45,path));
        }
    }

    [Fact]
    public void LayoutHasRequiredCountsAndClearAlleys()
    {
        Assert.Equal(80,FixedMap.Size); Assert.InRange(map.Walls.Count-4,10,14);
        Assert.InRange(map.Obstacles.Count,8,16); Assert.Equal(8,map.TerminalPositions.Count);
        Assert.Equal(2,map.Exits.Count); Assert.InRange(map.HatchPoints.Count,4,6); Assert.InRange(map.Plazas.Count,2,3);
        Assert.Equal(map.Exits[0],map.Exit);
        foreach(var b in map.Walls.Skip(4)) { Assert.True(b.MaxX-b.MinX>=6); Assert.True(b.MaxZ-b.MinZ>=6); }
        foreach(var plaza in map.Plazas)
        {
            Assert.True(plaza.MaxX-plaza.MinX>=14 && plaza.MaxZ-plaza.MinZ>=14);
            Assert.DoesNotContain(map.Walls, w => w.MinX<plaza.MaxX && w.MaxX>plaza.MinX && w.MinZ<plaza.MaxZ && w.MaxZ>plaza.MinZ);
        }
        var blockers=map.Walls.Concat(map.Obstacles.Select(o=>new Wall(o.MinX,o.MinZ,o.MaxX,o.MaxZ))).ToArray();
        for(int i=0;i<blockers.Length;i++) for(int j=i+1;j<blockers.Length;j++)
        {
            var a=blockers[i];var b=blockers[j];
            float gx=Math.Max(a.MinX,b.MinX)-Math.Min(a.MaxX,b.MaxX);
            float gz=Math.Max(a.MinZ,b.MinZ)-Math.Min(a.MaxZ,b.MaxZ);
            if(gx>0 && gz<=0) Assert.True(gx>=2.5f,$"x gap {i}/{j}: {gx}");
            if(gz>0 && gx<=0) Assert.True(gz>=2.5f,$"z gap {i}/{j}: {gz}");
            if(gx<=0 && gz<=0) Assert.True(i<map.Walls.Count && j<map.Walls.Count && (gx==0 || gz==0),$"overlapping blockers {i}/{j}");
        }
        // The two 4 m dead ends admit entry from the south, close at their north caps and on both sides.
        foreach(var (bottom,top) in new[]{(12f,36f),(46f,68f)})
        {
            Assert.True(map.CanStand(26,bottom-1)); Assert.True(map.CanStand(26,top-1));
            Assert.False(map.CanStand(26,top)); Assert.False(map.CanStand(23,top-1)); Assert.False(map.CanStand(29,top-1));
        }
    }

    [Fact]
    public void LandmarkSpacingAndInteractionCirclesAreClear()
    {
        for(int i=0;i<8;i++)
        {
            var t=map.TerminalPositions[i];
            for(int j=i+1;j<8;j++) Assert.True(Distance(t,map.TerminalPositions[j])>=14);
            foreach(var e in map.Exits) Assert.True(Distance(t,e)>=10);
            Assert.True(map.CanStand(t.X,t.Z,GameConstants.TerminalRadius+FixedMap.PlayerRadius));
        }
        Assert.True(Distance(map.Exits[0],map.Exits[1])>=60);
        foreach(var e in map.Exits) Assert.True(map.CanStand(e.X,e.Z,GameConstants.ExitRadius+FixedMap.PlayerRadius));
    }

    [Fact]
    public void SpawnsAreFarAndHiddenInThreeClusters()
    {
        var killer=map.SpawnPoints[0]; Assert.True(Distance(killer,new MapPoint(40,40))<=10);
        Assert.Equal(8,map.SpawnPoints.Count);
        foreach(var s in map.SpawnPoints.Skip(1))
        { Assert.True(Distance(killer,s)>20); Assert.True(map.SegmentBlocked(killer.X,killer.Z,s.X,s.Z),$"spawn {s.X},{s.Z}"); }
        Assert.True(Distance(map.SpawnPoints[1],map.SpawnPoints[3])<=8);
        Assert.True(Distance(map.SpawnPoints[4],map.SpawnPoints[5])<=8);
        Assert.True(Distance(map.SpawnPoints[6],map.SpawnPoints[7])<=8);
    }

    [Fact]
    public void DeterministicCoverSampleMeetsTarget()
    {
        var r=new Random(61023); int blocked=0;
        for(int i=0;i<2000;)
        {
            var a=RandomPoint(r);var b=RandomPoint(r); double d=Distance(a,b);
            if(d<10 || d>25) continue;
            i++; if(map.SegmentBlocked(a.X,a.Z,b.X,b.Z)) blocked++;
        }
        output.WriteLine($"Cover: {blocked}/2000 = {blocked/20.0:F2}%");
        Assert.InRange(blocked/2000.0,.35,.75);
    }

    [Fact]
    public void ObstaclesBlockBodiesAndMovementButNotSight()
    {
        foreach(var o in map.Obstacles)
        {
            float z=(o.MinZ+o.MaxZ)/2, x=(o.MinX+o.MaxX)/2;
            Assert.False(map.CanStand(x,z));
            Assert.False(map.SegmentBlocked(o.MinX-1,z,o.MaxX+1,z));
            var moved=map.Move(o.MinX-1,z,o.MaxX-o.MinX+2,0);
            Assert.True(moved.X<=o.MinX-FixedMap.PlayerRadius+.001f);
        }
        foreach(var w in map.Walls) Assert.True(map.SegmentBlocked(w.MinX-1,(w.MinZ+w.MaxZ)/2,w.MaxX+1,(w.MinZ+w.MaxZ)/2));
    }

    [Fact]
    public void PathsAreSafeShortAndAllocationFreeWithinBudget()
    {
        var nav=new NavGrid(map); var path=new List<MapPoint>(128);var r=new Random(61024);
        MapPoint NavPoint()
        {
            int cell;do {cell=r.Next(NavGrid.NodeCount);}while(!nav.IsWalkable(cell));
            return NavGrid.Center(cell);
        }
        var pairs=Enumerable.Range(0,200).Select(_=>(A:NavPoint(),B:NavPoint())).ToArray();
        var ratios=new List<double>();
        foreach(var (a,b) in pairs)
        {
            Assert.True(nav.FindPath(a.X,a.Z,b.X,b.Z,path),$"path {a.X},{a.Z} to {b.X},{b.Z}");
            double length=0;
            Assert.Equal(a,path[0]); Assert.Equal(b,path[^1]);
            for(int i=1;i<path.Count;i++)
            {
                Assert.True(nav.LineClear(path[i-1].X,path[i-1].Z,path[i].X,path[i].Z));
                length+=Distance(path[i-1],path[i]);
            }
            ratios.Add(length/Distance(a,b));
        }
        ratios.Sort(); double median=(ratios[99]+ratios[100])/2;
        output.WriteLine($"Path median stretch: {median:F4}"); Assert.True(median<=1.6);
        for(int i=0;i<200;i++) nav.FindPath(1,1,79,79,path);
        var elapsed=new double[200]; long allocated=GC.GetAllocatedBytesForCurrentThread();
        long started=Stopwatch.GetTimestamp();
        for(int i=0;i<200;i++) {var (a,b)=pairs[i];nav.FindPath(a.X,a.Z,b.X,b.Z,path);}
        double average=Stopwatch.GetElapsedTime(started).TotalMilliseconds/200;
        long bytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
        for(int i=0;i<200;i++) {long t=Stopwatch.GetTimestamp();nav.FindPath(1,1,79,79,path); elapsed[i]=Stopwatch.GetElapsedTime(t).TotalMilliseconds;}
        Array.Sort(elapsed);
        output.WriteLine($"Navigation: random mean {average:F4} ms; longest-diagonal p99 {elapsed[197]:F4} ms; allocations {bytes} bytes");
        Assert.Equal(0,bytes); Assert.True(average<=5);
    }

    [Theory]
    [InlineData(66.41f,40f)]
    [InlineData(11.59f,20f)]
    [InlineData(26f,35.59f)]
    public void FollowerEscapesTheSafetyMarginWithoutCrossingABlocker(float x,float z)
    {
        var nav=new NavGrid(map);var follower=new PathFollower();
        Assert.True(map.CanStand(x,z));
        for(int i=0;i<2000 && Distance(new MapPoint(x,z),new MapPoint(70,60))>.1;i++)
        {
            follower.Direction(nav,x,z,70,60,4,i*.05,out float dx,out float dz);
            var p=map.Move(x,z,dx*.2f,dz*.2f);x=p.X;z=p.Z;
            Assert.True(map.CanStand(x,z));
        }
        Assert.True(Distance(new MapPoint(x,z),new MapPoint(70,60))<=.1,$"stopped {x},{z}");
    }
    [Fact]
    public void FollowerReplansForGoalMovementTimerAndStallsAndTraversesDeadEnds()
    {
        var nav=new NavGrid(map); var follower=new PathFollower();
        void Query(double t,float gx=72,float gz=60)=>follower.Direction(nav,26,34,gx,gz,4,t,out _,out _);
        Query(0); Assert.Equal(1,follower.Replans);Query(.5); Assert.Equal(1,follower.Replans);
        Query(.6,75); Assert.Equal(2,follower.Replans);Query(1.5,75); Assert.Equal(3,follower.Replans);
        Query(1.7,75); Assert.Equal(3,follower.Replans); Query(2.51,75); Assert.Equal(4,follower.Replans);
        float x=26,z=34;
        for(int i=0;i<1600 && Distance(new MapPoint(x,z),new MapPoint(72,60))>.1;i++)
        {
            follower.Direction(nav,x,z,72,60,4,i*.05+2,out float dx,out float dz);
            var p=map.Move(x,z,dx*.2f,dz*.2f);x=p.X;z=p.Z;
        }
        Assert.True(Distance(new MapPoint(x,z),new MapPoint(72,60))<=.1,$"stopped {x},{z}");
    }
}
