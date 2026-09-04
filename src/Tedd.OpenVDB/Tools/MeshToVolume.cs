// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/MeshToVolume.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Converts index-space triangle/quad meshes to signed or unsigned narrow-band distance volumes.</summary>
public static partial class MeshToVolume
{
    public static Grid<T> Convert<T>(IMeshDataAdapter mesh, Transform transform, float exteriorBandWidth = 3, float interiorBandWidth = 3,
        MeshToVolumeFlags flags = MeshToVolumeFlags.None, Grid<int>? polygonIndexGrid = null, Func<Coord, bool>? interiorTest = null,
        InteriorTestStrategy interiorTestStrategy = InteriorTestStrategy.EveryVoxel, MeshToVolumeOptions? options = null) where T : unmanaged, IFloatingPointIeee754<T>
        => Convert<T, IMeshDataAdapter>(mesh, transform, exteriorBandWidth, interiorBandWidth, flags, polygonIndexGrid, interiorTest, interiorTestStrategy, options);

    public static Grid<T> Convert<T, TMesh>(TMesh mesh, Transform transform, float exteriorBandWidth = 3, float interiorBandWidth = 3,
        MeshToVolumeFlags flags = MeshToVolumeFlags.None, Grid<int>? polygonIndexGrid = null, Func<Coord, bool>? interiorTest = null,
        InteriorTestStrategy interiorTestStrategy = InteriorTestStrategy.EveryVoxel, MeshToVolumeOptions? options = null)
        where T : unmanaged, IFloatingPointIeee754<T> where TMesh : IMeshDataAdapter
    {
        ArgumentNullException.ThrowIfNull(mesh); ArgumentNullException.ThrowIfNull(transform);
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double)) throw new NotSupportedException("Mesh distance fields require float or double values.");
        if (!Enum.IsDefined(interiorTestStrategy)) throw new ArgumentOutOfRangeException(nameof(interiorTestStrategy));
        options ??= new(); options.Validate();
        return new MeshConversion<T>(mesh, transform, exteriorBandWidth, interiorBandWidth, flags, polygonIndexGrid, interiorTest, interiorTestStrategy, options).Run();
    }
    /// <summary>Transforms world-space float points through double arithmetic and narrows index points to float as in the native convenience function.</summary>
    public static Grid<T> ToLevelSet<T>(Transform transform, ReadOnlyMemory<Vec3f> points, ReadOnlyMemory<MeshTriangle> triangles,
        ReadOnlyMemory<MeshQuad> quads = default, float halfWidth = 3, MeshToVolumeOptions? options = null) where T : unmanaged, IFloatingPointIeee754<T>
        => ToSignedDistanceField<T>(transform, points, triangles, quads, halfWidth, halfWidth, options);
    public static Grid<T> ToSignedDistanceField<T>(Transform transform, ReadOnlyMemory<Vec3f> points, ReadOnlyMemory<MeshTriangle> triangles,
        ReadOnlyMemory<MeshQuad> quads = default, float exteriorBandWidth = 3, float interiorBandWidth = 3, MeshToVolumeOptions? options = null) where T : unmanaged, IFloatingPointIeee754<T>
        => ConvertWorld<T>(transform, points, triangles, quads, exteriorBandWidth, interiorBandWidth, false, options);
    public static Grid<T> ToUnsignedDistanceField<T>(Transform transform, ReadOnlyMemory<Vec3f> points, ReadOnlyMemory<MeshTriangle> triangles,
        ReadOnlyMemory<MeshQuad> quads = default, float bandWidth = 3, MeshToVolumeOptions? options = null) where T : unmanaged, IFloatingPointIeee754<T>
        => ConvertWorld<T>(transform, points, triangles, quads, bandWidth, bandWidth, true, options);
    private static Grid<T> ConvertWorld<T>(Transform transform, ReadOnlyMemory<Vec3f> points, ReadOnlyMemory<MeshTriangle> triangles,
        ReadOnlyMemory<MeshQuad> quads, float exteriorWidth, float interiorWidth, bool unsigned, MeshToVolumeOptions? options) where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(transform); options ??= new(); options.Validate();
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double)) throw new NotSupportedException("Mesh distance fields require float or double values.");
        if (points.IsEmpty) return new(T.CreateTruncating(exteriorWidth)); // Native convenience returns an unclassified identity grid.
        var indexPoints = new Vec3f[points.Length];
        for (int i = 0; i < indexPoints.Length; i++) { var p = points.Span[i]; var q = transform.WorldToIndex(new Vec3d(p.X, p.Y, p.Z)); indexPoints[i] = new((float)q.X, (float)q.Y, (float)q.Z); }
        return Convert<T, QuadAndTriangleDataAdapter<Vec3f>>(new(indexPoints, triangles, quads), transform, exteriorWidth, interiorWidth,
            unsigned ? MeshToVolumeFlags.UnsignedDistanceField : MeshToVolumeFlags.None, options: options);
    }
}

internal sealed partial class MeshConversion<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly record struct Polygon(Vec3d A, Vec3d B, Vec3d C, Vec3d D, int Vertices);
    private readonly record struct Triangle(Vec3d A, Vec3d B, Vec3d C, int Index);
    private static readonly Coord[] Neighbors =
    [
        new(1,0,0),new(-1,0,0),new(0,1,0),new(0,-1,0),new(0,0,1),new(0,0,-1),
        new(1,0,-1),new(-1,0,-1),new(1,0,1),new(-1,0,1),new(1,1,0),new(-1,1,0),new(1,-1,0),new(-1,-1,0),
        new(0,-1,1),new(0,-1,-1),new(0,1,1),new(0,1,-1),new(-1,-1,-1),new(-1,-1,1),new(1,-1,1),new(1,-1,-1),new(-1,1,-1),new(-1,1,1),new(1,1,1),new(1,1,-1)
    ];
    private static readonly T Max = T.CreateTruncating(typeof(T) == typeof(float) ? float.MaxValue : double.MaxValue);
    private static readonly T Boundary = T.CreateTruncating(0.75), Epsilon = T.CreateTruncating(1e-7);
    private readonly IMeshDataAdapter _mesh;
    private readonly Transform _transform;
    private readonly MeshToVolumeFlags _flags;
    private readonly MeshToVolumeOptions _options;
    private readonly Grid<int>? _indexGrid;
    private readonly Func<Coord, bool>? _interiorTest;
    private readonly InteriorTestStrategy _interiorStrategy;
    private readonly Grid<T> _grid;
    private readonly VdbTree<T> _distance;
    private VdbTree<int> _indices = null!;
    private Polygon[] _polygons = [];
    private T _exterior, _interior, _voxelSize;
    private long _evaluations;
    private readonly HashSet<Coord> _allocatedLeaves = [];
    internal MeshConversion(IMeshDataAdapter mesh, Transform transform, float exterior, float interior, MeshToVolumeFlags flags,
        Grid<int>? indexGrid, Func<Coord, bool>? interiorTest, InteriorTestStrategy interiorStrategy, MeshToVolumeOptions options)
    {
        _mesh = mesh; _transform = transform; _exterior = T.CreateTruncating(exterior); _interior = T.CreateTruncating(interior); _flags = flags; _options = options;
        _indexGrid = indexGrid; _interiorTest = interiorTest; _interiorStrategy = interiorStrategy; _grid = new(Max) { Transform = transform.Copy() }; _distance = _grid.Tree;
    }
    internal Grid<T> Run()
    {
        if (!T.IsFinite(_exterior) || T.IsNaN(_interior)) return _grid;
        _voxelSize = T.CreateTruncating(_transform.VoxelSize.X);
        if (!T.IsFinite(_voxelSize) || _voxelSize == T.Zero) return _grid;
        _exterior *= _voxelSize; if (_interior < Max) _interior *= _voxelSize;
        bool signed = (_flags & MeshToVolumeFlags.UnsignedDistanceField) == 0;
        if (!signed) _interior = T.Zero;
        SnapshotPolygons();
        _indices = new(_indexGrid?.Background ?? -1);
        if (_indexGrid is not null) { _indexGrid.SetTree(_indices); _indexGrid.Transform = _transform.Copy(); }
        _grid.GridClass = signed ? GridClass.LevelSet : GridClass.Unknown;
        for (int i = 0; i < _polygons.Length; i++)
        {
            _options.CancellationToken.ThrowIfCancellationRequested(); var p = _polygons[i]; if (p.Vertices is not (3 or 4)) continue;
            Rasterize(new(p.A, p.B, p.C, i)); if (p.Vertices == 4) Rasterize(new(p.A, p.D, p.C, i));
        }
        Checkpoint();
        if (signed)
        {
            if (_interiorTest is null) TraceExterior(); else EvaluateInterior();
            if (_interiorTest is null || _interiorStrategy != InteriorTestStrategy.EveryVoxel)
            {
                ComputeIntersectingSigns(); Checkpoint();
                if ((_flags & MeshToVolumeFlags.DisableIntersectingVoxelRemoval) == 0) RemoveIntersecting();
            }
        }
        Checkpoint();
        if (!_distance.HasActiveVoxels) { _distance.Clear(); _distance.SetBackground(_exterior); return _grid; }
        foreach (var leaf in _distance.EnumerateLeaves()) foreach (int offset in leaf.ActiveOffsets)
        {
            T value = leaf.GetValue(offset); leaf.SetValueOnly(offset, (signed && !(value < T.Zero) ? -_voxelSize : _voxelSize) * T.Sqrt(T.Abs(value)));
        }
        if (signed) { _distance.SetBackground(_exterior); SignedFloodFill.WithValues(_distance, _exterior, -_interior); }
        else ChangeBackground.Apply(_distance, _exterior);
        Checkpoint();
        if (_interior > _voxelSize * T.CreateTruncating(2) || _exterior > _voxelSize * T.CreateTruncating(2)) Expand();
        Checkpoint();
        if (signed && (_flags & MeshToVolumeFlags.DisableRenormalization) == 0) Renormalize();
        Checkpoint();
        if ((_flags & MeshToVolumeFlags.DisableNarrowBandTrimming) == 0 && T.Min(_interior, _exterior) < _voxelSize * T.CreateTruncating(4)) Trim(signed);
        return _grid;
    }
    private void SnapshotPolygons()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(_mesh.PolygonCount); ArgumentOutOfRangeException.ThrowIfNegative(_mesh.PointCount);
        _polygons = new Polygon[_mesh.PolygonCount];
        for (int i = 0; i < _polygons.Length; i++)
        {
            int count = _mesh.VertexCount(i); if (count is not (3 or 4)) continue;
            Vec3d a = Read(0), b = Read(1), c = Read(2), d = count == 4 ? Read(3) : default; _polygons[i] = new(a, b, c, d, count);
            Vec3d Read(int vertex)
            {
                Vec3d point = _mesh.GetIndexSpacePoint(i, vertex);
                if (!point.IsFinite) throw new ArgumentException("Mesh points must be finite.", "mesh");
                Coord.Floor(point); return point;
            }
        }
    }
    private void Rasterize(Triangle triangle)
    {
        double extent = (Vec3d.Max(triangle.A, Vec3d.Max(triangle.B, triangle.C)) - Vec3d.Min(triangle.A, Vec3d.Min(triangle.B, triangle.C))).Sorted().Z;
        int subdivisions = _polygons.Length < 1000 ? checked((int)(extent / 16)) : 0;
        Subdivide(triangle, subdivisions, _polygons.Length);
    }
    private void Subdivide(Triangle triangle, int subdivisions, long polygonCount)
    {
        if (subdivisions <= 0 || polygonCount >= 1000) { Voxelize(triangle); return; }
        Checkpoint(); Vec3d ac = (triangle.A + triangle.C) * 0.5, bc = (triangle.B + triangle.C) * 0.5, ab = (triangle.A + triangle.B) * 0.5;
        Subdivide(new(triangle.A, ab, ac, triangle.Index), subdivisions - 1, polygonCount * 4);
        Subdivide(new(ab, bc, ac, triangle.Index), subdivisions - 1, polygonCount * 4);
        Subdivide(new(ab, triangle.B, bc, triangle.Index), subdivisions - 1, polygonCount * 4);
        Subdivide(new(ac, bc, triangle.C, triangle.Index), subdivisions - 1, polygonCount * 4);
    }
    private void Voxelize(Triangle triangle)
    {
        Coord first = Coord.Floor(triangle.A); Stack<Coord> pending = new(); HashSet<Coord> visited = [];
        pending.Push(first); visited.Add(first); UpdateDistance(first, triangle);
        while (pending.TryPop(out var coordinate))
        {
            Checkpoint();
            foreach (var offset in Neighbors) { Coord neighbor = Add(coordinate, offset); if (visited.Add(neighbor) && UpdateDistance(neighbor, triangle)) pending.Push(neighbor); }
        }
    }
    private bool UpdateDistance(Coord coordinate, Triangle triangle)
    {
        Evaluate(); Vec3d center = coordinate.AsVec3d();
        T distance = T.CreateTruncating((center - Proximity.ClosestPointOnTriangleToPoint(triangle.A, triangle.C, triangle.B, center, out _)).LengthSquared);
        if (T.IsNaN(distance)) return false;
        T old = _distance.GetValue(coordinate);
        if (distance < old) { ReserveLeaf(coordinate); _distance.SetValueOn(coordinate, distance); _indices.SetValueOn(coordinate, triangle.Index); }
        else if (distance == old) _indices.SetValueOnly(coordinate, System.Math.Min(triangle.Index, _indices.GetValue(coordinate)));
        return !(distance > Boundary);
    }
    private Vec3d ClosestPoint(Coord coordinate, int polygonIndex)
    {
        if ((uint)polygonIndex >= (uint)_polygons.Length) throw new InvalidOperationException("Distance topology contains an invalid polygon index.");
        var p = _polygons[polygonIndex]; Vec3d center = coordinate.AsVec3d();
        var result = Proximity.ClosestPointOnTriangleToPoint(p.A, p.C, p.B, center, out _);
        if (p.Vertices == 4)
        {
            var second = Proximity.ClosestPointOnTriangleToPoint(p.A, p.D, p.C, center, out _);
            if ((center - second).LengthSquared < (center - result).LengthSquared) result = second;
        }
        return result;
    }
    private static Coord Add(Coord a, Coord b) => new(checked(a.X + b.X), checked(a.Y + b.Y), checked(a.Z + b.Z));
    private void Evaluate() { if (++_evaluations > _options.MaximumVoxelEvaluations) throw new InvalidOperationException("Mesh conversion exceeded MaximumVoxelEvaluations."); }
    private void ReserveLeaf(Coord coordinate)
    {
        Coord origin = coordinate & -8; if (_allocatedLeaves.Contains(origin)) return;
        if (_allocatedLeaves.Count >= _options.MaximumLeaves) throw new InvalidOperationException("Mesh conversion exceeded MaximumLeaves.");
        _allocatedLeaves.Add(origin);
    }
    private void Checkpoint() => _options.CancellationToken.ThrowIfCancellationRequested();
}
