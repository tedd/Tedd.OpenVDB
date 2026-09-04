// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Maps.h::NonlinearFrustumMap.
namespace Tedd.OpenVDB.Math;

/// <summary>Maps an index-space box to a tapered frustum, followed by an affine secondary map.</summary>
/// <remarks>This managed map is immutable. Taper is near-plane width divided by far-plane width.
/// Finite positive depth/taper and ordered positive box extents are required; these strengthen
/// native preconditions that otherwise allow nonfinite cached parameters.</remarks>
public sealed class NonlinearFrustumMap : MapBase
{
    private readonly Mat4d _inverse;
    private readonly double _lx, _xOffset, _yOffset, _depthOnLz;
    public NonlinearFrustumMap() : this(new BBoxd(Vec3d.Zero, Vec3d.One), 1, 1) { }
    public NonlinearFrustumMap(BBoxd boundingBox, double taper, double depth, Mat4d? secondMap = null)
    {
        if (!boundingBox.IsValid || boundingBox.IsEmpty || !boundingBox.Min.IsFinite || !boundingBox.Max.IsFinite)
            throw new ArgumentException("Frustum bounds must be finite and have positive extents.", nameof(boundingBox));
        var extents = boundingBox.Extents;
        if (!extents.IsFinite || extents.X <= 1e-15 || extents.Y <= 1e-15 || extents.Z <= 1e-15)
            throw new ArithmeticException("Frustum bounds must have nonzero extent on every axis.");
        if (!double.IsFinite(taper) || taper <= 0) throw new ArgumentOutOfRangeException(nameof(taper));
        if (!double.IsFinite(depth) || depth <= 0) throw new ArgumentOutOfRangeException(nameof(depth));
        Mat4d matrix = secondMap ?? Mat4d.Identity;
        if (!matrix.IsFinite) throw new ArgumentException("The secondary matrix must be finite.", nameof(secondMap));
        _inverse = matrix.InverseAffine();
        BoundingBox = boundingBox; Taper = taper; Depth = depth; SecondMap = matrix;
        _lx = extents.X; _xOffset = 0.5 * extents.X; _yOffset = 0.5 * extents.Y;
        _depthOnLz = depth / extents.Z; Gamma = (1 / taper - 1) / depth;
        if (!double.IsFinite(Gamma) || !double.IsFinite(_depthOnLz) || _depthOnLz == 0) throw new ArithmeticException("Frustum parameters overflowed or underflowed.");
    }
    public override string MapType => nameof(NonlinearFrustumMap);
    public override Mat4d Matrix => SecondMap;
    public override Mat4d InverseMatrix => _inverse;
    public override MapBase InverseMap() => throw new NotSupportedException("No map type represents an inverse frustum.");
    public override bool IsEqual(MapBase? other)
    {
        if (other is not NonlinearFrustumMap map || !BoundingBox.Min.ApproxEquals(map.BoundingBox.Min) || !BoundingBox.Max.ApproxEquals(map.BoundingBox.Max) ||
            !MathComparison.IsApproxEqual(Taper, map.Taper) || !MathComparison.IsApproxEqual(Depth, map.Depth)) return false;
        return SecondMap.TransformPoint(Vec3d.Zero).ApproxEquals(map.SecondMap.TransformPoint(Vec3d.Zero)) &&
            SecondMap.TransformPoint(Vec3d.UnitX).ApproxEquals(map.SecondMap.TransformPoint(Vec3d.UnitX)) &&
            SecondMap.TransformPoint(Vec3d.UnitY).ApproxEquals(map.SecondMap.TransformPoint(Vec3d.UnitY)) &&
            SecondMap.TransformPoint(Vec3d.UnitZ).ApproxEquals(map.SecondMap.TransformPoint(Vec3d.UnitZ));
    }
    public override MapBase PreTranslate(Vec3d value) => WithSecondMap(new AffineMap(SecondMap).PreTranslate(value).Matrix);
    public override MapBase PostTranslate(Vec3d value) => WithSecondMap(new AffineMap(SecondMap).PostTranslate(value).Matrix);
    public override MapBase PreScale(Vec3d value) => WithSecondMap(MapMatrix.Scale(SecondMap, value, true));
    public override MapBase PostScale(Vec3d value) => WithSecondMap(MapMatrix.Scale(SecondMap, value, false));
    public override MapBase PreRotate(double radians, Axis axis = Axis.X) => WithSecondMap(MapMatrix.Rotate(SecondMap, radians, axis, true));
    public override MapBase PostRotate(double radians, Axis axis = Axis.X) => WithSecondMap(MapMatrix.Rotate(SecondMap, radians, axis, false));
    public override MapBase PreShear(double value, Axis axis0, Axis axis1) => WithSecondMap(MapMatrix.Shear(SecondMap, value, axis0, axis1, true));
    public override MapBase PostShear(double value, Axis axis0, Axis axis1) => WithSecondMap(MapMatrix.Shear(SecondMap, value, axis0, axis1, false));
    public BBoxd BoundingBox { get; }
    public double Taper { get; }
    public double Depth { get; }
    public double Gamma { get; }
    public Mat4d SecondMap { get; }
    public override bool IsLinear => false;
    public override bool HasUniformScale => false;
    public bool IsValid => !BoundingBox.IsEmpty;
    public bool HasSimpleAffine
    {
        get
        {
            var x = SecondMap.TransformDirection(Vec3d.UnitX);
            var y = SecondMap.TransformDirection(Vec3d.UnitY);
            var z = SecondMap.TransformDirection(Vec3d.UnitZ);
            return System.Math.Abs(x.Length - y.Length) <= 1e-15 && System.Math.Abs(x.Length - z.Length) <= 1e-15 &&
                System.Math.Abs(x.Dot(y)) <= 1e-7 && System.Math.Abs(y.Dot(z)) <= 1e-7 && System.Math.Abs(z.Dot(x)) <= 1e-7;
        }
    }
    public override bool IsIdentity => System.Math.Abs(Taper - 1) <= 1e-15 && ApplyMap(Vec3d.UnitX).ApproxEquals(Vec3d.UnitX) &&
        ApplyMap(Vec3d.UnitY).ApproxEquals(Vec3d.UnitY) && ApplyMap(Vec3d.UnitZ).ApproxEquals(Vec3d.UnitZ);
    public override Vec3d VoxelSize => VoxelSizeAt(new Vec3d(0.5 * (BoundingBox.Min.X + BoundingBox.Max.X),
        0.5 * (BoundingBox.Min.Y + BoundingBox.Max.Y), BoundingBox.Min.Z));
    public override double Determinant => SecondMap.Determinant3x3;
    /// <summary>Retains the native determinant(loc) expression, whose Z argument is used in frustum-depth coordinates.</summary>
    /// <remarks>For the determinant of the actual index-space differential, use Jacobian(indexLocation).Determinant.</remarks>
    public override double DeterminantAt(Vec3d location)
    {
        double scale = Gamma * location.Z + 1;
        return Determinant * scale * scale * _depthOnLz / (_lx * _lx);
    }
    public override Vec3d ApplyMap(Vec3d indexLocation) => SecondMap.TransformPoint(ApplyFrustum(indexLocation));
    public override Vec3d ApplyInverseMap(Vec3d worldLocation)
    {
        Vec3d location = _inverse.TransformPoint(worldLocation);
        double inverseScale = _lx / (Gamma * location.Z + 1);
        return new Vec3d(location.X * inverseScale + _xOffset, location.Y * inverseScale + _yOffset, location.Z / _depthOnLz) + BoundingBox.Min;
    }
    private Vec3d Centered(Vec3d location) => location - BoundingBox.Min - new Vec3d(_xOffset, _yOffset, 0);
    private Vec3d ApplyFrustum(Vec3d indexLocation)
    {
        Vec3d centered = Centered(indexLocation);
        double z = centered.Z * _depthOnLz, scale = (Gamma * z + 1) / _lx;
        return new(centered.X * scale, centered.Y * scale, z);
    }
    public override Vec3d VoxelSizeAt(Vec3d location)
    {
        Vec3d position = ApplyMap(location);
        return new((ApplyMap(location + Vec3d.UnitX) - position).Length, (ApplyMap(location + Vec3d.UnitY) - position).Length, (ApplyMap(location + Vec3d.UnitZ) - position).Length);
    }
    public override Vec3d ApplyJacobian(Vec3d vector) => SecondMap.TransformDirection(vector);
    public override Vec3d ApplyInverseJacobian(Vec3d vector) => _inverse.TransformDirection(vector);
    public override Vec3d ApplyJacobianTranspose(Vec3d vector) => SecondMap.Transpose().TransformDirection(vector);
    public override Vec3d ApplyInverseJacobianTranspose(Vec3d vector) => _inverse.Transpose().TransformDirection(vector);
    public override Mat3d Jacobian(Vec3d indexLocation)
    {
        Vec3d centered = Centered(indexLocation);
        double scale = (Gamma * centered.Z * _depthOnLz + 1) / _lx, scale2 = Gamma * _depthOnLz / _lx;
        return new Mat3d(scale, 0, 0, 0, scale, 0, scale2 * centered.X, scale2 * centered.Y, _depthOnLz) * SecondMap.GetMat3();
    }
    public override Vec3d ApplyJacobian(Vec3d vector, Vec3d indexLocation) => vector * Jacobian(indexLocation);
    public override Vec3d ApplyInverseJacobian(Vec3d vector, Vec3d indexLocation)
    {
        Vec3d centered = Centered(indexLocation);
        double scale = (Gamma * centered.Z * _depthOnLz + 1) / _lx, scale2 = Gamma * _depthOnLz / _lx;
        Vec3d output = _inverse.TransformDirection(vector);
        return new((output.X - scale2 * centered.X * output.Z / _depthOnLz) / scale,
            (output.Y - scale2 * centered.Y * output.Z / _depthOnLz) / scale, output.Z / _depthOnLz);
    }
    public override Vec3d ApplyJacobianTranspose(Vec3d vector, Vec3d indexLocation) => vector * Jacobian(indexLocation).Transpose();
    public override Vec3d ApplyInverseJacobianTranspose(Vec3d gradient, Vec3d indexLocation) => gradient * InverseGradientMatrix(indexLocation);
    private Mat3d InverseGradientMatrix(Vec3d indexLocation)
    {
        Vec3d location = ApplyFrustum(indexLocation);
        double s = Gamma * location.Z + 1;
        if (System.Math.Abs(s) <= 1e-15) throw new ArithmeticException("The frustum differential is singular at its focal point.");
        double inverseS = 1 / s, pt0 = _lx * inverseS, pt2 = Gamma * pt0 * inverseS;
        Mat3d inverseTranspose = _inverse.GetMat3().Transpose();
        return Mat3d.FromRows(inverseTranspose.Row(0) * pt0 - inverseTranspose.Row(2) * (pt2 * location.X),
            inverseTranspose.Row(1) * pt0 - inverseTranspose.Row(2) * (pt2 * location.Y), inverseTranspose.Row(2) / _depthOnLz);
    }
    /// <summary>Ports the native Jacobian-curvature expression for an index-space Hessian and gradient.</summary>
    /// <remarks>The native zz correction uses one gamma factor, not gamma squared. This is retained for parity;
    /// for gamma other than zero or one the result can differ from the analytic world-space Hessian.</remarks>
    public override Mat3d ApplyInverseJacobianCurvature(Mat3d hessian, Vec3d gradient, Vec3d indexLocation)
    {
        Vec3d location = ApplyFrustum(indexLocation);
        Mat3d inverseGradient = InverseGradientMatrix(indexLocation);
        double inverseS = 1 / (Gamma * location.Z + 1), pt0 = _lx * inverseS, pt2 = Gamma * pt0 * inverseS, pt3 = pt2 * inverseS;
        Mat3d j = _inverse.GetMat3().Transpose();
        Mat3d zProduct = Mat3d.OuterProduct(j.Row(2), j.Row(2)) * (2 * pt3);
        Mat3d e0 = -(Mat3d.OuterProduct(j.Row(0), j.Row(2)) + Mat3d.OuterProduct(j.Row(2), j.Row(0))) * pt2 + zProduct * location.X;
        Mat3d e1 = -(Mat3d.OuterProduct(j.Row(1), j.Row(2)) + Mat3d.OuterProduct(j.Row(2), j.Row(1))) * pt2 + zProduct * location.Y;
        return inverseGradient.Transpose() * hessian.Transpose() * inverseGradient + e0 * gradient.X + e1 * gradient.Y;
    }
    /// <summary>Creates the native camera-oriented frustum. The up vector length is half the near-plane height.</summary>
    /// <remarks>Depth is the distance from the near plane to the far plane. Positive finite distances,
    /// dimensions and a finite nonzero direction are required. The index Y count is rounded from X/aspect.</remarks>
    public static NonlinearFrustumMap CreateFromCamera(Vec3d position, Vec3d direction, Vec3d up,
        double aspect, double nearDistance, double depth, int xCount, int zCount)
    {
        if (!position.IsFinite) throw new ArgumentException("The camera position must be finite.", nameof(position));
        if (!direction.IsFinite || !direction.TryNormalize(out Vec3d directionUnit))
            throw new ArgumentException("The camera direction must be finite and nonzero.", nameof(direction));
        if (!up.IsFinite || !(up.Length > 0)) throw new ArgumentException("Camera up must be finite and nonzero.", nameof(up));
        if (!double.IsFinite(aspect) || aspect <= 0) throw new ArgumentOutOfRangeException(nameof(aspect));
        if (!double.IsFinite(nearDistance) || nearDistance <= 0) throw new ArgumentOutOfRangeException(nameof(nearDistance));
        if (!double.IsFinite(depth) || depth <= 0) throw new ArgumentOutOfRangeException(nameof(depth));
        if (System.Math.Abs(up.Dot(direction)) > 1e-15)
            throw new ArgumentException("Camera up must be perpendicular to the frustum direction.", nameof(up));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(xCount); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(zCount);
        int yCount = checked((int)System.Math.Floor(xCount / aspect + 0.5));
        double nearWidth = aspect * 2 * up.Length, normalizedDepth = depth / nearWidth, gamma = nearWidth / nearDistance;
        double taper = 1 / (normalizedDepth * gamma + 1);
        Mat4d r1 = Mat4d.CreateRotation(Vec3d.UnitZ, directionUnit);
        Vec3d temporary = r1.Inverse().TransformPoint(up);
        Mat4d r2 = Mat4d.CreateRotation(Vec3d.UnitY, temporary);
        Mat4d matrix = Mat4d.CreateScale(nearWidth) * r2 * r1;
        Vec3d translation = position + nearDistance * directionUnit;
        matrix = matrix with { M30 = translation.X, M31 = translation.Y, M32 = translation.Z };
        return new(new BBoxd(Vec3d.Zero, new Vec3d(xCount, yCount, zCount)), taper, normalizedDepth, matrix);
    }
    public override Mat3d ApplyInverseJacobianCurvature(Mat3d hessian)
    {
        Mat3d inverse = _inverse.GetMat3();
        return inverse * hessian * inverse.Transpose();
    }

    public override NonlinearFrustumMap Copy() => new(BoundingBox, Taper, Depth, SecondMap);
    public NonlinearFrustumMap WithTaper(double taper) => new(BoundingBox, taper, Depth, SecondMap);
    public NonlinearFrustumMap WithDepth(double depth) => new(BoundingBox, Taper, depth, SecondMap);
    public NonlinearFrustumMap WithSecondMap(Mat4d map) => new(BoundingBox, Taper, Depth, map);
    public NonlinearFrustumMap PreMult(Mat4d matrix) => WithSecondMap(matrix * SecondMap);
    public NonlinearFrustumMap PostMult(Mat4d matrix) => WithSecondMap(SecondMap * matrix);
    public bool ApproxEquals(NonlinearFrustumMap other, double tolerance = 1e-15) => BoundingBox.ApproxEquals(other.BoundingBox, tolerance) &&
        System.Math.Abs(Taper - other.Taper) <= tolerance && System.Math.Abs(Depth - other.Depth) <= tolerance && SecondMap.ApproxEquals(other.SecondMap, tolerance);
}
