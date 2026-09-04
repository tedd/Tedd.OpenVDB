// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Quat.h. Components are (x,y,z,w).
using System.Runtime.InteropServices;
namespace Tedd.OpenVDB.Math;

/// <summary>Quaternion with vector part XYZ and scalar part W. Default is zero; use Identity for no rotation.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Quatd(double X, double Y, double Z, double W)
{
    public static Quatd Zero => default;
    public static Quatd Identity => new(0, 0, 0, 1);
    public double this[int index] => index switch { 0 => X, 1 => Y, 2 => Z, 3 => W, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    public double LengthSquared => X * X + Y * Y + Z * Z + W * W;
    public double Length => System.Math.Sqrt(LengthSquared);
    public Vec3d Vector => new(X, Y, Z);
    public bool IsFinite => Vector.IsFinite && double.IsFinite(W);
    public double Angle => Vector.LengthSquared > 1e-8 ? 2 * System.Math.Acos(W) : 0;
    public Vec3d Axis => Vector.LengthSquared > 1e-8 ? Vector / Vector.Length : Vec3d.UnitX;
    public bool Equals(Quatd other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    public static Quatd FromAxisAngle(Vec3d axis, double radians)
    {
        if (!MathComparison.IsApproxEqual(axis.Length, 1, 1e-7)) throw new ArgumentException("The quaternion rotation axis must be a unit vector.", nameof(axis));
        double s = System.Math.Sin(radians * 0.5);
        return new(axis.X * s, axis.Y * s, axis.Z * s, System.Math.Cos(radians * 0.5));
    }
    public static Quatd FromAxisAngle(Axis axis, double radians) => FromAxisAngle(axis switch
    {
        global::Tedd.OpenVDB.Math.Axis.X => Vec3d.UnitX,
        global::Tedd.OpenVDB.Math.Axis.Y => Vec3d.UnitY,
        global::Tedd.OpenVDB.Math.Axis.Z => Vec3d.UnitZ,
        _ => throw new ArgumentOutOfRangeException(nameof(axis))
    }, radians);
    /// <summary>Constructs from a proper unitary rotation. Reflections and nonrotation matrices throw.</summary>
    public static Quatd FromRotationMatrix(Mat3d matrix)
    {
        if (!matrix.IsUnitary) throw new ArithmeticException("A non-rotation matrix cannot construct a quaternion.");
        if (!MathComparison.IsApproxEqual(matrix.Determinant, 1)) throw new ArithmeticException("A reflection matrix cannot construct a quaternion.");
        return FromRotationMatrixUnchecked(matrix);
    }
    /// <summary>Source's UnsafeConstruct conversion: the caller guarantees a proper rotation.</summary>
    public static Quatd FromRotationMatrixUnchecked(Mat3d m)
    {
        double trace = m.Trace;
        if (trace > 0)
        {
            double w = 0.5 * System.Math.Sqrt(trace + 1), factor = 0.25 / w;
            return new(factor * (m.M12 - m.M21), factor * (m.M20 - m.M02), factor * (m.M01 - m.M10), w);
        }
        if (m.M00 > m.M11 && m.M00 > m.M22)
        {
            double x = 0.5 * System.Math.Sqrt(m.M00 - m.M11 - m.M22 + 1), factor = 0.25 / x;
            return new(x, factor * (m.M01 + m.M10), factor * (m.M20 + m.M02), factor * (m.M12 - m.M21));
        }
        if (m.M11 > m.M22)
        {
            double y = 0.5 * System.Math.Sqrt(m.M11 - m.M00 - m.M22 + 1), factor = 0.25 / y;
            return new(factor * (m.M01 + m.M10), y, factor * (m.M12 + m.M21), factor * (m.M20 - m.M02));
        }
        double z = 0.5 * System.Math.Sqrt(m.M22 - m.M00 - m.M11 + 1), f = 0.25 / z;
        return new(f * (m.M20 + m.M02), f * (m.M12 + m.M21), z, f * (m.M01 - m.M10));
    }
    public static Quatd operator +(Quatd a, Quatd b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Quatd operator -(Quatd a, Quatd b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static Quatd operator -(Quatd a) => new(-a.X, -a.Y, -a.Z, -a.W);
    public static Quatd operator *(Quatd a, double scale) => new(a.X * scale, a.Y * scale, a.Z * scale, a.W * scale);
    public static Quatd operator *(double scale, Quatd a) => a * scale;
    public static Quatd operator /(Quatd a, double scale) => new(a.X / scale, a.Y / scale, a.Z / scale, a.W / scale);
    public static Quatd operator *(Quatd a, Quatd b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
        a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
    public double Dot(Quatd other) => X * other.X + Y * other.Y + Z * other.Z + W * other.W;
    public Quatd Conjugate() => new(-X, -Y, -Z, W);
    public Quatd Unit()
    {
        double length = Length;
        if (length == 0) throw new ArithmeticException("Normalizing degenerate quaternion.");
        return this / length;
    }
    public bool TryNormalize(out Quatd normalized, double epsilon = 1e-8)
    {
        double length = Length;
        if (MathComparison.IsApproxZero(length, epsilon)) { normalized = this; return false; }
        normalized = this * (1 / length); return true;
    }
    public Quatd Inverse(double tolerance = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);
        double d = LengthSquared;
        if (MathComparison.IsApproxZero(d, tolerance)) throw new ArithmeticException("Cannot invert degenerate quaternion.");
        Quatd result = this / -d;
        return result with { W = -result.W };
    }
    public Mat3d ToMat3() => Mat3d.CreateRotation(this);
    public Mat4d ToMat4() => ToMat3().ToMat4();
    public Vec3d RotateVector(Vec3d vector) => ToMat3().Transform(vector);
    /// <summary>OpenVDB's quaternion derivative convention, without an additional one-half factor.</summary>
    public Quatd Derivative(Vec3d omega) => new(W * omega.X - Z * omega.Y + Y * omega.Z, Z * omega.X + W * omega.Y - X * omega.Z,
        -Y * omega.X + X * omega.Y + W * omega.Z, -X * omega.X - Y * omega.Y - Z * omega.Z);
    public bool ApproxEquals(Quatd other, double tolerance = 1e-7) => MathComparison.IsApproxEqual(X, other.X, tolerance) && MathComparison.IsApproxEqual(Y, other.Y, tolerance) &&
        MathComparison.IsApproxEqual(Z, other.Z, tolerance) && MathComparison.IsApproxEqual(W, other.W, tolerance);
    /// <summary>OpenVDB quaternion slerp. It follows the supplied quaternion signs; it does not automatically choose the shortest arc.</summary>
    public static Quatd Slerp(Quatd a, Quatd b, double weight, double tolerance = 1e-5)
    {
        double dot = a.Dot(b), angle = 0, sineAngle = 0;
        if (System.Math.Abs(dot) < 1) { angle = System.Math.Acos(dot); sineAngle = System.Math.Sin(angle); }
        if (sineAngle <= tolerance)
        {
            Quatd blended = a * (1 - weight) + b * weight;
            double lengthSquared = blended.LengthSquared;
            return lengthSquared <= tolerance * tolerance ? (weight < 0.5 ? a : b) : blended * (1 / System.Math.Sqrt(lengthSquared));
        }
        return a * (System.Math.Sin((1 - weight) * angle) / sineAngle) + b * (System.Math.Sin(weight * angle) / sineAngle);
    }
    public void CopyTo(Span<double> destination)
    {
        if (destination.Length < 4) throw new ArgumentException("At least four elements are required.", nameof(destination));
        destination[0] = X; destination[1] = Y; destination[2] = Z; destination[3] = W;
    }
    public static Quatd FromSpan(ReadOnlySpan<double> source)
    {
        if (source.Length < 4) throw new ArgumentException("At least four elements are required.", nameof(source));
        return new(source[0], source[1], source[2], source[3]);
    }
    public override string ToString() => $"[{X}, {Y}, {Z}, {W}]";
}
