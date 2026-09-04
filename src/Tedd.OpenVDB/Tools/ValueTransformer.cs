// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/ValueTransformer.h.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.Tools;

public delegate void ValueTransformOperation<TItem, TOperation>(TItem item, ref TOperation operation);
public delegate void ValueTransformJoin<TOperation>(ref TOperation operation, TOperation other);
public delegate void ValueTransformWriter<TInput, TOutput, TOperation>(VoxelValue<TInput> value, ValueAccessor<TOutput> output, ref TOperation operation)
    where TInput : unmanaged where TOutput : unmanaged;

/// <summary>Threading and materialization controls for native-style iterator operations.</summary>
public sealed record ValueTransformOptions
{
    public bool Threaded { get; init; } = true;
    public bool ShareOperation { get; init; } = true;
    public int GrainSize { get; init; } = 1024;
    public int MaximumDegreeOfParallelism { get; init; } = -1;
    public int MaximumItems { get; init; } = 10_000_000;
    public CancellationToken CancellationToken { get; init; }
    internal void Validate()
    {
        if (GrainSize < 1) throw new ArgumentOutOfRangeException(nameof(GrainSize));
        if (MaximumDegreeOfParallelism == 0 || MaximumDegreeOfParallelism < -1) throw new ArgumentOutOfRangeException(nameof(MaximumDegreeOfParallelism));
        if (MaximumItems < 0) throw new ArgumentOutOfRangeException(nameof(MaximumItems));
        CancellationToken.ThrowIfCancellationRequested();
    }
}

/// <summary>Iterator callbacks, output-tree transforms, reductions and allocation-free arithmetic voxel modifiers.</summary>
/// <remarks>Serial callbacks preserve native iteration order. Parallel partition and join order is a managed scheduling choice.
/// Callback mutation must stay within the current value or supplied output accessor; structural changes to input invalidate traversal.
/// Threaded callbacks must synchronize shared operation state and any activity-mask writes within a shared leaf.
/// Parallel output workers start with zero background except the root worker, which writes the supplied output tree, as native.
/// Failures can retain completed writes. No callbacks are retried.</remarks>
public static class ValueTransformer
{
    private sealed class Box<T>(T value) { internal T Value = value; }
    public static void ForEach<TItem>(IEnumerable<TItem> items, Action<TItem> operation, ValueTransformOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ForEach(items, operation, static (TItem item, ref Action<TItem> op) => op(item), (options ?? new()) with { ShareOperation = true });
    }
    /// <summary>Returns the externally visible operation. Nonshared execution copies the original operation even in serial mode.</summary>
    /// <remarks>copyOperation is required for nonshared reference-type operation objects. Value types default to native-style shallow value copies.</remarks>
    public static TOperation ForEach<TItem, TOperation>(IEnumerable<TItem> items, TOperation operation,
        ValueTransformOperation<TItem, TOperation> apply, ValueTransformOptions? options = null, Func<TOperation, TOperation>? copyOperation = null)
    {
        ArgumentNullException.ThrowIfNull(items); ArgumentNullException.ThrowIfNull(apply); options ??= new(); options.Validate();
        var root = new Box<TOperation>(options.ShareOperation ? operation : Copy(operation, copyOperation));
        if (!options.Threaded)
        {
            foreach (var item in Limited(items, options)) apply(item, ref root.Value);
        }
        else
        {
            TItem[] entries = Limited(items, options).ToArray(); int count = Partitions(entries.Length, options.GrainSize);
            var states = new Box<TOperation>[count]; if (count > 0) states[0] = root;
            for (int i = 1; i < count; i++) states[i] = options.ShareOperation ? root : new(Copy(operation, copyOperation));
            Parallel.For(0, count, ParallelOptions(options), part =>
            {
                int end = (int)System.Math.Min((long)entries.Length, (long)(part + 1) * options.GrainSize);
                var state = states[part];
                for (int i = part * options.GrainSize; i < end; i++) { options.CancellationToken.ThrowIfCancellationRequested(); apply(entries[i], ref state.Value); }
            });
        }
        return options.ShareOperation ? root.Value : operation;
    }
    /// <summary>Accumulates into the original operation; every split starts from a copy of its initial state. Serial execution never calls join.</summary>
    public static TOperation Accumulate<TItem, TOperation>(IEnumerable<TItem> items, TOperation operation,
        ValueTransformOperation<TItem, TOperation> apply, ValueTransformJoin<TOperation> join,
        ValueTransformOptions? options = null, Func<TOperation, TOperation>? copyOperation = null)
    {
        ArgumentNullException.ThrowIfNull(items); ArgumentNullException.ThrowIfNull(apply); ArgumentNullException.ThrowIfNull(join);
        options ??= new(); options.Validate(); TOperation original = Copy(operation, copyOperation); var root = new Box<TOperation>(operation);
        if (!options.Threaded) { foreach (var item in Limited(items, options)) apply(item, ref root.Value); return root.Value; }
        TItem[] entries = Limited(items, options).ToArray(); int count = Partitions(entries.Length, options.GrainSize); var states = new Box<TOperation>[count];
        if (count > 0) states[0] = root; for (int i = 1; i < count; i++) states[i] = new(Copy(original, copyOperation));
        Parallel.For(0, count, ParallelOptions(options), part =>
        {
            int end = (int)System.Math.Min((long)entries.Length, (long)(part + 1) * options.GrainSize); var state = states[part];
            for (int i = part * options.GrainSize; i < end; i++) { options.CancellationToken.ThrowIfCancellationRequested(); apply(entries[i], ref state.Value); }
        });
        for (int i = 1; i < count; i++) { options.CancellationToken.ThrowIfCancellationRequested(); join(ref root.Value, states[i].Value); }
        return root.Value;
    }
    public static void TransformValues<TInput, TOutput>(VdbTree<TInput> input, VdbTree<TOutput> output,
        Action<VoxelValue<TInput>, ValueAccessor<TOutput>> operation, ValueIterationMode mode = ValueIterationMode.Active,
        ValueTransformOptions? options = null, MergePolicy merge = MergePolicy.ActiveStates)
        where TInput : unmanaged where TOutput : unmanaged
    {
        ArgumentNullException.ThrowIfNull(operation);
        TransformValues(input, output, operation, static (VoxelValue<TInput> value, ValueAccessor<TOutput> accessor, ref Action<VoxelValue<TInput>, ValueAccessor<TOutput>> op) => op(value, accessor),
            mode, (options ?? new()) with { ShareOperation = true }, merge);
    }
    /// <summary>Writes transformed values through worker-local accessors and merges using the requested native policy.</summary>
    public static TOperation TransformValues<TInput, TOutput, TOperation>(VdbTree<TInput> input, VdbTree<TOutput> output,
        TOperation operation, ValueTransformWriter<TInput, TOutput, TOperation> apply, ValueIterationMode mode = ValueIterationMode.Active,
        ValueTransformOptions? options = null, MergePolicy merge = MergePolicy.ActiveStates, Func<TOperation, TOperation>? copyOperation = null)
        where TInput : unmanaged where TOutput : unmanaged
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(output); ArgumentNullException.ThrowIfNull(apply);
        if (ReferenceEquals(input, output)) throw new ArgumentException("Use ForEach for in-place transformation.", nameof(output));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode)); if (!Enum.IsDefined(merge)) throw new ArgumentOutOfRangeException(nameof(merge));
        options ??= new(); options.Validate(); var root = new Box<TOperation>(options.ShareOperation ? operation : Copy(operation, copyOperation));
        long version = input.TopologyVersion;
        if (!options.Threaded)
        {
            var accessor = output.GetAccessor();
            foreach (var value in Limited(input.EnumerateValues(mode), options)) { ValidateInput(); apply(value, accessor, ref root.Value); ValidateInput(); }
        }
        else
        {
            var entries = Limited(input.EnumerateValues(mode), options).ToArray(); ValidateInput(); int count = Partitions(entries.Length, options.GrainSize);
            var states = new Box<TOperation>[count]; var trees = new VdbTree<TOutput>[count];
            if (count > 0) { states[0] = root; trees[0] = output; }
            for (int i = 1; i < count; i++) { states[i] = options.ShareOperation ? root : new(Copy(operation, copyOperation)); trees[i] = new(default(TOutput)); }
            Parallel.For(0, count, ParallelOptions(options), part =>
            {
                var accessor = trees[part].GetAccessor(); var state = states[part];
                int end = (int)System.Math.Min((long)entries.Length, (long)(part + 1) * options.GrainSize);
                for (int i = part * options.GrainSize; i < end; i++) { options.CancellationToken.ThrowIfCancellationRequested(); ValidateInput(); apply(entries[i], accessor, ref state.Value); ValidateInput(); }
            });
            for (int i = 1; i < count; i++) { options.CancellationToken.ThrowIfCancellationRequested(); output.Merge(trees[i], merge); }
        }
        return options.ShareOperation ? root.Value : operation;
        void ValidateInput() { if (version != input.TopologyVersion) throw new InvalidOperationException("Input topology changed during value transformation."); }
    }
    public static void TransformValues<TInput, TOutput>(Grid<TInput> input, Grid<TOutput> output,
        Action<VoxelValue<TInput>, ValueAccessor<TOutput>> operation, ValueIterationMode mode = ValueIterationMode.Active,
        ValueTransformOptions? options = null, MergePolicy merge = MergePolicy.ActiveStates)
        where TInput : unmanaged where TOutput : unmanaged
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(output); TransformValues(input.Tree, output.Tree, operation, mode, options, merge);
    }
    public static void SetValueOnMin<T>(VdbTree<T> tree, Coord coordinate, T value) where T : unmanaged => Modify(tree, coordinate, value, 0);
    public static void SetValueOnMax<T>(VdbTree<T> tree, Coord coordinate, T value) where T : unmanaged => Modify(tree, coordinate, value, 1);
    public static void SetValueOnSum<T>(VdbTree<T> tree, Coord coordinate, T value) where T : unmanaged => Modify(tree, coordinate, value, 2);
    public static void SetValueOnMult<T>(VdbTree<T> tree, Coord coordinate, T value) where T : unmanaged => Modify(tree, coordinate, value, 3);
    public static void SetValueOnMin<T>(ValueAccessor<T> accessor, Coord coordinate, T value) where T : unmanaged => Modify(accessor, coordinate, value, 0);
    public static void SetValueOnMax<T>(ValueAccessor<T> accessor, Coord coordinate, T value) where T : unmanaged => Modify(accessor, coordinate, value, 1);
    public static void SetValueOnSum<T>(ValueAccessor<T> accessor, Coord coordinate, T value) where T : unmanaged => Modify(accessor, coordinate, value, 2);
    public static void SetValueOnMult<T>(ValueAccessor<T> accessor, Coord coordinate, T value) where T : unmanaged => Modify(accessor, coordinate, value, 3);
    private static void Modify<T>(VdbTree<T> tree, Coord coordinate, T value, int operation) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree); bool active = tree.Probe(coordinate, out var old, out var leaf, out _);
        T result = ValueModifierArithmetic<T>.Apply(old, value, operation);
        if (leaf is null && active && ValueEquality<T>.Equals(old, result)) return;
        (leaf ?? tree.TouchLeaf(coordinate)).SetValueOn(coordinate, result);
    }
    private static void Modify<T>(ValueAccessor<T> accessor, Coord coordinate, T value, int operation) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(accessor); T result = ValueModifierArithmetic<T>.Apply(accessor.GetValue(coordinate), value, operation); accessor.SetValueOn(coordinate, result);
    }
    private static T Copy<T>(T operation, Func<T, T>? copy) => copy is not null ? copy(operation) : typeof(T).IsValueType ? operation : throw new ArgumentException("Reference-type operation copies require a copyOperation delegate.", nameof(copy));
    private static int Partitions(int length, int grainSize) => (int)(((long)length + grainSize - 1) / grainSize);
    private static ParallelOptions ParallelOptions(ValueTransformOptions options) => new() { MaxDegreeOfParallelism = options.MaximumDegreeOfParallelism, CancellationToken = options.CancellationToken };
    private static IEnumerable<T> Limited<T>(IEnumerable<T> items, ValueTransformOptions options)
    {
        int count = 0; foreach (T item in items) { options.CancellationToken.ThrowIfCancellationRequested(); if (count++ >= options.MaximumItems) throw new InvalidOperationException("Value transformation item budget exceeded."); yield return item; }
    }
}