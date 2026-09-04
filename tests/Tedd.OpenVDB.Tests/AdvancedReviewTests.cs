using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Tools;

namespace Tedd.OpenVDB.Tests;

public sealed class AdvancedReviewTests
{
    private static readonly Lazy<Dictionary<string, JsonElement>> Fixtures = new(() =>
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-background-review.json")));
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", document.RootElement.GetProperty("upstreamCommit").GetString());
        return document.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(static entry => entry.GetProperty("name").GetString()!, static entry => entry.Clone());
    });
    public static IEnumerable<object[]> NativeCases() => Fixtures.Value.Keys.Select(static name => new object[] { name });

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void BackgroundEqualityAndRemappingMatchNativeVectorAndScalarRules(string name)
    {
        var fixture = Fixtures.Value[name];
        if (name.StartsWith("Vec2d")) Check(fixture, static x => new Vec2d(Number(x[0]), Number(x[1])));
        else if (name.StartsWith("Vec3d")) Check(fixture, static x => new Vec3d(Number(x[0]), Number(x[1]), Number(x[2])));
        else if (name.StartsWith("Vec3f")) Check(fixture, static x => new Vec3f((float)Number(x[0]), (float)Number(x[1]), (float)Number(x[2])));
        else if (name.StartsWith("Vec4d")) Check(fixture, static x => new Vec4d(Number(x[0]), Number(x[1]), Number(x[2]), Number(x[3])));
        else if (name.StartsWith("float")) Check(fixture, static x => (float)Number(x));
        else if (name.StartsWith("double")) Check(fixture, Number);
        else if (name.StartsWith("Half")) Check(fixture, static x => (Half)Number(x));
        else throw new InvalidOperationException(name);
    }

    private static void AssertBitsEqual<T>(T expected, T actual) where T : unmanaged
    {
        Assert.True(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref expected, 1))
            .SequenceEqual(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref actual, 1))));
    }

    private static double Number(JsonElement x) => x.ValueKind == JsonValueKind.String
        ? double.Parse(x.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : x.GetDouble();

    private static void Check<T>(JsonElement fixture, Func<JsonElement, T> read) where T : unmanaged
    {
        var old = read(fixture.GetProperty("old"));
        var replacement = read(fixture.GetProperty("replacement"));
        var value = read(fixture.GetProperty("sample"));
        var expected = read(fixture.GetProperty("afterReset"));
        var empty = fixture.GetProperty("rootEmpty").GetBoolean();
        var leaf = new LeafNode<T>(new Coord(0), value, false);
        leaf.SetValueOn(1, value); leaf.ResetBackground(old, replacement);
        Assert.Equal(expected, leaf.GetValue(0)); AssertBitsEqual(value, leaf.GetValue(1));
        Assert.False(leaf.IsValueOn(0)); Assert.True(leaf.IsValueOn(1));
        var tree = new VdbTree<T>(old); tree.AddTile(3, new Coord(0), value, false);
        Assert.Equal(empty, tree.IsEmpty);
        Assert.Equal(empty ? 0UL : 1UL << 36, tree.InactiveVoxelCount);
        tree.SetBackground(replacement, true);
        Assert.Equal(expected, tree.GetValue(new Coord(0))); Assert.False(tree.IsValueOn(new Coord(0)));
        Assert.Equal(replacement, tree.Background);
    }

    [Theory]
    [InlineData(MergePolicy.ActiveStates)]
    [InlineData(MergePolicy.Nodes)]
    [InlineData(MergePolicy.ActiveStatesAndNodes)]
    public void EqualBackgroundLeafTransferPreservesInactiveNaNAcrossAllPolicies(MergePolicy policy)
    {
        CheckNaNTransfer<float>(policy); CheckNaNTransfer<double>(policy); CheckNaNTransfer<Half>(policy);
    }

    private static void CheckNaNTransfer<T>(MergePolicy policy) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var a = new VdbTree<T>(T.CreateChecked(3)); var b = new VdbTree<T>(T.CreateChecked(3));
        a.SetValueOn(new Coord(0, 0, 0), T.One);
        var c = new Coord(8, 0, 0); b.SetValueOff(c, T.NaN); b.SetValueOff(new Coord(9, 0, 0), -T.CreateChecked(3));
        var moved = b.ProbeLeaf(c);
        var accessorA = a.GetAccessor(); var accessorB = b.GetAccessor();
        accessorA.GetValue(c); accessorB.GetValue(c);
        a.Merge(b, policy);
        Assert.Same(moved, a.ProbeLeaf(c)); Assert.True(T.IsNaN(a.GetValue(c)));
        Assert.False(a.IsValueOn(c)); Assert.True(T.IsNaN(accessorA.GetValue(c)));
        Assert.Equal(-T.CreateChecked(3), a.GetValue(new Coord(9, 0, 0)));
        Assert.Equal(T.CreateChecked(3), accessorB.GetValue(c)); Assert.True(b.IsEmpty);
    }

    [Fact]
    public void BackgroundOnlyAbsentRootWriteInvalidatesHandlesTraversalAndLeafManager()
    {
        var tree = new VdbTree<int>(7); tree.SetValueOn(new Coord(0), 1);
        var manager = new LeafManager<int>(tree, 1);
        using var iterator = tree.EnumerateValueHandles().GetEnumerator(); Assert.True(iterator.MoveNext());
        var handle = iterator.Current;
        tree.SetValueOnly(new Coord(4096, 0, 0), 7);
        Assert.Equal(1, tree.GetValueDepth(new Coord(4096, 0, 0))); Assert.Equal(1, tree.LeafCount);
        Assert.False(handle.IsValid); Assert.False(manager.IsValid);
        Assert.Throws<InvalidOperationException>(() => handle.SetValueOnly(2));
        Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
        Assert.Throws<InvalidOperationException>(() => manager.SwapBuffer(1));
        Assert.Equal(1, tree.GetValue(new Coord(0)));
        manager.Rebuild(); Assert.True(manager.IsValid);
    }

    [Fact]
    public void ValueHandleCallbackCannotWriteBackThroughInvalidatedTopology()
    {
        var tree = new VdbTree<int>(); tree.SetValueOn(new Coord(0), 1);
        var handle = tree.EnumerateValueHandles().Single();
        Assert.Throws<InvalidOperationException>(() => handle.ModifyValue(value => { tree.Clear(); return value + 1; }));
        Assert.True(tree.IsEmpty); Assert.False(handle.IsValid);
    }

    private static readonly Lazy<Dictionary<string, JsonElement>> HalfFixtures = new(() =>
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-half-filter-review.json")));
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", document.RootElement.GetProperty("upstreamCommit").GetString());
        return document.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(static entry => entry.GetProperty("name").GetString()!, static entry => entry.Clone());
    });
    public static IEnumerable<object[]> HalfFilterCases() => HalfFixtures.Value.Keys.Select(static name => new object[] { name });

    [Theory]
    [MemberData(nameof(HalfFilterCases))]
    public void HalfFiltersMatchNativeFloatCoefficientPromotion(string name)
    {
        var fixture = HalfFixtures.Value[name];
        var grid = new Grid<Half>((Half)0.125f); var tree = grid.Tree;
        tree.SetValueOn(new(0, 0, 0), (Half)5); tree.SetValueOn(new(1, 0, 0), (Half)(-1.337f));
        tree.SetValueOn(new(0, 1, 0), (Half)19.03f); tree.SetValueOn(new(0, 0, 1), (Half)0.12597656f);
        tree.SetValueOn(new(7, 0, 0), (Half)3.14159f); tree.SetValueOn(new(-1, 0, 0), (Half)7.777f);
        tree.SetValueOff(new(2, 0, 0), (Half)6.28125f); tree.AddTile(1, new(16, 0, 0), (Half)2.71875f, true);
        var mask = new Grid<float>(0.3712345f); mask.Tree.SetValueOff(new(1, 0, 0), 0.8234567f);
        var operation = fixture.GetProperty("operation").GetInt32(); var maskType = fixture.GetProperty("mask").GetInt32();
        var filter = new Filter<Half>(grid) { Parallel = false, ProcessTiles = operation == 3, IsMaskInverted = maskType == 2 };
        var maskArg = maskType == 0 ? null : mask;
        switch (operation)
        {
            case 0: filter.Mean(1, 2, maskArg); break;
            case 1: filter.Gaussian(1, 1, maskArg); break;
            case 2: filter.Median(1, 2, maskArg); break;
            case 3: filter.Offset((Half)1.337f, maskArg); break;
        }
        foreach (var sample in fixture.GetProperty("samples").EnumerateArray())
        {
            var coordinate = new Coord(sample[0].GetInt32(), sample[1].GetInt32(), sample[2].GetInt32());
            Assert.Equal((Half)sample[3].GetSingle(), tree.GetValue(coordinate));
            Assert.Equal(sample[4].GetInt32() != 0, tree.IsValueOn(coordinate));
            Assert.Equal(sample[5].GetInt32(), tree.GetValueDepth(coordinate));
        }
    }

    [Fact]
    public void HalfMeanRoundsOnlyAfterMultiplyingTheFloatCoefficient()
    {
        var grid = new Grid<Half>(); grid.Tree.SetValueOn(new Coord(0), (Half)5);
        var fraction = 1f / 3;
        Half expected = (Half)5;
        for (var pass = 0; pass < 3; ++pass) expected = (Half)((float)expected * fraction);
        new Filter<Half>(grid) { Parallel = false }.Mean();
        Assert.Equal(expected, grid.Tree.GetValue(new Coord(0)));
    }

    [Fact]
    public void DefaultVectorMediansUseNativeLexicographicOrdering()
    {
        CheckVectorMedian(static i => new Vec2d(i, 10000 - i));
        CheckVectorMedian(static i => new Vec3d(i, 10000 - i, -3 * i));
        CheckVectorMedian(static i => new Vec3f(i, 10000 - i, -3 * i));
        CheckVectorMedian(static i => new Vec3i(i, 10000 - i, -3 * i));
        CheckVectorMedian(static i => new Vec4d(i, 10000 - i, -3 * i, 7 * i));
    }

    private static void CheckVectorMedian<T>(Func<int, T> create) where T : unmanaged
    {
        var leaf = new LeafNode<T>(new Coord(0));
        for (var index = 0; index < 512; ++index)
        {
            leaf.SetValueOnly(index, create(511 - index));
            leaf.SetActiveState(index, ((511 - index) & 1) == 0);
        }
        var original = leaf.Values.ToArray();
        Assert.Equal(create(255), leaf.Median());
        T active = default, inactive = default;
        Assert.Equal(256, leaf.MedianOn(ref active)); Assert.Equal(create(254), active);
        Assert.Equal(256, leaf.MedianOff(ref inactive)); Assert.Equal(create(255), inactive);
        Assert.True(leaf.Values.SequenceEqual(original)); Assert.Equal(256, leaf.OnVoxelCount);
    }

    [Fact]
    public void HalfDenseToleranceComparesThePromotedDifferenceBeforeRounding()
    {
        Half background = (Half)0.001953125f, tolerance = (Half)0.0009765625f;
        Half sample = Half.BitDecrement(tolerance);
        Assert.True((float)background - (float)sample > (float)tolerance);
        var tree = new VdbTree<Half>(background);
        var dense = new Dense<Half>(new CoordBBox(new Coord(0), new Coord(0)), sample);
        DenseConversion.CopyFromDense(dense, tree, tolerance);
        Assert.Equal(sample, tree.GetValue(new Coord(0))); Assert.True(tree.IsValueOn(new Coord(0)));
        Assert.Equal(1UL, tree.ActiveVoxelCount);
    }

    [Fact]
    public void HalfLeafConstantToleranceDoesNotAcceptRoundedDownDifferences()
    {
        Half high = (Half)0.001953125f, tolerance = (Half)0.0009765625f;
        var leaf = new LeafNode<Half>(new Coord(0), high, true);
        leaf.SetValueOnly(1, Half.BitDecrement(tolerance));
        Assert.False(leaf.IsConstant(out Half _, out bool _, tolerance));
        Assert.False(leaf.IsConstant(out Half _, out Half _, out bool _, tolerance));
    }

    [Fact]
    public void HalfPruningRetainsValuesJustBeyondTheNativeTolerance()
    {
        Half high = (Half)0.001953125f, tolerance = (Half)0.0009765625f;
        var tree = new VdbTree<Half>(high); var leaf = tree.TouchLeaf(new Coord(0)); leaf.Fill(high, true);
        Half low = Half.BitDecrement(tolerance); leaf.SetValueOnly(1, low);
        Prune.Apply(tree, tolerance);
        Assert.Equal(1, tree.LeafCount); Assert.Same(leaf, tree.ProbeLeaf(new Coord(0)));
        Assert.Equal(low, leaf.GetValue(1)); Assert.Equal(512UL, tree.ActiveVoxelCount);
    }

    [Fact]
    public void HistogramRejectsOverflowedWidthBeforeAcceptingSamples()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Histogram(-double.MaxValue, double.MaxValue, 10));
        var stats = new Stats(); stats.Add(-double.MaxValue); stats.Add(double.MaxValue);
        Assert.ThrowsAny<ArgumentException>(() => new Histogram(stats, 10));
    }

    [Theory]
    [InlineData(double.Epsilon)]
    [InlineData(1e-308)]
    public void HistogramRejectsNonfiniteScale(double width)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Histogram(0, width, 10));
    }
}
