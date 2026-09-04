// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Vec3.h (signed integer specialization).
using System.Runtime.InteropServices;
namespace Tedd.OpenVDB.Math;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vec3i(int X, int Y, int Z)
{
    public Vec3i(int value) : this(value, value, value) { }
    public static Vec3i Zero => default;
    public static Vec3i One => new(1);
    public static Vec3i UnitX => new(1, 0, 0);
    public static Vec3i UnitY => new(0, 1, 0);
    public static Vec3i UnitZ => new(0, 0, 1);
    public int this[int index] => index switch { 0 => X, 1 => Y, 2 => Z, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    // Signed overflow is explicitly defined to wrap, where the C++ source has undefined overflow.
    public int LengthSquared => unchecked(X * X + Y * Y + Z * Z);
    public int Sum => unchecked(X + Y + Z);
    public int Product => unchecked(X * Y * Z);
    public static Vec3i operator +(Vec3i a, Vec3i b) => new(unchecked(a.X + b.X), unchecked(a.Y + b.Y), unchecked(a.Z + b.Z));
    public static Vec3i operator -(Vec3i a, Vec3i b) => new(unchecked(a.X - b.X), unchecked(a.Y - b.Y), unchecked(a.Z - b.Z));
    public static Vec3i operator -(Vec3i a) => new(unchecked(-a.X), unchecked(-a.Y), unchecked(-a.Z));
    public static Vec3i operator +(Vec3i a, int b) => a + new Vec3i(b);
    public static Vec3i operator -(Vec3i a, int b) => a - new Vec3i(b);
    public static Vec3i operator *(Vec3i a, int b) => new(unchecked(a.X * b), unchecked(a.Y * b), unchecked(a.Z * b));
    public static Vec3i operator *(int a, Vec3i b) => b * a;
    public static Vec3i operator *(Vec3i a, Vec3i b) => new(unchecked(a.X * b.X), unchecked(a.Y * b.Y), unchecked(a.Z * b.Z));
    public static Vec3i operator /(Vec3i a, int b) => new(a.X / b, a.Y / b, a.Z / b);
    public static Vec3i operator /(Vec3i a, Vec3i b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
    public int Dot(Vec3i b) => unchecked(X * b.X + Y * b.Y + Z * b.Z);
    public static int Dot(Vec3i a, Vec3i b) => a.Dot(b);
    public Vec3i Cross(Vec3i b) => new(unchecked(Y * b.Z - Z * b.Y), unchecked(Z * b.X - X * b.Z), unchecked(X * b.Y - Y * b.X));
    public static Vec3i Cross(Vec3i a, Vec3i b) => a.Cross(b);
    public static Vec3i Min(Vec3i a, Vec3i b) => new(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y), System.Math.Min(a.Z, b.Z));
    public static Vec3i Max(Vec3i a, Vec3i b) => new(System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y), System.Math.Max(a.Z, b.Z));
    public Vec3i Reversed() => new(Z, Y, X);
    public Vec3i Sorted()
    {
        int x = X, y = Y, z = Z;
        if (x > y) (x, y) = (y, x);
        if (y > z) (y, z) = (z, y);
        if (x > y) (x, y) = (y, x);
        return new(x, y, z);
    }
    public Coord AsCoord() => new(X, Y, Z);
    public Vec3d AsVec3d() => new(X, Y, Z);
    public Vec3f AsVec3f() => new(X, Y, Z);
    public static explicit operator Vec3i(Coord value) => new(value.X, value.Y, value.Z);
    public static explicit operator Coord(Vec3i value) => value.AsCoord();
    public static explicit operator Vec3i(Vec3d value) => new(checked((int)value.X), checked((int)value.Y), checked((int)value.Z));
    public static implicit operator Vec3d(Vec3i value) => value.AsVec3d();
    public void CopyTo(Span<int> destination)
    {
        if (destination.Length < 3) throw new ArgumentException("At least three components are required.", nameof(destination));
        destination[0] = X; destination[1] = Y; destination[2] = Z;
    }
    public static Vec3i FromSpan(ReadOnlySpan<int> source)
    {
        if (source.Length < 3) throw new ArgumentException("At least three components are required.", nameof(source));
        return new(source[0], source[1], source[2]);
    }
    public override string ToString() => $"[{X}, {Y}, {Z}]";
}
