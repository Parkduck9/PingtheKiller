using Xunit;

namespace PingTheKiller.Tests;

public sealed class ArchitectureTests
{
    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PingTheKiller.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Cannot find repository root.");
    }

    [Fact]
    public void SharedRuntimeDoesNotReferenceForbiddenDependencies()
    {
        string runtime = Path.Combine(FindRepositoryRoot(), "Shared", "Runtime");
        string[] files = Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            string source = File.ReadAllText(file);
            foreach (string forbidden in new[] { "UnityEngine", "System.Text.Json", "Newtonsoft", "System.Linq" })
                Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SharedPackageContainsNoBinOrObjDirectories()
    {
        string shared = Path.Combine(FindRepositoryRoot(), "Shared");
        foreach (string directory in Directory.GetDirectories(shared, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(directory);
            Assert.False(name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase), directory);
        }
    }
}
