// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class TreeValueTests
{
    [Theory]
    [InlineData(ValueIterationMode.All)]
    [InlineData(ValueIterationMode.Active)]
    [InlineData(ValueIterationMode.Inactive)]
    public void HandleSnapshotsMatchValueEnumeration(ValueIterationMode mode)
    {
        var tree = Setup(); Assert.Equal(tree.EnumerateValues(mode), tree.EnumerateValueHandles(mode).Select(h => h.Snapshot()));
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    public void DepthLimitsSelectStoredValues(int min, int max)
    {
        var tree = Setup(); var expected = tree.EnumerateValues().Where(v => 3 - v.Level >= min && 3 - v.Level <= max);
        Assert.Equal(expected, tree.EnumerateValueHandles(minDepth: min, maxDepth: max).Select(h => h.Snapshot()));
    }
    [Fact]
    public void MutationPreservesHandleAndTopologyWithNativeActivityEffects()
    {
        var tree = Setup(); var shape = tree.NodeCount(); var handles = tree.EnumerateValueHandles(ValueIterationMode.All).Where(v => v.Level == 3 || v.IsVoxel).Take(20).ToArray();
        foreach (var h in handles) { bool wasActive = h.IsActive; h.SetValue(42); Assert.Equal(wasActive, h.IsActive); Assert.Equal(42, h.Value); h.SetValueOff(); Assert.False(h.IsActive); h.SetValueOnly(17); Assert.False(h.IsActive); h.ModifyValue(v => v + 1); Assert.Equal(18, h.Value); Assert.Equal(h.IsVoxel, h.IsActive); }
        Assert.Equal(shape, tree.NodeCount()); Assert.All(handles, h => Assert.True(h.IsValid));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void StructuralChangesInvalidateLiveHandlesAndEnumerators(int operation)
    {
        var tree = Setup(); using var iterator = tree.EnumerateValueHandles().GetEnumerator(); Assert.True(iterator.MoveNext()); var handle = iterator.Current;
        switch (operation) { case 0: tree.Clear(); break; case 1: tree.TouchLeaf(new(8192, 0, 0)); break; case 2: tree.AddTile(3, new(0, 0, 0), 9, true); break; case 3: tree.Prune(); break; }
        Assert.False(handle.IsValid); Assert.Throws<InvalidOperationException>(() => handle.SetValue(1)); Assert.Throws<InvalidOperationException>(() => iterator.MoveNext());
    }
    [Fact]
    public void ActivatingInactiveIteratorVisitsEachStoredLocationOnce()
    {
        var tree = new VdbTree<int>(); tree.TouchLeaf(new(0, 0, 0)); long count = 0;
        foreach (var h in tree.EnumerateValueHandles(ValueIterationMode.Inactive)) { h.SetValueOn(9); count++; }
        Assert.Equal(32767 + 4095 + 512, count); Assert.Equal(1UL << 36, tree.ActiveVoxelCount); Assert.Equal(1L, tree.LeafCount);
    }
    [Fact]
    public void EmptyAndInvalidHandlesFailExplicitly()
    {
        TreeValue<int> empty = default; Assert.False(empty.IsValid); Assert.Throws<InvalidOperationException>(() => empty.SetValue(3));
        var tree = new VdbTree<int>(); Assert.Empty(tree.EnumerateValueHandles()); Assert.Throws<ArgumentOutOfRangeException>(() => tree.EnumerateValueHandles(minDepth: 3, maxDepth: 2).ToArray());
    }
    [Fact]
    public void ModifierNoOpsPreserveTilesAndOnlyMaterializeAbsentRootForStateCallback()
    {
        var tree = new VdbTree<int>(); tree.AddTile(3, new(0, 0, 0), 9, true); tree.ModifyValue(new(0, 0, 0), v => v); Assert.Equal(0, tree.GetValueDepth(new(0, 0, 0)));
        tree.ModifyValueAndActiveState(new(4096, 0, 0), s => s); Assert.Equal(1, tree.GetValueDepth(new(4096, 0, 0))); Assert.Equal(0L, tree.LeafCount);
    }
    [Fact]
    public void TreeEmptinessIsIndependentOfActivity()
    {
        var tree = new VdbTree<float>(1); Assert.True(tree.IsEmpty); Assert.False(tree.HasActiveVoxels);
        tree.AddTile(3, new(0, 0, 0), 1, false); Assert.True(tree.IsEmpty); tree.AddTile(3, new(4096, 0, 0), -1, false); Assert.False(tree.IsEmpty); Assert.False(tree.HasActiveVoxels);
        tree.Clear(); tree.TouchLeaf(new(0, 0, 0)); Assert.False(tree.IsEmpty); Assert.False(tree.HasActiveVoxels); tree.SetValueOn(new(0, 0, 0), 1); Assert.True(tree.HasActiveVoxels);
    }
    private static VdbTree<int> Setup()
    {
        var tree = new VdbTree<int>(7); tree.AddTile(3, new(-4096, 0, 0), 9, true); tree.AddTile(2, new(0, 0, 0), 11, true); tree.AddTile(1, new(128, 0, 0), 13, true); tree.SetValueOn(new(256, 0, 0), 17); tree.SetValueOff(new(257, 0, 0), 19); return tree;
    }
}


public class NativeIteratorConformanceTests
{
    private static readonly Lazy<System.Text.Json.JsonDocument> Fixture = new(() => System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-iterator.json"))));
    public static IEnumerable<object[]> Cases() { for (int mode = 0; mode < 3; mode++) for (int min = 0; min < 4; min++) for (int max = min; max < 4; max++) for (int mutation = 0; mutation < 4; mutation++) yield return [mode, min, max, mutation]; }
    [Theory]
    [MemberData(nameof(Cases))]
    public void OrderedTraversalAndMutationMatchNative(int mode, int min, int max, int mutation)
    {
        var root = Fixture.Value.RootElement; Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", root.GetProperty("commit").GetString());
        var expected = root.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("mode").GetInt32() == mode && c.GetProperty("min").GetInt32() == min && c.GetProperty("max").GetInt32() == max && c.GetProperty("mutation").GetInt32() == mutation);
        var tree = new VdbTree<int>(7); tree.AddTile(3, new(-4096, 0, 0), 9, true); tree.AddTile(3, new(4096, 0, 0), 19, false); tree.AddTile(2, new(0, 0, 0), 11, true); tree.AddTile(1, new(128, 0, 0), 13, true); tree.SetValueOn(new(256, 0, 0), 17); tree.SetValueOff(new(257, 0, 0), 19);
        var iteration = mode switch { 0 => ValueIterationMode.All, 1 => ValueIterationMode.Active, _ => ValueIterationMode.Inactive }; ulong hash = 14695981039346656037UL, count = 0;
        foreach (var item in tree.EnumerateValueHandles(iteration, min, max))
        {
            if (mutation == 1) item.SetValue(42); else if (mutation == 2) item.ModifyValue(v => v + 1); else if (mutation == 3) item.SetActiveState(true);
            var c = item.Coordinate; Mix(c.X); Mix(c.Y); Mix(c.Z); Mix(item.Value); Mix(item.IsActive ? 1 : 0); Mix(item.Level);
            if (count < 20) { var s = expected.GetProperty("first")[(int)count]; Assert.Equal(new[] { s[0].GetInt32(), s[1].GetInt32(), s[2].GetInt32(), s[3].GetInt32(), s[4].GetInt32(), s[5].GetInt32() }, new[] { c.X, c.Y, c.Z, item.Value, item.IsActive ? 1 : 0, item.Level }); }
            count++;
        }
        Assert.Equal(expected.GetProperty("count").GetUInt64(), count); Assert.Equal(expected.GetProperty("hash").GetUInt64(), hash);
        void Mix(long value) { hash = unchecked((hash ^ (ulong)value) * 1099511628211UL); }
    }
}
