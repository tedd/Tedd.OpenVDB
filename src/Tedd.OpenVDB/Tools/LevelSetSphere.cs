// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/LevelSetSphere.h.
using System.Numerics;
using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.Tools;

/// <summary>Constructs OpenVDB narrow-band sphere level sets using the source leapfrog rasterization.</summary>
public static class LevelSetSphere
{
    public static Grid<float> Create(float radius, Vec3f center, float voxelSize, float halfWidth = 3,
        CancellationToken cancellationToken = default) =>
        Create(radius, new Vec3d(center.X, center.Y, center.Z), voxelSize, halfWidth, cancellationToken);

    public static Grid<double> Create(double radius, Vec3d center, double voxelSize, double halfWidth = 3,
        CancellationToken cancellationToken = default) => Create<double>(radius, center, voxelSize, halfWidth, cancellationToken);

    /// <summary>Creates a level set with world-space radius/center and voxel-space half-width.</summary>
    /// <remarks>
    /// Half-width must exceed one voxel. Radii below 1.5 voxels return an empty LevelSet grid.
    /// Samples exactly on the band boundary are inactive. Finite grid-domain limits are checked.
    /// </remarks>
    public static Grid<T> Create<T>(T radius, Vec3d center, T voxelSize, T halfWidth,
        CancellationToken cancellationToken = default) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (!T.IsFinite(radius) || !(radius > T.Zero)) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!center.IsFinite) throw new ArgumentOutOfRangeException(nameof(center));
        if (!T.IsFinite(voxelSize) || !(voxelSize > T.Zero)) throw new ArgumentOutOfRangeException(nameof(voxelSize));
        if (!T.IsFinite(halfWidth) || !(halfWidth > T.One)) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        cancellationToken.ThrowIfCancellationRequested();
        var background = voxelSize * halfWidth;
        if (!T.IsFinite(background)) throw new ArgumentOutOfRangeException(nameof(halfWidth), "The world-space narrow band must be finite.");
        var grid = new Grid<T>(background)
        {
            Transform = Transform.CreateLinearTransform(double.CreateChecked(voxelSize)),
            GridClass = GridClass.LevelSet
        };
        var radiusInVoxels = radius / voxelSize;
        if (radiusInVoxels < T.CreateChecked(1.5)) return grid;
        var maximumRadius = radiusInVoxels + halfWidth;
        var centerX = T.CreateChecked(center.X) / voxelSize;
        var centerY = T.CreateChecked(center.Y) / voxelSize;
        var centerZ = T.CreateChecked(center.Z) / voxelSize;
        var minimumX = GridBound(T.Floor(centerX - maximumRadius));
        var maximumX = GridBound(T.Ceiling(centerX + maximumRadius));
        var minimumY = GridBound(T.Floor(centerY - maximumRadius));
        var maximumY = GridBound(T.Ceiling(centerY + maximumRadius));
        var minimumZ = GridBound(T.Floor(centerZ - maximumRadius));
        var maximumZ = GridBound(T.Ceiling(centerZ + maximumRadius));
        var accessor = grid.GetAccessor();
        // Native tbb::blocked_range uses an exclusive upper X bound; Y and Z are inclusive.
        for (long x = minimumX; x < maximumX; ++x)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deltaX = T.CreateChecked(x) - centerX;
            var squareX = deltaX * deltaX;
            for (long y = minimumY; y <= maximumY; ++y)
            {
                var deltaY = T.CreateChecked(y) - centerY;
                var squareXY = deltaY * deltaY + squareX;
                for (long z = minimumZ; z <= maximumZ;)
                {
                    var deltaZ = T.CreateChecked(z) - centerZ;
                    var value = T.Sqrt(squareXY + deltaZ * deltaZ) - radiusInVoxels;
                    var distance = T.Abs(value);
                    if (distance < halfWidth)
                    {
                        accessor.SetValueOn(new Coord((int)x, (int)y, (int)z), voxelSize * value);
                        ++z;
                    }
                    else z += 1 + long.CreateChecked(T.Floor(distance - halfWidth));
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        SignedFloodFill.Apply(grid.Tree);
        return grid;
    }

    private static int GridBound<T>(T value) where T : IFloatingPointIeee754<T>
    {
        var bound = double.CreateChecked(value);
        if (!double.IsFinite(bound) || bound < int.MinValue || bound > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(value), "The sphere narrow band exceeds the signed grid index domain.");
        return (int)bound;
    }
}
