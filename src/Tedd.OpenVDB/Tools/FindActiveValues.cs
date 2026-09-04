// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/FindActiveValues.h.
using System.Numerics;
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.Tools;

public readonly record struct TileData<T>(CoordBBox Bounds, T Value, int Level, bool State = true) where T : unmanaged;

/// <summary>Queries active leaf voxels and tiles intersecting an inclusive region.</summary>
/// <remarks>Update after any tree mutation; structural changes are detected, but value-only edits are not. Cached references remain GC-tracked.
/// Independent queries are thread-safe on an unchanged tree; useAccessor requires a separate finder per worker.</remarks>
public sealed class FindActiveValues<T> where T : unmanaged
{
    private sealed class Node
    {
        internal readonly CoordBBox Bounds;
        internal readonly InternalNode<T>? Branch;
        internal readonly LeafNode<T>? Leaf;
        internal readonly Node[] Children;
        internal Node(LeafNode<T> leaf) { Bounds = leaf.Bounds; Leaf = leaf; Children = []; }
        internal Node(InternalNode<T> branch)
        {
            Bounds = VdbTree<T>.NodeBounds(branch.Origin, branch.TotalLog2Dim); Branch = branch;
            Children = branch.Children.OrderBy(p => p.Key).Select(p => p.Value is LeafNode<T> leaf ? new Node(leaf) : new Node((InternalNode<T>)p.Value)).ToArray();
        }
    }
    private VdbTree<T> _tree = null!;
    private ValueAccessor<T> _accessor = null!;
    private long _version;
    private Node[] _roots = [];
    private TileData<T>[] _rootTiles = [];
    public FindActiveValues(VdbTree<T> tree) => Update(tree);
    public void Update(VdbTree<T> tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var entries = tree.Entries.OrderBy(p => p.Key).ToArray();
        var roots = entries.Where(p => p.Value.Child is not null).Select(p => new Node(p.Value.Child!)).ToArray();
        var tiles = entries.Where(p => p.Value.Child is null && p.Value.Active).Select(p => new TileData<T>(VdbTree<T>.NodeBounds(p.Key, 12), p.Value.Value, 3)).ToArray();
        _tree = tree; _accessor = tree.GetAccessor(); _version = tree.TopologyVersion; _roots = roots; _rootTiles = tiles;
    }
    public bool AnyActiveValues(CoordBBox bounds, bool useAccessor = false)
    {
        Validate(); if (bounds.IsEmpty) return false;
        // Widen before addition to avoid the native signed-coordinate midpoint overflow.
        Coord center = new((int)(((long)bounds.Min.X + bounds.Max.X) >> 1), (int)(((long)bounds.Min.Y + bounds.Max.Y) >> 1), (int)(((long)bounds.Min.Z + bounds.Max.Z) >> 1));
        if (useAccessor ? _accessor.IsValueOn(center) : _tree.IsValueOn(center)) return true;
        foreach (var tile in _rootTiles) if (tile.Bounds.Intersects(bounds)) return true;
        foreach (var node in _roots) if (Any(node, bounds, includeVoxels: true, includeTiles: true)) return true;
        return false;
    }
    public bool NoActiveValues(CoordBBox bounds, bool useAccessor = false) => !AnyActiveValues(bounds, useAccessor);
    public bool AnyActiveVoxels(CoordBBox bounds)
    {
        Validate(); if (bounds.IsEmpty) return false;
        foreach (var node in _roots) if (Any(node, bounds, true, false)) return true;
        return false;
    }
    public bool AnyActiveTiles(CoordBBox bounds)
    {
        Validate(); if (bounds.IsEmpty) return false;
        foreach (var tile in _rootTiles) if (tile.Bounds.Intersects(bounds)) return true;
        foreach (var node in _roots) if (Any(node, bounds, false, true)) return true;
        return false;
    }
    /// <summary>Preserves the pinned native FindActiveValues::count behavior.</summary>
    /// <remarks>Native partial-leaf containment can overcount, and internal reductions narrow through Int32.
    /// Use CountActiveValues for exact region counts, equivalent to the native standalone function.</remarks>
    public ulong Count(CoordBBox bounds) => CountCore(bounds, includeTiles: true, nativeCached: true);
    /// <summary>Counts actual active voxels, including intersected virtual voxels in tiles, with checked UInt64 totals.</summary>
    public ulong CountActiveValues(CoordBBox bounds) => CountCore(bounds, includeTiles: true, nativeCached: false);
    public ulong CountActiveLeafVoxels(CoordBBox bounds) => CountCore(bounds, includeTiles: false, nativeCached: false);
    public List<TileData<T>> ActiveTiles(CoordBBox bounds)
    {
        Validate(); var result = new List<TileData<T>>(); if (bounds.IsEmpty) return result;
        foreach (var tile in _rootTiles)
        {
            var overlap = tile.Bounds.Intersection(bounds); if (!overlap.IsEmpty) result.Add(tile with { Bounds = overlap });
        }
        foreach (var node in _roots) AppendTiles(node, bounds, result);
        return result;
    }
    private void Validate()
    {
        if (_version != _tree.TopologyVersion) throw new InvalidOperationException("The tree topology changed; call Update before querying cached regions.");
    }
    private ulong CountCore(CoordBBox bounds, bool includeTiles, bool nativeCached)
    {
        Validate(); if (bounds.IsEmpty) return 0;
        ulong total = 0;
        if (includeTiles) foreach (var tile in _rootTiles)
        {
            CoordBBox overlap = tile.Bounds.Intersection(bounds); if (!overlap.IsEmpty) total = Add(total, overlap.Volume, nativeCached);
        }
        foreach (var node in _roots) total = Add(total, CountNode(node, bounds, includeTiles, nativeCached), nativeCached);
        return total;
    }
    private static bool Any(Node node, CoordBBox bounds, bool includeVoxels, bool includeTiles)
    {
        if (!node.Bounds.Intersects(bounds)) return false;
        if (node.Leaf is { } leaf)
        {
            if (!includeVoxels) return false;
            if (bounds.Contains(node.Bounds)) return !leaf.IsEmpty;
            if (leaf.IsDense) return true;
            foreach (int offset in leaf.ActiveOffsets) if (bounds.Contains(leaf.OffsetToGlobalCoord(offset))) return true;
            return false;
        }
        if (includeTiles)
        {
            var branch = node.Branch!; var region = LocalRegion(branch, bounds);
            for (int x = region.Min.X; x <= region.Max.X; x++) for (int y = region.Min.Y; y <= region.Max.Y; y++)
                if (RowBits(branch.Active.Words, (x << (2 * branch.Log2Dim)) + (y << branch.Log2Dim), region.Min.Z, region.Max.Z) != 0) return true;
        }
        foreach (var child in node.Children) if (Any(child, bounds, includeVoxels, includeTiles)) return true;
        return false;
    }
    private static ulong CountNode(Node node, CoordBBox bounds, bool includeTiles, bool nativeCached)
    {
        if (!node.Bounds.Intersects(bounds)) return 0;
        if (node.Leaf is { } leaf)
        {
            if (nativeCached ? node.Bounds.Contains(bounds) : bounds.Contains(node.Bounds)) return (ulong)leaf.OnVoxelCount;
            if (leaf.IsDense) return node.Bounds.Intersection(bounds).Volume;
            ulong count = 0; foreach (int offset in leaf.ActiveOffsets) if (bounds.Contains(leaf.OffsetToGlobalCoord(offset))) count++;
            return count;
        }
        ulong children = 0, tiles = 0;
        foreach (var child in node.Children) children = Add(children, CountNode(child, bounds, includeTiles, nativeCached), nativeCached);
        if (includeTiles)
        {
            var branch = node.Branch!;
            if (bounds.Contains(node.Bounds)) tiles = checked((ulong)branch.Active.CountOn() * (1UL << (3 * branch.ChildLog2Dim)));
            else
            {
                var region = LocalRegion(branch, bounds);
                for (int x = region.Min.X; x <= region.Max.X; x++) for (int y = region.Min.Y; y <= region.Max.Y; y++)
                {
                    int row = (x << (2 * branch.Log2Dim)) + (y << branch.Log2Dim);
                    ulong bits = RowBits(branch.Active.Words, row, region.Min.Z, region.Max.Z);
                    while (bits != 0)
                    {
                        int offset = row + region.Min.Z + BitOperations.TrailingZeroCount(bits); bits &= bits - 1;
                        tiles = Add(tiles, VdbTree<T>.NodeBounds(branch.ChildOrigin(offset), branch.ChildLog2Dim).Intersection(bounds).Volume, nativeCached);
                    }
                }
            }
        }
        // Pinned TBB reductions infer int from identity literal 0, despite UInt64 body results.
        if (nativeCached) return unchecked((ulong)(int)children + (ulong)(int)tiles);
        return checked(children + tiles);
    }
    private static void AppendTiles(Node node, CoordBBox bounds, List<TileData<T>> result)
    {
        if (node.Leaf is not null || !node.Bounds.Intersects(bounds)) return;
        foreach (var child in node.Children) AppendTiles(child, bounds, result);
        var branch = node.Branch!; var region = LocalRegion(branch, bounds);
        for (int x = region.Min.X; x <= region.Max.X; x++) for (int y = region.Min.Y; y <= region.Max.Y; y++)
        {
            int row = (x << (2 * branch.Log2Dim)) + (y << branch.Log2Dim);
            ulong bits = RowBits(branch.Active.Words, row, region.Min.Z, region.Max.Z);
            while (bits != 0)
            {
                int offset = row + region.Min.Z + BitOperations.TrailingZeroCount(bits); bits &= bits - 1;
                var overlap = VdbTree<T>.NodeBounds(branch.ChildOrigin(offset), branch.ChildLog2Dim).Intersection(bounds);
                result.Add(new(overlap, branch.Values[offset], branch.Level));
            }
        }
    }
    private static CoordBBox LocalRegion(InternalNode<T> node, CoordBBox bounds)
    {
        var region = bounds.Intersection(VdbTree<T>.NodeBounds(node.Origin, node.TotalLog2Dim)); int shift = node.ChildLog2Dim;
        return new((region.Min - node.Origin) >> shift, (region.Max - node.Origin) >> shift);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RowBits(ReadOnlySpan<ulong> words, int row, int minZ, int maxZ)
    {
        int start = row + minZ; return (words[start >> 6] >> (start & 63)) & ((1UL << (maxZ - minZ + 1)) - 1);
    }
    private static ulong Add(ulong left, ulong right, bool nativeCached) => nativeCached ? unchecked(left + right) : checked(left + right);
}

public static class FindActiveValues
{
    public static bool AnyActiveValues<T>(VdbTree<T> tree, CoordBBox bounds) where T : unmanaged => new FindActiveValues<T>(tree).AnyActiveValues(bounds);
    public static bool AnyActiveVoxels<T>(VdbTree<T> tree, CoordBBox bounds) where T : unmanaged => new FindActiveValues<T>(tree).AnyActiveVoxels(bounds);
    public static bool AnyActiveTiles<T>(VdbTree<T> tree, CoordBBox bounds) where T : unmanaged => new FindActiveValues<T>(tree).AnyActiveTiles(bounds);
    public static bool NoActiveValues<T>(VdbTree<T> tree, CoordBBox bounds) where T : unmanaged => new FindActiveValues<T>(tree).NoActiveValues(bounds);
    public static ulong CountActiveValues<T>(VdbTree<T> tree, CoordBBox bounds) where T : unmanaged => new FindActiveValues<T>(tree).CountActiveValues(bounds);
    public static List<TileData<T>> ActiveTiles<T>(VdbTree<T> tree, CoordBBox bounds) where T : unmanaged => new FindActiveValues<T>(tree).ActiveTiles(bounds);
}
