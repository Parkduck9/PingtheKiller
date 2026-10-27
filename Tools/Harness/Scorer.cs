using System.Text.Json;

namespace PingTheKiller.Harness;

public sealed record CheckResult(string Id, int Score, int Max, bool Gate, bool Passed, string Evidence);
public sealed record ScoreReport(int Total, bool GatesPassed, IReadOnlyList<CheckResult> Items)
{
    // An omitted gate was not verified. Subset runs never imply a complete gate pass.
    public bool MeetsThreshold(int threshold) => Total >= threshold && GatesPassed;
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

public static class Scorer
{
    public static readonly IReadOnlyList<string> Ids = Array.AsReadOnly(new[] { "H1", "H2", "H3", "H4", "H5", "H6" });
    public static (int Max, bool Gate) Spec(string id) => id switch
    {
        "H1" => (25, true), "H2" => (20, false), "H3" => (20, true),
        "H4" => (15, true), "H5" => (10, false), "H6" => (10, false),
        _ => throw new ArgumentException($"Unknown check {id}.")
    };

    public static CheckResult Result(string id, bool passed, string evidence)
    {
        var (max, gate) = Spec(id);
        return new(id, passed ? max : 0, max, gate, passed, evidence);
    }

    public static ScoreReport Summarize(IEnumerable<CheckResult> source)
    {
        var items = source.ToArray();
        if (items.Select(x => x.Id).Distinct().Count() != items.Length)
            throw new ArgumentException("Duplicate check results.");
        foreach (var item in items)
        {
            var (max, gate) = Spec(item.Id);
            if (item.Max != max || item.Gate != gate || item.Score != (item.Passed ? max : 0))
                throw new ArgumentException($"Invalid result for {item.Id}.");
        }
        return new(items.Sum(x => x.Score), new[] { "H1", "H3", "H4" }
            .All(id => items.Any(x => x.Id == id && x.Passed && x.Score == x.Max)), items);
    }
}
