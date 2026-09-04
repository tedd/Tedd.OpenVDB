using System.IO.Compression;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class PointScatterConformanceTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() => { using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-point-scatter.json.gz")); using var gzip = new GZipStream(input, CompressionMode.Decompress); using var doc = JsonDocument.Parse(gzip); Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", doc.RootElement.GetProperty("upstreamCommit").GetString()); return doc.RootElement.Clone(); });
    public static IEnumerable<object[]> Engines() => Fixture.Value.GetProperty("engines").EnumerateArray().Select((x, i) => new object[] { i, x.GetProperty("seed").GetUInt32() });
    public static IEnumerable<object[]> Ranges() => Fixture.Value.GetProperty("ranges").EnumerateArray().Select((x, i) => new object[] { i, x.GetProperty("seed").GetUInt32(), x.GetProperty("minimum").GetUInt64(), x.GetProperty("maximum").GetUInt64() });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((x, i) => new object[] { i, x.GetProperty("scene").GetInt32(), x.GetProperty("transform").GetInt32(), x.GetProperty("mode").GetInt32(), x.GetProperty("profile").GetInt32() });
    [Theory, MemberData(nameof(Engines))]
    public void Mt19937MultipleTwistsAndCanonicalDoubleMatchNative(int index, uint seed)
    {
        var engine = new Mt19937(seed); foreach (var value in Fixture.Value.GetProperty("engines")[index].GetProperty("raw").EnumerateArray()) Assert.Equal(value.GetUInt32(), engine.NextUInt32());
        var random = new Random01(seed); foreach (var value in Fixture.Value.GetProperty("engines")[index].GetProperty("real").EnumerateArray()) Assert.Equal(value.GetDouble(), random.Next());
    }
    [Theory, MemberData(nameof(Ranges))]
    public void InclusiveIntegerRangesAndRejectionsMatchNative(int index, uint seed, ulong minimum, ulong maximum)
    { var random = new RandomUInt64(seed, minimum, maximum); foreach (var value in Fixture.Value.GetProperty("ranges")[index].GetProperty("values").EnumerateArray()) Assert.Equal(value.GetUInt64(), random.Next()); }
    [Theory, MemberData(nameof(Cases))]
    public void ScatteredCoordinatesCountersDensityAndEngineOwnershipMatchNative(int index, int scene, int transform, int mode, int profile)
    {
        var item = Fixture.Value.GetProperty("cases")[index]; var grid = CreateGrid(scene, transform); var engine = new Mt19937(profile == 0 ? 42u : profile == 1 ? 0u : 5489u); var points = new List<Vec3d>(); double spread = profile == 0 ? 1 : profile == 1 ? .5 : 0;
        BasePointScatter scatter = mode switch
        {
            0 => new UniformPointScatter(points.Add, 9ul, engine, spread),
            1 => new UniformPointScatter(points.Add, scene >= 4 && scene <= 5 ? .0000000001f : .007f, engine, spread),
            2 => new DenseUniformPointScatter(points.Add, scene == 5 ? .00000001f : scene == 4 ? .000002f : scene == 3 ? .005f : 1.25f, engine, spread),
            _ => new NonUniformPointScatter(points.Add, .6f, engine, spread)
        };
        foreach (var call in item.GetProperty("calls").EnumerateArray())
        {
            bool result = scatter switch { UniformPointScatter u => u.Scatter(grid), DenseUniformPointScatter d => d.Scatter(grid), NonUniformPointScatter n => n.Scatter(grid), _ => throw new InvalidOperationException() };
            Assert.Equal(call.GetProperty("result").GetBoolean(), result); Assert.Equal(call.GetProperty("pointCount").GetUInt64(), scatter.PointCount); Assert.Equal(call.GetProperty("voxelCount").GetUInt64(), scatter.VoxelCount);
        }
        if (scatter is UniformPointScatter uniform) { Assert.Equal(item.GetProperty("target").GetUInt64(), uniform.TargetPointCount); Assert.Equal(item.GetProperty("density").GetSingle(), uniform.PointsPerVolume); }
        Assert.Equal(item.GetProperty("engineNext").GetUInt32(), engine.NextUInt32()); Assert.Equal(item.GetProperty("points").GetArrayLength(), points.Count);
        int point = 0; foreach (var p in item.GetProperty("points").EnumerateArray()) { Vec3d actual = points[point++]; for (int axis = 0; axis < 3; ++axis) { double expected = p[axis].GetDouble(); Assert.True(System.Math.Abs(expected - actual[axis]) <= 3e-13 * System.Math.Max(1, System.Math.Abs(expected)), $"Point {point - 1}, axis {axis}: expected {expected:R}, actual {actual[axis]:R}"); } }
        Assert.Equal("scatter source", grid.Name);
    }
    internal static Grid<float> CreateGrid(int scene, int tr = 0)
    {
        var g = new Grid<float>(7) { Name = "scatter source" };
        if (scene == 1) g.Tree.SetValueOn(Coord.Zero, 1);
        if (scene == 2) { g.Tree.SetValueOn(new(-9, 0, 2), .25f); g.Tree.SetValueOn(new(-1, 2, -1), 2); g.Tree.SetValueOn(Coord.Zero, 0); g.Tree.SetValueOn(new(7, 7, 7), -2); g.Tree.SetValueOn(new(8, 1, -2), 3); g.Tree.SetValueOff(new(20), 90); }
        if (scene == 3) { g.Tree.AddTile(1, new(-8), .015625f, true); g.Tree.SetValueOn(Coord.Zero, 2); g.Tree.AddTile(1, new(8), .03125f, true); }
        if (scene == 4) { g.Tree.AddTile(2, Coord.Zero, .000001f, true); g.Tree.SetValueOn(new(-1), 1); }
        if (scene == 5) g.Tree.AddTile(3, Coord.Zero, .0000000001f, true);
        if (scene == 6) { g.Tree.SetValueOff(Coord.Zero, 5); g.Tree.AddTile(1, new(-8), 3, false); }
        if (scene == 7) { g.Tree.Fill(new(new(-2, -1, 0), new(2, 1, 1)), .5f, true); g.Tree.SetValueOn(Coord.Zero, 4); }
        var t = Transform.CreateLinearTransform();
        if (tr == 1) { t = Transform.CreateLinearTransform(.75); t.PostTranslate(new(.25, -.375, .125)); t.PostRotate(.2, Axis.Z); }
        if (tr == 2) { var m = new Mat4d(1.25, .25, 0, 0, 0, .75, .125, 0, 0, 0, 1.5, 0, 0, 0, 0, 1); t = Transform.CreateLinearTransform(m); }
        if (tr == 3) t = Transform.CreateFrustumTransform(new BBoxd(new Vec3d(-10), new Vec3d(10)), .7, 20, .5);
        g.Transform = t; return g;
    }
}
