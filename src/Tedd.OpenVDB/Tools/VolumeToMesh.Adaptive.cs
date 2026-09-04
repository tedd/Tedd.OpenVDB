// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Adaptation of VolumeToMesh.h MergeVoxelRegions and triangle relaxation.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

public sealed partial class VolumeToMesh
{
    private static readonly int[][] Faces = [[0, 1, 4, 5], [1, 2, 5, 6], [2, 3, 6, 7], [0, 3, 4, 7], [0, 1, 2, 3], [4, 5, 6, 7]];
    private static readonly Coord[] FaceCenters = [new(1, 1, 0), new(2, 1, 1), new(1, 1, 2), new(0, 1, 1), new(1, 0, 1), new(1, 2, 1)];
    private static bool NonManifold<T, P>(ValueAccessor<T> input, Coord origin, T iso, int dim) where T : unmanaged where P : struct, IScalar<T>
    {
        Span<bool> inside = stackalloc bool[8]; int signs = 0, half = dim / 2;
        for (int c = 0; c < 8; ++c) { inside[c] = P.Inside(input.GetValue(Add(origin, Corners[c] * dim)), iso); if (inside[c]) signs |= 1 << c; }
        if (MeshingTables.Adaptable[signs] == 0) return true;
        for (int e = 0; e < 12; ++e)
        {
            Coord p = Add(origin, Corners[EdgeA[e]] * dim + (Corners[EdgeB[e]] - Corners[EdgeA[e]]) * half); bool midpoint = P.Inside(input.GetValue(p), iso);
            if (inside[EdgeA[e]] != midpoint && inside[EdgeB[e]] != midpoint) return true;
        }
        for (int face = 0; face < 6; ++face)
        {
            bool midpoint = P.Inside(input.GetValue(Add(origin, FaceCenters[face] * half)), iso), allDifferent = true;
            foreach (int corner in Faces[face]) if (inside[corner] == midpoint) { allDifferent = false; break; }
            if (allDifferent) return true;
        }
        bool center = P.Inside(input.GetValue(Add(origin, new Coord(half))), iso); for (int c = 0; c < 8; ++c) if (inside[c] == center) return false; return true;
    }
    private static Vec3f Gradient<T, P>(ValueAccessor<T> input, Coord p) where T : unmanaged where P : struct, IScalar<T>
    {
        Vec3f result = new(P.Derivative(input.GetValue(Offset(p, 0, 1)), input.GetValue(Offset(p, 0, -1))), P.Derivative(input.GetValue(Offset(p, 1, 1)), input.GetValue(Offset(p, 1, -1))), P.Derivative(input.GetValue(Offset(p, 2, 1)), input.GetValue(Offset(p, 2, -1))));
        result.TryNormalize(out Vec3f normalized); return normalized;
    }
    private void MergeRegions<T, P>(Grid<T> grid, T iso, Auxiliary data, LeafNode<short>[] leaves, VdbTree<bool> border, bool invert, VdbTree<short>? referenceSigns, CancellationToken token) where T : unmanaged where P : struct, IScalar<T>
    {
        var input = grid.GetAccessor(); var indexAcc = data.Indices.GetAccessor(); var spatial = _spatialAdaptivity?.GetAccessor();
        foreach (var signs in leaves)
        {
            token.ThrowIfCancellationRequested(); var indices = data.Indices.ProbeLeaf(signs.Origin)!; Coord origin = signs.Origin;
            bool[] mask = new bool[512], gradientActive = new bool[512]; Vec3f[] gradients = new Vec3f[512]; float[] adaptability = new float[512];
            float adaptivity = (float)(referenceSigns is not null && referenceSigns.ProbeLeaf(origin) is null ? _secondaryAdaptivity : _adaptivity); bool useGradients = adaptivity < 1f; Array.Fill(adaptability, adaptivity);
            var maskLeaf = border.ProbeLeaf(origin); if (maskLeaf is not null) foreach (int offset in maskLeaf.ActiveOffsets) Mark2(maskLeaf.OffsetToGlobalCoord(offset));
            if (spatial is not null)
            {
                useGradients = false;
                for (int offset = 0; offset < 512; ++offset) { Coord p = indices.OffsetToGlobalCoord(offset); p = _spatialAdaptivity!.Transform.WorldToIndexCellCentered(grid.Transform.IndexToWorld(p)); float value = spatial.GetValue(p) * adaptivity; adaptability[offset] = value; if (value < 1f) useGradients = true; }
            }
            foreach (int offset in signs.ActiveOffsets)
            {
                short flags = signs.GetValue(offset); byte cell = (byte)flags; Coord p = signs.OffsetToGlobalCoord(offset);
                if ((flags & Seam) != 0 || MeshingTables.Adaptable[cell] == 0 || GroupCount(cell) > 1) { Mark2(p); continue; }
                if ((flags & Edges) == 0) continue;
                bool missing = !indexAcc.IsValueOn(p);
                for (int axis = 0; axis < 3 && !missing; ++axis)
                {
                    if ((flags & (XEdge << axis)) == 0) continue; int a = axis == 1 ? 2 : 1, b = axis == 0 ? 2 : 0; Coord p1 = Offset(p, a, -1), p2 = Offset(p1, b, -1), p3 = Offset(p2, a, 1);
                    missing = !indexAcc.IsValueOn(p1) || !indexAcc.IsValueOn(p2) || !indexAcc.IsValueOn(p3);
                }
                if (missing) Mark2(p);
            }
            for (int x = 0; x < 8; x += 2) for (int y = 0; y < 8; y += 2) for (int z = 0; z < 8; z += 2)
            { Coord p = Add(origin, new(x, y, z)); int offset = LeafNode<int>.CoordToOffset(p); if (!mask[offset] && NonManifold<T, P>(input, p, iso, 2)) mask[offset] = true; }
            if (useGradients) foreach (int offset in signs.ActiveOffsets)
            {
                Coord p = signs.OffsetToGlobalCoord(offset); if (mask[LeafNode<int>.CoordToOffset(p & ~1)]) continue; Vec3f gradient = Gradient<T, P>(input, p); gradients[offset] = invert || P.IsBoolean ? -gradient : gradient; gradientActive[offset] = true;
            }
            int region = 1;
            for (int dim = 2; dim <= 8; dim *= 2) for (int x = 0; x < 8; x += dim) for (int y = 0; y < 8; y += dim) for (int z = 0; z < 8; z += dim)
            {
                Coord local = new(x, y, z), p = Add(origin, local); int offset = LeafNode<int>.CoordToOffset(p);
                if (mask[offset] || NonManifold<T, P>(input, p, iso, dim) || useGradients && !Mergeable(local, dim, adaptability[offset], gradients, gradientActive))
                    mask[LeafNode<int>.CoordToOffset(p & ~((dim * 2) - 1))] = true;
                else
                {
                    for (int a = x; a < x + dim; ++a) for (int b = y; b < y + dim; ++b) for (int c = z; c < z + dim; ++c) indices.SetValueOnly((a << 6) + (b << 3) + c, region); ++region;
                }
            }
            void Mark2(Coord p) => mask[LeafNode<int>.CoordToOffset(p & ~1)] = true;
        }
    }
    private static bool Mergeable(Coord origin, int dim, float adaptivity, Vec3f[] gradients, bool[] active)
    {
        if (adaptivity < 1e-6) return false; Span<int> offsets = stackalloc int[512]; int count = 0;
        for (int x = origin.X; x < origin.X + dim; ++x) for (int y = origin.Y; y < origin.Y + dim; ++y) for (int z = origin.Z; z < origin.Z + dim; ++z) { int offset = (x << 6) + (y << 3) + z; if (active[offset]) offsets[count++] = offset; }
        for (int i = 0; i < count; ++i) for (int j = 0; j < count; ++j) if (1.0 - gradients[offsets[i]].Dot(gradients[offsets[j]]) > adaptivity) return false; return true;
    }
    private static int CountPoints(Auxiliary data, LeafNode<short>[] leaves)
    {
        int count = 0; foreach (var signs in leaves)
        {
            var indices = data.Indices.ProbeLeaf(signs.Origin)!; var regions = new HashSet<int>(); foreach (int offset in indices.ActiveOffsets) { int id = indices.GetValue(offset); if (id == 0) count = checked(count + GroupCount((byte)(signs.GetValue(offset) & Signs))); else if (id != -1) regions.Add(id); }
            count = checked(count + regions.Count);
        }
        return count;
    }
    private void RelaxTriangles<T, P>(Grid<T> grid, bool invert, CancellationToken token) where T : unmanaged where P : struct, IScalar<T>
    {
        byte[] mask = new byte[_points.Length], updates = new byte[_points.Length]; Vec3f[] sums = new Vec3f[_points.Length]; var input = grid.GetAccessor();
        foreach (var pool in _pools)
        {
            token.ThrowIfCancellationRequested(); foreach (var triangle in pool.Triangles.Span)
            {
                Vec3f a = _points[triangle.A], b = _points[triangle.B], c = _points[triangle.C]; Vec3f normal = Cross(c - a, b - a); normal.TryNormalize(out normal); Vec3f centroid = (a + b + c) * (1f / 3f);
                Coord p = grid.Transform.WorldToIndexCellCentered(new Vec3d(centroid.X, centroid.Y, centroid.Z)); Vec3f direction = Gradient<T, P>(input, p); if (invert || P.IsBoolean) direction = -direction;
                if (direction.Dot(normal) < -.5f) mask[triangle.A] = mask[triangle.B] = mask[triangle.C] = 1;
            }
        }
        foreach (var pool in _pools)
        {
            foreach (var q in pool.Quads.Span) for (int v = 0; v < 4; ++v) { int index = q[v]; if (mask[index] == 1) { sums[index] += _points[q.A] + _points[q.B] + _points[q.C] + _points[q.D]; updates[index] = unchecked((byte)(updates[index] + 4)); } }
            foreach (var t in pool.Triangles.Span) for (int v = 0; v < 3; ++v) { int index = t[v]; if (mask[index] == 1) { sums[index] += _points[t.A] + _points[t.B] + _points[t.C]; updates[index] = unchecked((byte)(updates[index] + 3)); } }
        }
        for (int i = 0; i < _points.Length; ++i) if (updates[i] > 0) _points[i] = sums[i] * (float)(1.0 / updates[i]);
    }
    private static Vec3f Cross(Vec3f a, Vec3f b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}
