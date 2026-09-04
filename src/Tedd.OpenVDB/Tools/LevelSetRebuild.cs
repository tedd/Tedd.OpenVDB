// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/LevelSetRebuild.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

/// <summary>Reconstructs a signed-distance narrow band through native polygon extraction and mesh conversion.</summary>
public static class LevelSetRebuild
{
    /// <summary>Rebuilds an isosurface with equal interior/exterior half widths measured in output voxels.</summary>
    /// <remarks>The result copies the chosen transform and receives the mesh converter's level-set classification; input metadata is not copied. Native rebuilding supports float and double grids. Mesh vertices pass through float world and index coordinates, including for double grids.</remarks>
    public static Grid<T> Rebuild<T>(Grid<T> grid, float isovalue = 0, float halfWidth = 3, Transform? transform = null,
        MeshToVolumeOptions? options = null, ulong maximumCells = ulong.MaxValue) where T : unmanaged, IFloatingPointIeee754<T>
        => Rebuild(grid, isovalue, halfWidth, halfWidth, transform, options, maximumCells);

    /// <summary>Rebuilds with independently specified exterior and interior widths in output voxels.</summary>
    /// <remarks>MaximumCells bounds cumulative meshing candidates; options separately bound mesh conversion and provide cancellation for the entire operation. Input grids, transforms and existing accessors remain unchanged on success or failure.</remarks>
    public static Grid<T> Rebuild<T>(Grid<T> grid, float isovalue, float exteriorBandWidth, float interiorBandWidth,
        Transform? transform = null, MeshToVolumeOptions? options = null, ulong maximumCells = ulong.MaxValue)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double)) throw new NotSupportedException("Level-set rebuild requires float or double values.");
        options ??= new(); options.Validate();
        CancellationToken token = options.CancellationToken;
        var mesher = new VolumeToMesh(isovalue) { MaximumCells = maximumCells };
        mesher.Extract(grid, token);
        Transform outputTransform = (transform ?? grid.Transform).Copy();
        var points = GC.AllocateUninitializedArray<Vec3f>(mesher.Points.Length);
        ReadOnlySpan<Vec3f> source = mesher.Points.Span;
        for (int i = 0; i < points.Length; ++i)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            Vec3f point = source[i];
            Vec3d index = outputTransform.WorldToIndex(new Vec3d(point.X, point.Y, point.Z));
            points[i] = new((float)index.X, (float)index.Y, (float)index.Z);
        }
        int count = 0;
        foreach (PolygonPool pool in mesher.PolygonPools) count = checked(count + pool.NumQuads + pool.NumTriangles);
        var polygons = GC.AllocateUninitializedArray<MeshQuad>(count);
        int position = 0;
        foreach (PolygonPool pool in mesher.PolygonPools)
        {
            token.ThrowIfCancellationRequested();
            pool.Quads.Span.CopyTo(polygons.AsSpan(position)); position += pool.NumQuads;
            foreach (MeshTriangle triangle in pool.Triangles.Span) polygons[position++] = new(triangle.A, triangle.B, triangle.C, -1);
        }
        // Keeping each pool's quads before its triangles preserves native primitive ordering.
        var adapter = new QuadAndTriangleDataAdapter<Vec3f>(points, default, polygons);
        return MeshToVolume.Convert<T, QuadAndTriangleDataAdapter<Vec3f>>(adapter, outputTransform,
            exteriorBandWidth, interiorBandWidth, MeshToVolumeFlags.DisableRenormalization, options: options);
    }

    /// <summary>Native value-typed entry point; isovalue and widths are narrowed to float before reconstruction.</summary>
    public static Grid<T> RebuildTyped<T>(Grid<T> grid, T isovalue, T exteriorBandWidth, T interiorBandWidth,
        Transform? transform = null, MeshToVolumeOptions? options = null, ulong maximumCells = ulong.MaxValue)
        where T : unmanaged, IFloatingPointIeee754<T>
        => Rebuild(grid, float.CreateTruncating(isovalue), float.CreateTruncating(exteriorBandWidth), float.CreateTruncating(interiorBandWidth), transform, options, maximumCells);
}
