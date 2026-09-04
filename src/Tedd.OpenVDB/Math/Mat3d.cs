// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Mat3.h, Mat.h and Quat.h.
using System.Runtime.InteropServices;

namespace Tedd.OpenVDB.Math;

/// <summary>Row-major 3x3 matrix. Vector postmultiplication uses OpenVDB's row-vector convention.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly partial record struct Mat3d(double M00, double M01, double M02, double M10, double M11, double M12, double M20, double M21, double M22)
{
    public static Mat3d Zero => default;
    public static Mat3d Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);
    public double this[int row, int column]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)row, 2u, nameof(row));
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)column, 2u, nameof(column));
            return (row * 3 + column) switch { 0 => M00, 1 => M01, 2 => M02, 3 => M10, 4 => M11, 5 => M12, 6 => M20, 7 => M21, _ => M22 };
        }
    }
    public Vec3d Row(int row) => new(this[row, 0], this[row, 1], this[row, 2]);
    public Vec3d Column(int column) => new(this[0, column], this[1, column], this[2, column]);
    public static Mat3d FromRows(Vec3d a, Vec3d b, Vec3d c) => new(a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z);
    public static Mat3d FromColumns(Vec3d a, Vec3d b, Vec3d c) => FromRows(a, b, c).Transpose();
    public double Determinant => M00 * (M11 * M22 - M12 * M21) + M01 * (M12 * M20 - M10 * M22) + M02 * (M10 * M21 - M11 * M20);
    public double Trace => M00 + M11 + M22;
    public bool IsFinite => Row(0).IsFinite && Row(1).IsFinite && Row(2).IsFinite;
    public bool IsIdentity => ApproxEquals(Identity);
    public bool IsDiagonal => MathComparison.IsApproxZero(System.Math.Abs(M01) + System.Math.Abs(M02) + System.Math.Abs(M10) + System.Math.Abs(M12) + System.Math.Abs(M20) + System.Math.Abs(M21));
    public bool IsSymmetric => ApproxEquals(Transpose());
    public bool IsUnitary => MathComparison.IsApproxEqual(System.Math.Abs(Determinant), 1) && (this * Transpose()).ApproxEquals(Identity);
    public bool Equals(Mat3d other) =>
        M00 == other.M00 && M01 == other.M01 && M02 == other.M02 && M10 == other.M10 && M11 == other.M11 &&
        M12 == other.M12 && M20 == other.M20 && M21 == other.M21 && M22 == other.M22;
    public override int GetHashCode() => HashCode.Combine(Row(0), Row(1), Row(2));
    public static Mat3d operator +(Mat3d a, Mat3d b) => FromRows(a.Row(0) + b.Row(0), a.Row(1) + b.Row(1), a.Row(2) + b.Row(2));
    public static Mat3d operator -(Mat3d a, Mat3d b) => FromRows(a.Row(0) - b.Row(0), a.Row(1) - b.Row(1), a.Row(2) - b.Row(2));
    public static Mat3d operator -(Mat3d a) => a * -1;
    public static Mat3d operator *(Mat3d a, double b) => FromRows(a.Row(0) * b, a.Row(1) * b, a.Row(2) * b);
    public static Mat3d operator *(double a, Mat3d b) => b * a;
    public static Mat3d operator /(Mat3d a, double b) => a * (1 / b);
    public static Mat3d operator *(Mat3d a, Mat3d b) => new(
        a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20, a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21, a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
        a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20, a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21, a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
        a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20, a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21, a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22);
    public static Vec3d operator *(Vec3d v, Mat3d m) => m.Transform(v);
    public static Vec3d operator *(Mat3d m, Vec3d v) => m.PreTransform(v);
    public Vec3d Transform(Vec3d v) => new(v.X * M00 + v.Y * M10 + v.Z * M20, v.X * M01 + v.Y * M11 + v.Z * M21, v.X * M02 + v.Y * M12 + v.Z * M22);
    public Vec3d PreTransform(Vec3d v) => new(M00 * v.X + M01 * v.Y + M02 * v.Z, M10 * v.X + M11 * v.Y + M12 * v.Z, M20 * v.X + M21 * v.Y + M22 * v.Z);
    public Mat3d Transpose() => new(M00, M10, M20, M01, M11, M21, M02, M12, M22);
    public Mat3d Cofactor() => new(
        M11 * M22 - M12 * M21, M12 * M20 - M10 * M22, M10 * M21 - M11 * M20,
        M02 * M21 - M01 * M22, M00 * M22 - M02 * M20, M01 * M20 - M00 * M21,
        M01 * M12 - M02 * M11, M02 * M10 - M00 * M12, M00 * M11 - M01 * M10);
    public Mat3d Adjoint() => Cofactor().Transpose();
    /// <summary>Inverts using the adjoint and determinant; tolerance is absolute, as in OpenVDB.</summary>
    public Mat3d Inverse(double tolerance = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);
        Mat3d inverse = Adjoint();
        double determinant = inverse.M00 * M00 + inverse.M01 * M10 + inverse.M02 * M20;
        if (MathComparison.IsApproxZero(determinant, tolerance)) throw new ArithmeticException("Inversion of singular 3x3 matrix.");
        return inverse * (1 / determinant);
    }
    public bool TryInverse(out Mat3d inverse, double tolerance = 0)
    {
        try { inverse = Inverse(tolerance); return true; }
        catch (ArithmeticException) { inverse = default; return false; }
    }
    public static Mat3d CreateScale(Vec3d scale) => new(scale.X, 0, 0, 0, scale.Y, 0, 0, 0, scale.Z);
    public static Mat3d CreateScale(double scale) => CreateScale(new Vec3d(scale));
    public static Mat3d CreateRotation(double radians, Axis axis = Axis.X) => Mat4d.CreateRotation(radians, axis).GetMat3();
    public static Mat3d CreateRotation(Vec3d axis, double radians)
    {
        Vec3d unit = axis.Unit();
        double c = System.Math.Cos(radians), s = System.Math.Sin(radians), t = 1 - c;
        double xy = unit.X * unit.Y * t, xz = unit.X * unit.Z * t, yz = unit.Y * unit.Z * t;
        return new(unit.X * unit.X * t + c, xy + unit.Z * s, xz - unit.Y * s,
            xy - unit.Z * s, unit.Y * unit.Y * t + c, yz + unit.X * s,
            xz + unit.Y * s, yz - unit.X * s, unit.Z * unit.Z * t + c);
    }
    /// <summary>Normalizes the quaternion algebraically; a near-zero quaternion yields identity, as in OpenVDB.</summary>
    public static Mat3d CreateRotation(Quatd quaternion, double epsilon = 1e-8)
    {
        double dot = quaternion.LengthSquared;
        double s = MathComparison.IsApproxZero(dot, epsilon) ? 0 : 2 / dot;
        double x = s * quaternion.X, y = s * quaternion.Y, z = s * quaternion.Z;
        double wx = x * quaternion.W, wy = y * quaternion.W, wz = z * quaternion.W;
        double xx = x * quaternion.X, xy = y * quaternion.X, xz = z * quaternion.X;
        double yy = y * quaternion.Y, yz = z * quaternion.Y, zz = z * quaternion.Z;
        return new(1 - (yy + zz), xy + wz, xz - wy, xy - wz, 1 - (xx + zz), yz + wx, xz + wy, yz - wx, 1 - (xx + yy));
    }
    public static Mat3d CreateShear(double factor, Axis axis0, Axis axis1) => Mat4d.CreateShear(factor, axis0, axis1).GetMat3();
    public Mat3d TimesDiagonal(Vec3d diagonal) => FromRows(Row(0) * diagonal, Row(1) * diagonal, Row(2) * diagonal);
    public Mat3d DiagonalTimes(Vec3d diagonal) => FromRows(Row(0) * diagonal.X, Row(1) * diagonal.Y, Row(2) * diagonal.Z);
    public static Mat3d OuterProduct(Vec3d a, Vec3d b) => FromRows(b * a.X, b * a.Y, b * a.Z);
    // Mat.h has a duplicate [2][1] assignment; initialize the intended [1][2] element explicitly.
    public static Mat3d Skew(Vec3d v) => new(0, v.Z, -v.Y, -v.Z, 0, v.X, v.Y, -v.X, 0);
    public Mat4d ToMat4() => new(M00, M01, M02, 0, M10, M11, M12, 0, M20, M21, M22, 0, 0, 0, 0, 1);
    public bool ApproxEquals(Mat3d other, double tolerance = 1e-8) { for (int row = 0; row < 3; ++row) for (int column = 0; column < 3; ++column) if (!MathComparison.IsApproxEqual(this[row, column], other[row, column], tolerance)) return false; return true; }
    /// <summary>Rotation-matrix slerp selects the shortest quaternion arc, matching OpenVDB's matrix overload.</summary>
    public static Mat3d Slerp(Mat3d a, Mat3d b, double weight)
    {
        Quatd q1 = Quatd.FromRotationMatrix(a), q2 = Quatd.FromRotationMatrix(b);
        if (q1.Dot(q2) < 0) q2 = -q2;
        return CreateRotation(Quatd.Slerp(q1, q2, weight));
    }
    public static Mat3d BezierLerp(Mat3d a, Mat3d b, Mat3d c, Mat3d d, double weight) =>
        Slerp(Slerp(Slerp(a, b, weight), Slerp(b, c, weight), weight), Slerp(Slerp(b, c, weight), Slerp(c, d, weight), weight), weight);
    public void CopyTo(Span<double> destination)
    {
        if (destination.Length < 9) throw new ArgumentException("At least nine elements are required.", nameof(destination));
        destination[0] = M00; destination[1] = M01; destination[2] = M02; destination[3] = M10; destination[4] = M11;
        destination[5] = M12; destination[6] = M20; destination[7] = M21; destination[8] = M22;
    }
    public static Mat3d FromSpan(ReadOnlySpan<double> values)
    {
        if (values.Length < 9) throw new ArgumentException("At least nine elements are required.", nameof(values));
        return new(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8]);
    }
}
