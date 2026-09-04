// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/LevelSetUtil.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tools;

/// <summary>Signed-distance interior, isosurface and fog-volume conversions.</summary>
public static class LevelSetUtil
{
    /// <summary>Returns a true-valued active mask of stored interior values, regardless of their activity.</summary>
    /// <remarks>As in OpenVDB, the isovalue is clamped below the background, and output paths are seeded by interior leaf voxels. Standalone interior tiles are not seeded.</remarks>
    public static VdbTree<bool> SdfInteriorMask<T>(VdbTree<T> tree, T isovalue = default) where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(tree);
        T limit = tree.Background - T.CreateChecked(typeof(T) == typeof(float) ? 1e-8 : typeof(T) == typeof(Half) ? 0.00097656 : 1e-15);
        if (limit < isovalue) isovalue = limit;
        var result = new VdbTree<bool>();
        foreach (var leaf in tree.EnumerateLeaves())
        {
            LeafNode<bool>? mask = null;
            for (int i = 0; i < LeafNode<T>.Size; ++i)
                if (leaf.GetValue(i) < isovalue) (mask ??= new(leaf.Origin)).SetValueOn(i, true);
            if (mask is not null) result.AddLeaf(mask);
        }
        PopulateTiles(tree, result, isovalue);
        return result;
    }

    /// <summary>Copies the input transform onto the interior mask. Input metadata is not copied.</summary>
    public static Grid<bool> SdfInteriorMask<T>(Grid<T> grid, T isovalue = default) where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(grid);
        return new(SdfInteriorMask(grid.Tree, isovalue)) { Transform = grid.Transform.Copy() };
    }

    /// <summary>Marks active leaf voxels that have a face neighbor on the other side of the isovalue.</summary>
    /// <remarks>Inactive voxels and active tiles are not candidates, but their values participate in neighboring sign comparisons.</remarks>
    public static VdbTree<bool> ExtractIsosurfaceMask<T>(VdbTree<T> tree, T isovalue = default) where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(tree);
        var result = new VdbTree<bool>();
        var accessor = tree.GetAccessor();
        foreach (var leaf in tree.EnumerateLeaves())
        {
            LeafNode<bool>? mask = null;
            foreach (int offset in leaf.ActiveOffsets)
            {
                bool under = leaf.GetValue(offset) < isovalue;
                Coord coordinate = leaf.OffsetToGlobalCoord(offset);
                for (int direction = 0; direction < 6; ++direction)
                {
                    if (!TryNeighbor(coordinate, direction, out Coord neighbor)) continue;
                    if (under == (accessor.GetValue(neighbor) < isovalue)) continue;
                    (mask ??= new(leaf.Origin)).SetValueOn(offset, true);
                    break;
                }
            }
            if (mask is not null) result.AddLeaf(mask);
        }
        return result;
    }

    public static Grid<bool> ExtractIsosurfaceMask<T>(Grid<T> grid, T isovalue = default) where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(grid);
        return new(ExtractIsosurfaceMask(grid.Tree, isovalue)) { Transform = grid.Transform.Copy() };
    }

    public static void SdfToFogVolume<T>(Grid<T> grid) where T : unmanaged, IFloatingPointIeee754<T> =>
        SdfToFogVolume(grid, T.BitDecrement(T.PositiveInfinity));

    /// <summary>Converts a signed-distance grid to fog density using the native cutoff and tile rules.</summary>
    /// <remarks>Transfers leaves into a replacement tree. Other references to the original tree observe the leaf removal. A shallow cutoff can produce leaf densities above one, matching OpenVDB.</remarks>
    public static void SdfToFogVolume<T>(Grid<T> grid, T cutoffDistance) where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(grid);
        var source = grid.Tree;
        var leaves = source.StealLeaves(source.Background, false);
        T minimum = FindMinimumSdf(source, leaves);
        cutoffDistance = -T.Abs(cutoffDistance);
        if (minimum > cutoffDistance) cutoffDistance = minimum;
        T weight = T.One / cutoffDistance;
        var result = new VdbTree<T>(T.Zero);
        foreach (var leaf in leaves)
        {
            for (int i = 0; i < LeafNode<T>.Size; ++i)
            {
                T value = leaf.GetValue(i);
                value = value > T.Zero ? T.Zero : value * weight;
                leaf.SetValueOff(i, value);
                if (value > T.Zero) leaf.SetValueOn(i);
            }
            if (!leaf.IsEmpty) result.AddLeaf(leaf);
        }
        foreach (var tile in result.EnumerateValues(ValueIterationMode.All).Where(value => !value.IsVoxel).ToArray())
            if (source.GetValue(tile.Coordinate) < T.Zero) result.AddTile(tile.Level, tile.Coordinate, T.One, true);
        foreach (var (coordinate, entry) in source.Entries)
            if (entry.Child is null && entry.Value < T.Zero) result.AddTile(3, coordinate, T.One, true);
        grid.SetTree(result);
        grid.GridClass = GridClass.FogVolume;
    }

    private static void PopulateTiles<T>(VdbTree<T> source, VdbTree<bool> result, T iso) where T : unmanaged, IFloatingPointIeee754<T>
    {
        foreach (var tile in result.EnumerateValues(ValueIterationMode.All).Where(value => !value.IsVoxel).ToArray())
            if (source.GetValue(tile.Coordinate) < iso) result.AddTile(tile.Level, tile.Coordinate, true, true);
    }

    internal static T FindMinimumSdf<T>(VdbTree<T> tree, IEnumerable<LeafNode<T>>? leaves = null) where T : unmanaged, IFloatingPointIeee754<T>
    {
        T minimum = T.BitDecrement(T.PositiveInfinity);
        foreach (var root in tree.Entries.Values)
        {
            if (root.Child is null) continue;
            foreach (var child in root.Child.Children.Values)
            {
                var lower = (InternalNode<T>)child;
                for (int i = 0; i < lower.Values.Length; ++i)
                    if (!lower.Children.ContainsKey(i) && lower.Values[i] < minimum) minimum = lower.Values[i];
            }
        }
        if (minimum > T.Zero)
            foreach (var leaf in leaves ?? tree.EnumerateLeaves())
                for (int i = 0; i < LeafNode<T>.Size; ++i)
                    if (leaf.GetValue(i) < minimum) minimum = leaf.GetValue(i);
        return minimum;
    }

    // Native ordering: +z, -z, +y, -y, +x, -x. Coordinate limits do not wrap.
    internal static bool TryNeighbor(Coord coordinate, int direction, out Coord neighbor)
    {
        long x = coordinate.X, y = coordinate.Y, z = coordinate.Z;
        switch (direction) { case 0: ++z; break; case 1: --z; break; case 2: ++y; break; case 3: --y; break; case 4: ++x; break; case 5: --x; break; default: throw new ArgumentOutOfRangeException(nameof(direction)); }
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue || z < int.MinValue || z > int.MaxValue) { neighbor = default; return false; }
        neighbor = new((int)x, (int)y, (int)z); return true;
    }
}
