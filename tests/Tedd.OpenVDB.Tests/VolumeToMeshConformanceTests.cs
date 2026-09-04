using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class VolumeToMeshConformanceTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() =>
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-volume-mesh.json")));
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", json.RootElement.GetProperty("upstreamCommit").GetString()); return json.RootElement.Clone();
    });
    public static IEnumerable<object[]> UniformCases() => Cases(false);
    public static IEnumerable<object[]> AdaptiveCases() => Cases(true);
    private static IEnumerable<object[]> Cases(bool adaptive) => Fixture.Value.GetProperty("cases").EnumerateArray().Select((c, i) => (c, i)).Where(p => (p.c.GetProperty("adaptivity").GetDouble() != 0) == adaptive).Select(p => new object[] { p.i, p.c.GetProperty("type").GetString()!, p.c.GetProperty("shape").GetInt32(), p.c.GetProperty("config").GetInt32(), p.c.GetProperty("adaptivity").GetDouble(), p.c.GetProperty("mask").GetInt32() });
    [Theory, MemberData(nameof(UniformCases))]
    public void UniformMeshingMatchesNative(int index, string type, int shape, int config, double adaptivity, int mask) => Run(index, type, shape, config, adaptivity, mask);
    [Theory, MemberData(nameof(AdaptiveCases))]
    public void AdaptiveMeshingMatchesNative(int index, string type, int shape, int config, double adaptivity, int mask) => Run(index, type, shape, config, adaptivity, mask);
    private static void Run(int index, string type, int shape, int config, double adaptivity, int mask)
    {
        var entry = Fixture.Value.GetProperty("cases")[index]; var mesher = new VolumeToMesh(entry.GetProperty("iso").GetDouble(), adaptivity, !entry.TryGetProperty("relax", out var relax) || relax.GetInt32() != 0);
        if (mask != 0) mesher.SetSurfaceMask(Mask(mask), mask == 2);
        if (type == "float") Extract<float>(mesher, entry, shape, config); else if (type == "double") Extract<double>(mesher, entry, shape, config); else mesher.Extract(BoolShape(config));
        var points = entry.GetProperty("points"); Assert.Equal(points.GetArrayLength(), mesher.Points.Length);
        for (int i = 0; i < mesher.Points.Length; ++i) for (int axis = 0; axis < 3; ++axis) { double expected = points[i][axis].GetDouble(), actual = mesher.Points.Span[i][axis]; Assert.True(System.Math.Abs(expected - actual) <= System.Math.Max(1, System.Math.Abs(expected)) * 2e-6, $"point{i},axis{axis}: expected{expected:R},actual{actual:R}"); }
        if (entry.TryGetProperty("pointFlags", out var flags)) Assert.Equal(flags.EnumerateArray().Select(v => v.GetByte()), mesher.PointFlags.ToArray());
        var pools = entry.GetProperty("pools"); Assert.Equal(pools.GetArrayLength(), mesher.PolygonPools.Count);
        for (int n = 0; n < pools.GetArrayLength(); ++n)
        {
            var pool = mesher.PolygonPools[n]; var quads = pools[n].GetProperty("quads"); var triangles = pools[n].GetProperty("triangles"); Assert.Equal(quads.GetArrayLength(), pool.NumQuads); Assert.Equal(triangles.GetArrayLength(), pool.NumTriangles);
            for (int i = 0; i < pool.NumQuads; ++i) { for (int j = 0; j < 4; ++j) Assert.Equal(quads[i][j].GetInt32(), pool.Quads.Span[i][j]); Assert.Equal(quads[i][4].GetByte(), pool.QuadFlags.Span[i]); }
            for (int i = 0; i < pool.NumTriangles; ++i) { for (int j = 0; j < 3; ++j) Assert.Equal(triangles[i][j].GetInt32(), pool.Triangles.Span[i][j]); Assert.Equal(triangles[i][3].GetByte(), pool.TriangleFlags.Span[i]); }
        }
    }
    private static void Extract<T>(VolumeToMesh mesher, JsonElement entry, int shape, int config) where T : unmanaged, IFloatingPointIeee754<T>
    {
        int reference = entry.TryGetProperty("reference", out var r) ? r.GetInt32() : 0;
        if (reference != 0) mesher.SetReferenceGrid(Shape<T>(1), entry.GetProperty("secondary").GetDouble());
        if (entry.TryGetProperty("spatial", out var s) && s.GetInt32() != 0)
        {
            var grid = new Grid<float>(.5f) { Transform = Transform.CreateLinearTransform(.75) }; for (int x = -10; x <= 10; ++x) for (int y = -10; y <= 10; ++y) for (int z = -10; z <= 10; ++z) grid.Tree.SetValueOn(new(x, y, z), (x + 4) * .125f); mesher.SetSpatialAdaptivity(grid);
        }
        if (entry.TryGetProperty("adaptivityMask", out var a) && a.GetInt32() != 0) { var grid = new Grid<bool>(); grid.Tree.Fill(new(new(-1), new(1)), false, true); mesher.SetAdaptivityMask(grid.Tree); }
        if (reference == 2) mesher.Extract(Shape<T>(9)); mesher.Extract(Shape<T>(shape, config));
    }
    private static readonly Coord[] Corners = [new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1), new(0, 1, 0), new(1, 1, 0), new(1, 1, 1), new(0, 1, 1)];
    internal static Grid<bool> BoolShape(int config) { var grid = new Grid<bool> { GridClass = GridClass.LevelSet }; for (int n = 0; n < 8; ++n) grid.Tree.SetValueOn(Corners[n], (config & (1 << n)) != 0); return grid; }
    internal static Grid<T> Shape<T>(int shape, int config = 0) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var grid = new Grid<T>(T.CreateChecked(3)) { GridClass = GridClass.LevelSet };
        if (shape == 0) { for (int n = 0; n < 8; ++n) grid.Tree.SetValueOn(Corners[n], T.CreateChecked((config & (1 << n)) != 0 ? -1 : 1)); }
        else if (shape is 1 or 2 or 5 or >= 7) { grid = LevelSetSphere.Create<T>(T.CreateChecked(3.25), new(-.25, .5, .125), T.CreateChecked(.75), T.CreateChecked(3)); if (shape == 2) grid.GridClass = GridClass.FogVolume; if (shape == 5) grid.Transform = Transform.CreateFrustumTransform(new BBoxd(new Vec3d(-8), new Vec3d(8)), .75, 12); }
        else if (shape == 3) { grid.Tree.AddTile(1, Coord.Zero, -T.One, true); grid.Tree.SetValueOn(new(8, 0, 0), T.CreateChecked(-.5)); }
        else if (shape == 4) { for (int x = -2; x <= 8; ++x) for (int y = -2; y <= 8; ++y) for (int z = -2; z <= 8; ++z) { Coord p = new(x, y, z); T value = T.CreateChecked(((x * x + y * 3 - z * 2) % 7 - 3) * .25); if ((x + y + z) % 5 != 0) grid.Tree.SetValueOn(p, value); else grid.Tree.SetValueOff(p, value); } }
        else if (shape == 6) { grid.Tree.SetValueOff(Coord.Zero, -T.One); grid.Tree.SetValueOff(new(1, 0, 0), T.One); }
        if (shape >= 7) foreach (var leaf in grid.Tree.EnumerateLeaves()) for (int offset = 0; offset < 512; ++offset) { Coord p = leaf.OffsetToGlobalCoord(offset); double plane = shape == 7 ? p.X * .75 - .375 : shape == 8 ? p.X * .75 + p.Y * .375 - p.Z * .25 - .125 : -p.X * .75 + .375; T value = T.Min(T.Max(leaf.GetValue(offset), T.CreateChecked(plane)), grid.Background); leaf.SetValueOnly(offset, value); leaf.SetActiveState(offset, T.Abs(value) < grid.Background); }
        return grid;
    }
    internal static Grid<bool> Mask(int mode) { var mask = new Grid<bool> { Transform = Transform.CreateLinearTransform(mode == 3 ? 1.5 : .75) }; mask.Tree.Fill(new(new(0, -12, -12), new(12)), false, true); return mask; }
}
