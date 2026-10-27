namespace PingTheKiller.Harness;

public static class HarnessRunner
{
    public static async Task<ScoreReport> RunAsync(Options options, CancellationToken ct = default)
    {
        var items = new List<CheckResult>();
        Task<MatchSession>? match = null;
        try
        {
            foreach (string id in Scorer.Ids.Where(options.Only.Contains))
            {
                if (id == "H6")
                {
                    items.Add(Scorer.Result(id, false, "skipped (needs U2b)"));
                    continue;
                }
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(id == "H2" ? options.Duration + options.Timeout * 10 :
                    TimeSpan.FromSeconds(id == "H1" ? 35 : 30) + options.Timeout * 10);
                try
                {
                    string evidence;
                    if (id == "H1") evidence = await FramingCheck.RunAsync(options, deadline.Token);
                    else
                    {
                        match ??= MatchSession.CreateAsync(options, deadline.Token);
                        var session = await match;
                        evidence = id switch
                        {
                            "H2" => await MatchChecks.SyncAsync(session, options, deadline.Token),
                            "H3" => await MatchChecks.MaskingAsync(session, deadline.Token),
                            "H4" => await MatchChecks.AbuseAsync(session, options, deadline.Token),
                            "H5" => await MatchChecks.ReconnectAsync(session, deadline.Token),
                            _ => throw new ArgumentOutOfRangeException(nameof(id))
                        };
                    }
                    items.Add(Scorer.Result(id, true, evidence));
                }
                catch (Exception ex)
                {
                    string reason = ex is OperationCanceledException ? "Timed out or cancelled waiting for server/check completion." : ex.Message;
                    items.Add(Scorer.Result(id, false, $"{ex.GetType().Name}: {reason}"));
                }
                Console.WriteLine($"{id} completed: {(items[^1].Passed ? "PASS" : "FAIL")} — {items[^1].Evidence}");
            }
        }
        finally
        {
            if (match is { IsCompletedSuccessfully: true }) await match.Result.DisposeAsync();
        }
        return Scorer.Summarize(items);
    }
}
