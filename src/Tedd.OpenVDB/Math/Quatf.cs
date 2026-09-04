// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Single-precision quaternion and XYZ decomposition from math/Quat.h and math/Mat.h.
using System.Runtime.InteropServices;
namespace Tedd.OpenVDB.Math;

/// <summary>Native single-precision quaternion in XYZW order, including XYZ Euler extraction for cutter instances.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Quatf(float X, float Y, float Z, float W)
{
    public static Quatf Zero => default;
    public static Quatf Identity => new(0, 0, 0, 1);
    public float this[int index] => index switch { 0 => X, 1 => Y, 2 => Z, 3 => W, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    public Vec3f Vector => new(X, Y, Z);
    public float LengthSquared => X * X + Y * Y + Z * Z + W * W;
    public float Length => System.MathF.Sqrt(LengthSquared);
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) && float.IsFinite(W);
    public bool Equals(Quatf other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    public static Quatf FromAxisAngle(Axis axis, float radians)
    {
        if ((uint)axis > 2) throw new ArgumentOutOfRangeException(nameof(axis));
        float s = (float)System.Math.Sin(radians * .5f);
        return new(axis == global::Tedd.OpenVDB.Math.Axis.X ? s : 0, axis == global::Tedd.OpenVDB.Math.Axis.Y ? s : 0, axis == global::Tedd.OpenVDB.Math.Axis.Z ? s : 0, (float)System.Math.Cos(radians * .5f));
    }
    public static Quatf FromAxisAngle(Vec3f axis, float radians)
    {
        if (!MathComparison.IsApproxEqual(axis.Length, 1, 1e-7f)) throw new ArgumentException("The quaternion axis must be a unit vector.", nameof(axis));
        float s = (float)System.Math.Sin(radians * .5f); return new(axis.X * s, axis.Y * s, axis.Z * s, (float)System.Math.Cos(radians * .5f));
    }
    public float Dot(Quatf other) => X * other.X + Y * other.Y + Z * other.Z + W * other.W;
    public Quatf Conjugate() => new(-X, -Y, -Z, W);
    public Quatf Unit() => Length == 0 ? throw new ArithmeticException("Normalizing degenerate quaternion.") : this / Length;
    public static Quatf operator +(Quatf a, Quatf b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Quatf operator -(Quatf a) => new(-a.X, -a.Y, -a.Z, -a.W);
    public static Quatf operator -(Quatf a, Quatf b) => a + -b;
    public static Quatf operator *(Quatf a, float s) => new(a.X * s, a.Y * s, a.Z * s, a.W * s);
    public static Quatf operator *(float s, Quatf a) => a * s;
    public static Quatf operator /(Quatf a, float s) => new(a.X / s, a.Y / s, a.Z / s, a.W / s);
    public static Quatf operator *(Quatf a, Quatf b) => new(a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y, a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z, a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X, a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
    /// <summary>Normalizes rotation magnitude using the native 1e-8 squared-length threshold; a near-zero quaternion maps to identity.</summary>
    public Mat4f ToMat4()
    {
        float d = LengthSquared, s = MathComparison.IsApproxEqual(d, 0, 1e-8f) ? 0 : (float)(2.0 / d);
        float x = s * X, y = s * Y, z = s * Z, wx = x * W, wy = y * W, wz = z * W, xx = x * X, xy = y * X, xz = z * X, yy = y * Y, yz = z * Y, zz = z * Z;
        return new(1 - (yy + zz), xy + wz, xz - wy, 0, xy - wz, 1 - (xx + zz), yz + wx, 0, xz + wy, yz - wx, 1 - (xx + yy), 0, 0, 0, 0, 1);
    }
    /// <summary>Returns native XYZ Euler angles, including the source's equal-angle decomposition at gimbal lock.</summary>
    public Vec3f EulerAnglesXyz()
    {
        Mat4f m = ToMat4();
        if (MathComparison.IsApproxEqual(m.M20, 1, 1e-8f)) { float angle = (float)(.5 * System.Math.Atan2(m.M12, m.M11)); return new(angle, (float)(System.Math.PI / 2), angle); }
        if (MathComparison.IsApproxEqual(m.M20, -1, 1e-8f)) { float angle = (float)(.5 * System.Math.Atan2(m.M12, m.M11)); return new(angle, (float)(-System.Math.PI / 2), -angle); }
        return new((float)System.Math.Atan2(-m.M21, m.M22), (float)System.Math.Atan2(m.M20, System.MathF.Sqrt(m.M21 * m.M21 + m.M22 * m.M22)), (float)System.Math.Atan2(-m.M10, m.M00));
    }
    public Vec3f RotateVector(Vec3f vector)
    {
        Mat4f m = ToMat4(); return new(vector.X * m.M00 + vector.Y * m.M10 + vector.Z * m.M20, vector.X * m.M01 + vector.Y * m.M11 + vector.Z * m.M21, vector.X * m.M02 + vector.Y * m.M12 + vector.Z * m.M22);
    }
    public void CopyTo(Span<float> destination)
    {
        if (destination.Length < 4) throw new ArgumentException("At least four elements are required.", nameof(destination)); destination[0] = X; destination[1] = Y; destination[2] = Z; destination[3] = W;
    }
    public static Quatf FromSpan(ReadOnlySpan<float> source) => source.Length < 4 ? throw new ArgumentException("At least four elements are required.", nameof(source)) : new(source[0], source[1], source[2], source[3]);
}
