// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of tools/Dense.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

public enum MemoryLayout { XYZ, ZYX }

/// <summary>A bounded dense grid with explicit strides. ZYX makes Z the contiguous axis.</summary>
public sealed class Dense<T> where T : unmanaged
{
    private readonly Memory<T> _data;
    public Dense(CoordBBox bounds, T value = default, MemoryLayout layout = MemoryLayout.ZYX) : this(bounds, layout)
    {
        _data = GC.AllocateUninitializedArray<T>(Count); _data.Span.Fill(value);
    }
    /// <summary>Wraps caller-owned memory without copying.</summary>
    public Dense(CoordBBox bounds, Memory<T> data, MemoryLayout layout = MemoryLayout.ZYX) : this(bounds, layout)
    {
        if (data.Length != Count) throw new ArgumentException("Memory length must equal the voxel count.", nameof(data));
        _data = data;
    }
    private Dense(CoordBBox bounds, MemoryLayout layout)
    {
        if (bounds.IsEmpty) throw new ArgumentException("Dense bounds must be nonempty.", nameof(bounds));
        if (!Enum.IsDefined(layout)) throw new ArgumentOutOfRangeException(nameof(layout));
        if (bounds.Volume128 > (UInt128)Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(bounds), "Dense volume exceeds the managed array limit.");
        Bounds = bounds; Layout = layout; Count = (int)bounds.Volume128;
        var d = bounds.Dimensions; Dimensions = new((int)d.X, (int)d.Y, (int)d.Z);
        XStride = layout == MemoryLayout.XYZ ? 1 : checked((int)(d.Y * d.Z));
        YStride = layout == MemoryLayout.XYZ ? (int)d.X : (int)d.Z;
        ZStride = layout == MemoryLayout.XYZ ? checked((int)(d.X * d.Y)) : 1;
    }
    public CoordBBox Bounds { get; }
    public Coord Origin => Bounds.Min;
    public Coord Dimensions { get; }
    public MemoryLayout Layout { get; }
    public int Count { get; }
    public int XStride { get; }
    public int YStride { get; }
    public int ZStride { get; }
    public Span<T> Data => _data.Span;
    public T this[Coord coordinate] { get => _data.Span[CoordToOffset(coordinate)]; set => _data.Span[CoordToOffset(coordinate)] = value; }
    public T this[int offset] { get => _data.Span[offset]; set => _data.Span[offset] = value; }
    public int CoordToOffset(Coord coordinate)
    {
        if (!Bounds.Contains(coordinate)) throw new ArgumentOutOfRangeException(nameof(coordinate));
        return (coordinate.X - Origin.X) * XStride + (coordinate.Y - Origin.Y) * YStride + (coordinate.Z - Origin.Z) * ZStride;
    }
    public int LocalCoordToOffset(Coord coordinate)
    {
        if ((uint)coordinate.X >= (uint)Dimensions.X || (uint)coordinate.Y >= (uint)Dimensions.Y || (uint)coordinate.Z >= (uint)Dimensions.Z) throw new ArgumentOutOfRangeException(nameof(coordinate));
        return coordinate.X * XStride + coordinate.Y * YStride + coordinate.Z * ZStride;
    }
    public Coord OffsetToLocalCoord(int offset)
    {
        if ((uint)offset >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(offset));
        if (Layout == MemoryLayout.ZYX) { int x = offset / XStride; offset -= x * XStride; return new(x, offset / YStride, offset % YStride); }
        int z = offset / ZStride; offset -= z * ZStride; return new(offset % YStride, offset / YStride, z);
    }
    public Coord OffsetToCoord(int offset) => Origin + OffsetToLocalCoord(offset);
    public void Fill(T value) => _data.Span.Fill(value);
    public Dense<T> DeepCopy() => new(Bounds, _data.ToArray().AsMemory(), Layout);
}

public static class DenseConversion
{
    public static void CopyToDense<T>(VdbTree<T> sparse, Dense<T> dense) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(sparse); ArgumentNullException.ThrowIfNull(dense);
        var accessor = sparse.GetAccessor(); var output = dense.Data;
        for (int i = 0; i < output.Length; i++) output[i] = accessor.GetValue(dense.OffsetToCoord(i));
    }
    /// <summary>Imports bounded dense values, snapping values near background to inactive background.</summary>
    /// <remarks>As in upstream, a wholly inactive uniform block is skipped, leaving an existing target block intact.</remarks>
    public static void CopyFromDense<T>(Dense<T> dense, VdbTree<T> sparse, T tolerance = default) where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(sparse); ArgumentNullException.ThrowIfNull(dense);
        if (!T.IsFinite(tolerance) || tolerance < T.Zero) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var blocks = new List<(LeafNode<T> Leaf, bool Constant, T Value, bool Active)>();
        CoordBBox bounds = dense.Bounds; Coord start = VdbTree<T>.Align(bounds.Min, 3);
        bool inherit = !sparse.IsEmpty;
        for (long x = start.X; x <= bounds.Max.X; x += 8) for (long y = start.Y; y <= bounds.Max.Y; y += 8) for (long z = start.Z; z <= bounds.Max.Z; z += 8)
        {
            Coord origin = new((int)x, (int)y, (int)z);
            LeafNode<T> leaf;
            if (!inherit) leaf = new(origin, sparse.Background, false);
            else if (sparse.ProbeLeaf(origin) is { } previous) leaf = previous.DeepCopy();
            else { bool on = sparse.ProbeValue(origin, out T value); leaf = new(origin, value, on); }
            foreach (Coord c in bounds.Intersection(leaf.Bounds))
            {
                T value = dense[c];
                if (ApproximatelyEqual(value, sparse.Background, tolerance)) leaf.SetValueOff(c, sparse.Background);
                else leaf.SetValueOn(c, value);
            }
            bool constant = leaf.IsConstant(out T first, out bool active, tolerance);
            blocks.Add((leaf, constant, first, active));
        }
        foreach (var block in blocks)
        {
            if (!block.Constant) sparse.AddLeaf(block.Leaf);
            else if (block.Active) sparse.AddTile(1, block.Leaf.Origin, block.Value, true);
        }
        Prune.PruneTiles(sparse, tolerance);
    }
    private static bool ApproximatelyEqual<T>(T a, T b, T tolerance) where T : unmanaged, INumber<T>
    {
        // Native comparisons consider NaN approximately equal; finite arithmetic avoids signed-overflow UB.
        if (T.IsNaN(a) || T.IsNaN(b)) return true;
        if (a == b) return true;
        if (typeof(T) == typeof(Half)) return MathF.Abs(float.CreateChecked(a) - float.CreateChecked(b)) <= float.CreateChecked(tolerance);
        try { return checked(a > b ? a - b : b - a) <= tolerance; } catch (OverflowException) { return false; }
    }
}
