// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Transform.h, Transform.cc and Maps.h.
namespace Tedd.OpenVDB.Math;

/// <summary>Mutable index/world transform that retains native map identity and specialized arithmetic.</summary>
/// <remarks>Maps are immutable snapshots; mutating a transform replaces its map. Concurrent access requires external synchronization during mutation.</remarks>
public sealed partial class Transform
{
    private MapBase _map = null!;
    private Mat4d _matrix, _inverse;
    private NonlinearFrustumMap? _frustum;
    /// <summary>The native default constructor uses an identity ScaleMap.</summary>
    public Transform() => SetMap(new ScaleMap());
    /// <summary>Creates an explicit AffineMap without simplification. The matrix factory follows native simplification.</summary>
    public Transform(Mat4d matrix) => SetMap(new AffineMap(matrix));
    public Transform(NonlinearFrustumMap frustum) { ArgumentNullException.ThrowIfNull(frustum); SetMap(frustum.Copy()); }
    /// <summary>Constructs from a map using native automatic linear simplification.</summary>
    public Transform(MapBase map)
    {
        ArgumentNullException.ThrowIfNull(map);
        SetMap(map.IsLinear && map is not UniformScaleMap ? MapBase.Simplify(map.GetAffineMap()) : map.Copy());
    }
    /// <summary>Creates a transform with exact map identity unless simplification is requested.</summary>
    public static Transform FromMap(MapBase map, bool simplify = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (simplify) return new(map);
        var result = new Transform(); result.SetMap(map.Copy()); return result;
    }
    public MapBase BaseMap => _map;
    /// <summary>Replaces the map from native transform framing without automatic simplification.</summary>
    public void Read(Stream stream, bool leaveOpen = true, IO.VdbReadOptions? options = null) => SetMap(IO.VdbFile.ReadTransformMap(stream, leaveOpen, options)._map);
    public void Write(Stream stream, bool leaveOpen = true) => IO.VdbFile.WriteTransformMap(stream, this, leaveOpen);
    /// <summary>The affine matrix, or the secondary affine matrix for a frustum.</summary>
    public Mat4d Matrix => _matrix;
    public Mat4d InverseMatrix => _inverse;
    public NonlinearFrustumMap? FrustumMap => _frustum;
    public string MapType => _map.MapType;
    public bool IsLinear => _map.IsLinear;
    public bool IsIdentity => _map.IsIdentity;
    public double VoxelVolume => _map.Determinant;
    public double VoxelVolumeAt(Vec3d location) => _map.DeterminantAt(location);
    public Vec3d VoxelSize => _map.VoxelSize;
    public Vec3d VoxelSizeAt(Vec3d location) => _map.VoxelSizeAt(location);
    public bool HasUniformScale => _map.HasUniformScale;
    public static Transform CreateLinearTransform(double voxelSize = 1) => new(new UniformScaleMap(voxelSize));
    public static Transform CreateLinearTransform(Mat4d matrix) => new(new AffineMap(matrix));
    public static Transform CreateFrustumTransform(BBoxd boundingBox, double taper, double depth, double voxelSize = 1) =>
        new(new NonlinearFrustumMap(boundingBox, taper, depth).PreScale(new Vec3d(voxelSize)));
    /// <summary>Copies the map, applying the same linear simplification as native Transform.copy().</summary>
    public Transform Copy() => new(_map.Copy());
    public Transform Inverse() => FromMap(_map.InverseMap());
    public Vec3d IndexToWorld(Vec3d coordinate) => _map.ApplyMap(coordinate);
    public Vec3d IndexToWorld(Coord coordinate) => _map.ApplyMap(coordinate.AsVec3d());
    public Vec3d WorldToIndex(Vec3d coordinate) => _map.ApplyInverseMap(coordinate);
    public Coord WorldToIndexCellCentered(Vec3d coordinate) => Coord.Round(WorldToIndex(coordinate));
    public Coord WorldToIndexNodeCentered(Vec3d coordinate) => Coord.Floor(WorldToIndex(coordinate));
    public Vec3d ApplyJacobian(Vec3d vector) => _map.ApplyJacobian(vector);
    public Vec3d ApplyInverseJacobian(Vec3d vector) => _map.ApplyInverseJacobian(vector);
    public Vec3d ApplyJacobianTranspose(Vec3d vector) => _map.ApplyJacobianTranspose(vector);
    public Vec3d ApplyInverseJacobianTranspose(Vec3d vector) => _map.ApplyInverseJacobianTranspose(vector);
    public Vec3d ApplyJacobian(Vec3d vector, Vec3d location) => _map.ApplyJacobian(vector, location);
    public Vec3d ApplyInverseJacobian(Vec3d vector, Vec3d location) => _map.ApplyInverseJacobian(vector, location);
    public Vec3d ApplyJacobianTranspose(Vec3d vector, Vec3d location) => _map.ApplyJacobianTranspose(vector, location);
    public Vec3d ApplyInverseJacobianTranspose(Vec3d vector, Vec3d location) => _map.ApplyInverseJacobianTranspose(vector, location);
    public Mat3d Jacobian(Vec3d location) => _map.Jacobian(location);
    public Mat3d ApplyInverseJacobianCurvature(Mat3d hessian) => _map.ApplyInverseJacobianCurvature(hessian);
    public Mat3d ApplyInverseJacobianCurvature(Mat3d hessian, Vec3d gradient, Vec3d location) => _map.ApplyInverseJacobianCurvature(hessian, gradient, location);
    public void PreTranslate(Vec3d value) => SetMap(_map.PreTranslate(value));
    public void PostTranslate(Vec3d value) => SetMap(_map.PostTranslate(value));
    public void PreScale(double value) => PreScale(new Vec3d(value));
    public void PostScale(double value) => PostScale(new Vec3d(value));
    public void PreScale(Vec3d value) => SetMap(_map.PreScale(value));
    public void PostScale(Vec3d value) => SetMap(_map.PostScale(value));
    public void PreRotate(double radians, Axis axis = Axis.X) => SetMap(_map.PreRotate(radians, axis));
    public void PostRotate(double radians, Axis axis = Axis.X) => SetMap(_map.PostRotate(radians, axis));
    public void PreShear(double value, Axis axis0, Axis axis1) => SetMap(_map.PreShear(value, axis0, axis1));
    public void PostShear(double value, Axis axis0, Axis axis1) => SetMap(_map.PostShear(value, axis0, axis1));
    public void PreMult(Mat4d matrix) => SetMatrix(matrix * _matrix);
    public void PostMult(Mat4d matrix) => SetMatrix(_matrix * matrix);
    public void PreMult(Mat3d matrix) => PreMult(matrix.ToMat4());
    public void PostMult(Mat3d matrix) => PostMult(matrix.ToMat4());
    public void SetFrustumTaper(double taper) => SetMap((_frustum ?? throw new InvalidOperationException("This is not a frustum transform.")).WithTaper(taper));
    public void SetFrustumDepth(double depth) => SetMap((_frustum ?? throw new InvalidOperationException("This is not a frustum transform.")).WithDepth(depth));
    public void IndexToWorld(ReadOnlySpan<Vec3d> source, Span<Vec3d> destination) => TransformPoints(source, destination, false);
    public void WorldToIndex(ReadOnlySpan<Vec3d> source, Span<Vec3d> destination) => TransformPoints(source, destination, true);
    private void TransformPoints(ReadOnlySpan<Vec3d> source, Span<Vec3d> destination, bool inverse)
    {
        if (destination.Length < source.Length) throw new ArgumentException("The destination is too short.", nameof(destination));
        MapBase map = _map;
        if (source.Overlaps(destination, out int offset) && offset > 0)
            for (int i = source.Length - 1; i >= 0; --i) destination[i] = inverse ? map.ApplyInverseMap(source[i]) : map.ApplyMap(source[i]);
        else
            for (int i = 0; i < source.Length; ++i) destination[i] = inverse ? map.ApplyInverseMap(source[i]) : map.ApplyMap(source[i]);
    }
    private void SetMatrix(Mat4d matrix) => SetMap(_frustum is null ? MapBase.Simplify(new AffineMap(matrix)) : _frustum.WithSecondMap(matrix));
    private void SetMap(MapBase map)
    {
        Mat4d matrix = map.Matrix, inverse = map.InverseMatrix;
        _map = map; _matrix = matrix; _inverse = inverse; _frustum = map as NonlinearFrustumMap;
    }
}
