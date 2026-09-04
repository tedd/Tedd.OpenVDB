// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class FilterTests
{
    [Fact]
    public void InactiveValuesAndTilesAreUntouchedByDefault()
    {
        var grid = new Grid<float>(0); grid.Tree.SetValueOn(new(0, 0, 0), 27); grid.Tree.SetValueOff(new(1, 0, 0), 5); grid.Tree.AddTile(1, new(8, 0, 0), 42, true);
        var shape = grid.Tree.DeepCopy(); new Filter<float>(grid) { Parallel = false }.Mean();
        Assert.Equal(5, grid.Tree.GetValue(new(1, 0, 0))); Assert.Equal(42, grid.Tree.GetValue(new(8, 0, 0))); Assert.True(shape.HasSameTopology(grid.Tree));
    }
    [Fact]
    public void OffsetUpdatesSymbolicRootTilesWithoutVoxelization()
    {
        var grid = new Grid<double>(3); grid.Tree.AddTile(3, new(0, 0, 0), 7, true); grid.Tree.AddTile(3, new(-4096, 0, 0), 9, false);
        new Filter<double>(grid) { ProcessTiles = true }.Offset(2);
        Assert.Equal(9, grid.Tree.GetValue(new(0, 0, 0))); Assert.Equal(9, grid.Tree.GetValue(new(-4096, 0, 0))); Assert.Equal(0L, grid.Tree.LeafCount); Assert.Equal(3, grid.Background);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroOrInvertedMaskControlsOffset(bool invert)
    {
        var grid = new Grid<float>(); grid.Tree.SetValueOn(new(0, 0, 0), 2); grid.Tree.SetValueOn(new(1, 0, 0), 2);
        var mask = new Grid<float>(); mask.Tree.SetValueOff(new(1, 0, 0), 1);
        new Filter<float>(grid) { IsMaskInverted = invert }.Offset(4, mask);
        Assert.Equal(invert ? 6 : 2, grid.Tree.GetValue(new(0, 0, 0))); Assert.Equal(invert ? 2 : 6, grid.Tree.GetValue(new(1, 0, 0)));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveIterationCountIsNoOp(int iterations)
    {
        var grid = Setup<float>(); var shape = grid.Tree.DeepCopy(); var f = new Filter<float>(grid) { ProcessTiles = true }; f.Mean(iterations: iterations); f.Gaussian(iterations: iterations); f.Median(iterations: iterations);
        Assert.True(shape.HasSameTopology(grid.Tree)); foreach (var v in shape.EnumerateValues(ValueIterationMode.All)) Assert.Equal(v.Value, grid.Tree.GetValue(v.Coordinate));
    }
    [Fact]
    public void WidthIsClampedToOne()
    {
        var a = Setup<float>(); var b = a.DeepCopy(); new Filter<float>(a).Mean(-1); new Filter<float>(b).Mean(1);
        foreach (var v in a.Tree.EnumerateValues()) Assert.Equal(v.Value, b.Tree.GetValue(v.Coordinate));
    }
    [Fact]
    public void SerialAndParallelPassesProduceIdenticalValues()
    {
        var a = Setup<double>(); var b = a.DeepCopy(); new Filter<double>(a) { Parallel = false, ProcessTiles = true }.Gaussian(); new Filter<double>(b) { Parallel = true, ProcessTiles = true }.Gaussian();
        Assert.True(a.Tree.HasSameTopology(b.Tree)); foreach (var v in a.Tree.EnumerateValues()) Assert.Equal(v.Value, b.Tree.GetValue(v.Coordinate));
    }
    [Fact]
    public void CancellationAndInvalidRangeFailBeforeFiltering()
    {
        var grid = Setup<float>(); var f = new Filter<float>(grid); Assert.Throws<ArgumentException>(() => f.SetMaskRange(1, 1)); Assert.Throws<ArgumentException>(() => f.SetMaskRange(float.NaN, 1));
        using var source = new CancellationTokenSource(); source.Cancel(); Assert.Throws<OperationCanceledException>(() => f.Mean(cancellationToken: source.Token)); Assert.Equal(9, grid.Tree.GetValue(new(-1, 0, 0)));
    }
    [Fact]
    public void CoordinateLimitsUseBackgroundBeyondIntegerDomain()
    {
        var grid = new Grid<double>(0); grid.Tree.SetValueOn(new(int.MaxValue, 0, 0), 27); new Filter<double>(grid) { Parallel = false }.Mean();
        Assert.InRange(grid.Tree.GetValue(new(int.MaxValue, 0, 0)), 0.999999, 1.000001);
    }
    [Fact]
    public void ConstantEqualBackgroundActiveTileNeedsNoRefinement()
    {
        var grid = new Grid<double>(1); grid.Tree.AddTile(3, new(0, 0, 0), 1, true); new Filter<double>(grid) { Parallel = false, ProcessTiles = true }.Mean();
        Assert.Equal(0L, grid.Tree.LeafCount); Assert.Equal(1, grid.Tree.GetValue(new(0, 0, 0)));
    }
    internal static Grid<T> Setup<T>() where T : unmanaged, IFloatingPointIeee754<T>
    {
        var grid = new Grid<T>(T.CreateChecked(0.5)); var t = grid.Tree;
        for (int x = 0; x < 4; x++) for (int y = 0; y < 4; y++) for (int z = 0; z < 4; z++)
        { var c = new Coord(x, y, z); if ((x + y + z) % 3 != 0) t.SetValueOn(c, T.CreateChecked((x * 3 - y + z) * 0.25)); else t.SetValueOff(c, T.CreateChecked(-2.5)); }
        t.AddTile(1, new(8, 0, 0), T.CreateChecked(3.25), true); t.AddTile(1, new(0, 8, 0), T.CreateChecked(-1.25), false); t.SetValueOn(new(-1, 0, 0), T.CreateChecked(9)); return grid;
    }
}
public class NativeFilterConformanceTests
{
    public static IEnumerable<object[]> Cases() { for (int t = 0; t < 2; t++) for (int op = 0; op < 4; op++) for (int tiles = 0; tiles < 2; tiles++) for (int m = 0; m < 3; m++) yield return [t, op, tiles, m]; }
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-filter.json"))));
    [Theory]
    [MemberData(nameof(Cases))]
    public void FilterMatchesNative(int type, int operation, int tiles, int mask)
    {
        if (type == 0) Check<float>(operation, tiles, mask, 0); else Check<double>(operation, tiles, mask, 24);
    }
    private static void Check<T>(int operation, int tiles, int maskType, int offset) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var root = Fixture.Value.RootElement; Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", root.GetProperty("commit").GetString());
        var expected = root.GetProperty("cases")[offset + operation * 6 + tiles * 3 + maskType]; var grid = FilterTests.Setup<T>();
        var mask = new Grid<float>(0.25f); mask.Tree.Fill(new(new(0, 0, 0), new(8, 8, 8)), 1, false); mask.Tree.SetValueOn(new(1, 1, 1), 0.5f);
        if (maskType == 2) { mask.Transform.PostScale(2); mask.Transform.PostTranslate(new(1, -1, 0.5)); }
        var f = new Filter<T>(grid) { Parallel = false, ProcessTiles = tiles != 0 }; if (maskType == 2) { f.SetMaskRange(0.1f, 0.9f); f.IsMaskInverted = true; }
        Grid<float>? mp = maskType == 0 ? null : mask; int width = operation % 2 + 1;
        switch (operation) { case 0: f.Mean(width, 2, mp); break; case 1: f.Gaussian(width, 2, mp); break; case 2: f.Median(width, 2, mp); break; case 3: f.Offset(T.CreateChecked(0.75), mp); break; }
        Assert.Equal(expected.GetProperty("active").GetUInt64(), grid.ActiveVoxelCount); Assert.Equal(expected.GetProperty("nodes").EnumerateArray().Select(x => x.GetInt64()), grid.Tree.NodeCount());
        foreach (var s in expected.GetProperty("samples").EnumerateArray())
        {
            var c = new Coord(s[0].GetInt32(), s[1].GetInt32(), s[2].GetInt32()); bool on = grid.Tree.ProbeValue(c, out T value); double wanted = s[3].GetDouble(), actual = double.CreateChecked(value);
            double tolerance = (typeof(T) == typeof(float) ? 2e-6 : 1e-12) * System.Math.Max(1, System.Math.Abs(wanted));
            Assert.True(System.Math.Abs(actual - wanted) <= tolerance && on == (s[4].GetInt32() != 0) && grid.Tree.GetValueDepth(c) == s[5].GetInt32(), $"{c}: managed {actual:R}, native {wanted:R}, on={on}, native on={s[4]}, depth={grid.Tree.GetValueDepth(c)} native depth={s[5]}");
        }
    }
}
