using System.Collections.Concurrent;
using System.Reflection;
using PingTheKiller.ServerCore;
using PingTheKiller.Shared.Game;

// B1-balance diagnostic sweep: all-bot matches in-process, with per-tick state sampling.
// usage: Sweep <survivors> <seedStart> <count> [label] [--detail]
static class Program
{
    static readonly FieldInfo PlayersField = typeof(GameServer).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!;

    sealed class M
    {
        public int Seed, Ticks, Captures, Rescues, LagTimeouts, StageAdvances, ElimThird, ElimTimeout, ElimCollapse, Escaped, Terminals, Hits, Misses, Kicks, Locks;
        public string Winner = "", Reason = "";
        public double PoweredAt = -1, OpenAt = -1, FirstCaptureAt = -1, FirstEscapeAt = -1;
        public double SurvHack, SurvRescue, SurvLag, SurvNormal, SurvSwitch, VisibleSum, NormalSum, LagConcurrent, LagTicks;
        public double Sec => Ticks / 20.0;
        public bool SWin => Winner == "Survivor";
        public int CapturesBeforePower, ActiveAtOpen=-1, ElimAtOpen=-1, CapAfterOpen, Cap2nd; public List<double> RescueDelays=new(); public Dictionary<int,double> CapAt=new();
    }

    static M Run(int seed, int survivors)
    {
        using var server = new GameServer(new ServerOptions { IsOffline = true, Seed = seed, BotCount = survivors + 1, AutoStart = true });
        var m = new M { Seed = seed };
        bool ended = false;
        server.Log += line =>
        {
            double t = m.Sec;
            if (line.StartsWith("exit ") && line.EndsWith(" open") && m.OpenAt < 0) { int a=0,e=0; foreach (var q in (List<Player>)PlayersField.GetValue(server)!) { if (q.IsActiveSurvivor) a++; if (q.Role==Role.Survivor && q.Status==PlayerStatus.Eliminated) e++; } m.ActiveAtOpen=a; m.ElimAtOpen=e; }
            if (line.StartsWith("exits powered")) { if (m.PoweredAt < 0) m.PoweredAt = t; }
            else if (line.StartsWith("exit ") && line.EndsWith(" open")) { if (m.OpenAt < 0) m.OpenAt = t; }
            else if (line.StartsWith("terminal ") && line.EndsWith(" complete")) m.Terminals++;
            else if (line.StartsWith("terminal ") && line.Contains(" locked")) m.Locks++;
            else if (line.StartsWith("kick terminal")) m.Kicks++;
            else if (line.StartsWith("stage ")) m.StageAdvances++;
            else if (line.StartsWith("status "))
            {
                if (line.EndsWith("Lagging reason=capture")) { int pid=int.Parse(line.Split(' ')[1].Substring(7)); m.CapAt[pid]=t; if (m.OpenAt>=0) m.CapAfterOpen++; m.Captures++; if (m.FirstCaptureAt < 0) m.FirstCaptureAt = t; if (m.PoweredAt < 0) m.CapturesBeforePower++; }
                else if (line.EndsWith("reason=third capture")) { m.Captures++; m.ElimThird++; if (m.PoweredAt < 0) m.CapturesBeforePower++; }
                else if (line.EndsWith("Eliminated reason=timeout")) { m.LagTimeouts++; m.ElimTimeout++; }
                else if (line.EndsWith("Eliminated reason=collapse")) m.ElimCollapse++;
                else if (line.Contains("Normal reason=")) { m.Rescues++; int pid=int.Parse(line.Split(' ')[1].Substring(7)); if (m.CapAt.TryGetValue(pid, out var c0)) m.RescueDelays.Add(t-c0); }
                else if (line.Contains("Escaped reason=")) { m.Escaped++; if (m.FirstEscapeAt < 0) m.FirstEscapeAt = t; }
            }
            else if (line.StartsWith("match end "))
            {
                foreach (var part in line.Split(' '))
                {
                    if (part.StartsWith("winner=")) m.Winner = part.Substring(7);
                    else if (part.StartsWith("reason=")) m.Reason = part.Substring(7);
                }
                ended = true;
            }
        };
        var players = (List<Player>)PlayersField.GetValue(server)!;
        byte lastPhase = 0;
        while (!ended && m.Ticks < 18100)
        {
            server.Tick(0.05f);
            m.Ticks++;
            if (ended) break;
            int lag = 0;
            foreach (var p in players)
            {

                Role role = p.Role;
                if (role == Role.Killer)
                {
                    byte phase = p.SwingPhase;
                    if (phase == 2 && lastPhase != 2) { if ((bool)p.SwingHit) m.Hits++; else m.Misses++; }
                    lastPhase = phase;
                    continue;
                }
                PlayerStatus st = p.Status;
                if (st == PlayerStatus.Lagging) { m.SurvLag += 0.05; lag++; }
                else if (st == PlayerStatus.Normal)
                {
                    m.NormalSum += 1;
                    if ((bool)p.Visible) m.VisibleSum += 1;
                    if ((bool)p.Hacking) m.SurvHack += 0.05;
                    else if ((bool)p.Rescuing) m.SurvRescue += 0.05;
                    else if ((int)p.SwitchExit >= 0) m.SurvSwitch += 0.05;
                    else m.SurvNormal += 0.05;
                }
            }
            if (lag > 0) { m.LagConcurrent += lag; m.LagTicks++; }
        }
        return m;
    }

    static double Pct(List<double> s, double q)
    {
        if (s.Count == 0) return double.NaN;
        s.Sort();
        double pos = (s.Count - 1) * q; int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
        return s[lo] + (s[hi] - s[lo]) * (pos - lo);
    }

    static int Main(string[] args)
    {
        int surv = int.Parse(args[0]), start = int.Parse(args[1]), count = int.Parse(args[2]);
        string label = args.Length > 3 && !args[3].StartsWith("--") ? args[3] : "";
        bool detail = args.Contains("--detail");
        var bag = new ConcurrentBag<M>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Parallel.ForEach(Enumerable.Range(start, count), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, s => bag.Add(Run(s, surv)));
        var runs = bag.OrderBy(r => r.Seed).ToList();
        if (detail)
            foreach (var r in runs)
                Console.WriteLine($"seed {r.Seed,5} {r.Sec,6:0} s {r.Winner,-8} {r.Reason,-16} esc {r.Escaped} cap {r.Captures} res {r.Rescues} stage {r.StageAdvances} elim3 {r.ElimThird} elimTO {r.ElimTimeout} elimCol {r.ElimCollapse} term {r.Terminals} pw {r.PoweredAt:0} op {r.OpenAt:0} hit {r.Hits} miss {r.Misses}");
        double n = runs.Count;
        var len = runs.Select(r => r.Sec).ToList();
        var pw = runs.Where(r => r.PoweredAt >= 0).Select(r => r.PoweredAt).ToList();
        var op = runs.Where(r => r.OpenAt >= 0).Select(r => r.OpenAt).ToList();
        double survSec = runs.Sum(r => r.SurvHack + r.SurvRescue + r.SurvLag + r.SurvNormal + r.SurvSwitch);
        Console.WriteLine($"{label} 1v{surv} seeds {start}-{start + count - 1}: SURV WIN {runs.Count(r => r.SWin) / n * 100:0.0}%  median {Pct(len, .5):0} s (p25 {Pct(len, .25):0} / p75 {Pct(len, .75):0})  <240s {runs.Count(r => r.Sec < 240) / n * 100:0.0}%  timeUp {runs.Count(r => r.Reason == "TimeUp") / n * 100:0}%");
        Console.WriteLine($"  per match: captures {runs.Average(r => r.Captures):0.00} (before power {runs.Average(r => r.CapturesBeforePower):0.00})  rescues {runs.Average(r => r.Rescues):0.00}  stageAdv {runs.Average(r => r.StageAdvances):0.00}  elim3rd {runs.Average(r => r.ElimThird):0.00}  elimTimeout {runs.Average(r => r.ElimTimeout):0.00}  elimCollapse {runs.Average(r => r.ElimCollapse):0.00}  escaped {runs.Average(r => r.Escaped):0.00}");
        Console.WriteLine($"  swings hit {runs.Average(r => r.Hits):0.0} miss {runs.Average(r => r.Misses):0.0}  kicks {runs.Average(r => r.Kicks):0.0}  locks {runs.Average(r => r.Locks):0.0}  terminals {runs.Average(r => r.Terminals):0.00}  powered {pw.Count / n * 100:0}% median {Pct(pw, .5):0} s  open {op.Count / n * 100:0}% median {Pct(op, .5):0} s  firstCap median {Pct(runs.Where(r => r.FirstCaptureAt >= 0).Select(r => r.FirstCaptureAt).ToList(), .5):0} s");
        Console.WriteLine($"  survivor time: hack {runs.Sum(r => r.SurvHack) / survSec * 100:0}%  rescue {runs.Sum(r => r.SurvRescue) / survSec * 100:0}%  switch {runs.Sum(r => r.SurvSwitch) / survSec * 100:0}%  lagging {runs.Sum(r => r.SurvLag) / survSec * 100:0}%  other {runs.Sum(r => r.SurvNormal) / survSec * 100:0}%  visible(of normal) {runs.Sum(r => r.VisibleSum) / runs.Sum(r => r.NormalSum) * 100:0}%  avg lagging when any {runs.Sum(r => r.LagConcurrent) / Math.Max(1, runs.Sum(r => r.LagTicks)):0.00}");
        var esc = string.Join(" ", Enumerable.Range(0, surv + 1).Select(k => $"{k}:{runs.Count(r => r.Escaped == k)}"));
        var rd=runs.SelectMany(r=>r.RescueDelays).ToList(); var op2=runs.Where(r=>r.ActiveAtOpen>=0).ToList();
        Console.WriteLine($"  at first open: active {op2.Average(r=>r.ActiveAtOpen):0.00} eliminated {op2.Average(r=>r.ElimAtOpen):0.00}; captures after open {runs.Average(r=>r.CapAfterOpen):0.00}; rescue delay median {Pct(rd,.5):0.0} s p75 {Pct(rd,.75):0.0} s; captures rescued {rd.Count/(double)runs.Sum(r=>r.Captures-r.ElimThird)*100:0}%");
        Console.WriteLine($"  escaped distribution {esc}   ({sw.Elapsed.TotalSeconds:0} s)");
        return 0;
    }
}
