using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PingTheKiller.Harness;
using Xunit;

namespace PingTheKiller.Harness.Tests;

// The harness may read static geometry from PingTheKiller.ServerCore.Map and nothing else from Server.Core
// (no GameServer, transports, ServerOptions, Player): it must stay a black-box TCP client.
public sealed class ServerCoreGuardTests
{
    private const string MapNamespace = "PingTheKiller.ServerCore.Map";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PingTheKiller.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    internal static IReadOnlyList<string> SourceViolations(string text) =>
        Regex.Matches(text, @"ServerCore(?!\.Map\b)[\w.]*").Select(m => m.Value).ToList();

    [Fact]
    public void SourceScanRejectsEveryNonMapServerCoreUsage()
    {
        Assert.Empty(SourceViolations("using PingTheKiller.ServerCore.Map; var m = new PingTheKiller.ServerCore.Map.FixedMap();"));
        Assert.NotEmpty(SourceViolations("using PingTheKiller.ServerCore;"));
        Assert.NotEmpty(SourceViolations("new PingTheKiller.ServerCore.GameServer(o)"));
        Assert.NotEmpty(SourceViolations("using T = PingTheKiller.ServerCore.Transport.TcpTransport;"));
        Assert.NotEmpty(SourceViolations("using static PingTheKiller.ServerCore.ServerOptions;"));
        Assert.NotEmpty(SourceViolations("PingTheKiller.ServerCore.MapX.Thing"));
    }

    [Fact]
    public void HarnessSourcesUseOnlyTheServerCoreMapNamespace()
    {
        string harness = Path.Combine(RepoRoot(), "Tools", "Harness");
        var files = Directory.EnumerateFiles(harness, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin")).ToList();
        Assert.Contains(files, f => f.EndsWith("ScenarioCheck.cs"));
        var offenders = files.SelectMany(f => SourceViolations(File.ReadAllText(f)).Select(v => $"{Path.GetFileName(f)}: {v}")).ToList();
        Assert.True(offenders.Count == 0, string.Join("\n", offenders));
        Assert.Contains(files, f => File.ReadAllText(f).Contains(MapNamespace)); // the scan is not vacuous
    }

    [Fact]
    public void HarnessAssemblyMetadataReferencesOnlyServerCoreMapTypes()
    {
        // Every type reference compiled into Harness.dll (signatures, locals, method bodies, lambdas and
        // async state machines alike) that resolves to the Server.Core assembly must be in the Map namespace.
        using var stream = File.OpenRead(typeof(ScenarioCheck).Assembly.Location);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var serverCore = new List<string>();
        foreach (var handle in md.TypeReferences)
        {
            var type = md.GetTypeReference(handle);
            var scope = type.ResolutionScope;
            while (scope.Kind == HandleKind.TypeReference)
            {
                type = md.GetTypeReference((TypeReferenceHandle)scope);
                scope = type.ResolutionScope;
            }
            if (scope.Kind != HandleKind.AssemblyReference) continue;
            var assembly = md.GetAssemblyReference((AssemblyReferenceHandle)scope);
            if (md.GetString(assembly.Name) != "Server.Core") continue;
            serverCore.Add($"{md.GetString(type.Namespace)}.{md.GetString(type.Name)}");
        }
        Assert.Contains($"{MapNamespace}.FixedMap", serverCore);
        Assert.All(serverCore, name => Assert.StartsWith(MapNamespace + ".", name));
    }

    [Fact]
    public void HarnessProjectReferencesOnlySharedAndServerCore()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot(), "Tools", "Harness", "Harness.csproj"));
        var references = project.Descendants().Where(e => e.Name.LocalName is "ProjectReference" or "PackageReference")
            .Select(e => e.Attribute("Include")!.Value.Replace('\\', '/')).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "../../Server.Core/Server.Core.csproj", "../../Shared/Shared.csproj" }, references);
    }
}
