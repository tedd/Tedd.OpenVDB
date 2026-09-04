// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class NativeSamplingTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-sampling.json"))));
    public static IEnumerable<object[]> Cases()
    {
        foreach (string type in new[] { "sbyte", "byte", "short", "ushort", "int", "float", "double", "half", "bool", "vec2d", "vec3d", "vec3f", "vec3i", "vec4d" })
            for (int order = 0; order < 3; order++) { yield return [type, order, false]; if (type is "vec3d" or "vec3f" or "vec3i") yield return [type, order, true]; }
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeInterpolationAndCoordinateSpacesMatch(string type, int order, bool staggered)
    {
        var root = Fixture.Value.RootElement; Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", root.GetProperty("commit").GetString());
        var entry = root.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("type").GetString() == type && c.GetProperty("order").GetInt32() == order && c.GetProperty("staggered").GetInt32() == (staggered ? 1 : 0));
        switch (type)
        {
            case "sbyte": Run(entry, (a, b, c, d) => (sbyte)a, x => [x], 0); break;
            case "byte": Run(entry, (a, b, c, d) => (byte)a, x => [x], 0); break;
            case "short": Run(entry, (a, b, c, d) => (short)a, x => [x], 0); break;
            case "ushort": Run(entry, (a, b, c, d) => (ushort)a, x => [x], 0); break;
            case "int": Run(entry, (a, b, c, d) => (int)a, x => [x], 0); break;
            case "float": Run(entry, (a, b, c, d) => (float)a, x => [x], 2e-6); break;
            case "double": Run(entry, (a, b, c, d) => a, x => [x], 2e-13); break;
            case "half": Run(entry, (a, b, c, d) => (Half)(float)a, x => [(double)x], 0); break;
            case "bool": Run(entry, (a, b, c, d) => a != 0, x => [x ? 1 : 0], 0); break;
            case "vec2d": Run(entry, (a, b, c, d) => new Vec2d(a, b), x => [x.X, x.Y], 2e-13); break;
            case "vec3d": Run(entry, (a, b, c, d) => new Vec3d(a, b, c), x => [x.X, x.Y, x.Z], 2e-13); break;
            case "vec3f": Run(entry, (a, b, c, d) => new Vec3f((float)a, (float)b, (float)c), x => [x.X, x.Y, x.Z], 2e-6); break;
            case "vec3i": Run(entry, (a, b, c, d) => new Vec3i((int)a, (int)b, (int)c), x => [x.X, x.Y, x.Z], 0); break;
            case "vec4d": Run(entry, (a, b, c, d) => new Vec4d(a, b, c, d), x => [x.X, x.Y, x.Z, x.W], 2e-13); break;
        }
    }
    private static void Run<T>(JsonElement entry, Func<double, double, double, double, T> make, Func<T, double[]> components, double tolerance) where T : unmanaged
    {
        var tree = new VdbTree<T>(make(0.125, 0.25, -0.5, 0.75));
        for (int x = -3; x <= 4; x++) for (int y = -3; y <= 4; y++) for (int z = -3; z <= 4; z++)
        {
            double a = ((x * 17 + y * 11 - z * 13) % 31) * 0.071;
            if (typeof(T) == typeof(sbyte)) a *= 35; else if (typeof(T) == typeof(byte)) a = 128 + a * 50; else if (typeof(T) == typeof(short)) a = 30000 + a * 500; else if (typeof(T) == typeof(ushort)) a = 60000 + a * 1500;
            T value = make(a, a * 1.337 + 0.25, -a * 0.793 - 0.5, a * 3.125 + 0.75);
            if ((x + y - z) % 3 == 0) tree.SetValueOn(new(x, y, z), value); else tree.SetValueOff(new(x, y, z), value);
        }
        var transform = new Transform(new Mat4d(1.25, 0.125, 0, 0, 0, 0.75, 0.25, 0, 0, 0, 1.5, 0, 2, -1, 0.5, 1));
        var target = transform.Copy(); target.PostTranslate(new(0.375, -0.25, 0.125));
        var order = (SamplingOrder)entry.GetProperty("order").GetInt32(); bool staggered = entry.GetProperty("staggered").GetInt32() != 0;
        var sampler = new GridSampler<T>(tree, transform, order, staggered); var cached = new GridSampler<T>(tree.GetAccessor(), transform, order, staggered);
        var dual = new DualGridSampler<T>(tree, transform, target, order, staggered); var aligned = new DualGridSampler<T>(tree.GetAccessor(), transform, transform.Copy(), order, staggered);
        Assert.False(dual.IsAligned); Assert.True(aligned.IsAligned);
        foreach (var sample in entry.GetProperty("samples").EnumerateArray())
        {
            var p = sample.GetProperty("position"); var position = new Vec3d(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble());
            Assert.Equal(sample.GetProperty("active").GetInt32() != 0, sampler.IndexSample(position, out T value)); Compare("value", value);
            Assert.Equal(sample.GetProperty("active").GetInt32() != 0, cached.IndexSample(position, out value)); Compare("value", value);
            Compare("world", sampler.WorldSample(transform.IndexToWorld(position)));
            var c = sample.GetProperty("coord"); var coordinate = new Coord(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt32());
            Compare("dual", dual.Sample(coordinate)); Compare("aligned", aligned.Sample(coordinate)); Compare("aligned", sampler.IndexSample(coordinate));
            void Compare(string field, T actual)
            {
                var expected = sample.GetProperty(field); var values = components(actual); Assert.Equal(expected.GetArrayLength(), values.Length);
                for (int i = 0; i < values.Length; i++)
                {
                    double wanted = expected[i].GetDouble(), epsilon = tolerance * System.Math.Max(1, System.Math.Abs(wanted));
                    Assert.True(System.Math.Abs(values[i] - wanted) <= epsilon, $"{typeof(T).Name}/{order}/{staggered} {field} at {position}, component {i}: native={wanted:R}, managed={values[i]:R}");
                }
            }
        }
    }
}

public class GridSamplerTests
{
    [Fact]
    public void IntegerAndStaggeredFractionalCoordinatesRemainDistinct()
    {
        var tree = new VdbTree<Vec3f>(); tree.SetValueOn(new(1, 0, 0), new(11, 12, 13)); tree.SetValueOn(new(0, 1, 0), new(21, 22, 23)); tree.SetValueOn(new(0, 0, 1), new(31, 32, 33));
        var sampler = new GridSampler<Vec3f>(tree, new Transform(), SamplingOrder.Point, true);
        Assert.Equal(default, sampler.IndexSample(Coord.Zero)); Assert.Equal(new Vec3f(11, 22, 33), sampler.IndexSample(Vec3d.Zero));
        Assert.Equal(default, sampler.SampleVoxel(0, 0, 0)); Assert.Equal(new Vec3f(11, 22, 33), sampler.SampleVoxel(0.0, 0.0, 0.0));
        Assert.Equal(sampler.IndexSample(Vec3d.Zero), StaggeredPointSampler.Sample(tree, Vec3d.Zero));
    }
    [Fact]
    public void SamplersCaptureOriginalReferencesAndDualAlignmentOnlyOnce()
    {
        var grid = new Grid<int>(); grid[Coord.Zero] = 7; var sampler = new GridSampler<int>(grid, SamplingOrder.Point);
        var target = grid.Transform.Copy(); var dual = new DualGridSampler<int>(grid, target, SamplingOrder.Point); Assert.True(dual.IsAligned);
        target.PostTranslate(new(100, 0, 0)); Assert.Equal(7, dual.Sample(Coord.Zero));
        grid.SetTree(new VdbTree<int>(19)); grid.Transform = Transform.CreateLinearTransform(4); Assert.Equal(7, sampler.IndexSample(Coord.Zero));
        sampler.Transform.PostTranslate(new(1, 0, 0)); Assert.Equal(7, sampler.WorldSample(new Vec3d(1, 0, 0))); Assert.Equal(19, grid[Coord.Zero]);
    }
    [Fact]
    public void TreeSamplesAreReadConcurrentAndAccessorSamplesAllocateNoMemoryAfterWarmup()
    {
        var tree = new VdbTree<Vec3d>(); tree.Fill(new(0, 0, 0, 7, 7, 7), new(1, 2, 3)); var sampler = new GridSampler<Vec3d>(tree, new Transform());
        Parallel.For(0, 2000, _ => Assert.Equal(new Vec3d(1, 2, 3), sampler.IndexSample(new Vec3d(2.25, 3.5, 4.75))));
        var cached = new GridSampler<Vec3d>(tree.GetAccessor(), new Transform(), SamplingOrder.Quadratic, true);
        for (int i = 0; i < 1000; i++) cached.IndexSample(new Vec3d(2.25, 3.5, 4.25));
        GC.Collect(2, GCCollectionMode.Forced, true, true); long before = GC.GetAllocatedBytesForCurrentThread(); Vec3d result = default;
        for (int i = 0; i < 1000; i++) result = cached.IndexSample(new Vec3d(2.25, 3.5, 4.25));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.Equal(0, allocated); Assert.Equal(new Vec3d(1, 2, 3), result);
    }
    [Fact]
    public void VectorPublicOverloadsAndLexicographicExtremaMatchNativeContract()
    {
        var tree = new VdbTree<Vec3i>(); tree.SetValueOn(new(0, 0, 0), new(1, 2, 3)); var p = new Vec3d(0.25, 0.375, 0.625);
        Assert.Equal(new GridSampler<Vec3i>(tree, new Transform()).IndexSample(p), BoxSampler.Sample(tree, p));
        Assert.Equal(new GridSampler<Vec3i>(tree, new Transform(), SamplingOrder.Quadratic).IndexSample(p), QuadraticSampler.Sample(tree.GetAccessor(), p));
        Assert.Equal(new GridSampler<Vec3i>(tree, new Transform(), SamplingOrder.Box, true).IndexSample(p), StaggeredBoxSampler.Sample(tree, p));
        Assert.Equal(new GridSampler<Vec3i>(tree, new Transform(), SamplingOrder.Quadratic, true).IndexSample(p), StaggeredQuadraticSampler.Sample(tree.GetAccessor(), p));
        Vec3i[] values = [new(1, 100, 0), new(1, -100, 0), new(0, 999, 999), new(2, -999, -999), new(1), new(1), new(1), new(1)];
        BoxSampler.Extrema(values, out var min, out var max); Assert.Equal(new Vec3i(0, 999, 999), min); Assert.Equal(new Vec3i(2, -999, -999), max);
    }
    [Fact]
    public void SamplingRejectsInvalidSupportAndTypesBeforeReading()
    {
        var tree = new VdbTree<float>(); var sampler = new GridSampler<float>(tree, new Transform());
        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.IndexSample(new Vec3d(double.NaN, 0, 0))); Assert.Throws<ArgumentOutOfRangeException>(() => sampler.IndexSample(new Vec3d(int.MaxValue, 0, 0)));
        Assert.Throws<NotSupportedException>(() => new GridSampler<Coord>(new VdbTree<Coord>(), new Transform())); Assert.Throws<NotSupportedException>(() => new GridSampler<float>(tree, new Transform(), staggered: true));
        var opaque = new GridSampler<Coord>(new VdbTree<Coord>(new(3)), new Transform(), SamplingOrder.Point); Assert.Equal(new Coord(3), opaque.IndexSample(Vec3d.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridSampler<float>(tree, new Transform(), (SamplingOrder)9));
    }
    [Fact]
    public void NarrowIntegerArithmeticPromotesBeforeInterpolation()
    {
        var tree = new VdbTree<short>(30000);
        Assert.Equal((short)30000, QuadraticSampler.Sample(tree, new Vec3d(0.5, 0.25, 0.75)));
        Assert.Equal((short)30000, new GridSampler<short>(tree, new Transform(), SamplingOrder.Quadratic).IndexSample(new Vec3d(0.5)));
        sbyte[] values = [-100, 100, -100, 100, -100, 100, -100, 100];
        Assert.Equal((sbyte)0, BoxSampler.TrilinearInterpolation<sbyte>(values, new Vec3d(0, 0, 0.5)));
        ushort[] high = Enumerable.Repeat((ushort)60000, 27).ToArray();
        Assert.Equal((ushort)60000, QuadraticSampler.TriquadraticInterpolation<ushort>(high, new Vec3d(0.625)));
    }
    [Fact]
    public void AffineAndFrustumAlignmentCompareCopiesAndRejectDifferentMaps()
    {
        var affine = new Transform(); Assert.True(affine.IsEquivalentTo(affine.Copy())); Assert.False(affine.IsEquivalentTo(null));
        var a = Transform.CreateFrustumTransform(new BBoxd(new Vec3d(-4, -4, 0), new Vec3d(4, 4, 10)), 0.5, 3);
        Assert.True(a.IsEquivalentTo(a.Copy())); Assert.False(a.IsEquivalentTo(affine)); var b = a.Copy(); b.PostTranslate(new(1, 0, 0)); Assert.False(a.IsEquivalentTo(b));
        b = new Transform(a.Matrix); Assert.False(a.IsEquivalentTo(b));
    }
}
