// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class ValueTransformerTests
{
    private sealed class Counter(int value) { public int Value = value; }
    private struct Total { public long Sum; public int Count; public int Joins; }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MutableOperationSharingAndValueCopiesPreserveExternalState(bool threaded, bool share)
    {
        int[] visited = new int[3073];
        var options = new ValueTransformOptions { Threaded = threaded, ShareOperation = share, GrainSize = 17, MaximumDegreeOfParallelism = 4 };
        int result = ValueTransformer.ForEach(Enumerable.Range(0, visited.Length), 11,
            (int index, ref int count) => { Interlocked.Increment(ref count); Interlocked.Increment(ref visited[index]); }, options);
        Assert.Equal(share ? visited.Length + 11 : 11, result);
        Assert.All(visited, count => Assert.Equal(1, count));
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReferenceOperationCopiesAreExplicitAndIndependent(bool threaded, bool share)
    {
        var original = new Counter(7); int copies = 0, callbacks = 0;
        var result = ValueTransformer.ForEach(Enumerable.Range(0, 91), original,
            (int _, ref Counter state) => { Interlocked.Increment(ref state.Value); Interlocked.Increment(ref callbacks); },
            new() { Threaded = threaded, ShareOperation = share, GrainSize = 7, MaximumDegreeOfParallelism = 4 },
            state => { Interlocked.Increment(ref copies); return new Counter(state.Value); });
        Assert.Same(original, result); Assert.Equal(share ? 98 : 7, original.Value);
        Assert.Equal(share ? 0 : threaded ? 13 : 1, copies); Assert.Equal(91, callbacks);
    }
    [Fact]
    public void ActionOverloadSharesClosureEvenWhenCopyingIsRequested()
    {
        int visits = 0;
        ValueTransformer.ForEach(Enumerable.Range(0, 517), _ => Interlocked.Increment(ref visits), new() { ShareOperation = false, GrainSize = 8 });
        Assert.Equal(517, visits);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReductionIncludesEachPartitionSeedAndJoinsOnlySplitStates(bool threaded)
    {
        var result = ValueTransformer.Accumulate(Enumerable.Range(1, 101), new Total { Sum = 19, Count = 3 },
            static (int value, ref Total total) => { total.Sum += value; total.Count++; },
            static (ref Total total, Total other) => { total.Sum += other.Sum; total.Count += other.Count; total.Joins++; },
            new() { Threaded = threaded, GrainSize = 10, MaximumDegreeOfParallelism = 4 });
        int partitions = threaded ? 11 : 1;
        Assert.Equal(5151 + partitions * 19L, result.Sum); Assert.Equal(101 + partitions * 3, result.Count); Assert.Equal(partitions - 1, result.Joins);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyOperationsRetainSeedsAndNeverInvokeCallbacks(bool threaded)
    {
        var options = new ValueTransformOptions { Threaded = threaded, ShareOperation = false, MaximumItems = 0 };
        Assert.Equal(11, ValueTransformer.ForEach(Array.Empty<int>(), 11, static (int _, ref int value) => throw new Exception(), options));
        Assert.Equal(11, ValueTransformer.Accumulate(Array.Empty<int>(), 11, static (int _, ref int value) => throw new Exception(), static (ref int value, int other) => throw new Exception(), options));
        var output = new VdbTree<int>(17);
        Assert.Equal(11, ValueTransformer.TransformValues(new VdbTree<int>(), output, 11,
            static (VoxelValue<int> _, ValueAccessor<int> target, ref int value) => throw new Exception(), options: options));
        Assert.True(output.IsEmpty); Assert.Equal(17, output.Background);
    }
    [Theory]
    [InlineData(MergePolicy.ActiveStates)]
    [InlineData(MergePolicy.Nodes)]
    [InlineData(MergePolicy.ActiveStatesAndNodes)]
    public void ParallelOutputWorkersSeeZeroBackgroundAndKeepDisjointWrites(MergePolicy policy)
    {
        var input = new VdbTree<int>();
        for (int i = 0; i < 21; i++) input.SetValueOn(new(i * 8, 0, 0), i + 1);
        var output = new VdbTree<int>(9); Coord sentinel = new(-4096); output.SetValueOn(sentinel, 77);
        var backgrounds = new int[21];
        ValueTransformer.TransformValues(input, output, (value, target) =>
        {
            backgrounds[value.Value - 1] = target.Tree.Background;
            target.SetValueOn(value.Coordinate, value.Value * 10 + target.GetValue(value.Coordinate));
        }, options: new() { GrainSize = 1, MaximumDegreeOfParallelism = 4 }, merge: policy);
        Assert.Equal(9, backgrounds[0]); Assert.All(backgrounds.Skip(1), value => Assert.Equal(0, value));
        Assert.Equal(9, output.Background); Assert.Equal(77, output.GetValue(sentinel)); Assert.Equal(22UL, output.ActiveVoxelCount);
        for (int i = 0; i < 21; i++)
        {
            Assert.Equal((i + 1) * 10 + (i == 0 ? 9 : 0), output.GetValue(new(i * 8, 0, 0)));
            Assert.Equal(i + 1, input.GetValue(new(i * 8, 0, 0)));
        }
    }
    [Theory]
    [InlineData(MergePolicy.ActiveStates)]
    [InlineData(MergePolicy.Nodes)]
    [InlineData(MergePolicy.ActiveStatesAndNodes)]
    public void CollidingWorkersRespectExistingLeafAndActiveValuePrecedence(MergePolicy policy)
    {
        var input = new VdbTree<int>(); for (int i = 0; i < 6; i++) input.SetValueOn(new(i, 0, 0), i);
        var output = new VdbTree<int>(); output.SetValueOn(Coord.Zero, 91);
        ValueTransformer.TransformValues(input, output, (value, target) =>
        {
            target.SetValueOn(Coord.Zero, 100 + value.Value);
            target.SetValueOn(new(value.Value + 1, 0, 0), 10 + value.Value);
        }, options: new() { GrainSize = 1, MaximumDegreeOfParallelism = 4 }, merge: policy);
        Assert.Equal(100, output.GetValue(Coord.Zero));
        Assert.Equal(policy == MergePolicy.Nodes ? 2UL : 7UL, output.ActiveVoxelCount);
        if (policy != MergePolicy.Nodes) for (int i = 0; i < 6; i++) Assert.Equal(10 + i, output.GetValue(new(i + 1, 0, 0)));
    }
    [Fact]
    public void ParallelForEachUpdatesExistingLeafValuesWithoutChangingActivity()
    {
        var tree = new VdbTree<int>(); tree.DenseFill(new(new(-8), new(7)), 4, true); tree.SetValueOff(new(-1), 9);
        ValueTransformer.ForEach(tree.EnumerateValueHandles(ValueIterationMode.All).Where(value => value.IsVoxel),
            value => value.SetValue(value.Value + 2), new() { GrainSize = 31, MaximumDegreeOfParallelism = 4 });
        Assert.Equal(4095UL, tree.ActiveVoxelCount); Assert.Equal(6, tree.GetValue(Coord.Zero)); Assert.Equal(11, tree.GetValue(new(-1))); Assert.False(tree.IsValueOn(new(-1)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StructuralInputMutationIsDetected(bool threaded)
    {
        var input = new VdbTree<int>(); input.SetValueOn(Coord.Zero, 1); var output = new VdbTree<int>();
        var error = Record.Exception(() => ValueTransformer.TransformValues(input, output, (_, _) => input.AddTile(3, new(4096), 1, true), options: new() { Threaded = threaded }));
        if (threaded) Assert.IsType<InvalidOperationException>(Assert.IsType<AggregateException>(error).InnerException);
        else Assert.IsType<InvalidOperationException>(error);
    }
    [Fact]
    public void SerialFailureRetainsCompletedWritesWithoutRetryingCallback()
    {
        var input = new VdbTree<int>(); for (int i = 0; i < 5; i++) input.SetValueOn(new(i, 0, 0), i);
        var output = new VdbTree<int>(); int calls = 0;
        Assert.Throws<ArithmeticException>(() => ValueTransformer.TransformValues(input, output, (value, target) =>
        {
            calls++; if (value.Value == 2) throw new ArithmeticException(); target.SetValueOn(value.Coordinate, value.Value);
        }, options: new() { Threaded = false }));
        Assert.Equal(3, calls); Assert.Equal(2UL, output.ActiveVoxelCount);
    }
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    public void ItemBudgetFailsAtTheDocumentedMaterializationBoundary(bool threaded, int expectedCalls)
    {
        int calls = 0;
        Assert.Throws<InvalidOperationException>(() => ValueTransformer.ForEach(Enumerable.Range(0, 5), _ => calls++, new() { Threaded = threaded, MaximumItems = 2 }));
        Assert.Equal(expectedCalls, calls);
    }
    [Fact]
    public void CancellationIsObservedBeforeAndDuringCallbacks()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); int calls = 0;
        Assert.Throws<OperationCanceledException>(() => ValueTransformer.ForEach([1, 2, 3], _ => calls++, new() { CancellationToken = cancellation.Token }));
        Assert.Equal(0, calls);
        using var midstream = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => ValueTransformer.ForEach([1, 2, 3], _ => { calls++; midstream.Cancel(); }, new() { Threaded = false, CancellationToken = midstream.Token }));
        Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InvalidExecutionOptionsFailBeforeCallbacks(int kind)
    {
        var options = kind switch
        {
            0 => new ValueTransformOptions { GrainSize = 0 },
            1 => new ValueTransformOptions { MaximumItems = -1 },
            2 => new ValueTransformOptions { MaximumDegreeOfParallelism = 0 },
            _ => new ValueTransformOptions { MaximumDegreeOfParallelism = -2 }
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => ValueTransformer.ForEach([1], _ => throw new Exception(), options));
    }
    [Fact]
    public void AliasedTreesAndUncopyableOperationsFailExplicitly()
    {
        var tree = new VdbTree<int>();
        Assert.Throws<ArgumentException>(() => ValueTransformer.TransformValues(tree, tree, (_, _) => { }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValueTransformer.TransformValues(tree, new VdbTree<int>(), (_, _) => { }, mode: (ValueIterationMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => ValueTransformer.TransformValues(tree, new VdbTree<int>(), (_, _) => { }, merge: (MergePolicy)99));
        Assert.Throws<ArgumentException>(() => ValueTransformer.ForEach([1], new Counter(1), static (int _, ref Counter value) => { }, new() { ShareOperation = false }));
        Assert.Throws<ArgumentException>(() => ValueTransformer.Accumulate([1], new Counter(1), static (int _, ref Counter value) => { }, static (ref Counter value, Counter other) => { }));
    }
    [Fact]
    public void ModifierRetainsWholeLexicographicVectorsAndUnorderedComponents()
    {
        var tree = new VdbTree<Vec3d>(); tree.SetValueOn(Coord.Zero, new(1, 9, -4));
        ValueTransformer.SetValueOnMin(tree, Coord.Zero, new Vec3d(1, 2, 500)); Assert.Equal(new Vec3d(1, 2, 500), tree.GetValue(Coord.Zero));
        ValueTransformer.SetValueOnMax(tree, Coord.Zero, new Vec3d(double.NaN, 100, 100)); Assert.Equal(new Vec3d(1, 2, 500), tree.GetValue(Coord.Zero));
        tree.SetValueOn(Coord.Zero, new(1, double.NaN, 5));
        ValueTransformer.SetValueOnMin(tree, Coord.Zero, new Vec3d(1, -100, -100));
        var result = tree.GetValue(Coord.Zero); Assert.Equal(1, result.X); Assert.True(double.IsNaN(result.Y)); Assert.Equal(5, result.Z);
    }
    [Fact]
    public void BooleanFalseIsActivatedAndInt64NeverRoundTripsThroughDouble()
    {
        var boolean = new VdbTree<bool>(); ValueTransformer.SetValueOnSum(boolean, Coord.Zero, false);
        Assert.True(boolean.IsValueOn(Coord.Zero)); Assert.False(boolean.GetValue(Coord.Zero));
        var integers = new VdbTree<long>(9007199254740993L); ValueTransformer.SetValueOnSum(integers, Coord.Zero, 2);
        Assert.Equal(9007199254740995L, integers.GetValue(Coord.Zero));
    }
    [Fact]
    public void UnchangedActiveTileModifiersDoNotMaterializeLeaves()
    {
        var tree = new VdbTree<int>(); tree.AddTile(3, Coord.Zero, 7, true); var accessor = tree.GetAccessor();
        ValueTransformer.SetValueOnMin(tree, Coord.Zero, 9); ValueTransformer.SetValueOnMax(accessor, Coord.Zero, 5);
        Assert.Equal(0, tree.LeafCount); Assert.Equal(0, tree.GetValueDepth(Coord.Zero)); Assert.Equal(1UL << 36, tree.ActiveVoxelCount);
    }
    [Fact]
    public void WarmArithmeticModifiersAllocateNoManagedMemory()
    {
        var tree = new VdbTree<long>(); var accessor = tree.GetAccessor(); accessor.SetValueOn(Coord.Zero, 1);
        for (int i = 0; i < 20_000; i++) { ValueTransformer.SetValueOnSum(accessor, Coord.Zero, 1); ValueTransformer.SetValueOnMin(tree, Coord.Zero, 1); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) { ValueTransformer.SetValueOnSum(accessor, Coord.Zero, 1); ValueTransformer.SetValueOnMin(tree, Coord.Zero, 1); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.Equal(0, allocated); Assert.Equal(1, tree.GetValue(Coord.Zero));
    }
}
