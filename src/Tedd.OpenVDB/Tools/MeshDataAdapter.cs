// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

/// <summary>Triangle/quad polygon access in the output grid's index space.</summary>
public interface IMeshDataAdapter
{
    int PolygonCount { get; }
    int PointCount { get; }
    int VertexCount(int polygon);
    Vec3d GetIndexSpacePoint(int polygon, int vertex);
}

/// <summary>Contiguous float/double points and triangles followed by quads, without copying caller memory.</summary>
public readonly struct QuadAndTriangleDataAdapter<TPoint> : IMeshDataAdapter where TPoint : unmanaged
{
    private readonly ReadOnlyMemory<TPoint> _points;
    private readonly ReadOnlyMemory<MeshTriangle> _triangles;
    private readonly ReadOnlyMemory<MeshQuad> _quads;
    public QuadAndTriangleDataAdapter(ReadOnlyMemory<TPoint> points, ReadOnlyMemory<MeshTriangle> triangles, ReadOnlyMemory<MeshQuad> quads = default)
    {
        if (typeof(TPoint) != typeof(Vec3f) && typeof(TPoint) != typeof(Vec3d)) throw new NotSupportedException("Mesh points must be Vec3f or Vec3d.");
        _points = points; _triangles = triangles; _quads = quads; PolygonCount = checked(triangles.Length + quads.Length);
    }
    public int PointCount => _points.Length;
    public int PolygonCount { get; }
    public int VertexCount(int polygon)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(polygon);
        if (polygon < _triangles.Length) return 3;
        return _quads.Span[polygon - _triangles.Length].VertexCount;
    }
    public Vec3d GetIndexSpacePoint(int polygon, int vertex)
    {
        int index = polygon < _triangles.Length ? _triangles.Span[polygon][vertex] : _quads.Span[polygon - _triangles.Length][vertex];
        TPoint point = _points.Span[index];
        if (typeof(TPoint) == typeof(Vec3d)) return Unsafe.As<TPoint, Vec3d>(ref point);
        var single = Unsafe.As<TPoint, Vec3f>(ref point); return new(single.X, single.Y, single.Z);
    }
}
[Flags]
public enum MeshToVolumeFlags { None = 0, UnsignedDistanceField = 1, DisableIntersectingVoxelRemoval = 2, DisableRenormalization = 4, DisableNarrowBandTrimming = 8 }
public enum InteriorTestStrategy { EveryVoxel, EveryTile }
/// <summary>Resource limits and cancellation for mesh conversion; limits apply to the generated distance tree and geometric evaluations.</summary>
public sealed record MeshToVolumeOptions
{
    public int MaximumLeaves { get; init; } = 1_000_000;
    public long MaximumVoxelEvaluations { get; init; } = 100_000_000;
    public CancellationToken CancellationToken { get; init; }
    internal void Validate()
    { ArgumentOutOfRangeException.ThrowIfNegative(MaximumLeaves); ArgumentOutOfRangeException.ThrowIfNegative(MaximumVoxelEvaluations); CancellationToken.ThrowIfCancellationRequested(); }
}
