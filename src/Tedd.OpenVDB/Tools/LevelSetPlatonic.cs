// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/LevelSetPlatonic.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

/// <summary>Constructs narrow-band level sets of the five native platonic solids.</summary>
public static class LevelSetPlatonic
{
    /// <summary>Creates a tetrahedron (4), cube (6), octahedron (8), dodecahedron (12), or icosahedron (20).</summary>
    /// <remarks>Vertices, scaling, center and convenience parameters use native float precision for both float and double output grids. Half-width is measured in voxels. Negative and zero scales retain native mesh-conversion behavior.</remarks>
    public static Grid<T> Create<T>(int faceCount, float scale = 1, Vec3f center = default, float voxelSize = .1f,
        float halfWidth = 3, MeshToVolumeOptions? options = null) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double)) throw new NotSupportedException("Platonic level sets require float or double values.");
        options ??= new(); options.Validate();
        Transform transform = Transform.CreateLinearTransform(voxelSize);
        var (points, triangles, quads) = Mesh(faceCount);
        for (int i = 0; i < points.Length; ++i) points[i] = scale * points[i] + center;
        return MeshToVolume.ToLevelSet<T>(transform, points, triangles, quads, halfWidth, options);
    }
    public static Grid<T> CreateTetrahedron<T>(float scale = 1, Vec3f center = default, float voxelSize = .1f, float halfWidth = 3, MeshToVolumeOptions? options = null) where T : unmanaged, IFloatingPointIeee754<T> => Create<T>(4, scale, center, voxelSize, halfWidth, options);
    public static Grid<T> CreateCube<T>(float scale = 1, Vec3f center = default, float voxelSize = .1f, float halfWidth = 3, MeshToVolumeOptions? options = null) where T : unmanaged, IFloatingPointIeee754<T> => Create<T>(6, scale, center, voxelSize, halfWidth, options);
    public static Grid<T> CreateOctahedron<T>(float scale = 1, Vec3f center = default, float voxelSize = .1f, float halfWidth = 3, MeshToVolumeOptions? options = null) where T : unmanaged, IFloatingPointIeee754<T> => Create<T>(8, scale, center, voxelSize, halfWidth, options);
    public static Grid<T> CreateDodecahedron<T>(float scale = 1, Vec3f center = default, float voxelSize = .1f, float halfWidth = 3, MeshToVolumeOptions? options = null) where T : unmanaged, IFloatingPointIeee754<T> => Create<T>(12, scale, center, voxelSize, halfWidth, options);
    public static Grid<T> CreateIcosahedron<T>(float scale = 1, Vec3f center = default, float voxelSize = .1f, float halfWidth = 3, MeshToVolumeOptions? options = null) where T : unmanaged, IFloatingPointIeee754<T> => Create<T>(20, scale, center, voxelSize, halfWidth, options);

    private static (Vec3f[] Points, MeshTriangle[] Triangles, MeshQuad[] Quads) Mesh(int faceCount) => faceCount switch
    {
        4 => (
            [new(0.0f, 1.0f, 0.0f), new(-0.942810297f, -0.333329707f, 0.0f), new(0.471405149f, -0.333329707f, 0.816497624f), new(0.471405149f, -0.333329707f, -0.816497624f)],
            [new(0, 2, 3), new(0, 3, 1), new(0, 1, 2), new(1, 3, 2)],
            []),
        6 => (
            [new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, 0.5f), new(-0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f)],
            [],
            [new(1, 0, 4, 5), new(2, 1, 5, 6), new(3, 2, 6, 7), new(0, 3, 7, 4), new(2, 3, 0, 1), new(5, 4, 7, 6)]),
        8 => (
            [new(0.0f, 0.0f, -1.0f), new(1.0f, 0.0f, 0.0f), new(0.0f, 0.0f, 1.0f), new(-1.0f, 0.0f, 0.0f), new(0.0f, -1.0f, 0.0f), new(0.0f, 1.0f, 0.0f)],
            [new(0, 4, 3), new(0, 1, 4), new(1, 2, 4), new(2, 3, 4), new(0, 3, 5), new(0, 5, 1), new(1, 5, 2), new(2, 5, 3)],
            []),
        12 => (
            [new(0.354437858f, 0.487842113f, -0.789344311f), new(0.573492587f, -0.186338872f, -0.78934437f), new(0.0f, -0.603005826f, -0.78934443f), new(-0.573492587f, -0.186338872f, -0.78934437f), new(-0.354437858f, 0.487842113f, -0.789344311f), new(-0.573492587f, 0.789345026f, -0.186338797f), new(-0.927930415f, -0.301502913f, -0.186338872f), new(0.0f, -0.975683928f, -0.186338902f), new(0.927930415f, -0.301502913f, -0.186338872f), new(0.573492587f, 0.789345026f, -0.186338797f), new(0.0f, 0.975683868f, 0.186338902f), new(-0.927930415f, 0.301502913f, 0.186338872f), new(-0.573492587f, -0.789345026f, 0.186338797f), new(0.573492587f, -0.789345026f, 0.186338797f), new(0.927930415f, 0.301502913f, 0.186338872f), new(0.0f, 0.603005826f, 0.78934443f), new(0.573492587f, 0.186338872f, 0.78934437f), new(0.354437858f, -0.487842113f, 0.789344311f), new(-0.354437858f, -0.487842113f, 0.789344311f), new(-0.573492587f, 0.186338872f, 0.78934437f)],
            [new(0, 3, 4), new(0, 10, 9), new(0, 8, 1), new(1, 7, 2), new(2, 6, 3), new(3, 5, 4), new(5, 15, 10), new(6, 19, 11), new(7, 18, 12), new(8, 17, 13), new(9, 16, 14), new(15, 17, 16)],
            [new(0, 1, 2, 3), new(0, 4, 5, 10), new(0, 9, 14, 8), new(1, 8, 13, 7), new(2, 7, 12, 6), new(3, 6, 11, 5), new(5, 11, 19, 15), new(6, 12, 18, 19), new(7, 13, 17, 18), new(8, 14, 16, 17), new(9, 10, 15, 16), new(15, 19, 18, 17)]),
        20 => (
            [new(0.0f, 0.0f, -1.0f), new(0.0f, 0.894427359f, -0.447213143f), new(0.850650847f, 0.276393682f, -0.447213203f), new(0.525731206f, -0.723606944f, -0.447213262f), new(-0.525731206f, -0.723606944f, -0.447213262f), new(-0.850650847f, 0.276393682f, -0.447213203f), new(-0.525731206f, 0.723606944f, 0.447213262f), new(-0.850650847f, -0.276393682f, 0.447213203f), new(0.0f, -0.894427359f, 0.447213143f), new(0.850650847f, -0.276393682f, 0.447213203f), new(0.525731206f, 0.723606944f, 0.447213262f), new(0.0f, 0.0f, 1.0f)],
            [new(2, 0, 1), new(3, 0, 2), new(4, 0, 3), new(5, 0, 4), new(1, 0, 5), new(6, 1, 5), new(7, 5, 4), new(8, 4, 3), new(9, 3, 2), new(10, 2, 1), new(10, 1, 6), new(6, 5, 7), new(7, 4, 8), new(8, 3, 9), new(9, 2, 10), new(6, 11, 10), new(10, 11, 9), new(9, 11, 8), new(8, 11, 7), new(7, 11, 6)],
            []),
        _ => throw new ArgumentOutOfRangeException(nameof(faceCount), "A platonic solid must have 4, 6, 8, 12 or 20 faces.")
    };
}
