using System;
using System.IO;
using System.Text;
using DuckAndCover.Core;

/// <summary>The lane mock-up: its walls frame the lane without standing in it, and both halves match.</summary>
static class LaneStudyTests
{
    [Test]
    static void LaneWallsStayOutOfTheLaneAndTheTrails()
    {
        var map = LaneStudy.Build();
        var lane = map.paths[0];
        Check.Equal("lane", lane.style, "first path is the lane");
        int edges = 0;
        foreach (var b in map.blocks)
        {
            if (string.IsNullOrEmpty(b.style)) continue;
            edges++;
            // The box's nearest point to the lane line must sit outside the lane.
            float hx = b.size.x * 0.5f, hz = b.size.z * 0.5f;
            float nearest = float.MaxValue;
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                nearest = Math.Min(nearest, lane.DistanceTo(b.center.x + i * hx, b.center.z + j * hz));
            Check.True(nearest >= LaneStudy.LaneWidth * 0.5f - 0.2f, b.name + " stays out of the lane (" + nearest.ToString("0.0") + " m)");
            foreach (var t in map.paths)
                if (t.style == "trail")
                    Check.True(t.DistanceTo(b.center.x, b.center.z) > t.width * 0.5f + 0.5f, b.name + " stays out of a trail");
        }
        Check.True(edges > 40, "the lane is walled in (" + edges + " styled blocks)");
    }

    [Test]
    static void LaneStudyIsSymmetric()
    {
        var map = LaneStudy.Build();
        foreach (var b in map.blocks)
        {
            bool twin = false;
            foreach (var o in map.blocks)
                if (Math.Abs(o.center.x + b.center.x) < 0.01f && Math.Abs(o.center.z + b.center.z) < 0.01f && o.style == b.style && o.kind == b.kind) twin = true;
            Check.True(twin, b.name + " has a turned twin");
        }
    }

    /// <summary>dotnet run --project Tests/CoreSim -- report lanestudy out.svg: a top-down plan of the mock-up.</summary>
    public static int Report(string[] args)
    {
        var map = LaneStudy.Build();
        const float S = 8f;
        string X(float x) => ((x + map.halfX) * S).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        string Y(float z) => ((map.halfZ - z) * S).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        string N(float v) => (v * S).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='").Append(N(map.halfX * 2f)).Append("' height='").Append(N(map.halfZ * 2f)).Append("'>");
        sb.Append("<rect width='100%' height='100%' fill='#4a5a2a'/>");
        foreach (var p in map.paths)
        {
            sb.Append("<polyline fill='none' stroke-linecap='round' stroke-linejoin='round' stroke='").Append(p.style == "lane" ? "#a08c7c" : "#6e8a3a").Append("' stroke-width='").Append(N(p.width)).Append("' points='");
            foreach (var q in p.points) sb.Append(X(q.x)).Append(',').Append(Y(q.z)).Append(' ');
            sb.Append("'/>");
        }
        foreach (var b in map.blocks)
        {
            if (b.kind == BlockKind.Ground) continue;
            string fill = b.style == "treeline" ? "#2e8a3a" : b.style == "rocks" ? "#6e4c82" : "#333";
            sb.Append("<rect x='").Append(X(b.Min.x)).Append("' y='").Append(Y(b.Max.z)).Append("' width='").Append(N(b.size.x)).Append("' height='").Append(N(b.size.z)).Append("' fill='").Append(fill).Append("'/>");
        }
        sb.Append("</svg>");
        if (args.Length > 0) { File.WriteAllText(args[0], sb.ToString()); Console.WriteLine("Wrote " + args[0]); }
        return 0;
    }
}
