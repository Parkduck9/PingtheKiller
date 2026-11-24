using System.Globalization;
using System.Text;
using PingTheKiller.ServerCore.Map;
using PingTheKiller.Shared.Game;

namespace PingTheKiller.Harness;

public static class MapSvg
{
    public static string Render(FixedMap map)
    {
        var svg = new StringBuilder();
        svg.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"800\" height=\"800\" viewBox=\"0 0 800 800\">");
        svg.AppendLine("<title>Map v2: 80 m city blocks (north up)</title><rect width=\"800\" height=\"800\" fill=\"#e8e6e1\"/>");
        string N(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        void Box(float x, float z, float xx, float zz, string fill) => svg.AppendLine($"<rect x=\"{N(x * 10)}\" y=\"{N((FixedMap.Size - zz) * 10)}\" width=\"{N((xx - x) * 10)}\" height=\"{N((zz - z) * 10)}\" fill=\"{fill}\"/>");
        foreach (var w in map.Walls) Box(w.MinX, w.MinZ, w.MaxX, w.MaxZ, "#2c2f33");
        foreach (var o in map.Obstacles) Box(o.MinX, o.MinZ, o.MaxX, o.MaxZ, "#888888");
        for (int i = 0; i <= FixedMap.Size; i += 10)
            svg.AppendLine($"<path d=\"M {i * 10} 0 V 800 M 0 {i * 10} H 800\" stroke=\"#999\" stroke-opacity=\".3\" fill=\"none\"/>");
        svg.AppendLine("<rect x=\"0\" y=\"0\" width=\"800\" height=\"800\" fill=\"none\" stroke=\"#2c2f33\" stroke-width=\"2\"/>");
        void Point(MapPoint p, float radius, string color, string label)
        {
            svg.AppendLine($"<circle cx=\"{N(p.X * 10)}\" cy=\"{N((FixedMap.Size - p.Z) * 10)}\" r=\"{N(radius * 10)}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"2\"/>");
            svg.AppendLine($"<text x=\"{N(p.X * 10 + 5)}\" y=\"{N((FixedMap.Size - p.Z) * 10 - 5)}\" font-family=\"sans-serif\" font-size=\"12\" fill=\"{color}\">{label}</text>");
        }
        for (int i = 0; i < map.TerminalPositions.Count; i++) Point(map.TerminalPositions[i], 1.5f, "#735000", $"T{i}");
        for (int i = 0; i < map.Exits.Count; i++) Point(map.Exits[i], 2, "#405594", $"E{i}");
        for (int i = 0; i < map.ExitSwitches.Count; i++) Point(map.ExitSwitches[i], GameConstants.ExitSwitchRadius, "#6a7fb8", $"W{i}");
        for (int i = 0; i < map.HatchPoints.Count; i++) Point(map.HatchPoints[i], .7f, "#574e39", $"H{i}");
        for (int i = 0; i < map.SpawnPoints.Count; i++) Point(map.SpawnPoints[i], .4f, i == 0 ? "#e5484d" : "#167e76", i == 0 ? "K" : $"S{i}");
        return svg.AppendLine("</svg>").ToString();
    }
    public static void Write(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Render(new FixedMap()));
    }
}
