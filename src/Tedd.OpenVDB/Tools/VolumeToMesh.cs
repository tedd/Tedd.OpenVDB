// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/VolumeToMesh.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Extracts native edge-group polygon surfaces from scalar sparse grids.</summary>
/// <remarks>Instances retain output buffers and optional reference-surface caches; do not use one instance concurrently.</remarks>
public sealed partial class VolumeToMesh
{
    private const short Signs = 0xff, InsideFlag = 0x100, XEdge = 0x200, YEdge = 0x400, ZEdge = 0x800, Edges = 0xe00, Seam = 0x1000;
    private readonly double _isovalue, _adaptivity;
    private readonly bool _relaxDisorientedTriangles;
    private double _secondaryAdaptivity;
    private IGrid? _referenceGrid;
    private Grid<bool>? _surfaceMask;
    private Grid<float>? _spatialAdaptivity;
    private VdbTree<bool>? _adaptivityMask;
    private bool _invertSurfaceMask;
    private Vec3f[] _points = [];
    private PolygonPool[] _pools = [];
    private byte[] _pointFlags = [];
    private static readonly Coord[] Corners = [new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1), new(0, 1, 0), new(1, 1, 0), new(1, 1, 1), new(0, 1, 1)];
    private static readonly Coord[] UnitAxes = [new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)];
    private static readonly int[] EdgeA = [0, 1, 3, 0, 4, 5, 7, 4, 0, 1, 2, 3];
    private static readonly int[] EdgeB = [1, 2, 2, 3, 5, 6, 6, 7, 4, 5, 6, 7];
    public VolumeToMesh(double isovalue = 0, double adaptivity = 0, bool relaxDisorientedTriangles = true)
    { _isovalue = isovalue; _adaptivity = adaptivity; _relaxDisorientedTriangles = relaxDisorientedTriangles; }
    public ReadOnlyMemory<Vec3f> Points => _points;
    public IReadOnlyList<PolygonPool> PolygonPools => _pools;
    public ReadOnlyMemory<byte> PointFlags => _pointFlags;
    /// <summary>Managed bound on cumulative candidate-cell additions, including duplicate edge neighborhoods.</summary>
    public ulong MaximumCells { get; set; } = ulong.MaxValue;
    public void SetReferenceGrid(IGrid? grid, double secondaryAdaptivity = 0) { _referenceGrid = grid; _secondaryAdaptivity = secondaryAdaptivity; _referenceCache = null; }
    public void SetSurfaceMask(Grid<bool>? mask, bool invertMask = false) { _surfaceMask = mask; _invertSurfaceMask = invertMask; }
    public void SetSpatialAdaptivity(Grid<float>? grid) => _spatialAdaptivity = grid;
    public void SetAdaptivityMask(VdbTree<bool>? tree) => _adaptivityMask = tree;
    public void Extract<T>(Grid<T> grid, CancellationToken cancellationToken = default) where T : unmanaged, INumber<T> => ExtractCore<T, NumericScalar<T>>(grid, cancellationToken);
    public void Extract(Grid<bool> grid, CancellationToken cancellationToken = default) => ExtractCore<bool, BoolScalar>(grid, cancellationToken);

    private void ExtractCore<T, P>(Grid<T> grid, CancellationToken token) where T : unmanaged where P : struct, IScalar<T>
    {
        ArgumentNullException.ThrowIfNull(grid); token.ThrowIfCancellationRequested(); _points = []; _pools = []; _pointFlags = [];
        bool adaptive = _adaptivity > 1e-7 || _secondaryAdaptivity > 1e-7;
        var budget = new CellBudget(MaximumCells); T iso = P.FromDouble(_isovalue);
        VdbTree<bool> intersection = Identify<T, P>(grid.Tree, iso, budget, token);
        VdbTree<bool> border = _adaptivityMask?.DeepCopy() ?? new();
        intersection = ApplySurfaceMask<T, P>(grid, iso, intersection, border, budget, token);
        if (intersection.IsEmpty) return;
        Auxiliary auxiliary = BuildAuxiliary<T, P>(grid.Tree, iso, intersection, token);
        var leaves = auxiliary.Signs.EnumerateLeaves().ToArray();
        var reference = PrepareReference<T, P>(grid, iso, auxiliary, leaves, budget, token);
        bool invert = !P.IsBoolean && grid.GridClass != GridClass.LevelSet;
        if (adaptive) MergeRegions<T, P>(grid, iso, auxiliary, leaves, border, invert, reference?.Auxiliary.Signs, token);
        int pointCount = CountPoints(auxiliary, leaves);
        try
        {
            _points = GC.AllocateUninitializedArray<Vec3f>(pointCount);
            if (reference is not null) _pointFlags = new byte[pointCount];
            ComputePoints<T, P>(grid, auxiliary, leaves, reference, token);
            _pools = ComputePolygons(auxiliary, leaves, invert, adaptive, reference?.Auxiliary.Signs, token);
            if (adaptive && _relaxDisorientedTriangles) RelaxTriangles<T, P>(grid, invert, token);
            if (reference is not null) SubdivideSeamQuads(token);
        }
        catch
        {
            // Never expose partially initialized unmanaged storage after an interrupted extraction.
            _points = []; _pools = []; _pointFlags = [];
            throw;
        }
    }
    private sealed class Auxiliary
    {
        internal readonly VdbTree<short> Signs = new();
        internal readonly VdbTree<int> Indices = new(-1);
    }
    private sealed class CellBudget(ulong limit)
    {
        private UInt128 _used;
        internal void Add(UInt128 count) { if (count > limit || _used > (UInt128)limit - count) throw new InvalidOperationException("Meshing exceeds MaximumCells."); _used += count; }
    }
    private interface IScalar<T> where T : unmanaged
    {
        static abstract bool IsBoolean { get; }
        static abstract bool Inside(T value, T iso);
        static abstract T FromDouble(double value);
        static abstract double ToDouble(T value);
        static abstract float Derivative(T positive, T negative);
    }
    private readonly struct NumericScalar<T> : IScalar<T> where T : unmanaged, INumber<T>
    {
        public static bool IsBoolean => false;
        public static bool Inside(T value, T iso) => value < iso;
        public static T FromDouble(double value) => T.CreateTruncating(value);
        public static double ToDouble(T value) => double.CreateTruncating(value);
        public static float Derivative(T positive, T negative) => float.CreateTruncating(T.CreateTruncating(double.CreateTruncating(positive - negative) * .5));
    }
    private readonly struct BoolScalar : IScalar<bool>
    {
        public static bool IsBoolean => true;
        public static bool Inside(bool value, bool iso) => value;
        public static bool FromDouble(double value) => value != 0;
        public static double ToDouble(bool value) => value ? 1 : 0;
        public static float Derivative(bool positive, bool negative) => positive != negative ? 1f : 0f;
    }
    private static int GroupCount(byte signs) => MeshingTables.EdgeGroups[signs * 13];
    private static int Group(byte signs, int edge) => MeshingTables.EdgeGroups[signs * 13 + edge];
    private static Coord Offset(Coord coordinate, int axis, int amount) => axis switch { 0 => new(checked(coordinate.X + amount), coordinate.Y, coordinate.Z), 1 => new(coordinate.X, checked(coordinate.Y + amount), coordinate.Z), _ => new(coordinate.X, coordinate.Y, checked(coordinate.Z + amount)) };
    private static Coord Add(Coord a, Coord b) => new(checked(a.X + b.X), checked(a.Y + b.Y), checked(a.Z + b.Z));
    private static void MarkEdge(VdbTree<bool> mask, Coord p, int axis, CellBudget budget)
    {
        budget.Add(4); int a = axis == 1 ? 2 : 1, b = axis == 0 ? 2 : 0;
        mask.SetActiveState(p, true); p = Offset(p, a, -1); mask.SetActiveState(p, true); p = Offset(p, b, -1); mask.SetActiveState(p, true); p = Offset(p, a, 1); mask.SetActiveState(p, true);
    }
    private static VdbTree<bool> Identify<T, P>(VdbTree<T> input, T iso, CellBudget budget, CancellationToken token) where T : unmanaged where P : struct, IScalar<T>
    {
        var mask = new VdbTree<bool>(); var accessor = input.GetAccessor();
        foreach (var leaf in input.EnumerateLeaves())
        {
            token.ThrowIfCancellationRequested();
            for (int axis = 0; axis < 3; ++axis)
            {
                int stride = axis == 0 ? 64 : axis == 1 ? 8 : 1;
                for (int offset = 0; offset < 512; ++offset)
                {
                    Coord local = LeafNode<T>.OffsetToLocalCoord(offset); if (local[axis] == 7) continue;
                    if ((leaf.IsValueOn(offset) || leaf.IsValueOn(offset + stride)) && P.Inside(leaf.GetValue(offset), iso) != P.Inside(leaf.GetValue(offset + stride), iso)) MarkEdge(mask, leaf.OffsetToGlobalCoord(offset), axis, budget);
                }
                Coord neighborOrigin = Offset(leaf.Origin, axis, 8); var neighbor = input.ProbeLeaf(neighborOrigin); bool neighborOn = accessor.ProbeValue(neighborOrigin, out T tileValue);
                for (int offset = 0; offset < 512; ++offset)
                {
                    Coord local = LeafNode<T>.OffsetToLocalCoord(offset); if (local[axis] != 7 || axis == 1 && local.Z == 7) continue;
                    int neighborOffset = offset - 7 * stride;
                    if (neighbor is not null)
                    {
                        if ((leaf.IsValueOn(offset) || neighbor.IsValueOn(neighborOffset)) && P.Inside(leaf.GetValue(offset), iso) != P.Inside(neighbor.GetValue(neighborOffset), iso)) MarkEdge(mask, leaf.OffsetToGlobalCoord(offset), axis, budget);
                    }
                    else if (!neighborOn && leaf.IsValueOn(offset) && P.Inside(leaf.GetValue(offset), iso) != P.Inside(tileValue, iso)) MarkEdge(mask, leaf.OffsetToGlobalCoord(offset), axis, budget);
                }
                Coord negative = Offset(leaf.Origin, axis, -1);
                if (input.ProbeLeaf(negative) is null && !accessor.ProbeValue(negative, out tileValue))
                {
                    // Native uses the entire internal-neighbor list here, not just the negative face.
                    foreach (int offset in leaf.ActiveOffsets)
                        if (LeafNode<T>.OffsetToLocalCoord(offset)[axis] < 7 && P.Inside(leaf.GetValue(offset), iso) != P.Inside(tileValue, iso)) MarkEdge(mask, Offset(leaf.OffsetToGlobalCoord(offset), axis, -1), axis, budget);
                }
            }
        }
        foreach (var tile in input.EnumerateValues())
        {
            if (tile.IsVoxel) continue; token.ThrowIfCancellationRequested(); bool inside = P.Inside(tile.Value, iso); int depth = 3 - tile.Level;
            for (int axis = 0; axis < 3; ++axis) for (int side = 0; side < 2; ++side)
            {
                Coord probe = side == 0 ? Offset(tile.Bounds.Max, axis, 1) : Offset(tile.Bounds.Min, axis, -1); bool process = true;
                if (depth >= input.GetValueDepth(probe))
                {
                    bool on = accessor.ProbeValue(probe, out T value); process = (side == 0 || !on) && inside != P.Inside(value, iso);
                }
                if (!process) continue;
                CoordBBox expanded = tile.Bounds.Expand(1); int coordinate = side == 0 ? tile.Bounds.Max[axis] : checked(tile.Bounds.Min[axis] - 1);
                Coord min = Offset(expanded.Min, axis, checked(coordinate - expanded.Min[axis])), max = Offset(expanded.Max, axis, checked(coordinate - expanded.Max[axis])); CoordBBox plane = new(min, max);
                budget.Add(plane.Volume128); mask.Fill(plane, false, true);
            }
        }
        return mask;
    }
    private VdbTree<bool> ApplySurfaceMask<T, P>(Grid<T> grid, T iso, VdbTree<bool> intersection, VdbTree<bool> border, CellBudget budget, CancellationToken token) where T : unmanaged where P : struct, IScalar<T>
    {
        if (_surfaceMask is null) return intersection;
        bool aligned = grid.Transform.IsEquivalentTo(_surfaceMask.Transform); var mask = _surfaceMask.GetAccessor();
        foreach (var leaf in intersection.EnumerateLeaves())
        {
            token.ThrowIfCancellationRequested(); foreach (int offset in leaf.ActiveOffsets)
            {
                Coord coordinate = leaf.OffsetToGlobalCoord(offset); Coord location = aligned ? coordinate : _surfaceMask.Transform.WorldToIndexCellCentered(grid.Transform.IndexToWorld(coordinate));
                if (mask.IsValueOn(location) == _invertSurfaceMask) leaf.SetValueOnly(offset, true);
            }
        }
        foreach (var leaf in intersection.EnumerateLeaves()) foreach (int offset in leaf.ActiveOffsets)
        {
            Coord p = leaf.OffsetToGlobalCoord(offset); bool value = leaf.GetValue(offset);
            for (int corner = 1; corner < 8; ++corner) if (intersection.ProbeValue(Add(p, Corners[corner]), out bool neighbor) && value != neighbor) { border.SetValueOn(p, true); break; }
        }
        var result = new VdbTree<bool>(); var input = grid.GetAccessor();
        foreach (var leaf in intersection.EnumerateLeaves())
        {
            token.ThrowIfCancellationRequested(); foreach (int offset in leaf.ActiveOffsets)
            {
                if (leaf.GetValue(offset)) continue; Coord p = leaf.OffsetToGlobalCoord(offset); bool inside = P.Inside(input.GetValue(p), iso);
                for (int axis = 0; axis < 3; ++axis) if (inside != P.Inside(input.GetValue(Offset(p, axis, 1)), iso)) MarkEdge(result, p, axis, budget);
            }
        }
        foreach (var leaf in result.EnumerateLeaves()) foreach (int offset in leaf.ActiveOffsets) if (intersection.GetValue(leaf.OffsetToGlobalCoord(offset))) leaf.SetValueOnly(offset, true);
        return result;
    }
    private static byte EvaluateSigns<T, P>(ValueAccessor<T> input, Coord p, T iso) where T : unmanaged where P : struct, IScalar<T>
    {
        int signs = 0; for (int corner = 0; corner < 8; ++corner) if (P.Inside(input.GetValue(Add(p, Corners[corner])), iso)) signs |= 1 << corner; return (byte)signs;
    }
    private static Auxiliary BuildAuxiliary<T, P>(VdbTree<T> input, T iso, VdbTree<bool> intersection, CancellationToken token) where T : unmanaged where P : struct, IScalar<T>
    {
        var data = new Auxiliary(); var accessor = input.GetAccessor();
        foreach (var leaf in intersection.EnumerateLeaves())
        {
            token.ThrowIfCancellationRequested(); foreach (int offset in leaf.ActiveOffsets)
            {
                Coord p = leaf.OffsetToGlobalCoord(offset); byte signs = EvaluateSigns<T, P>(accessor, p, iso); if (signs is 0 or 255) continue;
                bool inside = (signs & 1) != 0; int flags = inside ? InsideFlag : 0;
                if (!leaf.GetValue(offset)) { if (inside != ((signs & 2) != 0)) flags |= XEdge; if (inside != ((signs & 16) != 0)) flags |= YEdge; if (inside != ((signs & 8) != 0)) flags |= ZEdge; }
                int face = MeshingTables.AmbiguousFace[signs];
                if (face != 0)
                {
                    int axis = face is 1 or 3 ? 2 : face is 2 or 4 ? 0 : 1, direction = face is 1 or 4 or 5 ? -1 : 1;
                    int match = face switch { 1 => 3, 2 => 4, 3 => 1, 4 => 2, 5 => 6, _ => 5 };
                    if (MeshingTables.AmbiguousFace[EvaluateSigns<T, P>(accessor, Offset(p, axis, direction), iso)] == match) signs = unchecked((byte)~signs);
                }
                data.Signs.SetValueOn(p, (short)(flags | signs)); data.Indices.SetValueOn(p, 0);
            }
        }
        return data;
    }
    private static void ReadCell<T, P>(ValueAccessor<T> input, Coord p, Span<double> values) where T : unmanaged where P : struct, IScalar<T>
    { for (int corner = 0; corner < 8; ++corner) values[corner] = P.ToDouble(input.GetValue(Add(p, Corners[corner]))); }
    private static Vec3d ComputePoint(ReadOnlySpan<double> values, byte signs, int group, double iso)
    {
        Vec3d sum = Vec3d.Zero; int count = 0;
        for (int edge = 0; edge < 12; ++edge)
        {
            if (Group(signs, edge + 1) != group) continue;
            int a = EdgeA[edge], b = EdgeB[edge]; double t = (iso - values[a]) / (values[b] - values[a]); Coord ca = Corners[a], cb = Corners[b];
            Vec3d point = ca.AsVec3d() + (cb - ca).AsVec3d() * t; sum += point; ++count;
        }
        return count > 1 ? sum * (1.0 / count) : sum;
    }
    private void ComputePoints<T, P>(Grid<T> grid, Auxiliary data, LeafNode<short>[] leaves, ReferenceData<T>? reference, CancellationToken token) where T : unmanaged where P : struct, IScalar<T>
    {
        var accessor = grid.GetAccessor(); var referenceAccessor = reference?.Input.GetAccessor(); Span<double> values = stackalloc double[8]; int index = 0;
        foreach (var leaf in leaves)
        {
            token.ThrowIfCancellationRequested(); var indices = data.Indices.ProbeLeaf(leaf.Origin)!;
            var regions = new SortedDictionary<int, List<int>>();
            foreach (int offset in leaf.ActiveOffsets)
            {
                int region = indices.GetValue(offset);
                if (region != 0) { if (region != -1) { if (!regions.TryGetValue(region, out var offsets)) regions.Add(region, offsets = []); offsets.Add(offset); } continue; }
                Coord p = leaf.OffsetToGlobalCoord(offset); byte signs = (byte)leaf.GetValue(offset); indices.SetValueOnly(offset, index); ReadCell<T, P>(accessor, p, values);
                for (int group = 1; group <= GroupCount(signs); ++group)
                {
                    Vec3d point = ReferencePoint<T, P>(reference, referenceAccessor, p, leaf.GetValue(offset), group, values, _isovalue, out bool weighted);
                    if (weighted) _pointFlags[index] = 1;
                    if (!point.IsFinite) throw new ArgumentException("VolumeToMesh encountered NaN or infinite surface points.", nameof(grid));
                    point = grid.Transform.IndexToWorld(point + p.AsVec3d()); _points[index++] = new((float)point.X, (float)point.Y, (float)point.Z);
                }
            }
            foreach (var region in regions.Values)
            {
                Vec3d sum = Vec3d.Zero;
                foreach (int offset in region)
                {
                    Coord p = leaf.OffsetToGlobalCoord(offset); indices.SetValueOnly(offset, index); ReadCell<T, P>(accessor, p, values);
                    Vec3d point = ComputePoint(values, (byte)leaf.GetValue(offset), 1, _isovalue); sum += p.AsVec3d() + point;
                }
                if (region.Count > 1) sum *= 1.0 / region.Count;
                sum = grid.Transform.IndexToWorld(sum); _points[index++] = new((float)sum.X, (float)sum.Y, (float)sum.Z);
            }
        }
    }
    private static PolygonPool[] ComputePolygons(Auxiliary data, LeafNode<short>[] leaves, bool invert, bool adaptive, VdbTree<short>? referenceSigns, CancellationToken token)
    {
        var result = new PolygonPool[leaves.Length]; var indices = data.Indices.GetAccessor(); var signsAcc = data.Signs.GetAccessor();
        int[][] edges = [[1, 5, 7, 3], [9, 12, 11, 10], [4, 8, 6, 2]];
        for (int n = 0; n < leaves.Length; ++n)
        {
            token.ThrowIfCancellationRequested(); var leaf = leaves[n]; int count = 0;
            foreach (int offset in leaf.ActiveOffsets) { int flags = leaf.GetValue(offset); if ((flags & XEdge) != 0) ++count; if ((flags & YEdge) != 0) ++count; if ((flags & ZEdge) != 0) ++count; }
            var pool = result[n] = new PolygonPool(count, adaptive ? count : 0); int numQuads = 0, numTriangles = 0;
            foreach (int offset in leaf.ActiveOffsets)
            {
                short flags = leaf.GetValue(offset); if ((flags & Edges) == 0) continue; Coord p = leaf.OffsetToGlobalCoord(offset);
                if (!indices.ProbeValue(p, out int initial) || initial == -1) continue; bool reverse = ((flags & InsideFlag) != 0) != invert;
                int refFlags = referenceSigns?.GetValue(p) ?? 0;
                for (int axis = 0; axis < 3; ++axis)
                {
                    if ((flags & (XEdge << axis)) == 0) continue;
                    int a = axis == 1 ? 2 : 1, b = axis == 0 ? 2 : 0;
                    Coord p1 = Offset(p, a, -1), p2 = Offset(p1, b, -1), p3 = Offset(p2, a, 1);
                    if (!indices.ProbeValue(p1, out int i1) || !indices.ProbeValue(p2, out int i2) || !indices.ProbeValue(p3, out int i3)) continue;
                    int i0 = initial + GroupOffset((byte)flags, edges[axis][0]); i1 += GroupOffset((byte)signsAcc.GetValue(p1), edges[axis][1]); i2 += GroupOffset((byte)signsAcc.GetValue(p2), edges[axis][2]); i3 += GroupOffset((byte)signsAcc.GetValue(p3), edges[axis][3]);
                    var tags = ((flags & Seam) != 0 ? PolygonFlags.FractureSeam : 0) | ((refFlags & (XEdge << axis)) != 0 ? PolygonFlags.Exterior : 0);
                    AddPolygon(pool, ref numQuads, ref numTriangles, new(i0, i1, i2, i3), axis == 2 ? !reverse : reverse, tags, adaptive);
                }
            }
            pool.TrimQuads(numQuads); if (adaptive) pool.TrimTriangles(numTriangles);
        }
        return result;
        static int GroupOffset(byte signs, int edge) => GroupCount(signs) > 1 ? Group(signs, edge) - 1 : 0;
    }
    private static void AddPolygon(PolygonPool pool, ref int quads, ref int triangles, MeshQuad q, bool reverse, PolygonFlags flags, bool adaptive)
    {
        if (!adaptive || q.A != q.B && q.A != q.C && q.A != q.D && q.B != q.C && q.B != q.D && q.C != q.D)
        { pool.SetQuad(quads++, reverse ? new(q.D, q.C, q.B, q.A) : q, flags); return; }
        MeshTriangle? triangle = null;
        if (q.A == q.D && q.B != q.C && q.B != q.A && q.C != q.A) triangle = new(q.A, q.B, q.C);
        else if (q.B == q.C && q.A != q.D && q.A != q.B && q.D != q.B) triangle = new(q.A, q.B, q.D);
        else if (q.A == q.B && q.C != q.D && q.C != q.A && q.D != q.A) triangle = new(q.A, q.C, q.D);
        else if (q.C == q.D && q.A != q.B && q.A != q.C && q.B != q.C) triangle = new(q.A, q.B, q.C);
        if (triangle is MeshTriangle t) pool.SetTriangle(triangles++, reverse ? new(t.C, t.B, t.A) : t, flags);
    }
}
