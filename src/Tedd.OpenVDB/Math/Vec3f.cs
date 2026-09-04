// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Vec3.h; immutable .NET value semantics.
using System.Runtime.InteropServices;

namespace Tedd.OpenVDB.Math;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vec3f(float X, float Y, float Z)
{
    public Vec3f(float value) : this(value, value, value) { }
    public static Vec3f Zero => default;
    public static Vec3f One => new(1);
    public static Vec3f UnitX => new(1, 0, 0);
    public static Vec3f UnitY => new(0, 1, 0);
    public static Vec3f UnitZ => new(0, 0, 1);
    public float this[int index] => index switch { 0 => X, 1 => Y, 2 => Z, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    public float LengthSquared => X * X + Y * Y + Z * Z;
    public float Length => System.MathF.Sqrt(LengthSquared);
    public float Sum => X + Y + Z;
    public float Product => X * Y * Z;
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
    public bool Equals(Vec3f other) => X == other.X && Y == other.Y && Z == other.Z;
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static Vec3f operator +(Vec3f a, Vec3f b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3f operator -(Vec3f a, Vec3f b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3f operator -(Vec3f a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3f operator +(Vec3f a, float b) => new(a.X + b, a.Y + b, a.Z + b);
    public static Vec3f operator +(float a, Vec3f b) => b + a;
    public static Vec3f operator -(Vec3f a, float b) => new(a.X - b, a.Y - b, a.Z - b);
    public static Vec3f operator -(float a, Vec3f b) => new(a - b.X, a - b.Y, a - b.Z);
    public static Vec3f operator *(Vec3f a, float b) => new(a.X * b, a.Y * b, a.Z * b);
    public static Vec3f operator *(float a, Vec3f b) => b * a;
    public static Vec3f operator *(Vec3f a, Vec3f b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static Vec3f operator /(Vec3f a, float b) => new(a.X / b, a.Y / b, a.Z / b);
    public static Vec3f operator /(Vec3f a, Vec3f b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
    public static Vec3f operator /(float a, Vec3f b) => new(a / b.X, a / b.Y, a / b.Z);
    public float Dot(Vec3f other) => X * other.X + Y * other.Y + Z * other.Z;
    public static float Dot(Vec3f a, Vec3f b) => a.Dot(b);
    public static Vec3f Min(Vec3f a, Vec3f b) => new(b.X < a.X ? b.X : a.X, b.Y < a.Y ? b.Y : a.Y, b.Z < a.Z ? b.Z : a.Z);
    public static Vec3f Max(Vec3f a, Vec3f b) => new(a.X < b.X ? b.X : a.X, a.Y < b.Y ? b.Y : a.Y, a.Z < b.Z ? b.Z : a.Z);
    public Vec3f Abs() => new(System.MathF.Abs(X), System.MathF.Abs(Y), System.MathF.Abs(Z));
    public Vec3f Exp() => new(System.MathF.Exp(X), System.MathF.Exp(Y), System.MathF.Exp(Z));
    public Vec3f Log() => new(System.MathF.Log(X), System.MathF.Log(Y), System.MathF.Log(Z));
    public Vec3f Reversed() => new(Z, Y, X);
    public bool ApproxEquals(Vec3f other, float tolerance = 1e-7f) => MathComparison.IsRelOrApproxEqual(X, other.X, tolerance, tolerance) && MathComparison.IsRelOrApproxEqual(Y, other.Y, tolerance, tolerance) && MathComparison.IsRelOrApproxEqual(Z, other.Z, tolerance, tolerance);
    public Vec3f Unit(float epsilon = 0) => Unit(out _, epsilon);
    public Vec3f Unit(out float length, float epsilon = 0)
    {
        length = Length;
        if (MathComparison.IsApproxZero(length, epsilon)) throw new ArithmeticException("Normalizing null 3-vector.");
        return this / length;
    }
    public bool TryNormalize(out Vec3f normalized, float epsilon = 1e-7f)
    {
        float length = Length;
        if (MathComparison.IsApproxZero(length, epsilon)) { normalized = this; return false; }
        normalized = this * (1 / length); return true;
    }
    public Vec3f UnitSafe() => MathComparison.IsApproxZero(LengthSquared) ? UnitX : this / Length;
    public static Vec3f Lerp(Vec3f a, Vec3f b, float weight) => a * (1 - weight) + b * weight;
    public void CopyTo(Span<float> destination)
    {
        if (destination.Length < 3) throw new ArgumentException("At least 3 components are required.", nameof(destination));
        destination[0] = X; destination[1] = Y; destination[2] = Z;
    }
    public static Vec3f FromSpan(ReadOnlySpan<float> source)
    {
        if (source.Length < 3) throw new ArgumentException("At least 3 components are required.", nameof(source));
        return new(source[0], source[1], source[2]);
    }
    public override string ToString() => $"[{X}, {Y}, {Z}]";
    public float Component(Vec3f onto, float epsilon = 1e-7f) => MathComparison.IsApproxZero(onto.Length, epsilon) ? 0 : Dot(onto) / onto.Length;
    public Vec3f Projection(Vec3f onto, float epsilon = 1e-7f) => MathComparison.IsApproxZero(onto.LengthSquared, epsilon) ? Zero : onto * (Dot(onto) / onto.LengthSquared);
    public Vec3f Cross(Vec3f other) => new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);
    public static Vec3f Cross(Vec3f a, Vec3f b) => a.Cross(b);
    public Vec3d AsVec3d() => new(X, Y, Z);
    public static explicit operator Vec3f(Vec3d value) => new((float)value.X, (float)value.Y, (float)value.Z);
    public static implicit operator Vec3d(Vec3f value) => value.AsVec3d();
    public Vec3f Sorted()
    {
        float x = X, y = Y, z = Z;
        if (x > y) (x, y) = (y, x);
        if (y > z) (y, z) = (z, y);
        if (x > y) (x, y) = (y, x);
        return new(x, y, z);
    }
    public Vec3f ArbitraryPerpendicular() => System.MathF.Abs(X) >= System.MathF.Abs(Y)
        ? new Vec3f(-Z, 0, X) / System.MathF.Sqrt(X * X + Z * Z)
        : new Vec3f(0, Z, -Y) / System.MathF.Sqrt(Y * Y + Z * Z);
}
