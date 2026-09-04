using System.IO.Compression;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class ParticleAtlasConformanceTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() => { using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-particle-atlas.json.gz")); using var gzip = new GZipStream(input, CompressionMode.Decompress); using var doc = JsonDocument.Parse(gzip); Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", doc.RootElement.GetProperty("upstreamCommit").GetString()); return doc.RootElement.Clone(); });
    public static IEnumerable<object[]> Grids() => Fixture.Value.GetProperty("grids").EnumerateArray().Select((x, i) => new object[] { i, x.GetProperty("scene").GetInt32(), x.GetProperty("transform").GetInt32() });
    public static IEnumerable<object[]> Atlases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((x, i) => new object[] { i, x.GetProperty("scene").GetInt32(), x.GetProperty("profile").GetInt32(), x.GetProperty("limit").GetInt32() });
    [Theory, MemberData(nameof(Grids))]
    public void PointIndexOffsetsTopologyStableIndicesAndAllQueryOrdersMatchNative(int index, int scene, int transform)
    {
        var item = Fixture.Value.GetProperty("grids")[index]; var points = Particles(scene); var grid = PointIndexGrid.Create(points, CreateTransform(transform)); Verify(item.GetProperty("grid"), grid); Assert.Equal(item.GetProperty("valid").GetInt32() != 0, grid.IsValidPartition(points));
        var iterator = new PointIndexIterator(); foreach (var query in item.GetProperty("queries").EnumerateArray())
        {
            int kind = query.GetProperty("kind").GetInt32(), q = query.GetProperty("query").GetInt32(); Vec3d center = Center(q); double radius = Radius(q); var box = new BBoxd(center - new Vec3d(radius), center + new Vec3d(radius)); var cb = new CoordBBox(Coord.Round(box.Min), Coord.Round(box.Max));
            if (kind == 0) iterator.SearchAndUpdate(Coord.Round(center), grid); if (kind == 1) iterator.SearchAndUpdate(cb, grid); if (kind == 2) iterator.SearchAndUpdate(box, grid, points); if (kind == 3) iterator.SearchAndUpdate(center, radius, grid, points, true); if (kind == 4) iterator.SearchAndUpdate(center, radius, grid, points, false); if (kind == 5) iterator.WorldSpaceSearchAndUpdate(box, grid, points); if (kind == 6) iterator.WorldSpaceSearchAndUpdate(center, radius, grid, points, true);
            AssertIndices(query.GetProperty("indices"), iterator, $"kind {kind}, query {q}"); Assert.Equal((ulong)query.GetProperty("indices").GetArrayLength(), iterator.Count);
        }
    }
    [Theory, MemberData(nameof(Atlases))]
    public void AtlasLevelsExtremaPointPartitionsAndSpatialQueriesMatchNative(int index, int scene, int profile, int limit)
    {
        var item = Fixture.Value.GetProperty("cases")[index]; var points = Particles(scene); var atlas = ParticleAtlas.Create(points, profile == 0 ? .125 : profile == 1 ? 1 : 3, limit); var levels = item.GetProperty("levels"); Assert.Equal(levels.GetArrayLength(), atlas.Levels);
        for (int i = 0; i < atlas.Levels; ++i) { Assert.Equal(levels[i].GetProperty("minimum").GetDouble(), atlas.MinimumRadius(i)); Assert.Equal(levels[i].GetProperty("maximum").GetDouble(), atlas.MaximumRadius(i)); Verify(levels[i].GetProperty("grid"), atlas.PointIndexGrid(i)); }
        var iterator = atlas.CreateIterator(); for (int i = 0; i < item.GetProperty("levelIndices").GetArrayLength(); ++i) { iterator.UpdateFromLevel(i); AssertIndices(item.GetProperty("levelIndices")[i], iterator, $"level {i}"); }
        foreach (var query in item.GetProperty("queries").EnumerateArray()) { int kind = query.GetProperty("kind").GetInt32(), q = query.GetProperty("query").GetInt32(); Vec3d center = Center(q); double radius = Radius(q); if (kind == 0) iterator.WorldSpaceSearchAndUpdate(center, radius, points); else iterator.WorldSpaceSearchAndUpdate(new BBoxd(center - new Vec3d(radius), center + new Vec3d(radius)), points); AssertIndices(query.GetProperty("indices"), iterator, $"kind {kind}, query {q}"); }
    }
    private static void AssertIndices(JsonElement expected, IEnumerable<uint> actual, string label)
    { uint[] a = actual.ToArray(), b = expected.EnumerateArray().Select(x => x.GetUInt32()).ToArray(); Assert.True(b.SequenceEqual(a), $"{label}: expected [{string.Join(',', b)}], actual [{string.Join(',', a)}]"); }
    private static void Verify(JsonElement expected, PointIndexGrid grid)
    {
        Assert.Equal(expected.GetProperty("active").GetUInt64(), grid.ActiveVoxelCount); Assert.Equal(expected.GetProperty("roots").GetInt32(), grid.Tree.RootEntryCount); Assert.Equal(expected.GetProperty("nodes").EnumerateArray().Select(x => x.GetInt64()), grid.Tree.NodeCount());
        Vec3d size = grid.Transform.VoxelSize, zero = grid.Transform.IndexToWorld(Vec3d.Zero); for (int a = 0; a < 3; ++a) { double v = expected.GetProperty("voxelSize")[a].GetDouble(); Assert.True(v == size[a] || System.Math.Abs(v - size[a]) < 3e-13 * System.Math.Max(1, System.Math.Abs(v))); Assert.Equal(expected.GetProperty("zero")[a].GetDouble(), zero[a]); }
        var leaves = expected.GetProperty("leaves"); Assert.Equal(leaves.GetArrayLength(), grid.Tree.LeafCount); for (int i = 0; i < leaves.GetArrayLength(); ++i) { var item = leaves[i]; var leaf = grid.Tree.Leaves[i]; Assert.Equal(Coordinate(item.GetProperty("origin")), leaf.Origin); Assert.Equal(item.GetProperty("indices").EnumerateArray().Select(x => x.GetUInt32()), leaf.Indices.ToArray()); int offset = 0; foreach (var run in item.GetProperty("runs").EnumerateArray()) for (int k = 0; k < run[0].GetInt32(); ++k) { Assert.Equal(run[1].GetUInt32(), leaf.GetValue(offset)); Assert.Equal(run[2].GetInt32() != 0, leaf.IsValueOn(offset)); ++offset; } Assert.Equal(512, offset); }
    }
    internal static ParticleArray Particles(int scene)
    {
        List<Particle> p = []; if (scene == 0) return new(p.ToArray()); for (int i = 0; i < 80; ++i) { Vec3d position = new(((i * 47) % 53 - 26) * .75 + .5, ((i * 29) % 41 - 20) * .5 - .5, ((i * 19) % 31 - 15) * .625 + .25); double r = scene == 1 ? .75 : scene == 2 ? (.25 + (i % 7) * .125) : scene == 3 ? (1 << (i % 5)) : scene == 4 ? (i % 4 == 0 ? 0 : (i % 8) * .25) : (.5 + (i % 3) * 8); p.Add(new(position, r)); }
        foreach (Vec3d pos in new Vec3d[] { new(-.5), new(.5), new(7.5), new(-8.5), Vec3d.Zero, Vec3d.Zero, new(256.5), new(-257) }) p.Add(new(pos, scene == 1 ? .75 : scene == 2 ? .5 : 2)); return new(p.ToArray());
    }
    private static Transform CreateTransform(int profile) { var t = Transform.CreateLinearTransform(profile == 0 ? 1 : .75); if (profile == 2) { t.PostTranslate(new(.25, -.375, .125)); t.PostRotate(.2, Axis.Z); } if (profile == 3) t.PostScale(new Vec3d(1, 2, 3)); if (profile == 4) t = Transform.CreateFrustumTransform(new BBoxd(new Vec3d(-20), new Vec3d(20)), .7, 20, .5); return t; }
    private static Coord Coordinate(JsonElement x) => new(x[0].GetInt32(), x[1].GetInt32(), x[2].GetInt32());
    internal static Vec3d Center(int q) => q < 3 ? Vec3d.Zero : q == 3 ? new Vec3d(.5) : q == 4 ? new Vec3d(-7.5, 1, 2) : new Vec3d(15, -6, 5);
    internal static double Radius(int q) => new double[] { 0, .5, 1, 3, 7, 15 }[q];
}
