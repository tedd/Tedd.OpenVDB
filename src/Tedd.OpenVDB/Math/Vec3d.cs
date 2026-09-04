// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Vec3.h.
using System.Runtime.InteropServices;

namespace Tedd.OpenVDB.Math;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vec3d(double X, double Y, double Z)
{
    public Vec3d(double xyz) : this(xyz, xyz, xyz) { }
    public static Vec3d Zero => default;
    public static Vec3d One => new(1);
    public static Vec3d UnitX => new(1, 0, 0);
    public static Vec3d UnitY => new(0, 1, 0);
    public static Vec3d UnitZ => new(0, 0, 1);
    public double this[int axis] => axis switch { 0 => X, 1 => Y, 2 => Z, _ => throw new ArgumentOutOfRangeException(nameof(axis)) };
    public double LengthSquared => X * X + Y * Y + Z * Z;
    public double Length => System.Math.Sqrt(LengthSquared);
    public double Sum => X + Y + Z;
    public double Product => X * Y * Z;
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator -(Vec3d a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3d operator +(Vec3d a, double b) => new(a.X + b, a.Y + b, a.Z + b);
    public static Vec3d operator +(double a, Vec3d b) => b + a;
    public static Vec3d operator -(Vec3d a, double b) => new(a.X - b, a.Y - b, a.Z - b);
    public static Vec3d operator *(Vec3d a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public static Vec3d operator *(double a, Vec3d b) => b * a;
    public static Vec3d operator *(Vec3d a, Vec3d b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static Vec3d operator /(Vec3d a, double b) => new(a.X / b, a.Y / b, a.Z / b);
    public static Vec3d operator /(Vec3d a, Vec3d b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
    public double Dot(Vec3d other) => X * other.X + Y * other.Y + Z * other.Z;
    public static double Dot(Vec3d a, Vec3d b) => a.Dot(b);
    public Vec3d Cross(Vec3d other) => new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);
    public static Vec3d Cross(Vec3d a, Vec3d b) => a.Cross(b);
    public static Vec3d Min(Vec3d a, Vec3d b) => new(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y), System.Math.Min(a.Z, b.Z));
    public static Vec3d Max(Vec3d a, Vec3d b) => new(System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y), System.Math.Max(a.Z, b.Z));
    public Vec3d Abs() => new(System.Math.Abs(X), System.Math.Abs(Y), System.Math.Abs(Z));
    public Vec3d Exp() => new(System.Math.Exp(X), System.Math.Exp(Y), System.Math.Exp(Z));
    public Vec3d Log() => new(System.Math.Log(X), System.Math.Log(Y), System.Math.Log(Z));
    public Vec3d Reversed() => new(Z, Y, X);
    public Vec3d Sorted()
    {
        double x = X, y = Y, z = Z;
        if (x > y) (x, y) = (y, x);
        if (y > z) (y, z) = (z, y);
        if (x > y) (x, y) = (y, x);
        return new(x, y, z);
    }
    public bool ApproxEquals(Vec3d other, double tolerance = 1e-8) =>
        System.Math.Abs(X - other.X) <= tolerance && System.Math.Abs(Y - other.Y) <= tolerance && System.Math.Abs(Z - other.Z) <= tolerance;
    public Vec3d Unit(double epsilon = 0) => Unit(out _, epsilon);
    public Vec3d Unit(out double length, double epsilon = 0)
    {
        length = Length;
        if (length <= epsilon) throw new ArithmeticException("Normalizing null 3-vector.");
        return this / length;
    }
    public bool TryNormalize(out Vec3d normalized, double epsilon = 1e-7)
    {
        double length = Length;
        if (length <= epsilon) { normalized = this; return false; }
        normalized = this * (1 / length); return true;
    }
    public Vec3d UnitSafe() => LengthSquared > 1e-15 ? this / Length : UnitX;
    public double Component(Vec3d onto, double epsilon = 1e-7) => onto.Length <= epsilon ? 0 : Dot(onto) / onto.Length;
    public Vec3d Projection(Vec3d onto, double epsilon = 1e-7) => onto.LengthSquared <= epsilon ? Zero : onto * (Dot(onto) / onto.LengthSquared);
    /// <summary>Returns a unit perpendicular to this nonzero vector.</summary>
    public Vec3d ArbitraryPerpendicular() => System.Math.Abs(X) >= System.Math.Abs(Y)
        ? new Vec3d(-Z, 0, X) / System.Math.Sqrt(X * X + Z * Z)
        : new Vec3d(0, Z, -Y) / System.Math.Sqrt(Y * Y + Z * Z);
    public static Vec3d Lerp(Vec3d a, Vec3d b, double weight) => a * (1 - weight) + b * weight;
    public void CopyTo(Span<double> destination)
    {
        if (destination.Length < 3) throw new ArgumentException("At least three components are required.", nameof(destination));
        destination[0] = X; destination[1] = Y; destination[2] = Z;
    }
    public static Vec3d FromSpan(ReadOnlySpan<double> source)
    {
        if (source.Length < 3) throw new ArgumentException("At least three components are required.", nameof(source));
        return new(source[0], source[1], source[2]);
    }
    public override string ToString() => $"[{X}, {Y}, {Z}]";
}
