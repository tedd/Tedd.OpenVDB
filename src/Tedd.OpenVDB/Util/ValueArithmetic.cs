// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Util;

// Closed type branches specialize away in the JIT. No boxing, reflection, or dynamic code.
internal static class ValueArithmetic<T> where T : unmanaged
{
    internal static T Negative(T value)
    {
        if (typeof(T) == typeof(sbyte)) { var result = unchecked((sbyte)(-Unsafe.As<T, sbyte>(ref value))); return Unsafe.As<sbyte, T>(ref result); }
        if (typeof(T) == typeof(byte)) { var result = unchecked((byte)(0 - Unsafe.As<T, byte>(ref value))); return Unsafe.As<byte, T>(ref result); }
        if (typeof(T) == typeof(short)) { var result = unchecked((short)(-Unsafe.As<T, short>(ref value))); return Unsafe.As<short, T>(ref result); }
        if (typeof(T) == typeof(ushort)) { var result = unchecked((ushort)(0 - Unsafe.As<T, ushort>(ref value))); return Unsafe.As<ushort, T>(ref result); }
        if (typeof(T) == typeof(int)) { var result = unchecked((int)(-Unsafe.As<T, int>(ref value))); return Unsafe.As<int, T>(ref result); }
        if (typeof(T) == typeof(uint)) { var result = unchecked((uint)(0 - Unsafe.As<T, uint>(ref value))); return Unsafe.As<uint, T>(ref result); }
        if (typeof(T) == typeof(long)) { var result = unchecked((long)(-Unsafe.As<T, long>(ref value))); return Unsafe.As<long, T>(ref result); }
        if (typeof(T) == typeof(ulong)) { var result = unchecked((ulong)(0 - Unsafe.As<T, ulong>(ref value))); return Unsafe.As<ulong, T>(ref result); }
        if (typeof(T) == typeof(nint)) { var result = unchecked((nint)(-Unsafe.As<T, nint>(ref value))); return Unsafe.As<nint, T>(ref result); }
        if (typeof(T) == typeof(nuint)) { var result = unchecked((nuint)(0 - Unsafe.As<T, nuint>(ref value))); return Unsafe.As<nuint, T>(ref result); }
        if (typeof(T) == typeof(Int128)) { var result = unchecked((Int128)(-Unsafe.As<T, Int128>(ref value))); return Unsafe.As<Int128, T>(ref result); }
        if (typeof(T) == typeof(UInt128)) { var result = unchecked((UInt128)(0 - Unsafe.As<T, UInt128>(ref value))); return Unsafe.As<UInt128, T>(ref result); }
        if (typeof(T) == typeof(float)) { var result = unchecked((float)(-Unsafe.As<T, float>(ref value))); return Unsafe.As<float, T>(ref result); }
        if (typeof(T) == typeof(double)) { var result = unchecked((double)(-Unsafe.As<T, double>(ref value))); return Unsafe.As<double, T>(ref result); }
        if (typeof(T) == typeof(Half)) { var result = unchecked((Half)(-Unsafe.As<T, Half>(ref value))); return Unsafe.As<Half, T>(ref result); }
        if (typeof(T) == typeof(decimal)) { var result = unchecked((decimal)(-Unsafe.As<T, decimal>(ref value))); return Unsafe.As<decimal, T>(ref result); }
        if (typeof(T) == typeof(Vec2d)) { var result = unchecked((Vec2d)(-Unsafe.As<T, Vec2d>(ref value))); return Unsafe.As<Vec2d, T>(ref result); }
        if (typeof(T) == typeof(Vec3d)) { var result = unchecked((Vec3d)(-Unsafe.As<T, Vec3d>(ref value))); return Unsafe.As<Vec3d, T>(ref result); }
        if (typeof(T) == typeof(Vec3f)) { var result = unchecked((Vec3f)(-Unsafe.As<T, Vec3f>(ref value))); return Unsafe.As<Vec3f, T>(ref result); }
        if (typeof(T) == typeof(Vec3i)) { var result = unchecked((Vec3i)(-Unsafe.As<T, Vec3i>(ref value))); return Unsafe.As<Vec3i, T>(ref result); }
        if (typeof(T) == typeof(Vec4d)) { var result = unchecked((Vec4d)(-Unsafe.As<T, Vec4d>(ref value))); return Unsafe.As<Vec4d, T>(ref result); }
        if (typeof(T) == typeof(Coord)) { var result = unchecked((Coord)(-Unsafe.As<T, Coord>(ref value))); return Unsafe.As<Coord, T>(ref result); }
        if (typeof(T) == typeof(bool)) { var result = !Unsafe.As<T, bool>(ref value); return Unsafe.As<bool, T>(ref result); }
        throw new NotSupportedException($"Signed background operations are not defined for {typeof(T)}.");
    }
    internal static bool ApproximatelyEqual(T left, T right)
    {
        if (typeof(T) == typeof(float)) return !(System.MathF.Abs(Unsafe.As<T, float>(ref left) - Unsafe.As<T, float>(ref right)) > 1e-8f);
        if (typeof(T) == typeof(double)) return !(System.Math.Abs(Unsafe.As<T, double>(ref left) - Unsafe.As<T, double>(ref right)) > 1e-15);
        if (typeof(T) == typeof(Half)) return !(System.MathF.Abs((float)Unsafe.As<T, Half>(ref left) - (float)Unsafe.As<T, Half>(ref right)) > (float)(Half)0.00097656f);
        if (typeof(T) == typeof(Vec2d)) return Unsafe.As<T, Vec2d>(ref left).ApproxEquals(Unsafe.As<T, Vec2d>(ref right));
        if (typeof(T) == typeof(Vec3d)) return Unsafe.As<T, Vec3d>(ref left).ApproxEquals(Unsafe.As<T, Vec3d>(ref right));
        if (typeof(T) == typeof(Vec3f)) return Unsafe.As<T, Vec3f>(ref left).ApproxEquals(Unsafe.As<T, Vec3f>(ref right));
        if (typeof(T) == typeof(Vec4d)) return Unsafe.As<T, Vec4d>(ref left).ApproxEquals(Unsafe.As<T, Vec4d>(ref right));
        return ValueEquality<T>.Equals(left, right);
    }
    internal static T Reset(T value, T oldBackground, T newBackground, T oldNegative, T newNegative) =>
        ApproximatelyEqual(value, oldBackground) ? newBackground : ApproximatelyEqual(value, oldNegative) ? newNegative : value;
}
