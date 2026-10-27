using PingTheKiller.Harness;

if (args.Length == 1 && args[0] is "--help" or "-h")
{
    Console.WriteLine("Harness --host 127.0.0.1 --port 7777 --report docs/qa/harness-latest.json [--only H1,H3] [--duration 60] [--threshold 90] [--timeout 5]");
    Console.WriteLine("H1: any server, including --autostart. H2-H5: one fresh server --bots 0 WITHOUT --autostart; harness first human/host, SetRole(Killer), SetBots(6), StartMatch. Selected match checks share this match.");
    Console.WriteLine("Omitted gates are unverified (gatesPassed=false); --only does not rescale the 100-point score. H6: skipped (needs U2b).");
    return 0;
}

try
{
    var options = Options.Parse(args);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    Console.WriteLine("H2-H5 require a fresh lobby server WITHOUT --autostart; harness will configure 2 humans + 6 bots. Every network wait has a deadline.");
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
