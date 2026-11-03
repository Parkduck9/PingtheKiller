using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PingTheKiller.Client.QA.Tests
{
    // The world view is split over four files (WorldView, CharacterViews, WorldProps, WorldMaterials). Code-read checks that
    // are about the world view as a whole read them together; checks about one method read the file that holds it.
    internal static class WorldSources
    {
        public const string View = "World/WorldView.cs";
        public const string Characters = "World/CharacterViews.cs";
        public const string Props = "World/WorldProps.cs";
        public const string Materials = "World/WorldMaterials.cs";

        public static readonly string[] All = { View, Characters, Props, Materials };

        public static IEnumerable<string> FileNames() => All.Select(Path.GetFileName);

        public static string ReadAll() => string.Join("\n", All.Select(QaPaths.ReadScript));
    }
}
