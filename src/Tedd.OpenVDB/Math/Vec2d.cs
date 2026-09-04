// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Vec2.h; immutable .NET value semantics.
using System.Runtime.InteropServices;

namespace Tedd.OpenVDB.Math;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vec2d(double X, double Y)
{
    public Vec2d(double value) : this(value, value) { }
    public static Vec2d Zero => default;
    public static Vec2d One => new(1);
    public static Vec2d UnitX => new(1, 0);
    public static Vec2d UnitY => new(0, 1);
    public double this[int index] => index switch { 0 => X, 1 => Y, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    public double LengthSquared => X * X + Y * Y;
    public double Length => System.Math.Sqrt(LengthSquared);
    public double Sum => X + Y;
    public double Product => X * Y;
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
    public bool Equals(Vec2d other) => X == other.X && Y == other.Y;
    public override int GetHashCode() => HashCode.Combine(X, Y);
    public static Vec2d operator +(Vec2d a, Vec2d b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2d operator -(Vec2d a, Vec2d b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2d operator -(Vec2d a) => new(-a.X, -a.Y);
    public static Vec2d operator +(Vec2d a, double b) => new(a.X + b, a.Y + b);
    public static Vec2d operator +(double a, Vec2d b) => b + a;
    public static Vec2d operator -(Vec2d a, double b) => new(a.X - b, a.Y - b);
    public static Vec2d operator -(double a, Vec2d b) => new(a - b.X, a - b.Y);
    public static Vec2d operator *(Vec2d a, double b) => new(a.X * b, a.Y * b);
    public static Vec2d operator *(double a, Vec2d b) => b * a;
    public static Vec2d operator *(Vec2d a, Vec2d b) => new(a.X * b.X, a.Y * b.Y);
    public static Vec2d operator /(Vec2d a, double b) => new(a.X / b, a.Y / b);
    public static Vec2d operator /(Vec2d a, Vec2d b) => new(a.X / b.X, a.Y / b.Y);
    public static Vec2d operator /(double a, Vec2d b) => new(a / b.X, a / b.Y);
    public double Dot(Vec2d other) => X * other.X + Y * other.Y;
    public static double Dot(Vec2d a, Vec2d b) => a.Dot(b);
    public static Vec2d Min(Vec2d a, Vec2d b) => new(b.X < a.X ? b.X : a.X, b.Y < a.Y ? b.Y : a.Y);
    public static Vec2d Max(Vec2d a, Vec2d b) => new(a.X < b.X ? b.X : a.X, a.Y < b.Y ? b.Y : a.Y);
    public Vec2d Abs() => new(System.Math.Abs(X), System.Math.Abs(Y));
    public Vec2d Exp() => new(System.Math.Exp(X), System.Math.Exp(Y));
    public Vec2d Log() => new(System.Math.Log(X), System.Math.Log(Y));
    public Vec2d Reversed() => new(Y, X);
    public bool ApproxEquals(Vec2d other, double tolerance = 1e-7) => MathComparison.IsApproxEqual(X, other.X, tolerance) && MathComparison.IsApproxEqual(Y, other.Y, tolerance);
    public Vec2d Unit(double epsilon = 0) => Unit(out _, epsilon);
    public Vec2d Unit(out double length, double epsilon = 0)
    {
        length = Length;
        if (MathComparison.IsApproxZero(length, epsilon)) throw new ArithmeticException("Normalizing null 2-vector.");
        return this / length;
    }
    public bool TryNormalize(out Vec2d normalized, double epsilon = 1e-8)
    {
        double length = Length;
        if (MathComparison.IsApproxZero(length, epsilon)) { normalized = this; return false; }
        normalized = this * (1 / length); return true;
    }
    public Vec2d UnitSafe() => MathComparison.IsApproxZero(LengthSquared) ? UnitX : this / Length;
    public static Vec2d Lerp(Vec2d a, Vec2d b, double weight) => a * (1 - weight) + b * weight;
    public void CopyTo(Span<double> destination)
    {
        if (destination.Length < 2) throw new ArgumentException("At least 2 components are required.", nameof(destination));
        destination[0] = X; destination[1] = Y;
    }
    public static Vec2d FromSpan(ReadOnlySpan<double> source)
    {
        if (source.Length < 2) throw new ArgumentException("At least 2 components are required.", nameof(source));
        return new(source[0], source[1]);
    }
    public override string ToString() => $"[{X}, {Y}]";
    public double Component(Vec2d onto, double epsilon = 1e-8) => MathComparison.IsApproxZero(onto.Length, epsilon) ? 0 : Dot(onto) / onto.Length;
    public Vec2d Projection(Vec2d onto, double epsilon = 1e-8) => MathComparison.IsApproxZero(onto.LengthSquared, epsilon) ? Zero : onto * (Dot(onto) / onto.LengthSquared);
    public double Cross(Vec2d other) => X * other.Y - Y * other.X;
    public Vec2d ArbitraryPerpendicular() => new(-Y, X);
}
