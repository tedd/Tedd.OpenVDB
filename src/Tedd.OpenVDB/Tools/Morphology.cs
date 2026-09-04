// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed sparse adaptation of openvdb/tools/Morphology.h.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tools;

public enum NearestNeighbors { Face = 6, FaceEdge = 18, FaceEdgeVertex = 26 }
public enum TilePolicy { IgnoreTiles, ExpandTiles, PreserveTiles }

/// <summary>Topological dilation and erosion with OpenVDB's 6, 18, and 26-neighbor kernels.</summary>
/// <remarks>
/// PreserveTiles operates on sparse regions and refines only the changing boundaries.
/// ExpandTiles explicitly materializes every active tile as leaves, as requested by that policy.
/// Values are retained according to OpenVDB's topology operations; inactive subtrees removed by
/// PreserveTiles erosion become background. Coordinates beyond the signed grid domain are inactive.
/// </remarks>
public static class Morphology
{
    public static void DilateActiveValues<T>(VdbTree<T> tree, int iterations = 1,
        NearestNeighbors neighbors = NearestNeighbors.Face, TilePolicy mode = TilePolicy.PreserveTiles) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (iterations <= 0) return;
        var offsets = NeighborOffsets(neighbors);
        ValidatePolicy(mode);
        if (mode == TilePolicy.PreserveTiles)
        {
            var topology = new VdbTree<byte>();
            topology.TopologyUnion(tree);
            Dilate(topology, iterations, offsets, includeTiles: true);
            topology.Prune();
            tree.TopologyUnion(topology, preserveTiles: true);
            tree.Prune();
        }
        else
        {
            if (mode == TilePolicy.ExpandTiles) VoxelizeActiveTiles(tree);
            Dilate(tree, iterations, offsets, includeTiles: false);
        }
    }

    public static void ErodeActiveValues<T>(VdbTree<T> tree, int iterations = 1,
        NearestNeighbors neighbors = NearestNeighbors.Face, TilePolicy mode = TilePolicy.PreserveTiles) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (iterations <= 0) return;
        var offsets = NeighborOffsets(neighbors);
        ValidatePolicy(mode);
        if (mode == TilePolicy.PreserveTiles)
        {
            var topology = new VdbTree<byte>();
            topology.TopologyUnion(tree);
            Erode(topology, iterations, offsets, includeTiles: true);
            topology.Prune();
            tree.TopologyIntersection(topology);
        }
        else
        {
            if (mode == TilePolicy.ExpandTiles) VoxelizeActiveTiles(tree);
            Erode(tree, iterations, offsets, includeTiles: false);
        }
    }

    private static void Dilate<T>(VdbTree<T> tree, int iterations, Coord[] neighbors, bool includeTiles) where T : unmanaged
    {
        for (var iteration = 0; iteration < iterations; ++iteration)
        {
            // Capture finite stored regions, never enumerate voxels represented by an active tile.
            var seeds = tree.EnumerateValues().Where(value => includeTiles || value.IsVoxel).Select(static value => value.Bounds).ToArray();
            foreach (var seed in seeds)
                foreach (var offset in neighbors)
                {
                    var shifted = ShiftClipped(seed, offset);
                    if (!shifted.IsEmpty) tree.SetActiveState(shifted, true);
                }
        }
    }

    private static void Erode<T>(VdbTree<T> tree, int iterations, Coord[] neighbors, bool includeTiles) where T : unmanaged
    {
        for (var iteration = 0; iteration < iterations; ++iteration)
        {
            var snapshot = tree.DeepCopy();
            foreach (var seed in snapshot.EnumerateValues())
            {
                if (!includeTiles && !seed.IsVoxel) continue;
                if (seed.IsVoxel)
                {
                    var coordinate = seed.Coordinate;
                    foreach (var offset in neighbors)
                    {
                        if (!TryShift(coordinate, offset, out var neighbor) || !snapshot.IsValueOn(neighbor))
                        {
                            tree.SetActiveState(coordinate, false);
                            break;
                        }
                    }
                    continue;
                }
                foreach (var offset in neighbors)
                {
                    var valid = ValidCenters(seed.Bounds, offset);
                    foreach (var outside in Difference(seed.Bounds, valid)) tree.SetActiveState(outside, false);
                    if (valid.IsEmpty) continue;
                    var query = ShiftClipped(valid, offset);
                    foreach (var inactive in InactiveRegions(snapshot, query))
                        tree.SetActiveState(ShiftClipped(inactive, new Coord(-offset.X, -offset.Y, -offset.Z)), false);
                }
            }
        }
    }

    private static IEnumerable<CoordBBox> InactiveRegions<T>(VdbTree<T> tree, CoordBBox query) where T : unmanaged
    {
        var origin = VdbTree<T>.Align(query.Min, 12);
        for (long x = origin.X; x <= query.Max.X; x += 4096)
            for (long y = origin.Y; y <= query.Max.Y; y += 4096)
                for (long z = origin.Z; z <= query.Max.Z; z += 4096)
                {
                    var key = new Coord((int)x, (int)y, (int)z);
                    var clipped = query.Intersection(VdbTree<T>.NodeBounds(key, 12));
                    if (!tree.Entries.TryGetValue(key, out var entry)) { yield return clipped; continue; }
                    if (entry.Child is not null)
                    {
                        foreach (var inactive in InactiveRegions(entry.Child, clipped)) yield return inactive;
                    }
                    else if (!entry.Active) yield return clipped;
                }
    }

    private static IEnumerable<CoordBBox> InactiveRegions<T>(InternalNode<T> node, CoordBBox query) where T : unmanaged
    {
        var firstX = (query.Min.X - node.Origin.X) >> node.ChildLog2Dim;
        var firstY = (query.Min.Y - node.Origin.Y) >> node.ChildLog2Dim;
        var firstZ = (query.Min.Z - node.Origin.Z) >> node.ChildLog2Dim;
        var lastX = (query.Max.X - node.Origin.X) >> node.ChildLog2Dim;
        var lastY = (query.Max.Y - node.Origin.Y) >> node.ChildLog2Dim;
        var lastZ = (query.Max.Z - node.Origin.Z) >> node.ChildLog2Dim;
        for (var x = firstX; x <= lastX; ++x)
            for (var y = firstY; y <= lastY; ++y)
                for (var z = firstZ; z <= lastZ; ++z)
                {
                    var offset = (x << (2 * node.Log2Dim)) | (y << node.Log2Dim) | z;
                    var clipped = query.Intersection(VdbTree<T>.NodeBounds(node.ChildOrigin(offset), node.ChildLog2Dim));
                    if (!node.Children.TryGetValue(offset, out var child))
                    {
                        if (!node.Active.IsOn(offset)) yield return clipped;
                    }
                    else if (child is InternalNode<T> internalNode)
                    {
                        foreach (var inactive in InactiveRegions(internalNode, clipped)) yield return inactive;
                    }
                    else
                    {
                        var leaf = (LeafNode<T>)child;
                        if (leaf.IsEmpty) { yield return clipped; continue; }
                        if (leaf.IsDense) continue;
                        foreach (var coordinate in clipped)
                            if (!leaf.IsValueOn(coordinate)) yield return new CoordBBox(coordinate, coordinate);
                    }
                }
    }

    private static void VoxelizeActiveTiles<T>(VdbTree<T> tree) where T : unmanaged
    {
        tree.InvalidateAccessors();
        foreach (var key in tree.Entries.Keys.ToArray())
        {
            var entry = tree.Entries[key];
            var node = entry.Child;
            if (node is null && entry.Active)
            {
                node = new InternalNode<T>(2, key, entry.Value, true);
                tree.Entries[key] = new(default, false, node);
            }
            if (node is not null) VoxelizeActiveTiles(node);
        }
    }

    private static void VoxelizeActiveTiles<T>(InternalNode<T> node) where T : unmanaged
    {
        foreach (var offset in node.Active.OnIndices.ToArray()) node.TouchChild(offset);
        if (node.Level == 1) return;
        foreach (var child in node.Children.Values) VoxelizeActiveTiles((InternalNode<T>)child);
    }

    private static Coord[] NeighborOffsets(NearestNeighbors neighbors)
    {
        var maximumAxes = neighbors switch
        {
            NearestNeighbors.Face => 1,
            NearestNeighbors.FaceEdge => 2,
            NearestNeighbors.FaceEdgeVertex => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(neighbors))
        };
        var offsets = new List<Coord>((int)neighbors);
        for (var x = -1; x <= 1; ++x)
            for (var y = -1; y <= 1; ++y)
                for (var z = -1; z <= 1; ++z)
                {
                    var distance = System.Math.Abs(x) + System.Math.Abs(y) + System.Math.Abs(z);
                    if (distance >= 1 && distance <= maximumAxes) offsets.Add(new Coord(x, y, z));
                }
        return offsets.ToArray();
    }

    private static void ValidatePolicy(TilePolicy mode)
    {
        if (mode != TilePolicy.IgnoreTiles && mode != TilePolicy.ExpandTiles && mode != TilePolicy.PreserveTiles)
            throw new ArgumentOutOfRangeException(nameof(mode));
    }

    private static bool TryShift(Coord point, Coord offset, out Coord shifted)
    {
        long x = (long)point.X + offset.X, y = (long)point.Y + offset.Y, z = (long)point.Z + offset.Z;
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue || z < int.MinValue || z > int.MaxValue)
        { shifted = default; return false; }
        shifted = new Coord((int)x, (int)y, (int)z);
        return true;
    }

    private static CoordBBox ShiftClipped(CoordBBox box, Coord offset)
    {
        long minX = System.Math.Max(int.MinValue, (long)box.Min.X + offset.X), maxX = System.Math.Min(int.MaxValue, (long)box.Max.X + offset.X);
        long minY = System.Math.Max(int.MinValue, (long)box.Min.Y + offset.Y), maxY = System.Math.Min(int.MaxValue, (long)box.Max.Y + offset.Y);
        long minZ = System.Math.Max(int.MinValue, (long)box.Min.Z + offset.Z), maxZ = System.Math.Min(int.MaxValue, (long)box.Max.Z + offset.Z);
        if (minX > maxX || minY > maxY || minZ > maxZ) return CoordBBox.Empty;
        return new CoordBBox(new Coord((int)minX, (int)minY, (int)minZ), new Coord((int)maxX, (int)maxY, (int)maxZ));
    }

    private static CoordBBox ValidCenters(CoordBBox box, Coord offset)
    {
        var domain = new CoordBBox(new Coord(offset.X < 0 ? int.MinValue + 1 : int.MinValue,
            offset.Y < 0 ? int.MinValue + 1 : int.MinValue, offset.Z < 0 ? int.MinValue + 1 : int.MinValue),
            new Coord(offset.X > 0 ? int.MaxValue - 1 : int.MaxValue,
                offset.Y > 0 ? int.MaxValue - 1 : int.MaxValue, offset.Z > 0 ? int.MaxValue - 1 : int.MaxValue));
        return box.Intersection(domain);
    }

    private static IEnumerable<CoordBBox> Difference(CoordBBox box, CoordBBox cut)
    {
        if (cut.IsEmpty) { yield return box; yield break; }
        if (box.Min.X < cut.Min.X) yield return new CoordBBox(box.Min, new Coord(cut.Min.X - 1, box.Max.Y, box.Max.Z));
        if (box.Max.X > cut.Max.X) yield return new CoordBBox(new Coord(cut.Max.X + 1, box.Min.Y, box.Min.Z), box.Max);
        if (box.Min.Y < cut.Min.Y) yield return new CoordBBox(new Coord(cut.Min.X, box.Min.Y, box.Min.Z), new Coord(cut.Max.X, cut.Min.Y - 1, box.Max.Z));
        if (box.Max.Y > cut.Max.Y) yield return new CoordBBox(new Coord(cut.Min.X, cut.Max.Y + 1, box.Min.Z), new Coord(cut.Max.X, box.Max.Y, box.Max.Z));
        if (box.Min.Z < cut.Min.Z) yield return new CoordBBox(new Coord(cut.Min.X, cut.Min.Y, box.Min.Z), new Coord(cut.Max.X, cut.Max.Y, cut.Min.Z - 1));
        if (box.Max.Z > cut.Max.Z) yield return new CoordBBox(new Coord(cut.Min.X, cut.Min.Y, cut.Max.Z + 1), new Coord(cut.Max.X, cut.Max.Y, box.Max.Z));
    }
}
