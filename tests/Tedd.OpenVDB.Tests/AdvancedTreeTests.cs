// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class AdvancedTreeTests
{
    [Fact]
    public void BackgroundReplacementPreservesActivityAndUnrelatedValues()
    {
        var tree = new VdbTree<int>(5);
        tree.AddTile(3, new(-4096, 0, 0), -5, false);
        tree.AddTile(2, new(128, 0, 0), 5, false);
        tree.SetValueOff(new(0, 0, 0), -5);
        tree.SetValueOn(new(1, 0, 0), 5);
        tree.SetValueOff(new(2, 0, 0), 17);
        var accessor = tree.GetAccessor(); accessor.GetValue(new(0, 0, 0));
        tree.SetBackground(9, true);
        Assert.Equal(9, tree.Background);
        Assert.Equal(-9, accessor.GetValue(new(0, 0, 0)));
        Assert.Equal(-9, tree.GetValue(new(-4096, 0, 0)));
        Assert.Equal(9, tree.GetValue(new(128, 0, 0)));
        Assert.Equal(9, tree.GetValue(new(4096, 0, 0)));
        Assert.Equal(5, tree.GetValue(new(1, 0, 0)));
        Assert.True(tree.IsValueOn(new(1, 0, 0)));
        Assert.Equal(17, tree.GetValue(new(2, 0, 0)));
        Assert.Equal(1UL, tree.ActiveVoxelCount);
    }
    [Fact]
    public void BackgroundReplacementUsesNativeTolerance()
    {
        var tree = new VdbTree<float>(1e-4f);
        tree.SetValueOff(new(0, 0, 0), 1e-4f + 1e-9f);
        tree.SetValueOff(new(1, 0, 0), -1e-4f - 1e-9f);
        tree.SetValueOff(new(2, 0, 0), 1e-4f + 1e-6f);
        tree.SetValueOff(new(3, 0, 0), float.NaN);
        tree.SetBackground(2e-4f, true);
        Assert.Equal(2e-4f, tree.GetValue(new(0, 0, 0)));
        Assert.Equal(-2e-4f, tree.GetValue(new(1, 0, 0)));
        Assert.Equal(1e-4f + 1e-6f, tree.GetValue(new(2, 0, 0)));
        // Native isApproxEqual is !(abs(a-b)>tolerance), including NaN.
        Assert.Equal(2e-4f, tree.GetValue(new(3, 0, 0)));
    }
    [Fact]
    public void BackgroundOnlyDoesNotRewriteNodes()
    {
        var tree = new VdbTree<int>(5); tree.TouchLeaf(new(0, 0, 0));
        tree.SetBackground(8, false);
        Assert.Equal(5, tree.GetValue(new(0, 0, 0)));
        Assert.Equal(8, tree.GetValue(new(4096, 0, 0)));
    }
    [Fact]
    public void BooleanBackgroundNegationIsLogicalNot()
    {
        var tree = new VdbTree<bool>(false); tree.TouchLeaf(new(0, 0, 0)).SetValueOnly(new Coord(1, 0, 0), true);
        tree.SetValueOn(new(2, 0, 0), false); tree.SetBackground(true, true);
        Assert.True(tree.GetValue(new(0, 0, 0))); Assert.False(tree.GetValue(new(1, 0, 0))); Assert.False(tree.GetValue(new(2, 0, 0)));
    }
    [Fact]
    public void UnsignedBackgroundNegationWraps()
    {
        var tree = new VdbTree<ushort>(1); tree.SetValueOff(new(0, 0, 0), ushort.MaxValue); tree.SetBackground(2, true);
        Assert.Equal((ushort)65534, tree.GetValue(new(0, 0, 0)));
    }
    [Fact]
    public void AddAndStealLeafTransferIdentityAndInvalidateCaches()
    {
        var tree = new VdbTree<int>(7); var c = new Coord(1, 2, 3);
        tree.SetValueOn(c, 11); var accessor = tree.GetAccessor(); accessor.GetValue(c);
        var original = tree.ProbeLeaf(c); var replacement = new LeafNode<int>(c, 13, true);
        tree.AddLeaf(replacement); Assert.Same(replacement, tree.ProbeLeaf(c)); Assert.Equal(13, accessor.GetValue(c));
        Assert.Same(replacement, tree.StealLeaf(c, 19, false)); Assert.Null(tree.ProbeLeaf(c));
        Assert.Equal(19, accessor.GetValue(c)); Assert.False(accessor.IsValueOn(c)); Assert.Equal(0L, tree.LeafCount);
        Assert.Equal(2, tree.GetValueDepth(c)); Assert.Null(tree.StealLeaf(c));
        Assert.Equal(11, original!.GetValue(c));
        tree.AddLeaf(replacement); Assert.Equal(512UL, tree.ActiveVoxelCount);
    }
    [Fact]
    public void StealAllLeavesPreservesTilesAndDetachesBuffers()
    {
        var tree = new VdbTree<int>(7); tree.SetValueOn(new(0, 0, 0), 11); tree.SetValueOn(new(128, 0, 0), 12); tree.AddTile(3, new(4096, 0, 0), 21, true);
        var leaves = tree.StealLeaves(-9, true);
        Assert.Equal(2, leaves.Length); Assert.Equal(0L, tree.LeafCount); Assert.Equal(-9, tree.GetValue(new(0, 0, 0)));
        leaves[0].Fill(42, true); Assert.Equal(-9, tree.GetValue(new(0, 0, 0))); Assert.Equal(21, tree.GetValue(new(4096, 0, 0)));
        Assert.Equal((1UL << 36) + 1024, tree.ActiveVoxelCount);
    }
    [Theory]
    [InlineData(-9, -1)]
    [InlineData(-1, 0)]
    [InlineData(0, 7)]
    [InlineData(7, 8)]
    [InlineData(127, 128)]
    [InlineData(int.MinValue, int.MinValue + 8)]
    [InlineData(int.MaxValue - 8, int.MaxValue)]
    public void DenseFillMatchesDenseReferenceAtBoundaries(int min, int max)
    {
        var tree = new VdbTree<ushort>(0); var bounds = new CoordBBox(new(min, 0, 0), new(max, 1, 1));
        tree.DenseFill(bounds, 3, true); Assert.Equal(bounds.Volume, tree.ActiveVoxelCount);
        Assert.Equal((long)(((long)(max & -8) - (min & -8)) / 8 + 1), tree.LeafCount);
        foreach (Coord c in bounds) { Assert.Equal((ushort)3, tree.GetValue(c)); Assert.Equal(3, tree.GetValueDepth(c)); }
        Assert.Equal((ushort)0, tree.GetValue(new(min, 2, 2)));
    }
    [Fact]
    public void DenseFillMaterializesUniformInactiveBackground()
    {
        var tree = new VdbTree<int>(7); tree.DenseFill(new(new(0, 0, 0), new(15, 7, 7)), 7, false);
        Assert.Equal(2L, tree.LeafCount); Assert.False(tree.IsEmpty); Assert.False(tree.HasActiveVoxels); Assert.Equal(1024UL, tree.InactiveLeafVoxelCount);
        tree.Prune(); Assert.Equal(0, tree.RootEntryCount);
    }
    [Fact]
    public void VoxelizationPreflightsAllocationBudget()
    {
        var tree = new VdbTree<int>(7); tree.AddTile(2, new(0, 0, 0), 3, true); tree.AddTile(1, new(128, 0, 0), 5, false);
        Assert.Throws<InvalidOperationException>(() => tree.VoxelizeActiveTiles(4095));
        Assert.Equal(0L, tree.LeafCount); Assert.Equal(1, tree.GetValueDepth(new(0, 0, 0)));
        tree.VoxelizeActiveTiles(4096);
        Assert.Equal(4096L, tree.LeafCount); Assert.Equal(1UL << 21, tree.ActiveVoxelCount);
        Assert.Equal(5, tree.GetValue(new(128, 0, 0))); Assert.Equal(2, tree.GetValueDepth(new(128, 0, 0)));
        tree.Prune(); Assert.Equal(0L, tree.LeafCount); Assert.Equal(1, tree.GetValueDepth(new(0, 0, 0)));
    }
    [Fact]
    public void RootTileVoxelizationRejectsSmallBudgetWithoutAllocating()
    {
        var tree = new VdbTree<byte>(); tree.AddTile(3, new(0, 0, 0), 1, true);
        Assert.Throws<InvalidOperationException>(() => tree.VoxelizeActiveTiles(1_000_000)); Assert.Equal(0L, tree.LeafCount);
    }
    [Fact]
    public void CountsAndWholeLeafBoundsIncludeActiveTiles()
    {
        var tree = new VdbTree<int>(7); Assert.Equal(new long[] { 0, 0, 0, 1 }, tree.NodeCount());
        tree.SetValueOn(new(1, 2, 3), 9); Assert.Equal(new long[] { 1, 1, 1, 1 }, tree.NodeCount());
        Assert.Equal((1UL << 36) - 1, tree.InactiveVoxelCount); Assert.Equal(511UL, tree.InactiveLeafVoxelCount);
        tree.AddTile(3, new(4096, 0, 0), 7, false); Assert.Equal((1UL << 36) - 1, tree.InactiveVoxelCount);
        tree.AddTile(3, new(8192, 0, 0), -7, false); Assert.Equal((2UL << 36) - 1, tree.InactiveVoxelCount);
        tree.AddTile(1, new(16, 0, 0), 9, true); Assert.True(tree.TryGetLeafBoundingBox(out var b));
        Assert.Equal(new CoordBBox(new(0, 0, 0), new(23, 7, 7)), b);
    }
    [Fact]
    public void EmptyTreeHasNoLeafBoundsButOriginIndexRange()
    {
        var tree = new VdbTree<int>(); Assert.False(tree.TryGetLeafBoundingBox(out var b)); Assert.True(b.IsEmpty);
        Assert.Equal(new CoordBBox(new(0, 0, 0), new(0, 0, 0)), tree.GetIndexRange());
    }
    [Fact]
    public void ValueModifiersPreserveCallbackStateAndThrowBeforeMutation()
    {
        var tree = new VdbTree<int>(7); var c = new Coord(0, 0, 0); tree.ModifyValue(c, v => v + 2);
        Assert.Equal(9, tree.GetValue(c)); Assert.True(tree.IsValueOn(c));
        tree.ModifyValueAndActiveState(c, s => new(s.Value + 3, !s.IsActive)); Assert.Equal(12, tree.GetValue(c)); Assert.False(tree.IsValueOn(c));
        Assert.Throws<ArithmeticException>(() => tree.ModifyValue(new(4096, 0, 0), _ => throw new ArithmeticException())); Assert.Equal(1, tree.RootEntryCount);
    }
    [Theory]
    [InlineData(MergePolicy.ActiveStates)]
    [InlineData(MergePolicy.Nodes)]
    [InlineData(MergePolicy.ActiveStatesAndNodes)]
    public void MergeStealsLeavesAndInvalidatesBothAccessors(MergePolicy policy)
    {
        var a = new VdbTree<int>(5); var b = new VdbTree<int>(7); var c = new Coord(1, 0, 0); b.SetValueOn(c, 42); var leaf = b.ProbeLeaf(c);
        var aa = a.GetAccessor(); var ba = b.GetAccessor(); aa.GetValue(c); ba.GetValue(c); a.Merge(b, policy);
        Assert.Same(leaf, a.ProbeLeaf(c)); Assert.Equal(42, aa.GetValue(c)); Assert.Equal(7, ba.GetValue(c)); Assert.Equal(5, a.GetValue(new(0, 0, 0))); Assert.Equal(0, b.RootEntryCount);
    }
    [Fact]
    public void InvalidMergeInputsDoNotMutateTrees()
    {
        var tree = new VdbTree<int>(); tree.SetValueOn(new(0, 0, 0), 1);
        Assert.Throws<ArgumentException>(() => tree.Merge(tree)); Assert.Throws<ArgumentOutOfRangeException>(() => tree.Merge(new(), (MergePolicy)99)); Assert.Equal(1UL, tree.ActiveVoxelCount);
    }
    [Fact]
    public void LowerMedianUsesActiveSubsetWithoutReordering()
    {
        var leaf = new LeafNode<int>(new(0, 0, 0)); for (int i = 0; i < 512; i++) leaf.SetValueOnly(i, 511 - i);
        Assert.Equal(255, leaf.Median()); Assert.Equal(511, leaf.GetValue(0));
        leaf.SetValueOn(0, 20); leaf.SetValueOn(1, 10); leaf.SetValueOn(2, 40); leaf.SetValueOn(3, 30);
        int value = -1; Assert.Equal(4, leaf.MedianOn(ref value)); Assert.Equal(20, value);
        leaf.Fill(8, false); value = -1; Assert.Equal(0, leaf.MedianOn(ref value)); Assert.Equal(-1, value); Assert.Equal(512, leaf.MedianOff(ref value)); Assert.Equal(8, value);
    }
}

public class LeafManagerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void AuxiliaryBuffersAreIndependentSnapshots(int count)
    {
        var tree = new VdbTree<int>(); tree.SetValueOn(new(0, 0, 0), 7); tree.SetValueOn(new(8, 0, 0), 9); var manager = new LeafManager<int>(tree, count);
        Assert.Equal(2, manager.LeafCount); Assert.Equal(count, manager.AuxiliaryBufferCount);
        for (int i = 1; i <= count; i++) { Assert.Equal(7, manager.GetBuffer(0, i)[0]); manager.GetBuffer(0, i)[0] = 10 + i; }
        Assert.Equal(7, tree.GetValue(new(0, 0, 0))); manager.SyncAllBuffers();
        for (int i = 1; i <= count; i++) Assert.Equal(7, manager.GetBuffer(0, i)[0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.GetBuffer(0, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => manager.GetBuffer(0, count + 1));
    }
    [Fact]
    public void SwappingBuffersPreservesActivityIdentityAndCachedAccessors()
    {
        var tree = new VdbTree<int>(); var c = new Coord(0, 0, 0); tree.SetValueOn(c, 7); var leaf = tree.ProbeLeaf(c); var a = tree.GetAccessor(); a.GetValue(c);
        var manager = new LeafManager<int>(tree, 1); manager.GetBuffer(0, 1).Fill(42); manager.SwapBuffer(1);
        Assert.Same(leaf, tree.ProbeLeaf(c)); Assert.Equal(42, a.GetValue(c)); Assert.Equal(1UL, tree.ActiveVoxelCount); Assert.Equal(7, manager.GetBuffer(0, 1)[0]);
        manager.SwapBuffer(1); Assert.Equal(7, a.GetValue(c));
        GC.Collect(2, GCCollectionMode.Forced, true, true); Assert.Equal(7, a.GetValue(c));
        manager.SwapBuffer(1); manager.SwapBuffer(1); // warm generic paths
        long start = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 1000; i++) manager.SwapBuffer(1);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void StructuralChangesRequireRebuild(int kind)
    {
        var tree = new VdbTree<int>(); tree.SetValueOn(new(0, 0, 0), 7); var manager = new LeafManager<int>(tree, 1);
        switch (kind) { case 0: tree.TouchLeaf(new(8, 0, 0)); break; case 1: tree.AddTile(1, new(0, 0, 0), 9, true); break; case 2: tree.Clear(); break; case 3: tree.StealLeaf(new(0, 0, 0)); break; case 4: tree.AddLeaf(new(new(0, 0, 0))); break; }
        Assert.False(manager.IsValid); Assert.Throws<InvalidOperationException>(() => manager.SwapBuffer(1)); manager.Rebuild(2); Assert.True(manager.IsValid); Assert.Equal((int)tree.LeafCount, manager.LeafCount);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessingDistinctLeavesSupportsParallelExecution(bool parallel)
    {
        var tree = new VdbTree<int>(); for (int i = 0; i < 100; i++) tree.SetValueOn(new(i * 8, 0, 0), i); var manager = new LeafManager<int>(tree, 1);
        manager.ForEach((leaf, index) => leaf.SetValueOnly(0, index + 1), parallel);
        for (int i = 0; i < 100; i++) Assert.Equal(i + 1, tree.GetValue(new(i * 8, 0, 0))); Assert.True(manager.IsValid);
    }
}

public class NativeMergeConformanceTests
{
    public static IEnumerable<object[]> Cases()
    {
        for (int policy = 0; policy < 3; policy++) for (int a = 0; a < 9; a++) for (int b = 0; b < 9; b++) yield return [policy, a, b];
    }
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-advanced-tree.json"))));
    [Theory]
    [MemberData(nameof(Cases))]
    public void MergeMatchesNative(int policy, int a, int b)
    {
        var root = Fixture.Value.RootElement; Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", root.GetProperty("commit").GetString());
        var expected = root.GetProperty("cases")[policy * 81 + a * 9 + b];
        var ta = Setup(a, 5); var tb = Setup(b, 7); ta.Merge(tb, (MergePolicy)policy);
        var result = expected.GetProperty("result"); Assert.Equal(result.GetProperty("active").GetUInt64(), ta.ActiveVoxelCount);
        Assert.Equal(result.GetProperty("inactive").GetUInt64(), ta.InactiveVoxelCount); Assert.Equal(result.GetProperty("rootEntries").GetInt32(), ta.RootEntryCount);
        Assert.Equal(result.GetProperty("nodes").EnumerateArray().Select(x => x.GetInt64()), ta.NodeCount());
        Assert.Equal(expected.GetProperty("sourceEntries").GetInt32(), tb.RootEntryCount);
        foreach (var s in result.GetProperty("samples").EnumerateArray())
        {
            var c = new Coord(s[0].GetInt32(), s[1].GetInt32(), s[2].GetInt32()); bool on = ta.ProbeValue(c, out int value);
            Assert.True(value == s[3].GetInt32() && on == (s[4].GetInt32() != 0) && ta.GetValueDepth(c) == s[5].GetInt32(), $"{c}: value={value}, on={on}, depth={ta.GetValueDepth(c)}; native={s}");
        }
    }
    private static VdbTree<int> Setup(int scenario, int background)
    {
        var t = new VdbTree<int>(background);
        switch (scenario)
        {
            case 1: t.AddTile(3, new(0, 0, 0), -background, false); break;
            case 2: t.AddTile(3, new(0, 0, 0), 9, true); break;
            case 3: t.AddTile(2, new(0, 0, 0), 10, true); break;
            case 4: t.AddTile(1, new(0, 0, 0), 11, true); break;
            case 5: t.SetValueOn(new(0, 0, 0), 21); t.SetValueOff(new(1, 1, 1), -background); t.SetValueOn(new(7, 7, 7), 22); t.SetValueOff(new(8, 8, 8), 23); t.SetValueOn(new(128, 128, 128), 24); break;
            case 6: t.SetValueOn(new(4096, 0, 0), 25); t.SetValueOff(new(4097, 1, 1), -background); break;
            case 7: t.DenseFill(new(new(0, 0, 0), new(8, 8, 8)), 31, true); t.SetValueOff(new(1, 1, 1), 32); break;
            case 8: t.AddTile(3, new(0, 0, 0), -background, false); t.SetValueOn(new(0, 0, 0), 41); t.AddTile(1, new(8, 0, 0), 42, false); break;
        }
        return t;
    }
}
