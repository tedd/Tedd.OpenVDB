// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Vec4.h; immutable .NET value semantics.
using System.Runtime.InteropServices;

namespace Tedd.OpenVDB.Math;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vec4d(double X, double Y, double Z, double W)
{
    public Vec4d(double value) : this(value, value, value, value) { }
    public static Vec4d Zero => default;
    public static Vec4d One => new(1);
    public static Vec4d UnitX => new(1, 0, 0, 0);
    public static Vec4d UnitY => new(0, 1, 0, 0);
    public static Vec4d UnitZ => new(0, 0, 1, 0);
    public static Vec4d UnitW => new(0, 0, 0, 1);
    public double this[int index] => index switch { 0 => X, 1 => Y, 2 => Z, 3 => W, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    public double LengthSquared => X * X + Y * Y + Z * Z + W * W;
    public double Length => System.Math.Sqrt(LengthSquared);
    public double Sum => X + Y + Z + W;
    public double Product => X * Y * Z * W;
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z) && double.IsFinite(W);
    public bool Equals(Vec4d other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    public static Vec4d operator +(Vec4d a, Vec4d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Vec4d operator -(Vec4d a, Vec4d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static Vec4d operator -(Vec4d a) => new(-a.X, -a.Y, -a.Z, -a.W);
    public static Vec4d operator +(Vec4d a, double b) => new(a.X + b, a.Y + b, a.Z + b, a.W + b);
    public static Vec4d operator +(double a, Vec4d b) => b + a;
    public static Vec4d operator -(Vec4d a, double b) => new(a.X - b, a.Y - b, a.Z - b, a.W - b);
    public static Vec4d operator -(double a, Vec4d b) => new(a - b.X, a - b.Y, a - b.Z, a - b.W);
    public static Vec4d operator *(Vec4d a, double b) => new(a.X * b, a.Y * b, a.Z * b, a.W * b);
    public static Vec4d operator *(double a, Vec4d b) => b * a;
    public static Vec4d operator *(Vec4d a, Vec4d b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
    public static Vec4d operator /(Vec4d a, double b) => new(a.X / b, a.Y / b, a.Z / b, a.W / b);
    public static Vec4d operator /(Vec4d a, Vec4d b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z, a.W / b.W);
    public static Vec4d operator /(double a, Vec4d b) => new(a / b.X, a / b.Y, a / b.Z, a / b.W);
    public double Dot(Vec4d other) => X * other.X + Y * other.Y + Z * other.Z + W * other.W;
    public static double Dot(Vec4d a, Vec4d b) => a.Dot(b);
    public static Vec4d Min(Vec4d a, Vec4d b) => new(b.X < a.X ? b.X : a.X, b.Y < a.Y ? b.Y : a.Y, b.Z < a.Z ? b.Z : a.Z, b.W < a.W ? b.W : a.W);
    public static Vec4d Max(Vec4d a, Vec4d b) => new(a.X < b.X ? b.X : a.X, a.Y < b.Y ? b.Y : a.Y, a.Z < b.Z ? b.Z : a.Z, a.W < b.W ? b.W : a.W);
    public Vec4d Abs() => new(System.Math.Abs(X), System.Math.Abs(Y), System.Math.Abs(Z), System.Math.Abs(W));
    public Vec4d Exp() => new(System.Math.Exp(X), System.Math.Exp(Y), System.Math.Exp(Z), System.Math.Exp(W));
    public Vec4d Log() => new(System.Math.Log(X), System.Math.Log(Y), System.Math.Log(Z), System.Math.Log(W));
    public Vec4d Reversed() => new(W, Z, Y, X);
    public bool ApproxEquals(Vec4d other, double tolerance = 1e-8) => MathComparison.IsApproxEqual(X, other.X, tolerance) && MathComparison.IsApproxEqual(Y, other.Y, tolerance) && MathComparison.IsApproxEqual(Z, other.Z, tolerance) && MathComparison.IsApproxEqual(W, other.W, tolerance);
    public Vec4d Unit(double epsilon = 0) => Unit(out _, epsilon);
    public Vec4d Unit(out double length, double epsilon = 0)
    {
        length = Length;
        if (MathComparison.IsApproxZero(length, epsilon)) throw new ArithmeticException("Normalizing null 4-vector.");
        return this / length;
    }
    public bool TryNormalize(out Vec4d normalized, double epsilon = 1e-8)
    {
        double length = Length;
        if (MathComparison.IsApproxZero(length, epsilon)) { normalized = this; return false; }
        normalized = this * (1 / length); return true;
    }
    public Vec4d UnitSafe() => MathComparison.IsApproxZero(LengthSquared) ? UnitX : this / Length;
    public static Vec4d Lerp(Vec4d a, Vec4d b, double weight) => a * (1 - weight) + b * weight;
    public void CopyTo(Span<double> destination)
    {
        if (destination.Length < 4) throw new ArgumentException("At least 4 components are required.", nameof(destination));
        destination[0] = X; destination[1] = Y; destination[2] = Z; destination[3] = W;
    }
    public static Vec4d FromSpan(ReadOnlySpan<double> source)
    {
        if (source.Length < 4) throw new ArgumentException("At least 4 components are required.", nameof(source));
        return new(source[0], source[1], source[2], source[3]);
    }
    public override string ToString() => $"[{X}, {Y}, {Z}, {W}]";
    public Vec4d(Vec3d xyz, double w = 1) : this(xyz.X, xyz.Y, xyz.Z, w) { }
    public Vec3d Xyz => new(X, Y, Z);
}
