// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
namespace Tedd.OpenVDB.Math;

[StructLayout(LayoutKind.Sequential)]
public readonly record struct Vec2i(int X, int Y)
{
    public Vec2i(int value) : this(value, value) { }
    public static Vec2i Zero => default;
    public static Vec2i One => new(1);
    public int this[int index] => index switch { 0 => X, 1 => Y, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    public int Sum => X + Y;
    public int Product => X * Y;
    public int LengthSquared => X * X + Y * Y;
    public int Dot(Vec2i other) => X * other.X + Y * other.Y;
    public static Vec2i operator +(Vec2i a, Vec2i b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2i operator -(Vec2i a, Vec2i b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2i operator -(Vec2i a) => new(-a.X, -a.Y);
    public static Vec2i operator *(Vec2i a, int b) => new(a.X * b, a.Y * b);
    public static Vec2i operator *(int a, Vec2i b) => b * a;
    public static Vec2i operator *(Vec2i a, Vec2i b) => new(a.X * b.X, a.Y * b.Y);
    public static Vec2i operator /(Vec2i a, int b) => new(a.X / b, a.Y / b);
    public static Vec2i Min(Vec2i a, Vec2i b) => new(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y));
    public static Vec2i Max(Vec2i a, Vec2i b) => new(System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y));
    public void CopyTo(Span<int> destination) { if (destination.Length < 2) throw new ArgumentException("At least two components are required.", nameof(destination)); destination[0] = X; destination[1] = Y; }
    public static Vec2i FromSpan(ReadOnlySpan<int> source) => source.Length >= 2 ? new(source[0], source[1]) : throw new ArgumentException("At least two components are required.", nameof(source));
    public override string ToString() => $"[{X}, {Y}]";
}
