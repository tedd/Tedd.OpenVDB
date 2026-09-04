// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

internal sealed partial class MeshConversion<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly record struct Fragment(int Polygon, Coord Coordinate);
    private void Expand()
    {
        var frontier = new HashSet<Coord>();
        foreach (var leaf in _distance.EnumerateLeaves()) foreach (int offset in leaf.ActiveOffsets)
        {
            Coord coordinate = leaf.OffsetToGlobalCoord(offset);
            for (int i = 0; i < 6; i++) { Coord neighbor = Add(coordinate, Neighbors[i]); if (!_distance.IsValueOn(neighbor)) frontier.Add(neighbor); }
        }
        uint maximumIterations = uint.MaxValue;
        double estimate = 2 * System.Math.Ceiling(double.CreateTruncating((T.Max(_interior, _exterior) - _voxelSize * T.CreateTruncating(2)) / _voxelSize));
        if (estimate < uint.MaxValue) maximumIterations = (uint)estimate;
        for (uint iteration = 0; frontier.Count != 0 && iteration < maximumIterations; iteration++)
        {
            Checkpoint(); var next = new HashSet<Coord>();
            var updates = new List<(LeafNode<T> Distance, LeafNode<int> Indices, bool New)>();
            foreach (var group in frontier.GroupBy(c => c & -8).OrderBy(g => g.Key))
            {
                Checkpoint(); Coord origin = group.Key;
                var requested = group.Where(c => !_distance.IsValueOn(c)).OrderBy(LeafNode<T>.CoordToOffset).ToArray();
                if (requested.Length == 0) continue;
                var distance = _distance.ProbeLeaf(origin); var indices = _indices.ProbeLeaf(origin); bool created = distance is null;
                if (created) { distance = new(origin, _distance.GetValue(origin)); indices = new(origin, _indices.GetValue(origin)); }
                if (indices is null) throw new InvalidOperationException("Mesh distance/index leaf topology diverged.");
                CoordBBox region = CoordBBox.Empty;
                foreach (Coord c in requested) region = region.Expand(c);
                region = new(Add(region.Min, new(-1)), Add(region.Max, new(1)));
                var fragments = GatherFragments(region); var localFrontier = new SortedSet<int>(); bool changed = false;
                foreach (Coord coordinate in requested)
                {
                    if (!UpdateBand(coordinate, 5, fragments, distance!, indices, ref changed)) continue;
                    for (int i = 0; i < 26; i++)
                    {
                        Coord neighbor = Add(coordinate, Neighbors[i]);
                        if (distance!.Bounds.Contains(neighbor)) localFrontier.Add(LeafNode<T>.CoordToOffset(neighbor));
                        else if (i < 6) next.Add(neighbor);
                    }
                }
                if (!changed) continue;
                foreach (int offset in localFrontier)
                {
                    if (indices.IsValueOn(offset)) continue;
                    Coord coordinate = distance!.OffsetToGlobalCoord(offset); bool ignored = false;
                    if (UpdateBand(coordinate, 6, fragments, distance, indices, ref ignored))
                        for (int i = 0; i < 6; i++) next.Add(Add(coordinate, Neighbors[i]));
                }
                updates.Add((distance!, indices, created));
            }
            // Native distance activity and new leaves become visible only after the whole layer.
            foreach (var update in updates)
            {
                update.Distance.TopologyUnion(update.Indices);
                if (!update.New) continue;
                ReserveLeaf(update.Distance.Origin); _distance.AddLeaf(update.Distance); _indices.AddLeaf(update.Indices);
            }
            frontier = next;
        }
    }
    private List<Fragment> GatherFragments(CoordBBox region)
    {
        var result = new List<Fragment>(); Coord min = region.Min & -8, max = region.Max & -8;
        for (long x = min.X; x <= max.X; x += 8) for (long y = min.Y; y <= max.Y; y += 8) for (long z = min.Z; z <= max.Z; z += 8)
        {
            Coord origin = new((int)x, (int)y, (int)z); var leaf = _distance.ProbeLeaf(origin); if (leaf is null) continue;
            var indices = _indices.ProbeLeaf(origin)!;
            foreach (Coord coordinate in region.Intersect(leaf.Bounds))
            {
                int offset = LeafNode<T>.CoordToOffset(coordinate);
                if (leaf.IsValueOn(offset)) result.Add(new(indices.GetValue(offset), coordinate));
            }
        }
        result.Sort(static (a, b) => a.Polygon.CompareTo(b.Polygon)); return result;
    }
    private bool UpdateBand(Coord coordinate, int manhattanLimit, List<Fragment> fragments, LeafNode<T> distanceLeaf, LeafNode<int> indexLeaf, ref bool changed)
    {
        Evaluate(); int last = -1, closest = 0; double minimum = double.MaxValue; Vec3d center = coordinate.AsVec3d();
        foreach (var fragment in fragments)
        {
            if (last == fragment.Polygon) continue;
            long manhattan = System.Math.Abs((long)fragment.Coordinate.X - coordinate.X) + System.Math.Abs((long)fragment.Coordinate.Y - coordinate.Y) + System.Math.Abs((long)fragment.Coordinate.Z - coordinate.Z);
            if (manhattan > manhattanLimit) continue;
            last = fragment.Polygon;
            double candidate = (center - ClosestPoint(coordinate, last)).LengthSquared;
            if (candidate < minimum) { minimum = candidate; closest = last; }
        }
        T distance = T.CreateTruncating(System.Math.Sqrt(minimum)) * _voxelSize;
        int offset = LeafNode<T>.CoordToOffset(coordinate); bool inside = distanceLeaf.GetValue(offset) < T.Zero;
        T width = inside ? _interior : _exterior;
        if (!(distance < width)) return false;
        changed = true; distanceLeaf.SetValueOnly(offset, inside ? -distance : distance); indexLeaf.SetValueOn(offset, closest);
        return distance + _voxelSize < width;
    }
}
