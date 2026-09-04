// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

public static partial class MeshToVolume
{
    /// <summary>
    /// Traces exterior signs in an allocated squared-distance tree. Exterior values become negative;
    /// values at or below the native 0.75 boundary threshold retain their signs and activity is unchanged.
    /// </summary>
    public static void TraceExteriorBoundaries<T>(VdbTree<T> tree, CancellationToken cancellationToken = default)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(tree);
        cancellationToken.ThrowIfCancellationRequested();
        MeshConversion<T>.TraceExterior(tree, cancellationToken);
    }

    /// <summary>
    /// Creates a level-set box using the native helper's float index-space corners.
    /// Like OpenVDB, only the two opposite world-space bounds are transformed before constructing the box.
    /// </summary>
    public static Grid<T> CreateLevelSetBox<T>(BBoxd bounds, Transform transform, double halfWidth = 3, MeshToVolumeOptions? options = null)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(transform);
        Vec3d a = transform.WorldToIndex(bounds.Min), b = transform.WorldToIndex(bounds.Max);
        Vec3f min = new((float)a.X, (float)a.Y, (float)a.Z), max = new((float)b.X, (float)b.Y, (float)b.Z);
        Vec3f[] points =
        [
            new(min.X,min.Y,min.Z),new(min.X,min.Y,max.Z),new(max.X,min.Y,max.Z),new(max.X,min.Y,min.Z),
            new(min.X,max.Y,min.Z),new(min.X,max.Y,max.Z),new(max.X,max.Y,max.Z),new(max.X,max.Y,min.Z)
        ];
        MeshQuad[] faces = [new(0, 1, 2, 3), new(7, 6, 5, 4), new(4, 5, 1, 0), new(6, 7, 3, 2), new(0, 3, 7, 4), new(1, 5, 6, 2)];
        return Convert<T, QuadAndTriangleDataAdapter<Vec3f>>(new(points, default, faces), transform, (float)halfWidth, (float)halfWidth, options: options);
    }
}
