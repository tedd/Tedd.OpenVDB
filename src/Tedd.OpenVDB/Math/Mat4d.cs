// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Mat4.h and Maps.h; row-vector convention.
using System.Runtime.InteropServices;

namespace Tedd.OpenVDB.Math;

public enum Axis { X, Y, Z }

/// <summary>Double-precision row-major matrix using OpenVDB's row-vector convention.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Mat4d(
    double M00, double M01, double M02, double M03,
    double M10, double M11, double M12, double M13,
    double M20, double M21, double M22, double M23,
    double M30, double M31, double M32, double M33)
{
    public static Mat4d Identity => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    public double this[int row, int column]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)row, 3u, nameof(row));
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)column, 3u, nameof(column));
            return (row * 4 + column) switch
            {
                0 => M00, 1 => M01, 2 => M02, 3 => M03, 4 => M10, 5 => M11, 6 => M12, 7 => M13,
                8 => M20, 9 => M21, 10 => M22, 11 => M23, 12 => M30, 13 => M31, 14 => M32, _ => M33
            };
        }
    }
    public bool IsAffine => M03 == 0 && M13 == 0 && M23 == 0 && M33 == 1;
    public double Determinant3x3 => M00 * (M11 * M22 - M12 * M21) - M01 * (M10 * M22 - M12 * M20) + M02 * (M10 * M21 - M11 * M20);
    public Vec3d Translation => new(M30, M31, M32);
    public static Mat4d CreateScale(Vec3d scale) => new(scale.X, 0, 0, 0, 0, scale.Y, 0, 0, 0, 0, scale.Z, 0, 0, 0, 0, 1);
    public static Mat4d CreateScale(double scale) => CreateScale(new Vec3d(scale));
    public static Mat4d CreateTranslation(Vec3d translation) => Identity with { M30 = translation.X, M31 = translation.Y, M32 = translation.Z };
    public static Mat4d CreateRotation(double radians, Axis axis = Axis.X)
    {
        double c = System.Math.Cos(radians), s = System.Math.Sin(radians);
        return axis switch
        {
            Axis.X => new(1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, 0, 0, 1),
            Axis.Y => new(c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, 0, 0, 0, 1),
            Axis.Z => new(c, s, 0, 0, -s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(axis))
        };
    }
    /// <summary>Adds factor times axis1 to axis0, following OpenVDB's shear convention.</summary>
    public static Mat4d CreateShear(double factor, Axis axis0, Axis axis1)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)axis0, 2u, nameof(axis0));
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)axis1, 2u, nameof(axis1));
        if (axis0 == axis1) throw new ArgumentException("Shear axes must be different.", nameof(axis1));
        Span<double> values = stackalloc double[16];
        Identity.CopyTo(values);
        values[(int)axis1 * 4 + (int)axis0] = factor;
        return FromSpan(values);
    }
    public Vec3d TransformPoint(Vec3d v) => new(v.X * M00 + v.Y * M10 + v.Z * M20 + M30, v.X * M01 + v.Y * M11 + v.Z * M21 + M31, v.X * M02 + v.Y * M12 + v.Z * M22 + M32);
    public Vec3d TransformDirection(Vec3d v) => new(v.X * M00 + v.Y * M10 + v.Z * M20, v.X * M01 + v.Y * M11 + v.Z * M21, v.X * M02 + v.Y * M12 + v.Z * M22);
    public static Vec3d operator *(Vec3d v, Mat4d matrix) => matrix.TransformPoint(v);
    public static Mat4d operator *(Mat4d a, Mat4d b)
    {
        Span<double> result = stackalloc double[16];
        for (int row = 0; row < 4; ++row)
            for (int column = 0; column < 4; ++column)
                result[row * 4 + column] = a[row, 0] * b[0, column] + a[row, 1] * b[1, column] + a[row, 2] * b[2, column] + a[row, 3] * b[3, column];
        return FromSpan(result);
    }
    public Mat4d Transpose() => new(M00, M10, M20, M30, M01, M11, M21, M31, M02, M12, M22, M32, M03, M13, M23, M33);
    /// <summary>Inverts an affine matrix; the singularity threshold matches OpenVDB AffineMap.</summary>
    public Mat4d InverseAffine()
    {
        if (!IsAffine) throw new ArgumentException("The matrix must be affine.");
        double determinant = Determinant3x3;
        if (!double.IsFinite(determinant) || System.Math.Abs(determinant) < 3e-15)
            throw new ArithmeticException("Tried to initialize an affine transform from a nearly singular matrix.");
        double d = 1 / determinant;
        var inverse = new Mat4d(
            (M11 * M22 - M12 * M21) * d, (M02 * M21 - M01 * M22) * d, (M01 * M12 - M02 * M11) * d, 0,
            (M12 * M20 - M10 * M22) * d, (M00 * M22 - M02 * M20) * d, (M02 * M10 - M00 * M12) * d, 0,
            (M10 * M21 - M11 * M20) * d, (M01 * M20 - M00 * M21) * d, (M00 * M11 - M01 * M10) * d, 0,
            0, 0, 0, 1);
        Vec3d translation = inverse.TransformDirection(-Translation);
        return inverse with { M30 = translation.X, M31 = translation.Y, M32 = translation.Z };
    }
    public bool ApproxEquals(Mat4d other, double tolerance = 1e-8)
    {
        for (int row = 0; row < 4; ++row)
            for (int column = 0; column < 4; ++column)
                if (!(System.Math.Abs(this[row, column] - other[row, column]) <= tolerance)) return false;
        return true;
    }
    public void CopyTo(Span<double> destination)
    {
        if (destination.Length < 16) throw new ArgumentException("At least sixteen elements are required.", nameof(destination));
        destination[0] = M00; destination[1] = M01; destination[2] = M02; destination[3] = M03;
        destination[4] = M10; destination[5] = M11; destination[6] = M12; destination[7] = M13;
        destination[8] = M20; destination[9] = M21; destination[10] = M22; destination[11] = M23;
        destination[12] = M30; destination[13] = M31; destination[14] = M32; destination[15] = M33;
    }
    public static Mat4d FromSpan(ReadOnlySpan<double> source)
    {
        if (source.Length < 16) throw new ArgumentException("At least sixteen elements are required.", nameof(source));
        return new(source[0], source[1], source[2], source[3], source[4], source[5], source[6], source[7], source[8], source[9], source[10], source[11], source[12], source[13], source[14], source[15]);
    }
}
