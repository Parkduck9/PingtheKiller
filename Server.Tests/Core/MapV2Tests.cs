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
            Assert.True(nav.FindPath(p.X,p.Z,map.SpawnPoints[0].X,map.SpawnPoints[0].Z,path));
        }
    }

    [Fact]
    public void LayoutHasRequiredCountsAndClearAlleys()
    {
        Assert.Equal(80,FixedMap.Size); Assert.InRange(map.Walls.Count-4,18,28);
        Assert.InRange(map.Obstacles.Count,14,24); Assert.Equal(8,map.TerminalPositions.Count);
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
        foreach(var (x,bottom,top) in new[]{(31f,4f,15f),(25f,59f,68f)})
        {
            Assert.True(map.CanStand(x,bottom-1)); Assert.True(map.CanStand(x,top-1));
            Assert.False(map.CanStand(x,top)); Assert.False(map.CanStand(x-3,top-1)); Assert.False(map.CanStand(x+3,top-1));
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
        Assert.InRange(blocked/2000.0,.50,.75);
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
        var elapsed=new double[200]; long bytes=long.MaxValue; double average=0;
        // The counter is per thread, but a collection started by a parallel test can retire this thread's allocation
        // context and add its unused remainder once (observed: 6576 bytes in one window). A real
        // per-query allocation shows in every window, so the check takes the smallest of three windows of 200 queries.
        for(int window=0;window<3;window++)
        {
            long allocated=GC.GetAllocatedBytesForCurrentThread();
            long started=Stopwatch.GetTimestamp();
            for(int i=0;i<200;i++) {var (a,b)=pairs[i];nav.FindPath(a.X,a.Z,b.X,b.Z,path);}
            if(window==0) average=Stopwatch.GetElapsedTime(started).TotalMilliseconds/200;
            bytes=Math.Min(bytes,GC.GetAllocatedBytesForCurrentThread()-allocated);
        }
        for(int i=0;i<200;i++) {long t=Stopwatch.GetTimestamp();nav.FindPath(1,1,79,79,path); elapsed[i]=Stopwatch.GetElapsedTime(t).TotalMilliseconds;}
        Array.Sort(elapsed);
        output.WriteLine($"Navigation: random mean {average:F4} ms; longest-diagonal p99 {elapsed[197]:F4} ms; allocations {bytes} bytes");
        Assert.Equal(0,bytes); Assert.True(average<=5);
    }

    [Theory]
    [InlineData(53.41f,23f)]
    [InlineData(11.59f,11f)]
    [InlineData(28f,39.59f)]
    public void FollowerEscapesTheSafetyMarginWithoutCrossingABlocker(float x,float z)
    {
        var nav=new NavGrid(map);var follower=new PathFollower();
        Assert.True(map.CanStand(x,z));
        for(int i=0;i<2000 && Distance(new MapPoint(x,z),new MapPoint(75,57))>.1;i++)
        {
            follower.Direction(nav,x,z,75,57,4,i*.05,out float dx,out float dz);
            var p=map.Move(x,z,dx*.2f,dz*.2f);x=p.X;z=p.Z;
            Assert.True(map.CanStand(x,z));
        }
        Assert.True(Distance(new MapPoint(x,z),new MapPoint(75,57))<=.1,$"stopped {x},{z}");
    }
    [Fact]
    public void FollowerReplansForGoalMovementTimerAndStallsAndTraversesDeadEnds()
    {
        var nav=new NavGrid(map); var follower=new PathFollower();
        void Query(double t,float gx=72,float gz=57)=>follower.Direction(nav,31,12,gx,gz,4,t,out _,out _);
        Query(0); Assert.Equal(1,follower.Replans);Query(.5); Assert.Equal(1,follower.Replans);
        Query(.6,75); Assert.Equal(2,follower.Replans);Query(1.5,75); Assert.Equal(3,follower.Replans);
        Query(1.7,75); Assert.Equal(3,follower.Replans); Query(2.51,75); Assert.Equal(4,follower.Replans);
        float x=31,z=12;
        for(int i=0;i<1600 && Distance(new MapPoint(x,z),new MapPoint(72,57))>.1;i++)
        {
            follower.Direction(nav,x,z,72,57,4,i*.05+2,out float dx,out float dz);
            var p=map.Move(x,z,dx*.2f,dz*.2f);x=p.X;z=p.Z;
        }
        Assert.True(Distance(new MapPoint(x,z),new MapPoint(72,57))<=.1,$"stopped {x},{z}");
    }

    private const float RingWidth = 2.5f;
    private static Wall Box(Obstacle o) => new(o.MinX,o.MinZ,o.MaxX,o.MaxZ);
    private static double Gap(Wall a, Wall b)
    {
        double dx=Math.Max(0,Math.Max(a.MinX,b.MinX)-Math.Min(a.MaxX,b.MaxX));
        double dz=Math.Max(0,Math.Max(a.MinZ,b.MinZ)-Math.Min(a.MaxZ,b.MaxZ));
        return Math.Sqrt(dx*dx+dz*dz);
    }
    private static double Gap(Wall b, float x, float z)
    {
        double dx=Math.Max(0,Math.Max(b.MinX-x,x-b.MaxX)), dz=Math.Max(0,Math.Max(b.MinZ-z,z-b.MaxZ));
        return Math.Sqrt(dx*dx+dz*dz);
    }
    private static Wall Bounds(IReadOnlyList<Wall> g) =>
        new(g.Min(w=>w.MinX),g.Min(w=>w.MinZ),g.Max(w=>w.MaxX),g.Max(w=>w.MaxZ));
    private static bool Inside(Wall box, float x, float z) => x>=box.MinX && x<=box.MaxX && z>=box.MinZ && z<=box.MaxZ;
    private Wall[] Blockers() => map.Walls.Concat(map.Obstacles.Select(Box)).ToArray();
    private static MapPoint SwitchPoint(MapPoint exit)
    {
        float dx=FixedMap.Size/2-exit.X, dz=FixedMap.Size/2-exit.Z, length=MathF.Sqrt(dx*dx+dz*dz);
        return new MapPoint(exit.X+dx/length*4,exit.Z+dz/length*4);
    }

    // Buildings are the non-border walls; boxes that touch form one building group.
    private List<List<Wall>> BuildingGroups()
    {
        var boxes=map.Walls.Skip(4).ToArray(); var parent=Enumerable.Range(0,boxes.Length).ToArray();
        int Root(int i)=>parent[i]==i?i:parent[i]=Root(parent[i]);
        for(int i=0;i<boxes.Length;i++) for(int j=i+1;j<boxes.Length;j++)
            if(Gap(boxes[i],boxes[j])==0) parent[Root(i)]=Root(j);
        return Enumerable.Range(0,boxes.Length).GroupBy(Root).Select(g=>g.Select(i=>boxes[i]).ToList()).ToList();
    }

    // A loop is a building group with a footprint of at most 14 x 14 m and a walkable ring at least 2.5 m wide around it.
    // The ring is checked exactly against every other blocker, then flood-filled as one band of walkable NavGrid cells.
    private static bool IsLoop(NavGrid nav, List<Wall> group, IReadOnlyList<Wall> blockers)
    {
        var bounds=Bounds(group);
        if(bounds.MaxX-bounds.MinX>14 || bounds.MaxZ-bounds.MinZ>14) return false;
        if(blockers.Any(b=>!group.Contains(b) && group.Min(w=>Gap(w,b))<RingWidth)) return false;
        var ring=new HashSet<int>();
        for(int cell=0;cell<NavGrid.NodeCount;cell++)
        {
            var c=NavGrid.Center(cell); double d=group.Min(w=>Gap(w,c.X,c.Z));
            if(d>NavGrid.Clearance && d<=RingWidth-NavGrid.Clearance) ring.Add(cell);
        }
        if(ring.Count==0 || !ring.All(nav.IsWalkable)) return false;
        int first=ring.First(); var seen=new HashSet<int>{first}; var queue=new Queue<int>(); queue.Enqueue(first);
        while(queue.TryDequeue(out int cell))
        {
            int z=cell%NavGrid.Cells;
            foreach(int next in new[]{cell+NavGrid.Cells,cell-NavGrid.Cells,z<NavGrid.Cells-1?cell+1:-1,z>0?cell-1:-1})
                if(ring.Contains(next) && seen.Add(next)) queue.Enqueue(next);
        }
        return seen.Count==ring.Count;
    }

    private List<Wall> FindLoops(NavGrid nav)
    {
        var blockers=Blockers();
        return BuildingGroups().Where(g=>IsLoop(nav,g,blockers)).Select(Bounds).ToList();
    }

    [Fact]
    public void FrameKeepsTwoOppositeExitsAndHatchesClearOfExitsAndSwitches()
    {
        bool Near(MapPoint p,int edge)=>edge switch{0=>p.X<=6,1=>p.X>=FixedMap.Size-6,2=>p.Z<=6,_=>p.Z>=FixedMap.Size-6};
        Assert.Contains(Enumerable.Range(0,4),edge=>Near(map.Exits[0],edge) && Near(map.Exits[1],edge^1));
        var points=new List<MapPoint>(map.Exits);
        foreach(var e in map.Exits)
        {
            var s=SwitchPoint(e); points.Add(s);
            Assert.True(map.CanStand(s.X,s.Z,GameConstants.TerminalRadius+FixedMap.PlayerRadius),$"switch {s.X},{s.Z}");
        }
        Assert.InRange(map.HatchPoints.Count,5,6);
        foreach(var h in map.HatchPoints)
        {
            Assert.True(map.CanStand(h.X,h.Z),$"hatch {h.X},{h.Z}");
            foreach(var p in points) Assert.True(Distance(h,p)>=6,$"hatch {h.X},{h.Z} is {Distance(h,p):F2} m from {p.X},{p.Z}");
        }
    }

    // Contact of two touching boxes along their shared edge: the shared span, then each box's extent along that edge.
    private static (float Lo,float Hi,float AMin,float AMax,float BMin,float BMax) Contact(Wall a,Wall b)=>
        a.MaxX==b.MinX || b.MaxX==a.MinX
            ? (Math.Max(a.MinZ,b.MinZ),Math.Min(a.MaxZ,b.MaxZ),a.MinZ,a.MaxZ,b.MinZ,b.MaxZ)
            : (Math.Max(a.MinX,b.MinX),Math.Min(a.MaxX,b.MaxX),a.MinX,a.MaxX,b.MinX,b.MaxX);
    private static bool IsL(Wall a,Wall b)
    {
        var c=Contact(a,b);
        return c.Hi>c.Lo && ((c.Lo==c.AMin && c.Lo==c.BMin) || (c.Hi==c.AMax && c.Hi==c.BMax));
    }
    private static bool IsT(Wall a,Wall b)
    {
        var c=Contact(a,b);
        return c.Hi>c.Lo && ((c.Lo>c.AMin && c.Hi<c.AMax && c.Lo==c.BMin && c.Hi==c.BMax) ||
            (c.Lo>c.BMin && c.Hi<c.BMax && c.Lo==c.AMin && c.Hi==c.AMax));
    }
    // U: two separate arms on the same side of one cap, flush with both of its ends, with an alley between them.
    private static bool IsU(List<Wall> g)=>g.Count==3 && Enumerable.Range(0,3).Any(i=>
    {
        var cap=g[i]; var a=g[(i+1)%3]; var b=g[(i+2)%3];
        if(Gap(a,b)<2.5) return false;
        bool across=(a.MaxZ==cap.MinZ && b.MaxZ==cap.MinZ) || (a.MinZ==cap.MaxZ && b.MinZ==cap.MaxZ);
        bool along=(a.MaxX==cap.MinX && b.MaxX==cap.MinX) || (a.MinX==cap.MaxX && b.MinX==cap.MaxX);
        return (across && Math.Min(a.MinX,b.MinX)==cap.MinX && Math.Max(a.MaxX,b.MaxX)==cap.MaxX)
            || (along && Math.Min(a.MinZ,b.MinZ)==cap.MinZ && Math.Max(a.MaxZ,b.MaxZ)==cap.MaxZ);
    });

    [Fact]
    public void BuildingsVaryInSizeAndFormLTAndUShapes()
    {
        var footprints=map.Walls.Skip(4).GroupBy(w=>(Math.Min(w.MaxX-w.MinX,w.MaxZ-w.MinZ),Math.Max(w.MaxX-w.MinX,w.MaxZ-w.MinZ))).ToArray();
        output.WriteLine($"Footprints: {footprints.Length} distinct, most repeated {footprints.Max(g=>g.Count())} times");
        Assert.True(footprints.Length>=4);
        Assert.All(footprints,g=>Assert.True(g.Count()<=3,$"footprint {g.Key} x{g.Count()}"));
        var groups=BuildingGroups();
        Assert.Contains(groups,g=>g.Count==2 && IsL(g[0],g[1]));
        Assert.Contains(groups,g=>g.Count==2 && IsT(g[0],g[1]));
        Assert.Contains(groups,IsU);
    }

    [Fact]
    public void ShapeClassifiersTellLTAndUApart()
    {
        Assert.True(IsL(new Wall(0,0,10,6),new Wall(0,6,6,12)));
        Assert.False(IsT(new Wall(0,0,10,6),new Wall(0,6,6,12)));
        Assert.True(IsT(new Wall(0,6,14,12),new Wall(4,0,10,6)));
        Assert.False(IsL(new Wall(0,6,14,12),new Wall(4,0,10,6)));
        Assert.True(IsU(new List<Wall>{new(0,0,6,10),new(10,0,16,10),new(0,10,16,16)}));
        Assert.False(IsU(new List<Wall>{new(0,0,6,10),new(8,0,16,10),new(0,10,16,16)}));
    }

    [Fact]
    public void AtLeastSixLoopsWithContinuousRingsSpreadOverBothHalves()
    {
        var loops=FindLoops(new NavGrid(map));
        foreach(var l in loops) output.WriteLine($"Loop ({l.MinX},{l.MinZ})-({l.MaxX},{l.MaxZ})");
        float size=FixedMap.Size;
        int west=loops.Count(l=>l.MinX+l.MaxX<size), east=loops.Count(l=>l.MinX+l.MaxX>size);
        int south=loops.Count(l=>l.MinZ+l.MaxZ<size), north=loops.Count(l=>l.MinZ+l.MaxZ>size);
        output.WriteLine($"Loops: {loops.Count}; west {west}, east {east}, south {south}, north {north}");
        Assert.True(loops.Count>=6);
        Assert.True(Math.Min(Math.Min(west,east),Math.Min(south,north))>=2);
    }

    [Fact]
    public void LoopCheckRejectsNarrowRingsAndOversizedFootprints()
    {
        var nav=new NavGrid(map); var blockers=Blockers();
        var loop=BuildingGroups().First(g=>IsLoop(nav,g,blockers)); var b=Bounds(loop);
        var crowded=blockers.Append(new Wall(b.MaxX+2,b.MinZ,b.MaxX+8,b.MaxZ)).ToArray();
        Assert.False(IsLoop(nav,loop,crowded));
        Assert.False(IsLoop(nav,new List<Wall>{new(b.MinX,b.MinZ,b.MinX+15,b.MinZ+6)},blockers));
    }

    [Fact]
    public void NoOpenFieldOutsideAtMostTwoSmallPlazas()
    {
        Assert.InRange(map.Plazas.Count,1,2);
        foreach(var p in map.Plazas) Assert.True(p.MaxX-p.MinX<=18 && p.MaxZ-p.MinZ<=18);
        var blockers=Blockers(); double worst=0; var at=new MapPoint();
        for(int x=0;x<=FixedMap.Size;x++) for(int z=0;z<=FixedMap.Size;z++)
        {
            if(!map.CanStand(x,z) || map.Plazas.Any(p=>Inside(p,x,z))) continue;
            double d=blockers.Min(b=>Gap(b,x,z));
            if(d>worst){worst=d;at=new MapPoint(x,z);}
        }
        output.WriteLine($"Largest empty-circle radius outside plazas: {worst:F2} m at {at.X},{at.Z}");
        Assert.True(worst<=8);
    }

    [Fact]
    public void TerminalsHugBuildingsAndAtLeastTwoAreExposed()
    {
        foreach(var t in map.TerminalPositions)
            Assert.True(map.Walls.Skip(4).Min(w=>Gap(w,t.X,t.Z))<=6,$"terminal {t.X},{t.Z} has no building within 6 m");
        Assert.True(map.TerminalPositions.Count(t=>map.Plazas.Any(p=>Inside(p,t.X,t.Z)))>=2);
    }
}
