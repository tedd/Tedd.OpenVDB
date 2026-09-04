// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.Tree;

public sealed partial class LeafNode<T> where T : unmanaged
{
    /// <summary>Rewrites inactive values approximately equal to either sign of the old background.</summary>
    public void ResetBackground(T oldBackground, T newBackground)
    {
        if (ValueEquality<T>.Equals(oldBackground, newBackground)) return;
        ResetBackground(oldBackground, newBackground, ValueArithmetic<T>.Negative(oldBackground), ValueArithmetic<T>.Negative(newBackground));
    }
    internal void ResetBackground(T oldBackground, T newBackground, T oldNegative, T newNegative)
    {
        if (ValueEquality<T>.Equals(oldBackground, newBackground)) return;
        var values = _values;
        foreach (int offset in _valueMask.OffIndices)
            values[offset] = ValueArithmetic<T>.Reset(values[offset], oldBackground, newBackground, oldNegative, newNegative);
    }
    public void Merge(LeafNode<T> other, MergePolicy policy = MergePolicy.ActiveStatesAndNodes)
    {
        ArgumentNullException.ThrowIfNull(other);
        VdbTree<T>.ValidateMergePolicy(policy);
        if (policy == MergePolicy.Nodes) return;
        foreach (int offset in other.ActiveOffsets)
            if (_valueMask.IsOff(offset)) SetValueOn(offset, other.GetValue(offset));
    }
    internal void MergeActiveTile(T value)
    {
        foreach (int offset in _valueMask.OffIndices) _values[offset] = value;
        _valueMask.Fill(true);
    }
    /// <summary>The lower median of all 512 values, without changing their order.</summary>
    public T Median(IComparer<T>? comparer = null)
    {
        var scratch = _values.AsSpan().ToArray();
        scratch.AsSpan().Sort(comparer ?? ValueComparer<T>.Default);
        return scratch[(Size - 1) >> 1];
    }
    public int MedianOn(ref T value, IComparer<T>? comparer = null) => MedianState(true, ref value, comparer);
    public int MedianOff(ref T value, IComparer<T>? comparer = null) => MedianState(false, ref value, comparer);
    private int MedianState(bool active, ref T value, IComparer<T>? comparer)
    {
        int count = active ? OnVoxelCount : OffVoxelCount;
        if (count == 0) return 0;
        var scratch = GC.AllocateUninitializedArray<T>(count);
        int next = 0;
        foreach (int offset in active ? _valueMask.OnIndices : _valueMask.OffIndices) scratch[next++] = _values[offset];
        scratch.AsSpan().Sort(comparer ?? ValueComparer<T>.Default);
        value = scratch[(count - 1) >> 1];
        return count;
    }
    /// <summary>Applies a value function and activates the voxel, matching modifyValue.</summary>
    public void ModifyValue(Coord coordinate, Func<T, T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        int offset = CoordToOffset(coordinate);
        SetValueOn(offset, operation(_values[offset]));
    }
    public void ModifyValueAndActiveState(Coord coordinate, Func<VoxelState<T>, VoxelState<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        int offset = CoordToOffset(coordinate);
        var result = operation(new(_values[offset], _valueMask.IsOn(offset)));
        _values[offset] = result.Value;
        _valueMask.Set(offset, result.IsActive);
    }
    internal T[] SwapBuffer(T[] buffer)
    {
        if (buffer.Length != Size) throw new ArgumentException("A leaf buffer must contain 512 values.", nameof(buffer));
        T[] previous = _values;
        _values = buffer;
        return previous;
    }
}
