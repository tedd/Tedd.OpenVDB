// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Vec4.h; immutable .NET value semantics.
using System.Runtime.InteropServices;

namespace Tedd.OpenVDB.Math;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vec4f(float X, float Y, float Z, float W)
{
    public Vec4f(float value) : this(value, value, value, value) { }
    public static Vec4f Zero => default;
    public static Vec4f One => new(1);
    public static Vec4f UnitX => new(1, 0, 0, 0);
    public static Vec4f UnitY => new(0, 1, 0, 0);
    public static Vec4f UnitZ => new(0, 0, 1, 0);
    public static Vec4f UnitW => new(0, 0, 0, 1);
    public float this[int index] => index switch { 0 => X, 1 => Y, 2 => Z, 3 => W, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    public float LengthSquared => X * X + Y * Y + Z * Z + W * W;
    public float Length => System.MathF.Sqrt(LengthSquared);
    public float Sum => X + Y + Z + W;
    public float Product => X * Y * Z * W;
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z) && float.IsFinite(W);
    public bool Equals(Vec4f other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    public static Vec4f operator +(Vec4f a, Vec4f b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Vec4f operator -(Vec4f a, Vec4f b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static Vec4f operator -(Vec4f a) => new(-a.X, -a.Y, -a.Z, -a.W);
    public static Vec4f operator +(Vec4f a, float b) => new(a.X + b, a.Y + b, a.Z + b, a.W + b);
    public static Vec4f operator +(float a, Vec4f b) => b + a;
    public static Vec4f operator -(Vec4f a, float b) => new(a.X - b, a.Y - b, a.Z - b, a.W - b);
    public static Vec4f operator -(float a, Vec4f b) => new(a - b.X, a - b.Y, a - b.Z, a - b.W);
    public static Vec4f operator *(Vec4f a, float b) => new(a.X * b, a.Y * b, a.Z * b, a.W * b);
    public static Vec4f operator *(float a, Vec4f b) => b * a;
    public static Vec4f operator *(Vec4f a, Vec4f b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
    public static Vec4f operator /(Vec4f a, float b) => new(a.X / b, a.Y / b, a.Z / b, a.W / b);
    public static Vec4f operator /(Vec4f a, Vec4f b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
    public static Vec4f operator /(float a, Vec4f b) => new(a / b.X, a / b.Y, a / b.Z, a / b.W);
    public float Dot(Vec4f other) => X * other.X + Y * other.Y + Z * other.Z + W * other.W;
    public static float Dot(Vec4f a, Vec4f b) => a.Dot(b);
    public static Vec4f Min(Vec4f a, Vec4f b) => new(b.X < a.X ? b.X : a.X, b.Y < a.Y ? b.Y : a.Y, b.Z < a.Z ? b.Z : a.Z, b.W < a.W ? b.W : a.W);
    public static Vec4f Max(Vec4f a, Vec4f b) => new(a.X < b.X ? b.X : a.X, a.Y < b.Y ? b.Y : a.Y, a.Z < b.Z ? b.Z : a.Z, a.W < b.W ? b.W : a.W);
    public Vec4f Abs() => new(System.MathF.Abs(X), System.MathF.Abs(Y), System.MathF.Abs(Z), System.MathF.Abs(W));
    public Vec4f Exp() => new(System.MathF.Exp(X), System.MathF.Exp(Y), System.MathF.Exp(Z), System.MathF.Exp(W));
    public Vec4f Log() => new(System.MathF.Log(X), System.MathF.Log(Y), System.MathF.Log(Z), System.MathF.Log(W));
    public Vec4f Reversed() => new(W, Z, Y, X);
    public bool ApproxEquals(Vec4f other, float tolerance = 1e-8f) => MathComparison.IsApproxEqual(X, other.X, tolerance) && MathComparison.IsApproxEqual(Y, other.Y, tolerance) && MathComparison.IsApproxEqual(Z, other.Z, tolerance) && MathComparison.IsApproxEqual(W, other.W, tolerance);
    public Vec4f Unit(float epsilon = 0) => Unit(out _, epsilon);
    public Vec4f Unit(out float length, float epsilon = 0)
    {
        length = Length;
        if (MathComparison.IsApproxZero(length, epsilon)) throw new ArithmeticException("Normalizing null 4-vector.");
        return this / length;
    }
    public bool TryNormalize(out Vec4f normalized, float epsilon = 1e-8f)
    {
        float length = Length;
        if (MathComparison.IsApproxZero(length, epsilon)) { normalized = this; return false; }
        normalized = this * (1 / length); return true;
    }
    public Vec4f UnitSafe() => MathComparison.IsApproxZero(LengthSquared) ? UnitX : this / Length;
    public static Vec4f Lerp(Vec4f a, Vec4f b, float weight) => a * (1 - weight) + b * weight;
    public void CopyTo(Span<float> destination)
    {
        if (destination.Length < 4) throw new ArgumentException("At least 4 components are required.", nameof(destination));
        destination[0] = X; destination[1] = Y; destination[2] = Z; destination[3] = W;
    }
    public static Vec4f FromSpan(ReadOnlySpan<float> source)
    {
        if (source.Length < 4) throw new ArgumentException("At least 4 components are required.", nameof(source));
        return new(source[0], source[1], source[2], source[3]);
    }
    public override string ToString() => $"[{X}, {Y}, {Z}, {W}]";
    public Vec4f(Vec3f xyz, float w = 1) : this(xyz.X, xyz.Y, xyz.Z, w) { }
    public Vec3f Xyz => new(X, Y, Z);
}
