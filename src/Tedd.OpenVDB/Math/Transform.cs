// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Transform.h, Transform.cc and Maps.h.
namespace Tedd.OpenVDB.Math;

/// <summary>Mutable index/world transform with affine or nonlinear frustum mapping.</summary>
/// <remarks>Pre/post operations act on the affine component, including the secondary map of a frustum.
/// Concurrent readers are supported only while the transform is not being modified.</remarks>
public sealed partial class Transform
{
    private Mat4d _matrix, _inverse;
    private NonlinearFrustumMap? _frustum;
    public Transform() : this(Mat4d.Identity) { }
    public Transform(Mat4d matrix) => SetMatrix(matrix);
    public Transform(NonlinearFrustumMap frustum)
    {
        ArgumentNullException.ThrowIfNull(frustum);
        SetMatrix(frustum.SecondMap); _frustum = frustum.Copy();
    }
    /// <summary>The complete affine matrix, or the affine secondary matrix for a frustum.</summary>
    public Mat4d Matrix => _matrix;
    public Mat4d InverseMatrix => _inverse;
    public NonlinearFrustumMap? FrustumMap => _frustum;
    public string MapType => _frustum is null ? "AffineMap" : "NonlinearFrustumMap";
    public bool IsLinear => _frustum is null;
    public bool IsIdentity => _frustum?.IsIdentity ?? _matrix.ApproxEquals(Mat4d.Identity);
    /// <summary>Determinant of the affine component, following OpenVDB's positionless overload.</summary>
    public double VoxelVolume => _matrix.Determinant3x3;
    public double VoxelVolumeAt(Vec3d indexLocation) => _frustum?.DeterminantAt(indexLocation) ?? VoxelVolume;
    public Vec3d VoxelSize => _frustum?.VoxelSize ?? new Vec3d(_matrix.TransformDirection(Vec3d.UnitX).Length, _matrix.TransformDirection(Vec3d.UnitY).Length, _matrix.TransformDirection(Vec3d.UnitZ).Length);
    public Vec3d VoxelSizeAt(Vec3d indexLocation) => _frustum?.VoxelSizeAt(indexLocation) ?? VoxelSize;
    public bool HasUniformScale
    {
        get
        {
            if (_frustum is not null) return false;
            var x = _matrix.TransformDirection(Vec3d.UnitX);
            var y = _matrix.TransformDirection(Vec3d.UnitY);
            var z = _matrix.TransformDirection(Vec3d.UnitZ);
            double scaleSquared = System.Math.Pow(System.Math.Abs(VoxelVolume), 2.0 / 3.0);
            const double tolerance = 1e-8;
            return System.Math.Abs(x.LengthSquared / scaleSquared - 1) <= tolerance && System.Math.Abs(y.LengthSquared / scaleSquared - 1) <= tolerance &&
                System.Math.Abs(z.LengthSquared / scaleSquared - 1) <= tolerance && System.Math.Abs(x.Dot(y) / scaleSquared) <= tolerance &&
                System.Math.Abs(x.Dot(z) / scaleSquared) <= tolerance && System.Math.Abs(y.Dot(z) / scaleSquared) <= tolerance;
        }
    }
    public static Transform CreateLinearTransform(double voxelSize = 1) => new(Mat4d.CreateScale(voxelSize));
    public static Transform CreateLinearTransform(Mat4d matrix) => new(matrix);
    public static Transform CreateFrustumTransform(BBoxd boundingBox, double taper, double depth, double voxelSize = 1) =>
        new(new NonlinearFrustumMap(boundingBox, taper, depth, Mat4d.CreateScale(voxelSize)));
    public Transform Copy() => _frustum is null ? new(_matrix) : new(_frustum);
    public Transform Inverse() => _frustum is null ? new(_inverse) : throw new NotSupportedException("The inverse of a frustum cannot be represented as an OpenVDB transform map.");
    public Vec3d IndexToWorld(Vec3d coordinate) => _frustum?.ApplyMap(coordinate) ?? _matrix.TransformPoint(coordinate);
    public Vec3d IndexToWorld(Coord coordinate) => IndexToWorld(coordinate.AsVec3d());
    public Vec3d WorldToIndex(Vec3d coordinate) => _frustum?.ApplyInverseMap(coordinate) ?? _inverse.TransformPoint(coordinate);
    public Coord WorldToIndexCellCentered(Vec3d coordinate) => Coord.Round(WorldToIndex(coordinate));
    public Coord WorldToIndexNodeCentered(Vec3d coordinate) => Coord.Floor(WorldToIndex(coordinate));
    public Vec3d ApplyJacobian(Vec3d vector) => _matrix.TransformDirection(vector);
    public Vec3d ApplyInverseJacobian(Vec3d vector) => _inverse.TransformDirection(vector);
    public Vec3d ApplyJacobianTranspose(Vec3d vector) => _matrix.Transpose().TransformDirection(vector);
    public Vec3d ApplyInverseJacobianTranspose(Vec3d vector) => _inverse.Transpose().TransformDirection(vector);
    public Vec3d ApplyJacobian(Vec3d vector, Vec3d indexLocation) => _frustum?.ApplyJacobian(vector, indexLocation) ?? ApplyJacobian(vector);
    public Vec3d ApplyInverseJacobian(Vec3d vector, Vec3d indexLocation) => _frustum?.ApplyInverseJacobian(vector, indexLocation) ?? ApplyInverseJacobian(vector);
    public Vec3d ApplyJacobianTranspose(Vec3d vector, Vec3d indexLocation) => _frustum?.ApplyJacobianTranspose(vector, indexLocation) ?? ApplyJacobianTranspose(vector);
    public Vec3d ApplyInverseJacobianTranspose(Vec3d vector, Vec3d indexLocation) => _frustum?.ApplyInverseJacobianTranspose(vector, indexLocation) ?? ApplyInverseJacobianTranspose(vector);
    public Mat3d Jacobian(Vec3d indexLocation) => _frustum?.Jacobian(indexLocation) ?? _matrix.GetMat3();
    public Mat3d ApplyInverseJacobianCurvature(Mat3d hessian)
    {
        Mat3d inverse = _inverse.GetMat3();
        return inverse * hessian * inverse.Transpose();
    }
    public Mat3d ApplyInverseJacobianCurvature(Mat3d hessian, Vec3d gradient, Vec3d indexLocation) =>
        _frustum?.ApplyInverseJacobianCurvature(hessian, gradient, indexLocation) ?? ApplyInverseJacobianCurvature(hessian);

    public void PreTranslate(Vec3d translation) => PreMult(Mat4d.CreateTranslation(translation));
    public void PostTranslate(Vec3d translation) => PostMult(Mat4d.CreateTranslation(translation));
    public void PreScale(double scale) => PreScale(new Vec3d(scale));
    public void PostScale(double scale) => PostScale(new Vec3d(scale));
    public void PreScale(Vec3d scale) => PreMult(Mat4d.CreateScale(scale));
    public void PostScale(Vec3d scale) => PostMult(Mat4d.CreateScale(scale));
    public void PreRotate(double radians, Axis axis = Axis.X) => PreMult(Mat4d.CreateRotation(radians, axis));
    public void PostRotate(double radians, Axis axis = Axis.X) => PostMult(Mat4d.CreateRotation(radians, axis));
    public void PreShear(double factor, Axis axis0, Axis axis1) => PreMult(Mat4d.CreateShear(factor, axis0, axis1));
    public void PostShear(double factor, Axis axis0, Axis axis1) => PostMult(Mat4d.CreateShear(factor, axis0, axis1));
    public void PreMult(Mat4d matrix) => SetMatrix(matrix * _matrix);
    public void PostMult(Mat4d matrix) => SetMatrix(_matrix * matrix);
    public void PreMult(Mat3d matrix) => PreMult(matrix.ToMat4());
    public void PostMult(Mat3d matrix) => PostMult(matrix.ToMat4());
    public void SetFrustumTaper(double taper) => _frustum = (_frustum ?? throw new InvalidOperationException("This is not a frustum transform.")).WithTaper(taper);
    public void SetFrustumDepth(double depth) => _frustum = (_frustum ?? throw new InvalidOperationException("This is not a frustum transform.")).WithDepth(depth);
    /// <summary>Transforms a batch, allowing identical or partially overlapping spans.</summary>
    public void IndexToWorld(ReadOnlySpan<Vec3d> source, Span<Vec3d> destination) => TransformPoints(source, destination, false);
    public void WorldToIndex(ReadOnlySpan<Vec3d> source, Span<Vec3d> destination) => TransformPoints(source, destination, true);
    private void TransformPoints(ReadOnlySpan<Vec3d> source, Span<Vec3d> destination, bool inverse)
    {
        if (destination.Length < source.Length) throw new ArgumentException("The destination is too short.", nameof(destination));
        // Cache map state outside the loop. A managed map reference remains safe across GC safepoints.
        Mat4d matrix = inverse ? _inverse : _matrix;
        NonlinearFrustumMap? frustum = _frustum;
        if (source.Overlaps(destination, out int offset) && offset > 0)
            for (int i = source.Length - 1; i >= 0; --i) destination[i] = frustum is null ? matrix.TransformPoint(source[i]) : inverse ? frustum.ApplyInverseMap(source[i]) : frustum.ApplyMap(source[i]);
        else
            for (int i = 0; i < source.Length; ++i) destination[i] = frustum is null ? matrix.TransformPoint(source[i]) : inverse ? frustum.ApplyInverseMap(source[i]) : frustum.ApplyMap(source[i]);
    }
    private void SetMatrix(Mat4d matrix)
    {
        if (!matrix.IsFinite) throw new ArgumentException("Transform elements must be finite.", nameof(matrix));
        Mat4d inverse = matrix.InverseAffine();
        NonlinearFrustumMap? frustum = _frustum?.WithSecondMap(matrix);
        _matrix = matrix; _inverse = inverse; _frustum = frustum;
    }
}
