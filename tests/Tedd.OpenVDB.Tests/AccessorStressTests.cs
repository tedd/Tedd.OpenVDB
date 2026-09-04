using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class AccessorStressTests
{
    [Theory]
    [InlineData("fill")]
    [InlineData("leaf tile")]
    [InlineData("lower tile")]
    [InlineData("root tile")]
    [InlineData("prune")]
    [InlineData("clear")]
    [InlineData("clip")]
    [InlineData("topology union")]
    [InlineData("topology intersection")]
    [InlineData("topology difference")]
    [InlineData("combine")]
    public void CachedAccessorsFollowReplacedOrRemovedLeavesAndNeverWriteDetachedLeaves(string mutation)
    {
        var tree = new VdbTree<int>(-1);
        var point = new Coord(9, 17, 25);
        var neighbor = new Coord(10, 18, 26);
        var box = new CoordBBox(new Coord(8, 16, 24), new Coord(15, 23, 31));
        tree.Fill(box, 11, true);
        tree.TouchLeaf(point);
        var writer = tree.GetAccessor();
        var observer = tree.GetAccessor();
        Assert.True(writer.ProbeValue(point, out var initial));
        Assert.Equal(11, initial);
        Assert.True(observer.ProbeValue(neighbor, out _));
        var oldLeaf = Assert.IsType<LeafNode<int>>(tree.ProbeLeaf(point));
        var source = new VdbTree<int>(2);
        switch (mutation)
        {
            case "fill": tree.Fill(box, 12, false); break;
            case "leaf tile": tree.AddTile(1, point, 13, false); break;
            case "lower tile": tree.AddTile(2, point, 14, true); break;
            case "root tile": tree.AddTile(3, point, 15, true); break;
            case "prune": tree.Prune(); break;
            case "clear": tree.Clear(); break;
            case "clip": tree.Clip(new CoordBBox(new Coord(11, 19, 27), new Coord(14, 22, 30))); break;
            case "topology union": source.AddTile(3, point, 99, true); tree.TopologyUnion(source); break;
            case "topology intersection": tree.TopologyIntersection(source); break;
            case "topology difference": source.AddTile(3, point, 99, true); tree.TopologyDifference(source); break;
            case "combine": source.SetValueOn(point, 23); tree.Combine(source, static (int a, int b) => a + b); break;
            default: throw new InvalidOperationException(mutation);
        }

        var expectedOn = tree.ProbeValue(point, out var expectedValue);
        var expectedNeighborOn = tree.ProbeValue(neighbor, out var expectedNeighborValue);
        if (!ReferenceEquals(oldLeaf, tree.ProbeLeaf(point)))
        {
            // A cached reference must not revive detached leaf data after a structural mutation.
            oldLeaf.Fill(-777, true);
            Assert.Equal(expectedValue, tree.GetValue(point));
            Assert.Equal(expectedNeighborValue, tree.GetValue(neighbor));
        }
        Assert.Equal(expectedOn, writer.ProbeValue(point, out var value));
        Assert.Equal(expectedValue, value);
        Assert.Equal(expectedNeighborOn, observer.ProbeValue(neighbor, out value));
        Assert.Equal(expectedNeighborValue, value);
        writer.SetValueOn(point, 1001);
        Assert.Equal(1001, tree.GetValue(point));
        Assert.True(tree.IsValueOn(point));
        Assert.Equal(1001, observer.GetValue(point));
        observer.SetValueOff(neighbor, 1002);
        Assert.Equal(1002, tree.GetValue(neighbor));
        Assert.False(writer.IsValueOn(neighbor));
        Assert.Equal(1002, writer.GetValue(neighbor));
    }

    [Fact]
    public void SuccessfulCombineInvalidatesBothConsumedSourceAndReplacedDestinationAccessors()
    {
        var point = new Coord(17, 18, 19);
        var destination = new VdbTree<int>(10);
        var source = new VdbTree<int>(20);
        destination.SetValueOn(point, 100);
        source.SetValueOff(point, 200);
        var destinationAccessor = destination.GetAccessor();
        var sourceAccessor = source.GetAccessor();
        Assert.Equal(100, destinationAccessor.GetValue(point));
        Assert.Equal(200, sourceAccessor.GetValue(point));
        destination.Combine(source, static (int a, int b) => a + b, prune: true);
        Assert.True(destinationAccessor.ProbeValue(point, out var combined)); Assert.Equal(300, combined);
        Assert.False(sourceAccessor.ProbeValue(point, out var consumed)); Assert.Equal(20, consumed);
        Assert.Equal(30, destinationAccessor.GetValue(new Coord(10000)));
        sourceAccessor.SetValueOn(point, 700);
        Assert.Equal(700, source.GetValue(point));
        Assert.Equal(300, destinationAccessor.GetValue(point));
        destinationAccessor.SetValueOnly(point, 800);
        Assert.Equal(800, destination.GetValue(point));
        Assert.Equal(700, sourceAccessor.GetValue(point));
    }

    [Fact]
    public void ThrowingCombineLeavesCachedAccessorsAttachedToOriginalTrees()
    {
        var destination = new VdbTree<int>(10);
        var source = new VdbTree<int>(20);
        var point = new Coord(1, 2, 3);
        destination.SetValueOn(point, 111);
        source.SetValueOn(point, 222);
        var destinationLeaf = destination.ProbeLeaf(point);
        var sourceLeaf = source.ProbeLeaf(point);
        var a = destination.GetAccessor();
        var b = source.GetAccessor();
        Assert.Equal(111, a.GetValue(point));
        Assert.Equal(222, b.GetValue(point));
        var calls = 0;
        Assert.Throws<ArithmeticException>(() => destination.Combine(source,
            (VoxelState<int> left, VoxelState<int> right) =>
            {
                if (++calls == 10) throw new ArithmeticException("Expected callback failure.");
                return new VoxelState<int>(left.Value + right.Value, left.IsActive || right.IsActive);
            }));
        Assert.Same(destinationLeaf, destination.ProbeLeaf(point));
        Assert.Same(sourceLeaf, source.ProbeLeaf(point));
        Assert.Equal(111, a.GetValue(point));
        Assert.Equal(222, b.GetValue(point));
        a.SetValueOff(point, 333);
        b.SetValueOnly(point, 444);
        Assert.False(destination.ProbeValue(point, out var leftValue)); Assert.Equal(333, leftValue);
        Assert.True(source.ProbeValue(point, out var rightValue)); Assert.Equal(444, rightValue);
    }

    [Fact]
    public void LongLivedAccessorObservesDirectLeafEditsWithoutStructuralInvalidation()
    {
        var point = new Coord(1, 2, 3);
        var tree = new VdbTree<ushort>(ushort.MaxValue);
        var leaf = tree.TouchLeaf(point);
        var accessor = tree.GetAccessor();
        Assert.Equal(ushort.MaxValue, accessor.GetValue(point));
        leaf.SetValueOff(point, 12);
        Assert.False(accessor.ProbeValue(point, out var value)); Assert.Equal((ushort)12, value);
        leaf.SetValueOn(point, 42);
        Assert.True(accessor.ProbeValue(point, out value)); Assert.Equal((ushort)42, value);
        accessor.Clear();
        leaf.Fill(7, true);
        Assert.True(accessor.ProbeValue(point, out value)); Assert.Equal((ushort)7, value);
    }

    [Fact]
    public void BackgroundChangesPreserveExplicitValuesWhileRefreshingMissingReads()
    {
        var point = new Coord(1, 2, 3);
        var missing = new Coord(5000);
        var tree = new VdbTree<int>(10);
        tree.SetValueOn(point, 20);
        var cachedLeaf = tree.GetAccessor();
        var cachedMissing = tree.GetAccessor();
        Assert.Equal(20, cachedLeaf.GetValue(point));
        Assert.Equal(10, cachedMissing.GetValue(missing));
        tree.SetBackground(30);
        Assert.Equal(20, cachedLeaf.GetValue(point));
        Assert.Equal(30, cachedMissing.GetValue(missing));
        Assert.Equal(10, cachedLeaf.GetValue(new Coord(2, 2, 3)));
        tree.Clear();
        Assert.Equal(30, cachedLeaf.GetValue(point));
        cachedLeaf.SetValueOff(point, 40);
        Assert.Equal(40, tree.GetValue(point));
        Assert.Equal(40, cachedMissing.GetValue(point));
    }

    [Fact]
    public void UshortVoxelTransitionsMatchDenseReferenceThroughCompactingCollections() =>
        ExerciseRandomTransitions<ushort>(7391, ushort.MaxValue, static value => (ushort)value);

    [Fact]
    public void BooleanValuesRemainIndependentOfActivityThroughCachedTransitions() =>
        ExerciseRandomTransitions<bool>(1981, true, static value => (value & 1) != 0);

    [Fact]
    public void UnmanagedBlockRecordsSurviveCachedTransitionsAndCompactingCollections() =>
        ExerciseRandomTransitions<BlockRecord>(8191, new BlockRecord(0, 255, 0, ulong.MaxValue),
            static value => new BlockRecord((ushort)value, (byte)(value >> 3), (byte)(value >> 11), (ulong)(uint)value * 0x01010101UL));

    private static void ExerciseRandomTransitions<T>(int seed, T background, Func<int, T> makeValue) where T : unmanaged
    {
        const int length = 16 * 16 * 16;
        var values = Enumerable.Repeat(background, length).ToArray();
        var activity = new bool[length];
        var tree = new VdbTree<T>(background);
        var a = tree.GetAccessor();
        var b = tree.GetAccessor();
        var random = new Random(seed);
        for (var step = 0; step < 500; ++step)
        {
            var coordinate = new Coord(random.Next(16), random.Next(16), random.Next(16));
            var offset = Offset(coordinate);
            var value = makeValue(random.Next());
            var active = random.Next(2) == 0;
            // Both accessors deliberately retain the leaf which the following mutation may replace.
            Assert.Equal(values[offset], a.GetValue(coordinate));
            Assert.Equal(activity[offset], b.IsValueOn(coordinate));
            switch (random.Next(10))
            {
                case 0: a.SetValueOn(coordinate, value); values[offset] = value; activity[offset] = true; break;
                case 1: b.SetValueOff(coordinate, value); values[offset] = value; activity[offset] = false; break;
                case 2: a.SetValueOnly(coordinate, value); values[offset] = value; break;
                case 3: b.SetActiveState(coordinate, active); activity[offset] = active; break;
                case 4:
                    {
                        var min = new Coord(random.Next(12), random.Next(12), random.Next(12));
                        var max = new Coord(min.X + random.Next(5), min.Y + random.Next(5), min.Z + random.Next(5));
                        var box = new CoordBBox(min, max);
                        tree.Fill(box, value, active);
                        foreach (var point in box) { values[Offset(point)] = value; activity[Offset(point)] = active; }
                        break;
                    }
                case 5:
                    {
                        tree.AddTile(1, coordinate, value, active);
                        var origin = new Coord(coordinate.X & ~7, coordinate.Y & ~7, coordinate.Z & ~7);
                        var box = new CoordBBox(origin, new Coord(origin.X + 7, origin.Y + 7, origin.Z + 7));
                        foreach (var point in box) { values[Offset(point)] = value; activity[Offset(point)] = active; }
                        break;
                    }
                case 6: tree.Prune(); break;
                case 7: tree.Clear(); Array.Fill(values, background); Array.Fill(activity, false); break;
                case 8: tree.SetValueOnly(coordinate, value); values[offset] = value; break;
                default:
                    {
                        var min = new Coord(random.Next(12), random.Next(12), random.Next(12));
                        var box = new CoordBBox(min, new Coord(min.X + 3, min.Y + 3, min.Z + 3));
                        tree.SetActiveState(box, active);
                        foreach (var point in box) activity[Offset(point)] = active;
                        break;
                    }
            }
            if (step == 175 || step == 350)
            {
                var movablePressure = new byte[32 * 1024];
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.KeepAlive(movablePressure);
            }
            for (var check = 0; check < 32; ++check)
            {
                var sample = check == 0 ? coordinate : new Coord(random.Next(16), random.Next(16), random.Next(16));
                var expected = Offset(sample);
                Assert.Equal(activity[expected], a.ProbeValue(sample, out var observed));
                Assert.Equal(values[expected], observed);
                Assert.Equal(activity[expected], b.ProbeValue(sample, out observed));
                Assert.Equal(values[expected], observed);
                Assert.Equal(values[expected], tree.GetValue(sample));
            }
        }
        for (var offset = 0; offset < length; ++offset)
        {
            var point = new Coord(offset >> 8, (offset >> 4) & 15, offset & 15);
            Assert.Equal(activity[offset], a.ProbeValue(point, out var value));
            Assert.Equal(values[offset], value);
        }
    }

    [Fact]
    public async Task SeparateAccessorsReadFrozenTreeConcurrentlyDuringCompactingCollections()
    {
        var tree = new VdbTree<ushort>(ushort.MaxValue);
        for (var x = 0; x < 24; ++x)
            for (var y = 0; y < 24; ++y)
                for (var z = 0; z < 24; ++z)
                {
                    var point = new Coord(x, y, z);
                    var value = (ushort)(x * 271 + y * 31 + z);
                    if ((x + y + z) % 3 != 0) tree.SetValueOn(point, value);
                    else tree.SetValueOff(point, value);
                }
        var initialLeafCount = tree.LeafCount;
        var initialActivity = tree.ActiveVoxelCount;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readers = Enumerable.Range(0, 6).Select(worker => Task.Run(async () =>
        {
            var accessor = tree.GetAccessor(); // No accessor is shared across tasks.
            var random = new Random(8192 + worker);
            await start.Task;
            for (var index = 0; index < 8_000; ++index)
            {
                var point = new Coord(random.Next(-2, 27), random.Next(-2, 27), random.Next(-2, 27));
                var inside = point.X >= 0 && point.X < 24 && point.Y >= 0 && point.Y < 24 && point.Z >= 0 && point.Z < 24;
                var expectedValue = inside ? (ushort)(point.X * 271 + point.Y * 31 + point.Z) : ushort.MaxValue;
                var expectedActive = inside && (point.X + point.Y + point.Z) % 3 != 0;
                Assert.Equal(expectedActive, accessor.ProbeValue(point, out var observed));
                Assert.Equal(expectedValue, observed);
                Assert.Equal(expectedValue, accessor.GetValue(point));
                if ((index & 127) == 0) await Task.Yield();
            }
        })).ToArray();
        var collector = Task.Run(async () =>
        {
            await start.Task;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            await Task.Yield();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        });
        start.SetResult();
        await Task.WhenAll(readers.Append(collector));
        Assert.Equal(initialLeafCount, tree.LeafCount);
        Assert.Equal(initialActivity, tree.ActiveVoxelCount);
    }

    private static int Offset(Coord point) => (point.X << 8) | (point.Y << 4) | point.Z;
    private readonly record struct BlockRecord(ushort Material, byte Health, byte Flags, ulong Owner);
}
