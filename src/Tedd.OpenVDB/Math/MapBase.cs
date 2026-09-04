// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Maps.h and Maps.cc.
namespace Tedd.OpenVDB.Math;

/// <summary>Immutable map operations. Composition returns a new map, preserving native specialized arithmetic.</summary>
public abstract class MapBase
{
    public abstract string MapType { get; }
    public virtual bool IsLinear => true;
    public abstract bool HasUniformScale { get; }
    /// <summary>The affine matrix, or the secondary affine matrix for a frustum.</summary>
    public abstract Mat4d Matrix { get; }
    public virtual Mat4d InverseMatrix => Matrix.Inverse();
    public virtual bool IsIdentity => Matrix.ApproxEquals(Mat4d.Identity);
    public abstract double Determinant { get; }
    public virtual double DeterminantAt(Vec3d location) => Determinant;
    public abstract Vec3d VoxelSize { get; }
    public virtual Vec3d VoxelSizeAt(Vec3d location) => VoxelSize;
    public abstract Vec3d ApplyMap(Vec3d value);
    public abstract Vec3d ApplyInverseMap(Vec3d value);
    public abstract Vec3d ApplyJacobian(Vec3d value);
    public abstract Vec3d ApplyInverseJacobian(Vec3d value);
    public abstract Vec3d ApplyJacobianTranspose(Vec3d value);
    public abstract Vec3d ApplyInverseJacobianTranspose(Vec3d value);
    public virtual Vec3d ApplyJacobian(Vec3d value, Vec3d location) => ApplyJacobian(value);
    public virtual Vec3d ApplyInverseJacobian(Vec3d value, Vec3d location) => ApplyInverseJacobian(value);
    public virtual Vec3d ApplyJacobianTranspose(Vec3d value, Vec3d location) => ApplyJacobianTranspose(value);
    public virtual Vec3d ApplyInverseJacobianTranspose(Vec3d value, Vec3d location) => ApplyInverseJacobianTranspose(value);
    public virtual Mat3d Jacobian(Vec3d location) => Matrix.GetMat3();
    public abstract Mat3d ApplyInverseJacobianCurvature(Mat3d value);
    public virtual Mat3d ApplyInverseJacobianCurvature(Mat3d value, Vec3d gradient, Vec3d location) => ApplyInverseJacobianCurvature(value);
    public Vec3d ApplyJT(Vec3d value) => ApplyJacobianTranspose(value);
    public Vec3d ApplyJT(Vec3d value, Vec3d location) => ApplyJacobianTranspose(value, location);
    public Vec3d ApplyIJT(Vec3d value) => ApplyInverseJacobianTranspose(value);
    public Vec3d ApplyIJT(Vec3d value, Vec3d location) => ApplyInverseJacobianTranspose(value, location);
    public Mat3d ApplyIJC(Mat3d value) => ApplyInverseJacobianCurvature(value);
    public Mat3d ApplyIJC(Mat3d value, Vec3d gradient, Vec3d location) => ApplyInverseJacobianCurvature(value, gradient, location);
    public abstract MapBase Copy();
    public abstract MapBase InverseMap();
    public virtual AffineMap GetAffineMap() => new(Matrix);
    public virtual bool IsEqual(MapBase? other) => other is not null && MapType == other.MapType && GetAffineMap().IsEqual(other.GetAffineMap());
    public virtual MapBase PreTranslate(Vec3d value) => Simplify(new AffineMap(Mat4d.CreateTranslation(value) * Matrix));
    public virtual MapBase PostTranslate(Vec3d value) => Simplify(new AffineMap(Matrix * Mat4d.CreateTranslation(value)));
    public virtual MapBase PreScale(Vec3d value) => Simplify(new AffineMap(MapMatrix.Scale(Matrix, value, true)));
    public virtual MapBase PostScale(Vec3d value) => Simplify(new AffineMap(MapMatrix.Scale(Matrix, value, false)));
    public MapBase PreScale(double value) => PreScale(new Vec3d(value));
    public MapBase PostScale(double value) => PostScale(new Vec3d(value));
    public virtual MapBase PreRotate(double radians, Axis axis = Axis.X) => Simplify(new AffineMap(MapMatrix.Rotate(Matrix, radians, axis, true)));
    public virtual MapBase PostRotate(double radians, Axis axis = Axis.X) => Simplify(new AffineMap(MapMatrix.Rotate(Matrix, radians, axis, false)));
    public virtual MapBase PreShear(double value, Axis axis0, Axis axis1) => Simplify(new AffineMap(MapMatrix.Shear(Matrix, value, axis0, axis1, true)));
    public virtual MapBase PostShear(double value, Axis axis0, Axis axis1) => Simplify(new AffineMap(MapMatrix.Shear(Matrix, value, axis0, axis1, false)));
    public static MapBase Simplify(AffineMap affine)
    {
        ArgumentNullException.ThrowIfNull(affine);
        if (affine.IsDiagonal)
        {
            Vec3d scale = affine.ApplyMap(Vec3d.One);
            return IsUniform(scale) ? new UniformScaleMap(scale.X) : new ScaleMap(scale);
        }
        if (affine.Matrix.GetMat3().IsDiagonal)
        {
            Vec3d translation = affine.ApplyMap(Vec3d.Zero), scale = affine.ApplyMap(Vec3d.One) - translation;
            return IsUniform(scale) ? new UniformScaleTranslateMap(scale.X, translation) : new ScaleTranslateMap(scale, translation);
        }
        return affine;
    }
    internal static bool IsUniform(Vec3d scale) => MathComparison.IsApproxEqual(scale.X, scale.Y) && MathComparison.IsApproxEqual(scale.X, scale.Z);
}

internal static class MapMatrix
{
    internal static Mat4d Scale(Mat4d matrix, Vec3d scale, bool pre)
    {
        Span<double> values = stackalloc double[16]; matrix.CopyTo(values);
        if (pre) { for (int row = 0; row < 3; ++row) for (int column = 0; column < 4; ++column) values[row * 4 + column] *= scale[row]; }
        else { for (int row = 0; row < 4; ++row) for (int column = 0; column < 3; ++column) values[row * 4 + column] *= scale[column]; }
        return Mat4d.FromSpan(values);
    }
    internal static Mat4d Shear(Mat4d matrix, double value, Axis axis0, Axis axis1, bool pre)
    {
        ValidateAxis(axis0); ValidateAxis(axis1);
        if (axis0 == axis1) throw new ArgumentException("Shear axes must differ.", nameof(axis1));
        Span<double> values = stackalloc double[16]; matrix.CopyTo(values);
        for (int i = 0; i < 4; ++i)
        {
            int to = pre ? (int)axis1 * 4 + i : i * 4 + (int)axis0;
            int from = pre ? (int)axis0 * 4 + i : i * 4 + (int)axis1;
            values[to] += value * values[from];
        }
        return Mat4d.FromSpan(values);
    }
    internal static Mat4d Rotate(Mat4d matrix, double radians, Axis axis, bool pre)
    {
        ValidateAxis(axis); double c = System.Math.Cos(radians), s = -System.Math.Sin(radians);
        Span<double> values = stackalloc double[16]; matrix.CopyTo(values);
        int first = axis == Axis.X ? 1 : 0, second = axis == Axis.Z ? 1 : 2;
        for (int i = 0; i < 4; ++i)
        {
            int a = pre ? first * 4 + i : i * 4 + first, b = pre ? second * 4 + i : i * 4 + second;
            double x = values[a], y = values[b];
            if (pre)
            {
                values[a] = axis == Axis.Y ? c * x + s * y : c * x - s * y;
                values[b] = axis == Axis.Y ? -s * x + c * y : s * x + c * y;
            }
            else
            {
                values[b] = axis == Axis.Y ? c * y + s * x : c * y - s * x;
                values[a] = axis == Axis.Y ? c * x - s * y : c * x + s * y;
            }
        }
        return Mat4d.FromSpan(values);
    }
    private static void ValidateAxis(Axis axis) => ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)axis, 2u, nameof(axis));
}
