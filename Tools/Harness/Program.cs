using PingTheKiller.Harness;

if (args.Length == 2 && args[0] == "--render-map")
{
    MapSvg.Write(args[1]);
    Console.WriteLine($"Map written: {Path.GetFullPath(args[1])}");
    return 0;
}

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Harness --render-map <path.svg>");
    Console.WriteLine("Harness --host 127.0.0.1 --port 7777 --h6-port 7778 [--h6-host <host>] --report docs/qa/harness-latest.json [--only H1,H3] [--duration 60] [--threshold 90] [--timeout 5] [--expect-los]");
    Console.WriteLine("H1-H5: one fresh SANDBOX server (U2a rules) WITHOUT --autostart: Server --port 7777 --bots 0 --bind 127.0.0.1 --sandbox; harness first human/host, SetRole(Killer), SetBots(6), StartMatch. Selected match checks share this match.");
    Console.WriteLine("H6: a SECOND fresh NORMAL (non-sandbox) server WITHOUT --autostart: Server --port 7778 --bots 0 --bind 127.0.0.1, passed as --h6-port 7778 (--h6-host defaults to --host); 3 humans play hack, capture, rescue, escape, GameResult. No skills are used.");
    Console.WriteLine("A full run therefore needs two server instances (sandbox for H1-H5, normal for H6): Harness --port 7777 --h6-port 7778.");
    Console.WriteLine("--only H6 without --h6-port uses --port. H6 together with any of H2-H5 fails at once if --h6-port is missing or equals --port on the same host.");
    Console.WriteLine("Survivor worlds may carry the Killer's entry only with a clear line of sight (U2e contract); the harness judges every entry itself. --expect-los (no value) makes H6 also require at least one legal visible entry and one blocked-and-absent tick.");
    Console.WriteLine("Omitted gates are unverified (gatesPassed=false); --only does not rescale the 100-point score.");
    return 0;
}

try
{
    var options = Options.Parse(args);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    Console.WriteLine("H1-H5 require a fresh sandbox lobby server (--bots 0 --bind 127.0.0.1 --sandbox, WITHOUT --autostart; 2 humans + 6 bots); H6 requires its own fresh normal lobby server (--bots 0 --bind 127.0.0.1, no --sandbox; --h6-port; 3 humans + 0 bots). Every network wait has a deadline.");
    var report = await HarnessRunner.RunAsync(options, cancellation.Token);
    Console.WriteLine("ID  SCORE  GATE  RESULT");
    foreach (var item in report.Items)
        Console.WriteLine($"{item.Id,-3} {item.Score,2}/{item.Max,-2}  {(item.Gate ? "yes" : "no"),-4}  {(item.Passed ? "PASS" : "FAIL")}  {item.Evidence}");
    Console.WriteLine($"Total: {report.Total}/100; gatesPassed: {report.GatesPassed}; threshold: {options.Threshold}");
    string path = Path.GetFullPath(options.Report);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, report.ToJson());
    return report.MeetsThreshold(options.Threshold) ? 0 : 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Harness: {ex.Message}");
    return 2;
}
