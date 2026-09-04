// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class DenseTests
{
    [Theory]
    [InlineData(MemoryLayout.XYZ)]
    [InlineData(MemoryLayout.ZYX)]
    public void ExplicitStridesAndOffsetsRoundTrip(MemoryLayout layout)
    {
        var d = new Dense<int>(new(new(-3, -4, -5), new(6, 7, 8)), 0, layout);
        foreach (Coord c in d.Bounds) { int index = d.CoordToOffset(c); Assert.Equal(c, d.OffsetToCoord(index)); Assert.Equal(index, d.LocalCoordToOffset(c - d.Origin)); d[c] = index; }
        for (int i = 0; i < d.Count; i++) Assert.Equal(i, d.Data[i]);
        Assert.Equal(layout == MemoryLayout.XYZ ? 1 : 168, d.XStride); Assert.Equal(layout == MemoryLayout.XYZ ? 120 : 1, d.ZStride);
    }
    [Theory]
    [InlineData(MemoryLayout.XYZ)]
    [InlineData(MemoryLayout.ZYX)]
    public void ExternalMemoryIsSharedAndDeepCopyIsIndependent(MemoryLayout layout)
    {
        var values = new int[24]; var d = new Dense<int>(new(new(0, 0, 0), new(1, 2, 3)), values.AsMemory(), layout); d.Fill(7); Assert.All(values, v => Assert.Equal(7, v));
        var copy = d.DeepCopy(); copy[0] = 9; Assert.Equal(7, d[0]); values[0] = 11; Assert.Equal(11, d[0]); Assert.Equal(9, copy[0]);
    }
    [Fact]
    public void InvalidBoundsAndOffsetsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new Dense<int>(CoordBBox.Empty)); Assert.Throws<ArgumentOutOfRangeException>(() => new Dense<byte>(CoordBBox.Infinite));
        Assert.Throws<ArgumentException>(() => new Dense<int>(CoordBBox.CreateCube(new(0, 0, 0), 2), new int[7].AsMemory()));
        var d = new Dense<int>(new(new(0, 0, 0), new(1, 2, 3))); Assert.Throws<ArgumentOutOfRangeException>(() => d.CoordToOffset(new(-1, 0, 0))); Assert.Throws<ArgumentOutOfRangeException>(() => d.OffsetToLocalCoord(24)); Assert.Throws<ArgumentOutOfRangeException>(() => d.LocalCoordToOffset(new(0, 3, 0)));
    }
    [Theory]
    [InlineData(MemoryLayout.XYZ)]
    [InlineData(MemoryLayout.ZYX)]
    public void SparseDenseRoundTripPreservesValuesAcrossSignedBoundaries(MemoryLayout layout)
    {
        var dense = new Dense<int>(new(new(-9, -2, -1), new(9, 2, 1)), 7, layout); for (int i = 0; i < dense.Count; i++) dense[i] = i % 5 == 0 ? 7 : i;
        var sparse = new VdbTree<int>(7); DenseConversion.CopyFromDense(dense, sparse); var output = new Dense<int>(dense.Bounds, 0, layout); DenseConversion.CopyToDense(sparse, output);
        Assert.True(dense.Data.SequenceEqual(output.Data)); Assert.Equal((ulong)dense.Data.ToArray().Count(v => v != 7), sparse.ActiveVoxelCount);
    }
    [Fact]
    public void UniformDenseBlocksBecomeTilesAndBackgroundBlocksAreSkipped()
    {
        var d = new Dense<ushort>(CoordBBox.CreateCube(new(0, 0, 0), 128), 3); var tree = new VdbTree<ushort>(); DenseConversion.CopyFromDense(d, tree);
        Assert.Equal(0L, tree.LeafCount); Assert.Equal(1, tree.GetValueDepth(new(0, 0, 0))); Assert.Equal(1UL << 21, tree.ActiveVoxelCount);
        d.Fill(0); DenseConversion.CopyFromDense(d, tree); Assert.Equal((ushort)3, tree.GetValue(new(0, 0, 0))); // native skips uniform inactive replacements
    }
    [Fact]
    public void ToleranceSnapsBackgroundWithoutChangingOutsidePartialBlocks()
    {
        var tree = new VdbTree<float>(1); tree.SetValueOn(new(7, 7, 7), 9); var d = new Dense<float>(new(new(0, 0, 0), new(1, 1, 1)), 1.001f);
        d[new Coord(1, 1, 1)] = 2; DenseConversion.CopyFromDense(d, tree, 0.01f); Assert.Equal(1, tree.GetValue(new(0, 0, 0))); Assert.False(tree.IsValueOn(new(0, 0, 0))); Assert.Equal(2, tree.GetValue(new(1, 1, 1))); Assert.Equal(9, tree.GetValue(new(7, 7, 7)));
    }
    [Fact]
    public void InactiveOnlyTreeSuppliesInheritedBlockValues()
    {
        var tree = new VdbTree<int>(7); tree.SetValueOff(new(7, 7, 7), 9); var d = new Dense<int>(new(new(0, 0, 0), new(1, 1, 1)), 3); DenseConversion.CopyFromDense(d, tree);
        Assert.Equal(9, tree.GetValue(new(7, 7, 7))); Assert.False(tree.IsValueOn(new(7, 7, 7)));
    }
    [Fact]
    public void IntegerEndpointConversionDoesNotOverflow()
    {
        var bounds = new CoordBBox(new(int.MaxValue - 1, int.MinValue, 0), new(int.MaxValue, int.MinValue + 1, 1)); var d = new Dense<int>(bounds, 3); var tree = new VdbTree<int>(); DenseConversion.CopyFromDense(d, tree); Assert.Equal(8UL, tree.ActiveVoxelCount);
        var output = new Dense<int>(bounds); DenseConversion.CopyToDense(tree, output); Assert.True(d.Data.SequenceEqual(output.Data));
    }
}

public class NativeDenseConformanceTests
{
    public static IEnumerable<object[]> Cases() { for (int l = 0; l < 2; l++) for (int t = 0; t < 5; t++) for (int p = 0; p < 4; p++) yield return [l, t, p]; }
    private static readonly Lazy<System.Text.Json.JsonDocument> Fixture = new(() => System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-dense.json"))));
    [Theory]
    [MemberData(nameof(Cases))]
    public void DenseConversionMatchesNative(int layout, int target, int pattern)
    {
        var tree = new VdbTree<double>(1);
        if (target == 1) tree.SetValueOff(new(7, 7, 7), 9); if (target == 2) tree.SetValueOn(new(7, 7, 7), 9); if (target == 3) tree.AddTile(1, new(0, 0, 0), 9, true); if (target == 4) tree.AddTile(3, new(0, 0, 0), 9, true);
        var bounds = new CoordBBox(new(-1, 0, 0), new(8, 2, 2)); var dense = new Dense<double>(bounds, 1, (MemoryLayout)layout);
        foreach (Coord c in bounds) { double value = pattern switch { 1 => 2, 2 => 1.0005, 3 => (c.X + c.Y + c.Z) % 3 != 0 ? (c.X * 3 + c.Y + c.Z) * 0.25 : 1, _ => 1 }; dense[c] = value; }
        DenseConversion.CopyFromDense(dense, tree, 0.001);
        var root = Fixture.Value.RootElement; Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", root.GetProperty("commit").GetString()); var result = root.GetProperty("cases")[layout * 20 + target * 4 + pattern].GetProperty("result");
        Assert.Equal(result.GetProperty("active").GetUInt64(), tree.ActiveVoxelCount); Assert.Equal(result.GetProperty("inactive").GetUInt64(), tree.InactiveVoxelCount); Assert.Equal(result.GetProperty("nodes").EnumerateArray().Select(x => x.GetInt64()), tree.NodeCount());
        foreach (var s in result.GetProperty("samples").EnumerateArray())
        {
            var c = new Coord(s[0].GetInt32(), s[1].GetInt32(), s[2].GetInt32()); bool on = tree.ProbeValue(c, out double value);
            Assert.True(value == s[3].GetDouble() && on == (s[4].GetInt32() != 0) && tree.GetValueDepth(c) == s[5].GetInt32(), $"{c}: value={value:R}, on={on}, depth={tree.GetValueDepth(c)} native={s}");
        }
    }
}
