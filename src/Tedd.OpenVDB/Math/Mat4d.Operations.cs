// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Full inverse and homogeneous transforms ported from openvdb/math/Mat4.h.
namespace Tedd.OpenVDB.Math;

public readonly partial record struct Mat4d
{
    public static Mat4d Zero => default;
    public double Trace => M00 + M11 + M22 + M33;
    public bool HasTranslation => M30 != 0 || M31 != 0 || M32 != 0 || M33 != 1;
    public bool IsFinite => Row(0).IsFinite && Row(1).IsFinite && Row(2).IsFinite && Row(3).IsFinite;
    public Vec4d Row(int row) => new(this[row, 0], this[row, 1], this[row, 2], this[row, 3]);
    public Vec4d Column(int column) => new(this[0, column], this[1, column], this[2, column], this[3, column]);
    public Mat3d GetMat3() => new(M00, M01, M02, M10, M11, M12, M20, M21, M22);
    public Mat4d WithMat3(Mat3d m) => this with { M00 = m.M00, M01 = m.M01, M02 = m.M02, M10 = m.M10, M11 = m.M11, M12 = m.M12, M20 = m.M20, M21 = m.M21, M22 = m.M22 };
    public static Mat4d FromRows(Vec4d a, Vec4d b, Vec4d c, Vec4d d) => new(a.X, a.Y, a.Z, a.W, b.X, b.Y, b.Z, b.W, c.X, c.Y, c.Z, c.W, d.X, d.Y, d.Z, d.W);
    public static Mat4d FromColumns(Vec4d a, Vec4d b, Vec4d c, Vec4d d) => FromRows(a, b, c, d).Transpose();
    public static Mat4d operator +(Mat4d a, Mat4d b) => FromRows(a.Row(0) + b.Row(0), a.Row(1) + b.Row(1), a.Row(2) + b.Row(2), a.Row(3) + b.Row(3));
    public static Mat4d operator -(Mat4d a, Mat4d b) => FromRows(a.Row(0) - b.Row(0), a.Row(1) - b.Row(1), a.Row(2) - b.Row(2), a.Row(3) - b.Row(3));
    public static Mat4d operator -(Mat4d a) => a * -1;
    public static Mat4d operator *(Mat4d a, double scale) => FromRows(a.Row(0) * scale, a.Row(1) * scale, a.Row(2) * scale, a.Row(3) * scale);
    public static Mat4d operator *(double scale, Mat4d a) => a * scale;
    public static Mat4d operator /(Mat4d a, double scale) => a * (1 / scale);
    public static Mat4d CreateRotation(Quatd quaternion) => Mat3d.CreateRotation(quaternion).ToMat4();
    public static Mat4d CreateRotation(Vec3d axis, double radians) => Mat3d.CreateRotation(axis, radians).ToMat4();
    public Vec4d Transform(Vec4d v) => new(v.X * M00 + v.Y * M10 + v.Z * M20 + v.W * M30, v.X * M01 + v.Y * M11 + v.Z * M21 + v.W * M31,
        v.X * M02 + v.Y * M12 + v.Z * M22 + v.W * M32, v.X * M03 + v.Y * M13 + v.Z * M23 + v.W * M33);
    public Vec4d PreTransform(Vec4d v) => new(M00 * v.X + M01 * v.Y + M02 * v.Z + M03 * v.W, M10 * v.X + M11 * v.Y + M12 * v.Z + M13 * v.W,
        M20 * v.X + M21 * v.Y + M22 * v.Z + M23 * v.W, M30 * v.X + M31 * v.Y + M32 * v.Z + M33 * v.W);
    public Vec3d PreTransform(Vec3d v) => new(M00 * v.X + M01 * v.Y + M02 * v.Z + M03, M10 * v.X + M11 * v.Y + M12 * v.Z + M13, M20 * v.X + M21 * v.Y + M22 * v.Z + M23);
    public static Vec4d operator *(Vec4d v, Mat4d m) => m.Transform(v);
    public static Vec4d operator *(Mat4d m, Vec4d v) => m.PreTransform(v);
    public static Vec3d operator *(Mat4d m, Vec3d v) => m.PreTransform(v);
    /// <summary>Postmultiplies a point with homogeneous division. A zero homogeneous W returns zero, matching OpenVDB.</summary>
    public Vec3d TransformH(Vec3d point)
    {
        double w = point.X * M03 + point.Y * M13 + point.Z * M23 + M33;
        return w != 0 ? TransformPoint(point) / w : Vec3d.Zero;
    }
    /// <summary>Premultiplies a point with homogeneous division. A zero homogeneous W returns zero.</summary>
    public Vec3d PreTransformH(Vec3d point)
    {
        double w = point.X * M30 + point.Y * M31 + point.Z * M32 + M33;
        return w != 0 ? PreTransform(point) / w : Vec3d.Zero;
    }
    public double Determinant => M00 * Minor(0, 0) - M01 * Minor(0, 1) + M02 * Minor(0, 2) - M03 * Minor(0, 3);
    private double Minor(int row, int column)
    {
        Span<double> values = stackalloc double[9];
        int index = 0;
        for (int r = 0; r < 4; r++)
            if (r != row)
                for (int c = 0; c < 4; c++)
                    if (c != column) values[index++] = this[r, c];
        return Mat3d.FromSpan(values).Determinant;
    }
    public Mat4d Cofactor()
    {
        Span<double> values = stackalloc double[16];
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++) values[row * 4 + column] = ((row + column) & 1) == 0 ? Minor(row, column) : -Minor(row, column);
        return FromSpan(values);
    }
    public Mat4d Adjoint() => Cofactor().Transpose();
    /// <summary>General 4x4 inverse using OpenVDB's block inverse and pivoted elimination fallback.</summary>
    /// <remarks>Unlike InverseAffine, this accepts perspective matrices and defaults to an exact zero determinant threshold.</remarks>
    public Mat4d Inverse(double tolerance = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);
        double detA = Determinant3x3;
        bool perspective = !IsAffine;
        double determinant = perspective ? Determinant : detA;
        if (MathComparison.IsApproxZero(determinant, tolerance)) throw new ArithmeticException("Inversion of singular 4x4 matrix.");
        if (MathComparison.IsApproxZero(detA, 1e-8))
        {
            if (!InvertByElimination(out Mat4d result, tolerance)) throw new ArithmeticException("Inversion of singular 4x4 matrix.");
            return result;
        }
        Mat3d inverse = GetMat3().Adjoint() * (1 / detA);
        if (!perspective)
        {
            Vec3d translation = -(Translation * inverse);
            return inverse.ToMat4() with { M30 = translation.X, M31 = translation.Y, M32 = translation.Z };
        }
        Vec3d r = Translation * inverse;
        Vec3d p = inverse * new Vec3d(M03, M13, M23);
        double h = M33 - p.Dot(Translation);
        if (MathComparison.IsApproxZero(h, tolerance)) throw new ArithmeticException("Inversion of singular 4x4 matrix.");
        h = 1 / h;
        inverse += Mat3d.OuterProduct(p * h, r);
        return new(inverse.M00, inverse.M01, inverse.M02, -h * p.X,
            inverse.M10, inverse.M11, inverse.M12, -h * p.Y,
            inverse.M20, inverse.M21, inverse.M22, -h * p.Z,
            -h * r.X, -h * r.Y, -h * r.Z, h);
    }
    public bool TryInverse(out Mat4d inverse, double tolerance = 0)
    {
        try { inverse = Inverse(tolerance); return true; }
        catch (ArithmeticException) { inverse = default; return false; }
    }
    private bool InvertByElimination(out Mat4d result, double tolerance)
    {
        Span<double> temp = stackalloc double[16], inverse = stackalloc double[16];
        CopyTo(temp); Identity.CopyTo(inverse);
        double determinant = 1;
        for (int i = 0; i < 4; i++)
        {
            int row = i;
            double max = System.Math.Abs(temp[i * 4 + i]);
            for (int k = i + 1; k < 4; k++)
                if (System.Math.Abs(temp[k * 4 + i]) > max) { row = k; max = System.Math.Abs(temp[k * 4 + i]); }
            if (max == 0) { result = default; return false; }
            if (row != i)
            {
                determinant = -determinant;
                for (int k = 0; k < 4; k++)
                {
                    (temp[row * 4 + k], temp[i * 4 + k]) = (temp[i * 4 + k], temp[row * 4 + k]);
                    (inverse[row * 4 + k], inverse[i * 4 + k]) = (inverse[i * 4 + k], inverse[row * 4 + k]);
                }
            }
            double pivot = temp[i * 4 + i];
            determinant *= pivot;
            for (int k = 0; k < 4; k++) { temp[i * 4 + k] /= pivot; inverse[i * 4 + k] /= pivot; }
            for (int j = i + 1; j < 4; j++)
            {
                double t = temp[j * 4 + i];
                if (t != 0)
                    for (int k = 0; k < 4; k++) { temp[j * 4 + k] -= temp[i * 4 + k] * t; inverse[j * 4 + k] -= inverse[i * 4 + k] * t; }
            }
        }
        for (int i = 3; i > 0; i--)
            for (int j = 0; j < i; j++)
            {
                double t = temp[j * 4 + i];
                if (t != 0)
                    for (int k = 0; k < 4; k++) inverse[j * 4 + k] -= inverse[i * 4 + k] * t;
            }
        result = FromSpan(inverse);
        return determinant * determinant >= tolerance * tolerance;
    }
}
