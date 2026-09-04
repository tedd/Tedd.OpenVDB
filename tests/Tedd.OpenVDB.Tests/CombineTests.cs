using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public class CombineTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SparseCompositeMatchesPointwiseReference(int operation)
    {
        var a = new VdbTree<int>(-2); var b = new VdbTree<int>(3);
        a.AddTile(2, new(-1, -1, -1), 7, true);
        a.SetValueOff(new(-1, -2, -3), 11);
        b.AddTile(1, new(-1, -1, -1), 17, false);
        b.SetValueOn(new(2, 3, 4), 9);
        var expectedA = a.DeepCopy(); var expectedB = b.DeepCopy();
        var accessorB = b.GetAccessor(); accessorB.GetValue(new(2, 3, 4));
        switch (operation)
        {
            case 0: Composite.CompSum(a, b); break;
            case 1: Composite.CompMul(a, b); break;
            case 2: Composite.CompMin(a, b); break;
            case 3: Composite.CompMax(a, b); break;
        }
        Assert.Equal(0, b.RootEntryCount);
        Assert.Equal(3, accessorB.GetValue(new(2, 3, 4)));
        foreach (var c in new CoordBBox(new(-10, -10, -10), new(10, 10, 10)))
        {
            int x = expectedA.GetValue(c), y = expectedB.GetValue(c);
            int value = operation switch { 0 => x + y, 1 => x * y, 2 => System.Math.Min(x, y), _ => System.Math.Max(x, y) };
            Assert.Equal(value, a.GetValue(c));
            Assert.Equal(expectedA.IsValueOn(c) || expectedB.IsValueOn(c), a.IsValueOn(c));
        }
        int bg = operation switch { 0 => 1, 1 => -6, 2 => -2, _ => 3 };
        Assert.Equal(bg, a.Background);
        Assert.Equal(bg, a.GetValue(new(100000, 100000, 100000)));
    }
    [Fact]
    public void TileCombinationDoesNotVoxelizeLargeUniformRegions()
    {
        var a = new VdbTree<int>(); a.AddTile(3, default, 4, true);
        var b = new VdbTree<int>(); b.AddTile(3, default, 6, true);
        Composite.CompSum(a, b);
        Assert.Equal(10, a.GetValue(default)); Assert.Equal(0, a.LeafCount);
        Assert.Equal(68719476736UL, a.ActiveVoxelCount);
    }
    [Fact]
    public void StateCallbackCanDeactivateWithoutLosingValues()
    {
        var a = new VdbTree<int>(); a.SetValueOn(default, 4);
        var b = new VdbTree<int>(); b.SetValueOn(default, 6);
        a.Combine(b, static (VoxelState<int> x, VoxelState<int> y) => new VoxelState<int>(x.Value + y.Value, false));
        Assert.Equal(10, a.GetValue(default)); Assert.False(a.IsValueOn(default));
    }
    [Fact]
    public void FailingCallbackLeavesBothInputsIntact()
    {
        var a = new VdbTree<int>(); a.SetValueOn(default, 4);
        var b = new VdbTree<int>(); b.SetValueOn(default, 6);
        Assert.Throws<InvalidOperationException>(() => a.Combine(b, static (int _, int _) => throw new InvalidOperationException()));
        Assert.Equal(4, a.GetValue(default)); Assert.Equal(6, b.GetValue(default));
    }
    [Fact]
    public void ReplaceRetainsInactiveTargetDataAndSource()
    {
        var a = new VdbTree<int>(-1); a.SetValueOff(default, 17);
        var b = new VdbTree<int>(); b.SetValueOn(new(1, 2, 3), 8);
        Composite.CompReplace(a, b);
        Assert.Equal(17, a.GetValue(default)); Assert.Equal(8, a.GetValue(new(1, 2, 3)));
        Assert.Equal(1UL, b.ActiveVoxelCount);
    }
    [Fact]
    public void CachedReadsAndWritesAllocateNothingAfterMaterialization()
    {
        var tree = new VdbTree<int>(); var accessor = tree.GetAccessor();
        for (int i = 0; i < 20000; i++) { accessor.SetValueOn(new(1, 2, 3), i); _ = accessor.GetValue(new(1, 2, 3)); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        long sum = 0;
        for (int i = 0; i < 10000; i++) { accessor.SetValueOn(new(1, 2, 3), i); sum += accessor.GetValue(new(1, 2, 3)); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(49995000, sum); Assert.Equal(0, allocated);
    }
    [Fact]
    public void AccessorReferencesSurviveCompactingCollection()
    {
        var tree = new VdbTree<int>(); var accessor = tree.GetAccessor(); accessor.SetValueOn(default, 42);
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        Assert.Equal(42, accessor.GetValue(default)); accessor.SetValueOn(default, 43);
        Assert.Equal(43, tree.GetValue(default));
    }
}
