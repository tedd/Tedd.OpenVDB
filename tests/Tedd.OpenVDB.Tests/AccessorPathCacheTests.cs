using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using System.Runtime.CompilerServices;

namespace Tedd.OpenVDB.Tests;

public sealed class AccessorPathCacheTests
{
    [Fact]
    public void ValueEqualityMutationCannotLeaveSecondaryLeavesAttachedToOldTopology()
    {
        var tree = new VdbTree<ReentrantValue>();
        var cachedPoint = new Coord(8, 0, 0);
        var target = new Coord(4096, 0, 0);
        tree.SetValueOn(cachedPoint, new(11));
        tree.AddTile(3, target, new(1), true);
        var accessor = tree.GetAccessor();
        Assert.Equal(11, accessor.GetValue(cachedPoint).Value);
        ReentrantValue.OnCompare = () => tree.AddTile(3, cachedPoint, new(99), true);
        try { accessor.SetValueOn(target, new(2)); }
        finally { ReentrantValue.OnCompare = null; }
        Assert.Equal(2, accessor.GetValue(target).Value);
        Assert.Equal(99, accessor.GetValue(cachedPoint).Value);
    }

    private readonly struct ReentrantValue(int value) : IEquatable<ReentrantValue>
    {
        public int Value { get; } = value;
        public static Action? OnCompare;
        public bool Equals(ReentrantValue other)
        {
            var callback = OnCompare;
            OnCompare = null;
            callback?.Invoke();
            return Value == other.Value;
        }
        public override bool Equals(object? other) => other is ReentrantValue value && Equals(value);
        public override int GetHashCode() => Value;
    }

    [Fact]
    public void TreeDoesNotRetainAbandonedAccessors()
    {
        var tree = new VdbTree<int>(-1);
        var weak = CreateTemporaryAccessor(tree);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        Assert.False(weak.TryGetTarget(out _));
        tree.Clear();
        GC.KeepAlive(tree);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<ValueAccessor<int>> CreateTemporaryAccessor(VdbTree<int> tree)
    {
        var accessor = new ValueAccessor<int>(tree);
        accessor.SetValueOn(new(1, 2, 3), 42);
        return new(accessor);
    }

    [Fact]
    public async Task ConcurrentlyConstructedAccessorsAreAllInvalidated()
    {
        var tree = new VdbTree<int>(-1);
        var point = new Coord(1, 2, 3);
        tree.SetValueOn(point, 42);
        var accessors = await Task.WhenAll(Enumerable.Range(0, 128).Select(_ => Task.Run(() =>
        {
            var accessor = tree.GetAccessor();
            Assert.Equal(42, accessor.GetValue(point));
            return accessor;
        })));
        tree.AddTile(3, point, 73, false);
        foreach (var accessor in accessors)
        {
            Assert.False(accessor.ProbeValue(point, out int value));
            Assert.Equal(73, value);
        }
        accessors[0].SetValueOn(point, 99);
        foreach (var accessor in accessors) Assert.Equal(99, accessor.GetValue(point));
    }

    [Fact]
    public void CachedPathsFollowSignedCoordinatesAndIndependentTopologyChanges()
    {
        Coord[] points = [new(int.MinValue), new(-4097), new(-4096), new(-129), new(-128),
            new(-9), new(-8), new(-1), new(0), new(7), new(8), new(127), new(128),
            new(4095), new(4096), new(int.MaxValue)];
        var tree = new VdbTree<int>(-100);
        var reference = new VdbTree<int>(-100);
        var accessor = tree.GetAccessor();
        for (int phase = 0; phase < 4; ++phase)
        {
            for (int i = 0; i < points.Length; ++i)
            {
                Coord p = points[i];
                accessor.SetValueOn(p, i + 100 * phase);
                reference.SetValueOn(p, i + 100 * phase);
                Assert.Equal(reference.GetValue(p), accessor.GetValue(p));
            }
            foreach (int level in new[] { 1, 2, 3 })
            {
                foreach (Coord p in points)
                {
                    accessor.GetValue(p); // Prime every level before replacing its subtree.
                    tree.AddTile(level, p, 1000 + level, phase % 2 == 0);
                    reference.AddTile(level, p, 1000 + level, phase % 2 == 0);
                    foreach (Coord check in points)
                    {
                        Assert.Equal(reference.ProbeValue(check, out int expected), accessor.ProbeValue(check, out int actual));
                        Assert.Equal(expected, actual);
                        Assert.Equal(expected, accessor.GetValue(check));
                    }
                }
            }
        }
    }

    [Fact]
    public void UniformTileWritesPreserveTopologyAndInactiveValues()
    {
        var tree = new VdbTree<int>(-1);
        var accessor = tree.GetAccessor();
        var point = new Coord(-9, 128, 4096);
        tree.AddTile(2, point, 17, true);
        accessor.SetValueOn(point, 17);
        Assert.Equal(0, tree.LeafCount);
        Assert.Equal(17, accessor.GetValue(point));
        tree.AddTile(2, point, 19, false);
        accessor.SetValueOff(point, 19);
        accessor.SetValueOnly(point, 19);
        Assert.Equal(0, tree.LeafCount);
        Assert.False(accessor.ProbeValue(point, out int value));
        Assert.Equal(19, value);
        accessor.SetValueOn(point, 23);
        Assert.Equal(1, tree.LeafCount);
        Assert.Equal(23, accessor.GetValue(point));
        Assert.True(accessor.IsValueOn(point));
    }
}
