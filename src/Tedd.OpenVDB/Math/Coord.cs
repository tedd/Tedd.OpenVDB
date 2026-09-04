// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Coord.h.
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tedd.OpenVDB.Math;

/// <summary>Signed 32-bit voxel coordinates, ordered lexicographically (X, Y, Z).</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Coord(int X, int Y, int Z) : IComparable<Coord>
{
    public Coord(int xyz) : this(xyz, xyz, xyz) { }
    public static Coord Zero => default;
    public static Coord MinValue => new(int.MinValue);
    public static Coord MaxValue => new(int.MaxValue);
    public int this[int axis] => axis switch { 0 => X, 1 => Y, 2 => Z, _ => throw new ArgumentOutOfRangeException(nameof(axis)) };
    public Coord WithComponent(int axis, int value) => axis switch
    {
        0 => this with { X = value },
        1 => this with { Y = value },
        2 => this with { Z = value },
        _ => throw new ArgumentOutOfRangeException(nameof(axis))
    };
    public int CompareTo(Coord other)
    {
        int comparison = X.CompareTo(other.X);
        if (comparison != 0) return comparison;
        comparison = Y.CompareTo(other.Y);
        return comparison != 0 ? comparison : Z.CompareTo(other.Z);
    }
    public static bool operator <(Coord a, Coord b) => a.CompareTo(b) < 0;
    public static bool operator <=(Coord a, Coord b) => a.CompareTo(b) <= 0;
    public static bool operator >(Coord a, Coord b) => a.CompareTo(b) > 0;
    public static bool operator >=(Coord a, Coord b) => a.CompareTo(b) >= 0;
    // Overflow is explicitly two's-complement wrapping, independently of the caller's checked context.
    public static Coord operator +(Coord a, Coord b) => new(unchecked(a.X + b.X), unchecked(a.Y + b.Y), unchecked(a.Z + b.Z));
    public static Coord operator -(Coord a, Coord b) => new(unchecked(a.X - b.X), unchecked(a.Y - b.Y), unchecked(a.Z - b.Z));
    public static Coord operator -(Coord a) => new(unchecked(-a.X), unchecked(-a.Y), unchecked(-a.Z));
    public static Coord operator *(Coord a, int scale) => new(unchecked(a.X * scale), unchecked(a.Y * scale), unchecked(a.Z * scale));
    public static Coord operator *(int scale, Coord a) => a * scale;
    public static Coord operator >>(Coord a, int bits) => new(a.X >> bits, a.Y >> bits, a.Z >> bits);
    public static Coord operator <<(Coord a, int bits) => new(a.X << bits, a.Y << bits, a.Z << bits);
    public static Coord operator &(Coord a, int mask) => new(a.X & mask, a.Y & mask, a.Z & mask);
    public static Coord operator |(Coord a, int mask) => new(a.X | mask, a.Y | mask, a.Z | mask);
    public Coord OffsetBy(int value) => this + new Coord(value);
    public Coord OffsetBy(int dx, int dy, int dz) => this + new Coord(dx, dy, dz);
    public static Coord Min(Coord a, Coord b) => new(System.Math.Min(a.X, b.X), System.Math.Min(a.Y, b.Y), System.Math.Min(a.Z, b.Z));
    public static Coord Max(Coord a, Coord b) => new(System.Math.Max(a.X, b.X), System.Math.Max(a.Y, b.Y), System.Math.Max(a.Z, b.Z));
    /// <summary>True when any component of a is less than its corresponding component of b.</summary>
    public static bool LessThan(Coord a, Coord b) => a.X < b.X || a.Y < b.Y || a.Z < b.Z;
    /// <summary>Aligns toward negative infinity to a power-of-two node boundary.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Coord AlignDown(int log2Dimension)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)log2Dimension, 31u, nameof(log2Dimension));
        return this & unchecked(-1 << log2Dimension);
    }
    public Vec3d AsVec3d() => new(X, Y, Z);
    /// <summary>Cell-centered rounding: half-integers round toward positive infinity, as in OpenVDB.</summary>
    public static Coord Round(Vec3d value) => new(checked((int)System.Math.Floor(value.X + 0.5)), checked((int)System.Math.Floor(value.Y + 0.5)), checked((int)System.Math.Floor(value.Z + 0.5)));
    public static Coord Floor(Vec3d value) => new(checked((int)System.Math.Floor(value.X)), checked((int)System.Math.Floor(value.Y)), checked((int)System.Math.Floor(value.Z)));
    public static Coord Ceiling(Vec3d value) => new(checked((int)System.Math.Ceiling(value.X)), checked((int)System.Math.Ceiling(value.Y)), checked((int)System.Math.Ceiling(value.Z)));
    public static Coord Ceil(Vec3d value) => Ceiling(value);
    /// <summary>The coordinate hash used by OpenVDB; log2TableSize is in [0,32].</summary>
    public uint Hash(int log2TableSize = 20)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)log2TableSize, 32u, nameof(log2TableSize));
        uint mask = log2TableSize == 32 ? uint.MaxValue : (1u << log2TableSize) - 1u;
        return mask & unchecked((uint)X * 73856093u ^ (uint)Y * 19349669u ^ (uint)Z * 83492791u);
    }
    public override int GetHashCode() => unchecked((int)Hash(32));
    public override string ToString() => $"[{X}, {Y}, {Z}]";
}
