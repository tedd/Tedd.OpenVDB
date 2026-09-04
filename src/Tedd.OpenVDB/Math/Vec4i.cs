// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
namespace Tedd.OpenVDB.Math;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vec4i(int X, int Y, int Z, int W)
{
    public Vec4i(int value) : this(value, value, value, value) { }
    public Vec4i(Vec3i xyz, int w = 1) : this(xyz.X, xyz.Y, xyz.Z, w) { }
    public Vec3i Xyz => new(X, Y, Z);
    public static Vec4i Zero => default;
    public static Vec4i One => new(1);
    public int this[int index] => index switch { 0 => X, 1 => Y, 2 => Z, 3 => W, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    public int Sum => X + Y + Z + W;
    public int Product => X * Y * Z * W;
    public int LengthSquared => X * X + Y * Y + Z * Z + W * W;
    public int Dot(Vec4i other) => X * other.X + Y * other.Y + Z * other.Z + W * other.W;
    public static Vec4i operator +(Vec4i a, Vec4i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W);
    public static Vec4i operator -(Vec4i a, Vec4i b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W);
    public static Vec4i operator -(Vec4i a) => new(-a.X, -a.Y, -a.Z, -a.W);
    public static Vec4i operator *(Vec4i a, int b) => new(a.X * b, a.Y * b, a.Z * b, a.W * b);
    public static Vec4i operator *(int a, Vec4i b) => b * a;
    public static Vec4i operator *(Vec4i a, Vec4i b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
    public static Vec4i operator /(Vec4i a, int b) => new(a.X / b, a.Y / b, a.Z / b, a.W / b);
    public void CopyTo(Span<int> destination) { if (destination.Length < 4) throw new ArgumentException("At least four components are required.", nameof(destination)); destination[0] = X; destination[1] = Y; destination[2] = Z; destination[3] = W; }
    public static Vec4i FromSpan(ReadOnlySpan<int> source) => source.Length >= 4 ? new(source[0], source[1], source[2], source[3]) : throw new ArgumentException("At least four components are required.", nameof(source));
    public override string ToString() => $"[{X}, {Y}, {Z}, {W}]";
}
