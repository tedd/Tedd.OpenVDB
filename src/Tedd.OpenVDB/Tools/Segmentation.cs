// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/LevelSetUtil.h component and narrow-band segmentation.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;

namespace Tedd.OpenVDB.Tools;

/// <summary>Face-connected active topology and signed-distance segmentation.</summary>
public static class Segmentation
{
    /// <summary>Extracts six-connected active components, ordered by decreasing active voxel count.</summary>
    /// <remarks>Mask values remain false; connectivity is encoded by activity. Active tiles are voxelized as in OpenVDB. The allocation limit is checked before expansion. Equal-size components have deterministic coordinate order.</remarks>
    public static IReadOnlyList<VdbTree<bool>> ExtractActiveVoxelSegmentMasks<T>(VdbTree<T> tree, ulong maximumNewLeaves = ulong.MaxValue) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree);
        var topology = new VdbTree<bool>();
        topology.TopologyUnion(tree);
        Prune.PruneInactive(topology);
        topology.VoxelizeActiveTiles(maximumNewLeaves);
        var nodes = topology.EnumerateLeaves().OrderBy(node => node.Origin).ToArray();
        var leafLabels = new Dictionary<Coord, int[]>();
        var components = new List<Component>();
        var stack = new Stack<int>(LeafNode<bool>.Size);
        foreach (var leaf in nodes)
        {
            int[] labels = new int[LeafNode<bool>.Size];
            Array.Fill(labels, -1);
            leafLabels.Add(leaf.Origin, labels);
            foreach (int seed in leaf.ActiveOffsets)
            {
                if (labels[seed] >= 0) continue;
                int label = components.Count;
                var component = new Component(leaf.Origin, label);
                components.Add(component);
                labels[seed] = label;
                stack.Push(seed);
                while (stack.TryPop(out int offset))
                {
                    component.Mask.SetOn(offset);
                    int x = offset >> 6, y = (offset >> 3) & 7, z = offset & 7;
                    if (z > 0) Visit(offset - 1);
                    if (z < 7) Visit(offset + 1);
                    if (y > 0) Visit(offset - 8);
                    if (y < 7) Visit(offset + 8);
                    if (x > 0) Visit(offset - 64);
                    if (x < 7) Visit(offset + 64);
                }
                void Visit(int offset)
                {
                    if (labels[offset] >= 0 || !leaf.IsValueOn(offset)) return;
                    labels[offset] = label;
                    stack.Push(offset);
                }
            }
        }
        // Only positive boundary faces need comparison; each connection is undirected.
        foreach (var leaf in nodes)
        {
            int[] labels = leafLabels[leaf.Origin];
            foreach (int offset in leaf.ActiveOffsets)
            {
                Coord coordinate = leaf.OffsetToGlobalCoord(offset);
                for (int direction = 0; direction <= 4; direction += 2)
                {
                    int local = direction == 0 ? offset & 7 : direction == 2 ? (offset >> 3) & 7 : offset >> 6;
                    if (local != 7 || !LevelSetUtil.TryNeighbor(coordinate, direction, out Coord neighbor)) continue;
                    if (!leafLabels.TryGetValue(VdbTree<bool>.Align(neighbor, 3), out int[]? neighborLabels)) continue;
                    int other = neighborLabels[LeafNode<bool>.CoordToOffset(neighbor)];
                    if (other >= 0) Union(labels[offset], other);
                }
            }
        }
        var groups = new Dictionary<int, VdbTree<bool>>();
        foreach (var component in components)
        {
            int id = Find(component.Id);
            if (!groups.TryGetValue(id, out var mask)) groups.Add(id, mask = new());
            var leaf = mask.TouchLeaf(component.Origin);
            foreach (int offset in component.Mask.OnIndices) leaf.SetValueOn(offset);
        }
        return groups.OrderByDescending(pair => pair.Value.ActiveVoxelCount).ThenBy(pair => pair.Key).Select(pair => pair.Value).ToArray();

        int Find(int id)
        {
            int root = id;
            while (components[root].Parent != root) root = components[root].Parent;
            while (id != root) { int next = components[id].Parent; components[id].Parent = root; id = next; }
            return root;
        }
        void Union(int a, int b)
        {
            a = Find(a); b = Find(b);
            if (a == b) return;
            if (a < b) components[b].Parent = a; else components[a].Parent = b;
        }
    }

    public static IReadOnlyList<Grid<bool>> ExtractActiveVoxelSegmentMasks<T>(Grid<T> grid, ulong maximumNewLeaves = ulong.MaxValue) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(grid);
        return ExtractActiveVoxelSegmentMasks(grid.Tree, maximumNewLeaves).Select(tree => new Grid<bool>(tree) { Transform = grid.Transform.Copy() }).ToArray();
    }

    /// <summary>Copies active components into independent trees, with native empty and single-component behavior.</summary>
    /// <remarks>An empty input produces one empty tree. A single component preserves all inactive input data; multiple components copy only masked active values. Active tiles are restored at their original depth.</remarks>
    public static IReadOnlyList<VdbTree<T>> SegmentActiveVoxels<T>(VdbTree<T> tree, ulong maximumNewLeaves = ulong.MaxValue) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree);
        var masks = ExtractActiveVoxelSegmentMasks(tree, maximumNewLeaves);
        if (masks.Count == 0) return [new(tree.Background)];
        if (masks.Count == 1) return [tree.DeepCopy()];
        return masks.Select(mask => MaskedCopy(tree, mask)).ToArray();
    }

    public static IReadOnlyList<Grid<T>> SegmentActiveVoxels<T>(Grid<T> grid, ulong maximumNewLeaves = ulong.MaxValue) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(grid);
        return SegmentActiveVoxels(grid.Tree, maximumNewLeaves).Select(tree => Wrap(grid, tree)).ToArray();
    }

    /// <summary>Segments zero crossings, expands each component along increasing absolute distance, and restores its interior sign.</summary>
    /// <remarks>Expansion follows active leaf voxels and six-connected strictly increasing distances. Active tiles are not narrow-band candidates. Interior and exterior band widths may differ.</remarks>
    public static IReadOnlyList<VdbTree<T>> SegmentSdf<T>(VdbTree<T> tree) where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(tree);
        var masks = ExtractActiveVoxelSegmentMasks(LevelSetUtil.ExtractIsosurfaceMask(tree));
        if (masks.Count == 0) return [new(tree.Background)];
        T inside = -T.Abs(LevelSetUtil.FindMinimumSdf(tree)), outside = T.Abs(tree.Background);
        var result = new List<VdbTree<T>>(masks.Count);
        foreach (var mask in masks)
        {
            ExpandNarrowBand(tree, mask);
            var segment = MaskedCopy(tree, mask);
            SignedFloodFill.WithValues(segment, outside, inside);
            result.Add(segment);
        }
        return result;
    }

    public static IReadOnlyList<Grid<T>> SegmentSdf<T>(Grid<T> grid) where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(grid);
        return SegmentSdf(grid.Tree).Select(tree => Wrap(grid, tree)).ToArray();
    }

    private static void ExpandNarrowBand<T>(VdbTree<T> source, VdbTree<bool> mask) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var queue = new Queue<Coord>();
        foreach (var leaf in mask.EnumerateLeaves())
            foreach (int offset in leaf.ActiveOffsets) queue.Enqueue(leaf.OffsetToGlobalCoord(offset));
        var sourceAccessor = source.GetAccessor();
        var maskAccessor = mask.GetAccessor();
        while (queue.TryDequeue(out Coord coordinate))
        {
            T distance = T.Abs(sourceAccessor.GetValue(coordinate));
            for (int direction = 0; direction < 6; ++direction)
            {
                if (!LevelSetUtil.TryNeighbor(coordinate, direction, out Coord neighbor) || maskAccessor.IsValueOn(neighbor)) continue;
                var leaf = source.ProbeLeaf(neighbor);
                if (leaf is null || !leaf.IsValueOn(neighbor) || !(T.Abs(leaf.GetValue(neighbor)) > distance)) continue;
                maskAccessor.SetValueOn(neighbor, false);
                queue.Enqueue(neighbor);
            }
        }
    }

    private static VdbTree<T> MaskedCopy<T>(VdbTree<T> source, VdbTree<bool> mask) where T : unmanaged
    {
        var result = new VdbTree<T>(source.Background);
        foreach (var maskLeaf in mask.EnumerateLeaves())
        {
            if (maskLeaf.IsEmpty) continue;
            var inputLeaf = source.ProbeLeaf(maskLeaf.Origin);
            if (inputLeaf is not null)
            {
                var outputLeaf = result.TouchLeaf(maskLeaf.Origin);
                foreach (int offset in maskLeaf.ActiveOffsets) outputLeaf.SetValueOn(offset, inputLeaf.GetValue(offset));
            }
            else
            {
                int depth = source.GetValueDepth(maskLeaf.Origin);
                if (depth >= 0) result.AddTile(3 - depth, maskLeaf.Origin, source.GetValue(maskLeaf.Origin), true);
            }
        }
        return result;
    }

    private static Grid<T> Wrap<T>(Grid<T> source, VdbTree<T> tree) where T : unmanaged
    {
        var result = new Grid<T>(tree) { Transform = source.Transform.Copy() };
        foreach (var pair in source.Metadata) result.SetMetadata(pair.Key, pair.Value);
        return result;
    }

    private sealed class Component(Coord origin, int id)
    {
        internal readonly Coord Origin = origin;
        internal readonly int Id = id;
        internal int Parent = id;
        internal readonly NodeMask Mask = new(3);
    }
}
