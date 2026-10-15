// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tree/LeafNode.h with the standard Log2Dim=3 configuration.

using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Util;

namespace Tedd.OpenVDB.Tree;

/// <summary>An OpenVDB leaf containing 8 x 8 x 8 independently valued and active voxels.</summary>
/// <remarks>
/// Coordinate access follows upstream local addressing: only the low three bits of each
/// coordinate select the voxel. The enclosing tree routes coordinates to the correct leaf.
/// Instances are mutable and are not thread-safe.
/// </remarks>
public sealed partial class LeafNode<T> where T : unmanaged
{
    public const int Log2Dim = 3;
    public const int Dim = 1 << Log2Dim;
    public const int Size = 1 << (3 * Log2Dim);
    public const int Level = 0;
    public const int Total = Log2Dim;
    public const int NumValues = Size;
    public const int NumVoxels = Size;

    private T[] _values;
    private readonly NodeMask _valueMask;
    // NodeMask never replaces its backing words; both references observe all edits.
    private readonly ulong[] _activityWords;

    public LeafNode(Coord origin, T value = default, bool active = false)
    {
        Origin = new Coord(origin.X & -Dim, origin.Y & -Dim, origin.Z & -Dim);
        _values = GC.AllocateUninitializedArray<T>(Size);
        _values.AsSpan().Fill(value); // Initialize every byte-bearing value before exposing the buffer.
        _valueMask = new NodeMask(Log2Dim, active);
        _activityWords = _valueMask.Storage;
    }

    private LeafNode(LeafNode<T> other)
    {
        Origin = other.Origin;
        _values = (T[])other._values.Clone();
        _valueMask = other._valueMask.DeepCopy();
        _activityWords = _valueMask.Storage;
    }

    public Coord Origin { get; }
    public CoordBBox Bounds => new(Origin, new Coord(Origin.X + Dim - 1, Origin.Y + Dim - 1, Origin.Z + Dim - 1));
    public CoordBBox BoundingBox => Bounds;
    public int OnVoxelCount => _valueMask.CountOn();
    public int OffVoxelCount => Size - OnVoxelCount;
    public bool IsEmpty => _valueMask.IsOff();
    public bool IsDense => _valueMask.IsOn();
    public ReadOnlySpan<T> Values => _values;

    /// <summary>Returns an independent snapshot of the activity mask.</summary>
    /// <remarks>Use SetActiveState to mutate this leaf; modifying the snapshot does not alter the leaf.</remarks>
    public NodeMask ValueMask => _valueMask.DeepCopy();

    /// <summary>Enumerates active offsets without allocating a mask snapshot.</summary>
    public NodeMask.IndexEnumerable ActiveOffsets => _valueMask.OnIndices;

    public LeafNode<T> DeepCopy() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CoordToOffset(Coord coordinate) =>
        ((coordinate.X & (Dim - 1)) << (2 * Log2Dim)) |
        ((coordinate.Y & (Dim - 1)) << Log2Dim) |
        (coordinate.Z & (Dim - 1));

    public static Coord OffsetToLocalCoord(int offset)
    {
        ValidateOffset(offset);
        return new Coord(offset >> (2 * Log2Dim), (offset >> Log2Dim) & (Dim - 1), offset & (Dim - 1));
    }

    public Coord OffsetToGlobalCoord(int offset)
    {
        var local = OffsetToLocalCoord(offset);
        return new Coord(Origin.X + local.X, Origin.Y + local.Y, Origin.Z + local.Z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetValue(Coord coordinate) => _values[CoordToOffset(coordinate)];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetValue(int offset)
    {
        ValidateOffset(offset);
        return _values[offset];
    }

    public bool IsValueOn(Coord coordinate) => _valueMask.IsOn(CoordToOffset(coordinate));
    public bool IsValueOn(int offset) => _valueMask.IsOn(offset);
    public bool IsValueOff(Coord coordinate) => !IsValueOn(coordinate);
    public bool IsValueOff(int offset) => !IsValueOn(offset);

    public bool ProbeValue(Coord coordinate, out T value) => ProbeValue(CoordToOffset(coordinate), out value);

    public bool ProbeValue(int offset, out T value)
    {
        ValidateOffset(offset);
        value = _values[offset];
        return _valueMask.IsOn(offset);
    }

    public void SetValueOn(Coord coordinate) => _valueMask.SetOn(CoordToOffset(coordinate));
    public void SetValueOn(int offset) => _valueMask.SetOn(offset);
    public void SetValueOff(Coord coordinate) => _valueMask.SetOff(CoordToOffset(coordinate));
    public void SetValueOff(int offset) => _valueMask.SetOff(offset);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValueOn(Coord coordinate, T value)
    {
        int offset = CoordToOffset(coordinate);
        _values[offset] = value;
        ref ulong word = ref _activityWords[offset >> 6];
        if (word != ulong.MaxValue) word |= 1UL << (offset & 63);
    }

    public void SetValueOn(int offset, T value)
    {
        ValidateOffset(offset);
        _values[offset] = value;
        _valueMask.SetOn(offset);
    }

    public void SetValueOff(Coord coordinate, T value) => SetValueOff(CoordToOffset(coordinate), value);

    public void SetValueOff(int offset, T value)
    {
        ValidateOffset(offset);
        _values[offset] = value;
        _valueMask.SetOff(offset);
    }

    public void SetValueOnly(Coord coordinate, T value) => SetValueOnly(CoordToOffset(coordinate), value);

    public void SetValueOnly(int offset, T value)
    {
        ValidateOffset(offset);
        _values[offset] = value;
    }

    public void SetActiveState(Coord coordinate, bool active) => _valueMask.Set(CoordToOffset(coordinate), active);
    public void SetActiveState(int offset, bool active) => _valueMask.Set(offset, active);

    /// <summary>Changes stored values without changing activity.</summary>
    public void Fill(T value) => _values.AsSpan().Fill(value);

    public void Fill(T value, bool active)
    {
        _values.AsSpan().Fill(value);
        _valueMask.Fill(active);
    }

    /// <summary>Fills the inclusive intersection of the supplied box and this leaf.</summary>
    public void Fill(CoordBBox bounds, T value, bool active = true)
    {
        var clipped = Bounds.Intersection(bounds);
        if (clipped.IsEmpty) return;
        // Iterate local coordinates: global int.MaxValue is a valid upper endpoint.
        var min = new Coord(clipped.Min.X - Origin.X, clipped.Min.Y - Origin.Y, clipped.Min.Z - Origin.Z);
        var max = new Coord(clipped.Max.X - Origin.X, clipped.Max.Y - Origin.Y, clipped.Max.Z - Origin.Z);
        if (min.X == 0 && min.Y == 0 && min.Z == 0 && max.X == Dim - 1 && max.Y == Dim - 1 && max.Z == Dim - 1)
        {
            Fill(value, active);
            return;
        }
        var values = _values.AsSpan();
        var length = max.Z - min.Z + 1;
        for (var x = min.X; x <= max.X; ++x)
        {
            var xOffset = x << (2 * Log2Dim);
            for (var y = min.Y; y <= max.Y; ++y)
            {
                var offset = xOffset | (y << Log2Dim) | min.Z;
                values.Slice(offset, length).Fill(value);
                for (var end = offset + length; offset < end; ++offset) _valueMask.Set(offset, active);
            }
        }
    }

    /// <summary>Replaces values outside bounds with inactive background voxels.</summary>
    public void Clip(CoordBBox bounds, T background)
    {
        var clipped = Bounds.Intersection(bounds);
        if (clipped.IsEmpty)
        {
            Fill(background, false);
            return;
        }
        if (clipped.Min == Bounds.Min && clipped.Max == Bounds.Max) return;
        for (var offset = 0; offset < Size; ++offset)
            if (!clipped.Contains(OffsetToGlobalCoord(offset))) SetValueOff(offset, background);
    }

    /// <summary>Tests exact stored-value and activity uniformity.</summary>
    /// <remarks>NaN is never constant; this preserves NaN voxels when pruning.</remarks>
    public bool IsConstant(out T value, out bool active)
    {
        value = _values[0];
        if (!_valueMask.IsConstant(out active)) return false;
        var values = _values;
        for (var offset = 1; offset < values.Length; ++offset)
            if (!ValueEquality<T>.Equals(values[offset], value)) return false;
        return true;
    }

    public void CopyValuesTo(Span<T> destination) => _values.AsSpan().CopyTo(destination);

    /// <summary>Replaces all values and initializes all activity bits to the supplied state.</summary>
    public void SetValues(ReadOnlySpan<T> values, bool active)
    {
        if (values.Length != Size) throw new ArgumentException($"Exactly {Size} values are required.", nameof(values));
        values.CopyTo(_values);
        _valueMask.Fill(active);
    }

    public bool HasSameTopology<TOther>(LeafNode<TOther> other) where TOther : unmanaged
    {
        ArgumentNullException.ThrowIfNull(other);
        return _valueMask.Equals(other._valueMask);
    }

    /// <summary>Unions activity while retaining this leaf's values.</summary>
    public void TopologyUnion<TOther>(LeafNode<TOther> other) where TOther : unmanaged
    {
        ArgumentNullException.ThrowIfNull(other);
        _valueMask.OrWith(other._valueMask);
    }

    /// <summary>Intersects activity while retaining this leaf's values.</summary>
    public void TopologyIntersection<TOther>(LeafNode<TOther> other) where TOther : unmanaged
    {
        ArgumentNullException.ThrowIfNull(other);
        _valueMask.AndWith(other._valueMask);
    }

    /// <summary>Subtracts activity while retaining this leaf's values.</summary>
    public void TopologyDifference<TOther>(LeafNode<TOther> other) where TOther : unmanaged
    {
        ArgumentNullException.ThrowIfNull(other);
        _valueMask.ExceptWith(other._valueMask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ValidateOffset(int offset)
    {
        if ((uint)offset >= Size) throw new ArgumentOutOfRangeException(nameof(offset));
    }
}

/// <summary>Numeric leaf uniformity overloads corresponding to OpenVDB's tolerance-based comparisons.</summary>
public static class NumericLeafOperations
{
    /// <summary>Tests whether every value lies within tolerance of the first value and all activity is uniform.</summary>
    /// <remarks>Tolerance must be finite and nonnegative. NaN values never compare uniform.</remarks>
    public static bool IsConstant<T>(this LeafNode<T> leaf, out T value, out bool active, T tolerance)
        where T : unmanaged, System.Numerics.INumber<T>
    {
        ArgumentNullException.ThrowIfNull(leaf);
        ValidateTolerance(tolerance);
        var values = leaf.Values;
        value = values[0];
        active = leaf.IsDense;
        if (!active && !leaf.IsEmpty) return false;
        if (T.IsNaN(value)) return false;
        for (var index = 1; index < values.Length; ++index)
            if (!WithinTolerance(values[index], value, tolerance)) return false;
        return true;
    }

    /// <summary>Tests whether maximum minus minimum is at most tolerance and all activity is uniform.</summary>
    /// <remarks>This range test is stricter than requiring every value to lie within tolerance of the first value.</remarks>
    public static bool IsConstant<T>(this LeafNode<T> leaf, out T minimum, out T maximum, out bool active, T tolerance = default)
        where T : unmanaged, System.Numerics.INumber<T>
    {
        ArgumentNullException.ThrowIfNull(leaf);
        ValidateTolerance(tolerance);
        var values = leaf.Values;
        minimum = maximum = values[0];
        active = leaf.IsDense;
        if (!active && !leaf.IsEmpty) return false;
        if (T.IsNaN(minimum)) return false;
        for (var index = 1; index < values.Length; ++index)
        {
            var value = values[index];
            if (T.IsNaN(value)) return false;
            if (value < minimum) minimum = value;
            if (value > maximum) maximum = value;
            if (!WithinTolerance(minimum, maximum, tolerance)) return false;
        }
        return true;
    }

    private static void ValidateTolerance<T>(T tolerance) where T : System.Numerics.INumber<T>
    {
        if (!T.IsFinite(tolerance) || tolerance < T.Zero)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance must be finite and nonnegative.");
    }

    private static bool WithinTolerance<T>(T left, T right, T tolerance) where T : unmanaged, System.Numerics.INumber<T>
    {
        if (ValueEquality<T>.Equals(left, right)) return true;
        if (T.IsNaN(left) || T.IsNaN(right)) return false;
        // C++ half subtraction promotes both operands to float.
        if (typeof(T) == typeof(Half)) return MathF.Abs(float.CreateChecked(left) - float.CreateChecked(right)) <= float.CreateChecked(tolerance);
        try
        {
            var difference = left > right ? checked(left - right) : checked(right - left);
            return difference <= tolerance;
        }
        catch (OverflowException)
        {
            // A positive mathematical difference outside T's range exceeds any representable tolerance.
            return false;
        }
    }
}
