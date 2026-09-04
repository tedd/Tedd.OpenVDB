// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Vec2.h; immutable .NET value semantics.
using System.Runtime.InteropServices;

namespace Tedd.OpenVDB.Math;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vec2f(float X, float Y)
{
    public Vec2f(float value) : this(value, value) { }
    public static Vec2f Zero => default;
    public static Vec2f One => new(1);
    public static Vec2f UnitX => new(1, 0);
    public static Vec2f UnitY => new(0, 1);
    public float this[int index] => index switch { 0 => X, 1 => Y, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    public float LengthSquared => X * X + Y * Y;
    public float Length => System.MathF.Sqrt(LengthSquared);
    public float Sum => X + Y;
    public float Product => X * Y;
    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y);
    public bool Equals(Vec2f other) => X == other.X && Y == other.Y;
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static Vec2f operator +(Vec2f a, Vec2f b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2f operator -(Vec2f a, Vec2f b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2f operator -(Vec2f a) => new(-a.X, -a.Y);
    public static Vec2f operator +(Vec2f a, float b) => new(a.X + b, a.Y + b);
    public static Vec2f operator +(float a, Vec2f b) => b + a;
    public static Vec2f operator -(Vec2f a, float b) => new(a.X - b, a.Y - b);
    public static Vec2f operator -(float a, Vec2f b) => new(a - b.X, a - b.Y);
    public static Vec2f operator *(Vec2f a, float b) => new(a.X * b, a.Y * b);
    public static Vec2f operator *(float a, Vec2f b) => b * a;
    public static Vec2f operator *(Vec2f a, Vec2f b) => new(a.X * b.X, a.Y * b.Y);
    public static Vec2f operator /(Vec2f a, float b) => new(a.X / b, a.Y / b);
    public static Vec2f operator /(Vec2f a, Vec2f b) => new(a.X / b.X, a.Y / b.Y);
    public static Vec2f operator /(float a, Vec2f b) => new(a / b.X, a / b.Y);
    public float Dot(Vec2f other) => X * other.X + Y * other.Y;
    public static float Dot(Vec2f a, Vec2f b) => a.Dot(b);
    public static Vec2f Min(Vec2f a, Vec2f b) => new(b.X < a.X ? b.X : a.X, b.Y < a.Y ? b.Y : a.Y);
    public static Vec2f Max(Vec2f a, Vec2f b) => new(a.X < b.X ? b.X : a.X, a.Y < b.Y ? b.Y : a.Y);
    public Vec2f Abs() => new(System.MathF.Abs(X), System.MathF.Abs(Y));
    public Vec2f Exp() => new(System.MathF.Exp(X), System.MathF.Exp(Y));
    public Vec2f Log() => new(System.MathF.Log(X), System.MathF.Log(Y));
    public Vec2f Reversed() => new(Y, X);
    public bool ApproxEquals(Vec2f other, float tolerance = 1e-7f) => MathComparison.IsApproxEqual(X, other.X, tolerance) && MathComparison.IsApproxEqual(Y, other.Y, tolerance);
    public Vec2f Unit(float epsilon = 0) => Unit(out _, epsilon);
    public Vec2f Unit(out float length, float epsilon = 0)
    {
        length = Length;
        if (MathComparison.IsApproxZero(length, epsilon)) throw new ArithmeticException("Normalizing null 2-vector.");
        return this / length;
    }
    public bool TryNormalize(out Vec2f normalized, float epsilon = 1e-8f)
    {
        float length = Length;
        if (MathComparison.IsApproxZero(length, epsilon)) { normalized = this; return false; }
        normalized = this * (1 / length); return true;
    }
    public Vec2f UnitSafe() => MathComparison.IsApproxZero(LengthSquared) ? UnitX : this / Length;
    public static Vec2f Lerp(Vec2f a, Vec2f b, float weight) => a * (1 - weight) + b * weight;
    public void CopyTo(Span<float> destination)
    {
        if (destination.Length < 2) throw new ArgumentException("At least 2 components are required.", nameof(destination));
        destination[0] = X; destination[1] = Y;
    }
    public static Vec2f FromSpan(ReadOnlySpan<float> source)
    {
        if (source.Length < 2) throw new ArgumentException("At least 2 components are required.", nameof(source));
        return new(source[0], source[1]);
    }
    public override string ToString() => $"[{X}, {Y}]";
    public float Component(Vec2f onto, float epsilon = 1e-8f) => MathComparison.IsApproxZero(onto.Length, epsilon) ? 0 : Dot(onto) / onto.Length;
    public Vec2f Projection(Vec2f onto, float epsilon = 1e-8f) => MathComparison.IsApproxZero(onto.LengthSquared, epsilon) ? Zero : onto * (Dot(onto) / onto.LengthSquared);
    public float Cross(Vec2f other) => X * other.Y - Y * other.X;
    public Vec2f ArbitraryPerpendicular() => new(-Y, X);
}
