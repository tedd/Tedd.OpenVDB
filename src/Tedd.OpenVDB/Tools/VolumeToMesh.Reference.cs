// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Reference-fragment seam reconstruction from tools/VolumeToMesh.h.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

public sealed partial class VolumeToMesh
{
    private const uint DirtyPoint = 0x80000000, InvalidPoint = 0x40000000;
    private object? _referenceCache;
    private sealed class ReferenceData<T>(Grid<T> input, Auxiliary auxiliary, uint[] quantized) where T : unmanaged
    {
        internal readonly Grid<T> Input = input;
        internal readonly Auxiliary Auxiliary = auxiliary;
        internal readonly uint[] Quantized = quantized;
    }
    private ReferenceData<T>? PrepareReference<T, P>(Grid<T> input, T iso, Auxiliary data, LeafNode<short>[] leaves, CellBudget budget, CancellationToken token) where T : unmanaged where P : struct, IScalar<T>
    {
        if (_referenceGrid is not Grid<T> reference) return null;
        if (_referenceCache is not ReferenceData<T> cached)
        {
            var intersections = Identify<T, P>(reference.Tree, iso, budget, token);
            var auxiliary = BuildAuxiliary<T, P>(reference.Tree, iso, intersections, token);
            int pointCount = 0;
            foreach (var leaf in auxiliary.Signs.EnumerateLeaves())
            {
                var indices = auxiliary.Indices.ProbeLeaf(leaf.Origin)!;
                foreach (int offset in leaf.ActiveOffsets) { indices.SetValueOnly(offset, pointCount); pointCount = checked(pointCount + GroupCount((byte)(leaf.GetValue(offset) & Signs))); }
            }
            cached = new(reference, auxiliary, new uint[pointCount]); _referenceCache = cached;
        }
        var referenceSigns = cached.Auxiliary.Signs;
        foreach (var leaf in leaves)
        {
            var refLeaf = referenceSigns.ProbeLeaf(leaf.Origin); if (refLeaf is null) continue;
            foreach (int offset in leaf.ActiveOffsets)
            {
                byte other = (byte)refLeaf.GetValue(offset); short flags = leaf.GetValue(offset);
                if (GroupCount(other) > 0 && other != (byte)flags) leaf.SetValueOnly(offset, (short)(flags | Seam));
            }
        }
        var additions = new List<Coord>(); var signs = data.Signs.GetAccessor();
        foreach (var leaf in leaves) foreach (int offset in leaf.ActiveOffsets)
        {
            short flags = leaf.GetValue(offset); if ((flags & Seam) != 0 || (flags & Edges) == 0) continue; Coord p = leaf.OffsetToGlobalCoord(offset); bool neighborSeam = false;
            for (int axis = 0; axis < 3 && !neighborSeam; ++axis)
            {
                if ((flags & (XEdge << axis)) == 0) continue; int a = axis == 1 ? 2 : 1, b = axis == 0 ? 2 : 0;
                Coord p1 = Offset(p, a, -1), p2 = Offset(p1, b, -1), p3 = Offset(p2, a, 1);
                neighborSeam = (signs.GetValue(p1) & Seam) != 0 || (signs.GetValue(p2) & Seam) != 0 || (signs.GetValue(p3) & Seam) != 0;
            }
            if (neighborSeam) additions.Add(p);
        }
        foreach (Coord p in additions) data.Signs.SetValueOnly(p, (short)(data.Signs.GetValue(p) | Seam));
        var accessor = input.GetAccessor(); Span<double> values = stackalloc double[8]; double numericIso = P.ToDouble(iso);
        foreach (var leaf in leaves)
        {
            token.ThrowIfCancellationRequested(); var refLeaf = referenceSigns.ProbeLeaf(leaf.Origin); var refIndices = cached.Auxiliary.Indices.ProbeLeaf(leaf.Origin); if (refLeaf is null || refIndices is null) continue;
            foreach (int offset in leaf.ActiveOffsets)
            {
                short flags = leaf.GetValue(offset); if ((flags & Seam) == 0 || !refLeaf.IsValueOn(offset)) continue;
                byte lhs = (byte)flags, rhs = (byte)refLeaf.GetValue(offset); ReadCell<T, P>(accessor, leaf.OffsetToGlobalCoord(offset), values);
                for (int group = 1; group <= GroupCount(lhs); ++group)
                {
                    int other = MatchGroup(group, lhs, rhs); if (other < 0) continue; int index = refIndices.GetValue(offset) + other - 1;
                    if ((cached.Quantized[index] & DirtyPoint) != 0) continue;
                    var point = MaskedPoint(values, lhs, rhs, group, numericIso, out int samples);
                    cached.Quantized[index] = DirtyPoint | (samples > 0 ? Pack(point) : InvalidPoint);
                }
            }
        }
        return cached;
    }
    private static int MatchGroup(int group, byte source, byte target)
    { for (int edge = 1; edge <= 12; ++edge) if (Group(source, edge) == group && Group(target, edge) != 0) return Group(target, edge); return -1; }
    private static Vec3d EdgePoint(ReadOnlySpan<double> values, int edge, double iso)
    {
        int a = EdgeA[edge], b = EdgeB[edge]; double t = (iso - values[a]) / (values[b] - values[a]); Coord p = Corners[a], q = Corners[b];
        return new(p.X == q.X ? p.X : t, p.Y == q.Y ? p.Y : t, p.Z == q.Z ? p.Z : t);
    }
    private static Vec3d MaskedPoint(ReadOnlySpan<double> values, byte source, byte mask, int group, double iso, out int samples)
    {
        Vec3d sum = Vec3d.Zero; samples = 0;
        for (int edge = 0; edge < 12; ++edge) if (Group(source, edge + 1) == group && Group(mask, edge + 1) == 0) { sum += EdgePoint(values, edge, iso); ++samples; }
        return samples > 1 ? sum * (1.0 / samples) : sum;
    }
    private static Vec3d WeightedPoint(Vec3d point, ReadOnlySpan<double> values, byte signs, int group, double iso)
    {
        Span<Vec3d> samples = stackalloc Vec3d[12]; Span<double> weights = stackalloc double[12]; int count = 0;
        for (int edge = 0; edge < 12; ++edge) if (Group(signs, edge + 1) == group) samples[count++] = EdgePoint(values, edge, iso);
        if (count == 1) return samples[0];
        for (int i = 0; i < count; ++i) weights[i] = (samples[i] - point).LengthSquared;
        double min = weights[0], max = weights[0];
        for (int i = 1; i < count; ++i) { min = System.Math.Min(min, weights[i]); max = System.Math.Max(max, weights[i]); }
        double offset = max + min * .1, weight = 0;
        for (int i = 0; i < count; ++i) { weights[i] = offset - weights[i]; weight += weights[i]; }
        Vec3d result = Vec3d.Zero; for (int i = 0; i < count; ++i) result += samples[i] * (weights[i] / weight); return result;
    }
    private static uint Pack(Vec3d point) => ((uint)(point.X * 1023) & 1023) << 20 | ((uint)(point.Y * 1023) & 1023) << 10 | ((uint)(point.Z * 1023) & 1023);
    private static Vec3d Unpack(uint point) => new(((point >> 20) & 1023) * .0009775171, ((point >> 10) & 1023) * .0009775171, (point & 1023) * .0009775171);
    private static Vec3d ReferencePoint<T, P>(ReferenceData<T>? reference, ValueAccessor<T>? refAccessor, Coord p, short flags, int group, ReadOnlySpan<double> values, double iso, out bool weighted) where T : unmanaged where P : struct, IScalar<T>
    {
        weighted = false; byte source = (byte)flags;
        if (reference is null || (flags & Seam) == 0 || !reference.Auxiliary.Signs.ProbeValue(p, out short otherFlags)) return ComputePoint(values, source, group, iso);
        byte other = (byte)otherFlags; int refGroup = MatchGroup(group, source, other);
        if (other == 0 || refGroup < 0) return ComputePoint(values, source, group, iso);
        Span<double> otherValues = stackalloc double[8]; ReadCell<T, P>(refAccessor!, p, otherValues);
        uint quantized = reference.Quantized[reference.Auxiliary.Indices.GetValue(p) + refGroup - 1];
        if ((quantized & DirtyPoint) != 0 && (quantized & InvalidPoint) == 0) { weighted = true; return WeightedPoint(Unpack(quantized), otherValues, other, refGroup, iso); }
        return ComputePoint(otherValues, other, refGroup, iso);
    }
    private void SubdivideSeamQuads(CancellationToken token)
    {
        var centroids = new List<Vec3f>();
        foreach (var pool in _pools)
        {
            token.ThrowIfCancellationRequested(); int count = 0;
            for (int n = 0; n < pool.NumQuads; ++n)
            {
                byte flags = pool.QuadFlagArray[n]; MeshQuad q = pool.QuadArray[n];
                if ((flags & (byte)PolygonFlags.FractureSeam) == 0 || (flags & (byte)PolygonFlags.Exterior) != 0) continue;
                if ((_pointFlags[q.A] | _pointFlags[q.B] | _pointFlags[q.C] | _pointFlags[q.D]) == 0) continue;
                if (!Planar(ToDouble(_points[q.A]), ToDouble(_points[q.B]), ToDouble(_points[q.C]), ToDouble(_points[q.D]), (double)1e-6f)) { pool.QuadFlagArray[n] |= (byte)PolygonFlags.Subdivided; ++count; }
            }
            if (count == 0) continue;
            var replacement = new PolygonPool(pool.NumQuads - count, checked(pool.NumTriangles + count * 4)); int ti = 0, qi = 0;
            for (int n = 0; n < pool.NumQuads; ++n)
            {
                var q = pool.QuadArray[n]; var flags = (PolygonFlags)pool.QuadFlagArray[n];
                if ((flags & PolygonFlags.Subdivided) == 0) { replacement.SetQuad(qi++, q, flags); continue; }
                int center = checked(_points.Length + centroids.Count); centroids.Add((_points[q.A] + _points[q.B] + _points[q.C] + _points[q.D]) * .25f);
                replacement.SetTriangle(ti++, new(q.A, center, q.D), flags); replacement.SetTriangle(ti++, new(q.A, q.B, center), flags); replacement.SetTriangle(ti++, new(q.B, q.C, center), flags); replacement.SetTriangle(ti++, new(q.C, q.D, center), flags);
            }
            for (int n = 0; n < pool.NumTriangles; ++n) replacement.SetTriangle(ti++, pool.TriangleArray[n], (PolygonFlags)pool.TriangleFlagArray[n]);
            pool.QuadArray = replacement.QuadArray; pool.QuadFlagArray = replacement.QuadFlagArray; pool.TriangleArray = replacement.TriangleArray; pool.TriangleFlagArray = replacement.TriangleFlagArray;
        }
        if (centroids.Count > 0) { int oldCount = _points.Length; Array.Resize(ref _points, checked(oldCount + centroids.Count)); centroids.CopyTo(_points, oldCount); Array.Resize(ref _pointFlags, _points.Length); }
        foreach (var pool in _pools)
        {
            for (int n = 0; n < pool.NumQuads; ++n) { MeshQuad q = pool.QuadArray[n]; if ((_pointFlags[q.A] | _pointFlags[q.B] | _pointFlags[q.C] | _pointFlags[q.D]) == 0) pool.QuadFlagArray[n] &= unchecked((byte)~(byte)PolygonFlags.FractureSeam); }
            for (int n = 0; n < pool.NumTriangles; ++n) { MeshTriangle t = pool.TriangleArray[n]; if ((_pointFlags[t.A] | _pointFlags[t.B] | _pointFlags[t.C]) == 0) pool.TriangleFlagArray[n] &= unchecked((byte)~(byte)PolygonFlags.FractureSeam); }
        }
    }
    private static Vec3d ToDouble(Vec3f p) => new(p.X, p.Y, p.Z);
    private static bool Planar(Vec3d a, Vec3d b, Vec3d c, Vec3d d, double epsilon)
    {
        Vec3d normal = (c - a).Cross(b - d); normal.TryNormalize(out normal); double plane = (a + b + c + d).Dot(normal) * .25;
        return !(System.Math.Abs(a.Dot(normal) - plane) > epsilon || System.Math.Abs(b.Dot(normal) - plane) > epsilon || System.Math.Abs(c.Dot(normal) - plane) > epsilon || System.Math.Abs(d.Dot(normal) - plane) > epsilon);
    }
}
