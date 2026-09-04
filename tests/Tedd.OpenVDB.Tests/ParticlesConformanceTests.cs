using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class ParticlesConformanceTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() =>
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-particles.json.gz")); using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress); using var doc = JsonDocument.Parse(gzip);
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", doc.RootElement.GetProperty("upstreamCommit").GetString()); return doc.RootElement.Clone();
    });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((x, i) => new object[] { i, x.GetProperty("type").GetString()!, x.GetProperty("scene").GetInt32(), x.GetProperty("mode").GetInt32(), x.GetProperty("profile").GetInt32(), x.GetProperty("attributes").GetInt32() != 0 });
    public static IEnumerable<object[]> Partitions() => Fixture.Value.GetProperty("partitions").EnumerateArray().Select((x, i) => new object[] { i, x.GetProperty("log").GetInt32(), x.GetProperty("transform").GetInt32(), x.GetProperty("voxel").GetInt32() != 0, x.GetProperty("record").GetInt32() != 0, x.GetProperty("cell").GetInt32() != 0 });
    [Theory, MemberData(nameof(Cases))]
    public void ParticleGeometryTopologyCountersAndAttributesMatchNative(int index, string type, int scene, int mode, int profile, bool attributes)
    {
        JsonElement item = Fixture.Value.GetProperty("cases")[index]; if (type == "float") Compare<float>(item, scene, mode, profile, attributes); else if (type == "double") Compare<double>(item, scene, mode, profile, attributes); else Compare<bool>(item, scene, mode, profile, attributes);
    }
    private static void Compare<T>(JsonElement item, int scene, int mode, int profile, bool attributes) where T : unmanaged
    {
        Grid<T> grid = Grid<T>(profile); var particles = Particles(scene);
        if (attributes)
        {
            var raster = new ParticlesToLevelSet<T, int>(grid) { GrainSize = 0 }; if (profile == 6) { raster.MinimumRadius = .25; raster.MaximumRadius = 4; }
            void Apply(ParticleArray<int> p) { if (mode == 0) raster.RasterizeSpheres(p); else if (mode == 1) raster.RasterizeSpheres(p, 2.25); else raster.RasterizeTrails(p, .7); }
            Apply(particles); if (profile == 5) Apply(Particles(4)); Assert.Null(raster.AttributeGrid); raster.Finalize(profile != 0 && profile != 2);
            Assert.Equal(item.GetProperty("minimumCount").GetUInt64(), raster.MinimumRadiusCount); Assert.Equal(item.GetProperty("maximumCount").GetUInt64(), raster.MaximumRadiusCount); Assert.Equal(item.GetProperty("voxelSize").GetDouble(), raster.VoxelSize); Assert.Equal(item.GetProperty("halfWidth").GetDouble(), raster.HalfWidth);
            Assert.NotNull(raster.AttributeGrid); Verify(item.GetProperty("attributeGrid"), raster.AttributeGrid);
        }
        else
        {
            var raster = new ParticlesToLevelSet<T>(grid) { GrainSize = 0 }; if (mode == 0) raster.RasterizeSpheres(particles); else if (mode == 1) raster.RasterizeSpheres(particles, 2.25); else raster.RasterizeTrails(particles, .7); raster.Finalize(profile != 0);
            Assert.Equal(item.GetProperty("minimumCount").GetUInt64(), raster.MinimumRadiusCount); Assert.Equal(item.GetProperty("maximumCount").GetUInt64(), raster.MaximumRadiusCount); Assert.Equal(item.GetProperty("voxelSize").GetDouble(), raster.VoxelSize); Assert.Equal(item.GetProperty("halfWidth").GetDouble(), raster.HalfWidth);
        }
        Verify(item.GetProperty("grid"), grid);
    }
    [Theory, MemberData(nameof(Partitions))]
    public void StableBinsBucketsLocalOffsetsAndCellConventionsMatchNative(int index, int log, int transform, bool voxel, bool record, bool cell)
    {
        JsonElement item = Fixture.Value.GetProperty("partitions")[index]; var partition = PointPartitioner.Create(new PointArray(PartitionPoints()), Transform(transform), voxel, record, cell, log);
        Assert.Equal(cell, partition.UsingCellCenteredTransform); var buckets = item.GetProperty("buckets"); Assert.Equal(buckets.GetArrayLength(), partition.Count);
        for (int i = 0; i < partition.Count; ++i) { Assert.Equal(Coordinate(buckets[i].GetProperty("origin")), partition.Origin(i)); Assert.Equal(buckets[i].GetProperty("indices").EnumerateArray().Select(x => x.GetInt32()), partition.Indices(i).ToArray()); }
        Assert.Equal(item.GetProperty("offsets").EnumerateArray().Select(x => x.GetInt32()), partition.VoxelOffsets.ToArray());
    }
    internal static void Verify<T>(JsonElement expected, Grid<T> grid) where T : unmanaged
    {
        Assert.Equal((GridClass)expected.GetProperty("class").GetInt32(), grid.GridClass); Assert.Equal(expected.GetProperty("name").GetString(), grid.Name); Equal(expected.GetProperty("background"), grid.Background, "background");
        Assert.Equal(expected.GetProperty("active").GetUInt64(), grid.ActiveVoxelCount); Assert.Equal(expected.GetProperty("tiles").GetUInt64(), grid.Tree.ActiveTileCount); Assert.Equal(expected.GetProperty("roots").GetInt32(), grid.Tree.RootEntryCount);
        for (int a = 0; a < 3; ++a) { Assert.InRange(System.Math.Abs(expected.GetProperty("voxelSize")[a].GetDouble() - grid.Transform.VoxelSize[a]), 0, 1e-12); Assert.InRange(System.Math.Abs(expected.GetProperty("worldZero")[a].GetDouble() - grid.Transform.IndexToWorld(Vec3d.Zero)[a]), 0, 1e-12); }
        var leaves = expected.GetProperty("leaves"); Assert.Equal(leaves.GetArrayLength(), grid.Tree.LeafCount);
        foreach (var item in leaves.EnumerateArray())
        {
            Coord origin = Coordinate(item.GetProperty("origin")); var leaf = grid.Tree.ProbeLeaf(origin); Assert.NotNull(leaf); int offset = 0;
            foreach (var run in item.GetProperty("runs").EnumerateArray()) for (int count = run[0].GetInt32(); count > 0; --count, ++offset) { Assert.Equal(run[2].GetInt32() != 0, leaf.IsValueOn(offset)); Equal(run[1], leaf.GetValue(offset), $"{origin}, offset {offset}"); }
            Assert.Equal(512, offset);
        }
        foreach (var probe in expected.GetProperty("probes").EnumerateArray()) { Coord c = new(probe[0].GetInt32(), probe[1].GetInt32(), probe[2].GetInt32()); Equal(probe[3], grid.Tree.GetValue(c), c.ToString()); Assert.Equal(probe[4].GetInt32() != 0, grid.Tree.IsValueOn(c)); Assert.Equal(probe[5].GetInt32(), grid.Tree.GetValueDepth(c)); }
    }
    private static void Equal<T>(JsonElement value, T actual, string location) where T : unmanaged
    {
        double expected = value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble(), observed = Convert.ToDouble(actual, CultureInfo.InvariantCulture), tolerance = typeof(T) == typeof(float) ? 3e-6 : typeof(T) == typeof(double) ? 3e-12 : 0;
        Assert.True(expected == observed || double.IsNaN(expected) && double.IsNaN(observed) || System.Math.Abs(expected - observed) <= tolerance * System.Math.Max(1, System.Math.Abs(expected)), $"{location}: expected {expected:R}, got {observed:R}");
    }
    private static Coord Coordinate(JsonElement value) => new(value[0].GetInt32(), value[1].GetInt32(), value[2].GetInt32());
    internal static Grid<T> Grid<T>(int profile) where T : unmanaged
    {
        T background = typeof(T) == typeof(bool) ? (T)(object)false : typeof(T) == typeof(float) ? (T)(object)2.25f : (T)(object)2.25;
        Grid<T> grid = new(background) { Transform = ToolsTransform(), Name = "particles" };
        if (profile == 4)
        {
            if (typeof(T) == typeof(bool)) { grid.Tree.Fill(new(new(40), new(42)), default, true); grid.Tree.SetValueOff(new(-32), (T)(object)true); }
            else
            {
                grid = typeof(T) == typeof(float) ? (Grid<T>)(object)LevelSetSphere.Create<float>(2, new(-1, 0, 0), .75f, 3) : (Grid<T>)(object)LevelSetSphere.Create<double>(2, new(-1, 0, 0), .75, 3);
                grid.Transform.PostTranslate(new(.25, -.375, .125)); grid.Name = "particles";
            }
        }
        return grid;
    }
    internal static ParticleArray<int> Particles(int scene)
    {
        (Vec3d P, Vec3d V, double R, int A)[] values = [(new(.25, .5, -.25), new(3, 1, -2), 3, 11)];
        if (scene == 1) values = [(new(0), new(0), 3, 17), (new(0), new(0), 3, 29), (new(2, 0, 0), new(2, 0, 0), 2, 41)];
        if (scene == 2) values = [(new(0), new(1, 0, 0), .5, 11), (new(0), new(1, 0, 0), 101, 12), (new(0), new(1, 0, 0), -1, 13), (new(-2, 1, .5), new(-1, 2, 0), 2, 14)];
        if (scene == 3) values = [(new(0), new(1, 0, 0), 20, 71)];
        if (scene == 4) values = [(new(-7.5, 0, 0), new(2, 1, 0), 2.75, 1), (new(-8.5, 1, 0), new(1, 0, 0), 2.5, 2), (new(7.5, 0, 0), new(1, 0, 0), 3, 3), (new(8.5, 0, 0), new(-2, 1, 0), 3.25, 4)];
        if (scene == 5) values = [(new(257, 0, 0), new(0), 2, 91), (new(0, 257, 0), new(0), 2, 92), (new(-257, 0, 0), new(0), 2, 93), (new(-1, -257, 0), new(0), 2, 94)];
        if (scene == 6) values = [(new(0), new(0), 3, 61)];
        if (scene == 7) values = [values[0], (new(double.NaN, 0, 0), new(0), 2, 81), (new(0, double.PositiveInfinity, 0), new(0), 2, 82)];
        return new(values.Select(x => new Particle(x.P, x.R, x.V)).ToArray(), values.Select(x => x.A).ToArray());
    }
    internal static Vec3d[] PartitionPoints()
    {
        List<Vec3d> points = []; for (int i = 0; i < 96; ++i) points.Add(new(((i * 47) % 103 - 51) * 8.0 + .5, ((i * 29) % 61 - 30) * 9.0 - .5, ((i * 19) % 43 - 21) * 3.0 + .25));
        points.AddRange([new(-.5), new(.5), new(7.5), new(-8.5), new(255.5), new(256.5), new(0), new(0), new(double.NaN, 0, 0), new(0, double.PositiveInfinity, 0)]); return points.ToArray();
    }
    private static Transform ToolsTransform() { var t = global::Tedd.OpenVDB.Math.Transform.CreateLinearTransform(.75); t.PostTranslate(new(.25, -.375, .125)); return t; }
    private static Transform Transform(int mode)
    {
        var t = global::Tedd.OpenVDB.Math.Transform.CreateLinearTransform(.75); if (mode == 1) { t.PostTranslate(new(.25, -.375, .125)); t.PostRotate(.2, Axis.Z); }
        if (mode == 2) t = global::Tedd.OpenVDB.Math.Transform.CreateFrustumTransform(new(new Vec3d(-4), new Vec3d(4)), .75, 8, 1); return t;
    }
}
