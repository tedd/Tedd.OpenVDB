// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

internal sealed partial class MeshConversion<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private void TraceExterior() => TraceExterior(_distance, _options.CancellationToken);
    internal static void TraceExterior(VdbTree<T> tree, CancellationToken cancellationToken)
    {
        var leaves = tree.EnumerateLeaves().ToArray();
        var nodes = leaves.ToDictionary(leaf => leaf.Origin);
        for (int axis = 2; axis >= 0; axis--)
        {
            int componentA = axis == 0 ? 1 : 0, componentB = axis == 2 ? 1 : 2;
            int step = axis == 0 ? 64 : axis == 1 ? 8 : 1;
            Coord direction = Coord.Zero.WithComponent(axis, 8);
            foreach (var first in leaves)
            {
                cancellationToken.ThrowIfCancellationRequested(); if (nodes.ContainsKey(Add(first.Origin, -direction))) continue;
                var last = first; while (nodes.TryGetValue(Add(last.Origin, direction), out var next)) last = next;
                for (int a = 0; a < 8; a++) for (int b = 0; b < 8; b++)
                {
                    Coord local = Coord.Zero.WithComponent(componentA, a).WithComponent(componentB, b);
                    int offset = LeafNode<T>.CoordToOffset(local);
                    var node = first;
                    while (TraceLine(node, offset, step) && nodes.TryGetValue(Add(node.Origin, direction), out var next)) node = next;
                    node = last; offset += step * 7;
                    while (TraceLine(node, offset, -step) && nodes.TryGetValue(Add(node.Origin, -direction), out var previous)) node = previous;
                }
            }
        }
        // The native leaf scan and buffered face synchronization compute this same
        // six-connected sign closure. Queueing newly negative voxels avoids rescanning unchanged leaves.
        Queue<Coord> negative = new();
        foreach (var leaf in leaves) for (int offset = 0; offset < 512; offset++) if (leaf.GetValue(offset) < T.Zero) negative.Enqueue(leaf.OffsetToGlobalCoord(offset));
        while (negative.TryDequeue(out Coord coordinate))
        {
            if ((negative.Count & 8191) == 0) cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < 6; i++)
            {
                Coord neighbor = Add(coordinate, Neighbors[i]); if (!nodes.TryGetValue(neighbor & -8, out var node)) continue;
                int offset = LeafNode<T>.CoordToOffset(neighbor); T value = node.GetValue(offset);
                if (value > Boundary) { node.SetValueOnly(offset, -value); negative.Enqueue(neighbor); }
            }
        }
    }
    private static bool TraceLine(LeafNode<T> node, int offset, int step)
    {
        bool outside = true;
        for (int i = 0; i < 8; i++, offset += step)
        {
            T value = node.GetValue(offset);
            if (value < T.Zero) outside = true;
            else { if (!(value > Boundary)) outside = false; if (outside) node.SetValueOnly(offset, -value); }
        }
        return outside;
    }
    private void EvaluateInterior()
    {
        var manager = new NodeManager<T>(_distance);
        manager.ForEachBottomUp(node =>
        {
            Checkpoint();
            if (_interiorStrategy == InteriorTestStrategy.EveryTile && node.Leaf is { } leaf) FloodLeaf(leaf);
            else foreach (var value in node.EnumerateValues(ValueIterationMode.All))
                if (!_interiorTest!(value.Coordinate)) value.SetValueOnly(-value.Value);
        });
    }
    private void FloodLeaf(LeafNode<T> leaf)
    {
        Span<byte> states = stackalloc byte[512]; states.Clear(); Stack<int> pending = new();
        for (int offset = 0; offset < 512; offset++)
        {
            if (T.Abs(leaf.GetValue(offset)) <= Boundary) { states[offset] = 3; continue; }
            if (states[offset] != 0) continue;
            byte state = _interiorTest!(leaf.OffsetToGlobalCoord(offset)) ? (byte)1 : (byte)2;
            pending.Push(offset); states[offset] = state;
            while (pending.TryPop(out int current))
            {
                if (state == 2) leaf.SetValueOnly(current, -leaf.GetValue(current));
                for (int dimension = 2; dimension >= 0; dimension--)
                {
                    int component = (current >> (dimension * 3)) & 7, step = 1 << (dimension * 3);
                    // Preserve native boundary-component predicate, which requires both faces to be absent.
                    if (component <= 0 || component >= 7) continue;
                    for (int direction = -1; direction <= 1; direction += 2)
                    {
                        int neighbor = current + step * direction; if (states[neighbor] != 0) continue;
                        if (T.Abs(leaf.GetValue(neighbor)) <= Boundary) states[neighbor] = 3;
                        else { states[neighbor] = state; pending.Push(neighbor); }
                    }
                }
            }
        }
    }
    private void ComputeIntersectingSigns()
    {
        foreach (var leaf in _distance.EnumerateLeaves())
        {
            Checkpoint(); var indexLeaf = _indices.ProbeLeaf(leaf.Origin)!;
            var points = new Vec3d[512]; var directions = new Vec3d[512]; var computed = new bool[512];
            foreach (int offset in leaf.ActiveOffsets)
            {
                T distance = leaf.GetValue(offset); if (distance < T.Zero || distance > Boundary) continue;
                Coord coordinate = leaf.OffsetToGlobalCoord(offset); Vec3d center = coordinate.AsVec3d();
                CoordBBox neighborhood = new(Coord.Max(Add(coordinate, new(-1)), leaf.Bounds.Min), Coord.Min(Add(coordinate, new(1)), leaf.Bounds.Max));
                bool flip = false;
                foreach (Coord neighbor in neighborhood)
                {
                    int n = LeafNode<T>.CoordToOffset(neighbor), polygon = indexLeaf.GetValue(n);
                    if (polygon == -1 || !(leaf.GetValue(n) < -Boundary)) continue;
                    if (!computed[n]) { computed[n] = true; points[n] = ClosestPoint(neighbor, polygon); (neighbor.AsVec3d() - points[n]).TryNormalize(out directions[n]); }
                    (center - points[n]).TryNormalize(out var direction);
                    if (directions[n].Dot(direction) > 0) { flip = true; break; }
                }
                if (!flip)
                {
                    foreach (var delta in Neighbors)
                    {
                        Coord neighbor = Add(coordinate, delta); if (neighborhood.Contains(neighbor) || !_distance.ProbeValue(neighbor, out T other) || !(other < -Boundary)) continue;
                        Vec3d point = ClosestPoint(neighbor, _indices.GetValue(neighbor));
                        (center - point).TryNormalize(out var a); (neighbor.AsVec3d() - point).TryNormalize(out var b);
                        if (b.Dot(a) > 0) { flip = true; break; }
                    }
                }
                if (flip) leaf.SetValueOnly(offset, -distance);
            }
        }
    }
    private void RemoveIntersecting()
    {
        var leaves = _distance.EnumerateLeaves().ToArray();
        foreach (var leaf in leaves)
        {
            Checkpoint();
            foreach (int offset in leaf.ActiveOffsets)
            {
                T distance = leaf.GetValue(offset); if (distance < T.Zero || distance > Boundary) continue;
                Coord coordinate = leaf.OffsetToGlobalCoord(offset); bool negative = false;
                foreach (var delta in Neighbors) if (_distance.GetValue(Add(coordinate, delta)) < T.Zero) { negative = true; break; }
                if (!negative) leaf.SetValueOnly(offset, Boundary + Epsilon);
            }
        }
        foreach (var leaf in leaves)
        {
            Checkpoint(); var indices = _indices.ProbeLeaf(leaf.Origin)!;
            foreach (int offset in leaf.ActiveOffsets)
            {
                if (!(leaf.GetValue(offset) > Boundary)) continue; Coord coordinate = leaf.OffsetToGlobalCoord(offset); bool boundary = false;
                foreach (var delta in Neighbors) if (!(_distance.GetValue(Add(coordinate, delta)) > Boundary)) { boundary = true; break; }
                if (!boundary) { leaf.SetValueOff(offset); indices.SetValueOff(offset); }
            }
        }
        Prune.PruneInactive(_distance); Prune.PruneInactive(_indices);
    }
    private void Renormalize()
    {
        var manager = new LeafManager<T>(_distance, 1); T offset = T.CreateTruncating(0.8 * double.CreateTruncating(_voxelSize)), invDx = T.One / _voxelSize;
        foreach (var leaf in _distance.EnumerateLeaves()) foreach (int n in leaf.ActiveOffsets) leaf.SetValueOnly(n, leaf.GetValue(n) - offset);
        for (int i = 0; i < manager.LeafCount; i++)
        {
            Checkpoint(); var leaf = manager[i]; Span<T> buffer = manager.GetBuffer(i, 1);
            foreach (int n in leaf.ActiveOffsets)
            {
                T phi = leaf.GetValue(n); Coord c = leaf.OffsetToGlobalCoord(n);
                T xp = _distance.GetValue(Add(c, Neighbors[0])) - phi, xm = phi - _distance.GetValue(Add(c, Neighbors[1]));
                T yp = _distance.GetValue(Add(c, Neighbors[2])) - phi, ym = phi - _distance.GetValue(Add(c, Neighbors[3]));
                T zp = _distance.GetValue(Add(c, Neighbors[4])) - phi, zm = phi - _distance.GetValue(Add(c, Neighbors[5]));
                T norm = FiniteDifference.GodunovNormSquared(phi > T.Zero, xm, xp, ym, yp, zm, zp);
                T difference = T.Sqrt(norm) * invDx - T.One, sign = phi / T.Sqrt(phi * phi + norm);
                buffer[n] = phi - _voxelSize * sign * difference;
            }
        }
        for (int i = 0; i < manager.LeafCount; i++)
        {
            var leaf = manager[i]; ReadOnlySpan<T> buffer = manager.GetBuffer(i, 1);
            foreach (int n in leaf.ActiveOffsets) { T old = leaf.GetValue(n), candidate = buffer[n]; leaf.SetValueOnly(n, (candidate < old ? candidate : old) + (offset - Epsilon)); }
        }
    }
    private void Trim(bool signed)
    {
        T interior = signed ? _interior : _exterior;
        foreach (var leaf in _distance.EnumerateLeaves()) foreach (int n in leaf.ActiveOffsets)
        {
            T value = leaf.GetValue(n);
            if (value < T.Zero) { if (!(value > -interior)) leaf.SetValueOff(n, -interior); }
            else if (!(value < _exterior)) leaf.SetValueOff(n, _exterior);
        }
        Prune.PruneLevelSet(_distance, _exterior, -interior);
    }
}
