// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

internal static class GeneralSamplingArithmetic<T> where T : unmanaged
{
    internal static bool IsVector3 => typeof(T) == typeof(Vec3d) || typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec3i);
    internal static bool Supported => typeof(T) == typeof(bool) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(byte) || typeof(T) == typeof(short) || typeof(T) == typeof(ushort) || typeof(T) == typeof(int) || typeof(T) == typeof(uint) || typeof(T) == typeof(long) || typeof(T) == typeof(ulong) || typeof(T) == typeof(nint) || typeof(T) == typeof(nuint) || typeof(T) == typeof(Int128) || typeof(T) == typeof(UInt128) || typeof(T) == typeof(Half) || typeof(T) == typeof(float) || typeof(T) == typeof(double) || typeof(T) == typeof(decimal) || typeof(T) == typeof(Vec2d) || typeof(T) == typeof(Vec3d) || typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec3i) || typeof(T) == typeof(Vec4d);
    internal static T Linear(T a, T b, double w)
    {
        if (typeof(T) == typeof(sbyte)) { var value = SamplingArithmetic<sbyte>.Linear(Unsafe.As<T, sbyte>(ref a), Unsafe.As<T, sbyte>(ref b), w); return Unsafe.As<sbyte, T>(ref value); }
        if (typeof(T) == typeof(byte)) { var value = SamplingArithmetic<byte>.Linear(Unsafe.As<T, byte>(ref a), Unsafe.As<T, byte>(ref b), w); return Unsafe.As<byte, T>(ref value); }
        if (typeof(T) == typeof(short)) { var value = SamplingArithmetic<short>.Linear(Unsafe.As<T, short>(ref a), Unsafe.As<T, short>(ref b), w); return Unsafe.As<short, T>(ref value); }
        if (typeof(T) == typeof(ushort)) { var value = SamplingArithmetic<ushort>.Linear(Unsafe.As<T, ushort>(ref a), Unsafe.As<T, ushort>(ref b), w); return Unsafe.As<ushort, T>(ref value); }
        if (typeof(T) == typeof(int)) { var value = SamplingArithmetic<int>.Linear(Unsafe.As<T, int>(ref a), Unsafe.As<T, int>(ref b), w); return Unsafe.As<int, T>(ref value); }
        if (typeof(T) == typeof(uint)) { var value = SamplingArithmetic<uint>.Linear(Unsafe.As<T, uint>(ref a), Unsafe.As<T, uint>(ref b), w); return Unsafe.As<uint, T>(ref value); }
        if (typeof(T) == typeof(long)) { var value = SamplingArithmetic<long>.Linear(Unsafe.As<T, long>(ref a), Unsafe.As<T, long>(ref b), w); return Unsafe.As<long, T>(ref value); }
        if (typeof(T) == typeof(ulong)) { var value = SamplingArithmetic<ulong>.Linear(Unsafe.As<T, ulong>(ref a), Unsafe.As<T, ulong>(ref b), w); return Unsafe.As<ulong, T>(ref value); }
        if (typeof(T) == typeof(nint)) { var value = SamplingArithmetic<nint>.Linear(Unsafe.As<T, nint>(ref a), Unsafe.As<T, nint>(ref b), w); return Unsafe.As<nint, T>(ref value); }
        if (typeof(T) == typeof(nuint)) { var value = SamplingArithmetic<nuint>.Linear(Unsafe.As<T, nuint>(ref a), Unsafe.As<T, nuint>(ref b), w); return Unsafe.As<nuint, T>(ref value); }
        if (typeof(T) == typeof(Int128)) { var value = SamplingArithmetic<Int128>.Linear(Unsafe.As<T, Int128>(ref a), Unsafe.As<T, Int128>(ref b), w); return Unsafe.As<Int128, T>(ref value); }
        if (typeof(T) == typeof(UInt128)) { var value = SamplingArithmetic<UInt128>.Linear(Unsafe.As<T, UInt128>(ref a), Unsafe.As<T, UInt128>(ref b), w); return Unsafe.As<UInt128, T>(ref value); }
        if (typeof(T) == typeof(Half)) { var value = SamplingArithmetic<Half>.Linear(Unsafe.As<T, Half>(ref a), Unsafe.As<T, Half>(ref b), w); return Unsafe.As<Half, T>(ref value); }
        if (typeof(T) == typeof(float)) { var value = SamplingArithmetic<float>.Linear(Unsafe.As<T, float>(ref a), Unsafe.As<T, float>(ref b), w); return Unsafe.As<float, T>(ref value); }
        if (typeof(T) == typeof(double)) { var value = SamplingArithmetic<double>.Linear(Unsafe.As<T, double>(ref a), Unsafe.As<T, double>(ref b), w); return Unsafe.As<double, T>(ref value); }
        if (typeof(T) == typeof(decimal)) { var value = SamplingArithmetic<decimal>.Linear(Unsafe.As<T, decimal>(ref a), Unsafe.As<T, decimal>(ref b), w); return Unsafe.As<decimal, T>(ref value); }
        if (typeof(T) == typeof(Vec2d))
        {
            var x = Unsafe.As<T, Vec2d>(ref a); var y = Unsafe.As<T, Vec2d>(ref b);
            var value = new Vec2d(SamplingArithmetic<double>.Linear(x.X, y.X, w), SamplingArithmetic<double>.Linear(x.Y, y.Y, w)); return Unsafe.As<Vec2d, T>(ref value);
        }
        if (typeof(T) == typeof(Vec3d))
        {
            var x = Unsafe.As<T, Vec3d>(ref a); var y = Unsafe.As<T, Vec3d>(ref b);
            var value = new Vec3d(SamplingArithmetic<double>.Linear(x.X, y.X, w), SamplingArithmetic<double>.Linear(x.Y, y.Y, w), SamplingArithmetic<double>.Linear(x.Z, y.Z, w)); return Unsafe.As<Vec3d, T>(ref value);
        }
        if (typeof(T) == typeof(Vec3f))
        {
            var x = Unsafe.As<T, Vec3f>(ref a); var y = Unsafe.As<T, Vec3f>(ref b);
            var value = new Vec3f(SamplingArithmetic<float>.Linear(x.X, y.X, w), SamplingArithmetic<float>.Linear(x.Y, y.Y, w), SamplingArithmetic<float>.Linear(x.Z, y.Z, w)); return Unsafe.As<Vec3f, T>(ref value);
        }
        if (typeof(T) == typeof(Vec3i))
        {
            var x = Unsafe.As<T, Vec3i>(ref a); var y = Unsafe.As<T, Vec3i>(ref b);
            var value = new Vec3i(SamplingArithmetic<int>.Linear(x.X, y.X, w), SamplingArithmetic<int>.Linear(x.Y, y.Y, w), SamplingArithmetic<int>.Linear(x.Z, y.Z, w)); return Unsafe.As<Vec3i, T>(ref value);
        }
        if (typeof(T) == typeof(Vec4d))
        {
            var x = Unsafe.As<T, Vec4d>(ref a); var y = Unsafe.As<T, Vec4d>(ref b);
            var value = new Vec4d(SamplingArithmetic<double>.Linear(x.X, y.X, w), SamplingArithmetic<double>.Linear(x.Y, y.Y, w), SamplingArithmetic<double>.Linear(x.Z, y.Z, w), SamplingArithmetic<double>.Linear(x.W, y.W, w)); return Unsafe.As<Vec4d, T>(ref value);
        }
        if (typeof(T) == typeof(bool)) { bool x = Unsafe.As<T, bool>(ref a), y = Unsafe.As<T, bool>(ref b); bool value = x || ((y ? 1 : 0) - (x ? 1 : 0)) * w != 0; return Unsafe.As<bool, T>(ref value); }
        throw new NotSupportedException($"Interpolation is not defined for {typeof(T)}.");
    }
    internal static T Quadratic(T a, T b, T c, double w)
    {
        if (typeof(T) == typeof(sbyte)) { var value = SamplingArithmetic<sbyte>.Quadratic(Unsafe.As<T, sbyte>(ref a), Unsafe.As<T, sbyte>(ref b), Unsafe.As<T, sbyte>(ref c), w); return Unsafe.As<sbyte, T>(ref value); }
        if (typeof(T) == typeof(byte)) { var value = SamplingArithmetic<byte>.Quadratic(Unsafe.As<T, byte>(ref a), Unsafe.As<T, byte>(ref b), Unsafe.As<T, byte>(ref c), w); return Unsafe.As<byte, T>(ref value); }
        if (typeof(T) == typeof(short)) { var value = SamplingArithmetic<short>.Quadratic(Unsafe.As<T, short>(ref a), Unsafe.As<T, short>(ref b), Unsafe.As<T, short>(ref c), w); return Unsafe.As<short, T>(ref value); }
        if (typeof(T) == typeof(ushort)) { var value = SamplingArithmetic<ushort>.Quadratic(Unsafe.As<T, ushort>(ref a), Unsafe.As<T, ushort>(ref b), Unsafe.As<T, ushort>(ref c), w); return Unsafe.As<ushort, T>(ref value); }
        if (typeof(T) == typeof(int)) { var value = SamplingArithmetic<int>.Quadratic(Unsafe.As<T, int>(ref a), Unsafe.As<T, int>(ref b), Unsafe.As<T, int>(ref c), w); return Unsafe.As<int, T>(ref value); }
        if (typeof(T) == typeof(uint)) { var value = SamplingArithmetic<uint>.Quadratic(Unsafe.As<T, uint>(ref a), Unsafe.As<T, uint>(ref b), Unsafe.As<T, uint>(ref c), w); return Unsafe.As<uint, T>(ref value); }
        if (typeof(T) == typeof(long)) { var value = SamplingArithmetic<long>.Quadratic(Unsafe.As<T, long>(ref a), Unsafe.As<T, long>(ref b), Unsafe.As<T, long>(ref c), w); return Unsafe.As<long, T>(ref value); }
        if (typeof(T) == typeof(ulong)) { var value = SamplingArithmetic<ulong>.Quadratic(Unsafe.As<T, ulong>(ref a), Unsafe.As<T, ulong>(ref b), Unsafe.As<T, ulong>(ref c), w); return Unsafe.As<ulong, T>(ref value); }
        if (typeof(T) == typeof(nint)) { var value = SamplingArithmetic<nint>.Quadratic(Unsafe.As<T, nint>(ref a), Unsafe.As<T, nint>(ref b), Unsafe.As<T, nint>(ref c), w); return Unsafe.As<nint, T>(ref value); }
        if (typeof(T) == typeof(nuint)) { var value = SamplingArithmetic<nuint>.Quadratic(Unsafe.As<T, nuint>(ref a), Unsafe.As<T, nuint>(ref b), Unsafe.As<T, nuint>(ref c), w); return Unsafe.As<nuint, T>(ref value); }
        if (typeof(T) == typeof(Int128)) { var value = SamplingArithmetic<Int128>.Quadratic(Unsafe.As<T, Int128>(ref a), Unsafe.As<T, Int128>(ref b), Unsafe.As<T, Int128>(ref c), w); return Unsafe.As<Int128, T>(ref value); }
        if (typeof(T) == typeof(UInt128)) { var value = SamplingArithmetic<UInt128>.Quadratic(Unsafe.As<T, UInt128>(ref a), Unsafe.As<T, UInt128>(ref b), Unsafe.As<T, UInt128>(ref c), w); return Unsafe.As<UInt128, T>(ref value); }
        if (typeof(T) == typeof(Half)) { var value = SamplingArithmetic<Half>.Quadratic(Unsafe.As<T, Half>(ref a), Unsafe.As<T, Half>(ref b), Unsafe.As<T, Half>(ref c), w); return Unsafe.As<Half, T>(ref value); }
        if (typeof(T) == typeof(float)) { var value = SamplingArithmetic<float>.Quadratic(Unsafe.As<T, float>(ref a), Unsafe.As<T, float>(ref b), Unsafe.As<T, float>(ref c), w); return Unsafe.As<float, T>(ref value); }
        if (typeof(T) == typeof(double)) { var value = SamplingArithmetic<double>.Quadratic(Unsafe.As<T, double>(ref a), Unsafe.As<T, double>(ref b), Unsafe.As<T, double>(ref c), w); return Unsafe.As<double, T>(ref value); }
        if (typeof(T) == typeof(decimal)) { var value = SamplingArithmetic<decimal>.Quadratic(Unsafe.As<T, decimal>(ref a), Unsafe.As<T, decimal>(ref b), Unsafe.As<T, decimal>(ref c), w); return Unsafe.As<decimal, T>(ref value); }
        if (typeof(T) == typeof(Vec2d))
        {
            var x = Unsafe.As<T, Vec2d>(ref a); var y = Unsafe.As<T, Vec2d>(ref b); var z = Unsafe.As<T, Vec2d>(ref c);
            var value = new Vec2d(SamplingArithmetic<double>.Quadratic(x.X, y.X, z.X, w), SamplingArithmetic<double>.Quadratic(x.Y, y.Y, z.Y, w)); return Unsafe.As<Vec2d, T>(ref value);
        }
        if (typeof(T) == typeof(Vec3d))
        {
            var x = Unsafe.As<T, Vec3d>(ref a); var y = Unsafe.As<T, Vec3d>(ref b); var z = Unsafe.As<T, Vec3d>(ref c);
            var value = new Vec3d(SamplingArithmetic<double>.Quadratic(x.X, y.X, z.X, w), SamplingArithmetic<double>.Quadratic(x.Y, y.Y, z.Y, w), SamplingArithmetic<double>.Quadratic(x.Z, y.Z, z.Z, w)); return Unsafe.As<Vec3d, T>(ref value);
        }
        if (typeof(T) == typeof(Vec3f))
        {
            var x = Unsafe.As<T, Vec3f>(ref a); var y = Unsafe.As<T, Vec3f>(ref b); var z = Unsafe.As<T, Vec3f>(ref c);
            var value = new Vec3f(SamplingArithmetic<float>.Quadratic(x.X, y.X, z.X, w), SamplingArithmetic<float>.Quadratic(x.Y, y.Y, z.Y, w), SamplingArithmetic<float>.Quadratic(x.Z, y.Z, z.Z, w)); return Unsafe.As<Vec3f, T>(ref value);
        }
        if (typeof(T) == typeof(Vec3i))
        {
            var x = Unsafe.As<T, Vec3i>(ref a); var y = Unsafe.As<T, Vec3i>(ref b); var z = Unsafe.As<T, Vec3i>(ref c);
            var value = new Vec3i(SamplingArithmetic<int>.Quadratic(x.X, y.X, z.X, w), SamplingArithmetic<int>.Quadratic(x.Y, y.Y, z.Y, w), SamplingArithmetic<int>.Quadratic(x.Z, y.Z, z.Z, w)); return Unsafe.As<Vec3i, T>(ref value);
        }
        if (typeof(T) == typeof(Vec4d))
        {
            var x = Unsafe.As<T, Vec4d>(ref a); var y = Unsafe.As<T, Vec4d>(ref b); var z = Unsafe.As<T, Vec4d>(ref c);
            var value = new Vec4d(SamplingArithmetic<double>.Quadratic(x.X, y.X, z.X, w), SamplingArithmetic<double>.Quadratic(x.Y, y.Y, z.Y, w), SamplingArithmetic<double>.Quadratic(x.Z, y.Z, z.Z, w), SamplingArithmetic<double>.Quadratic(x.W, y.W, z.W, w)); return Unsafe.As<Vec4d, T>(ref value);
        }
        if (typeof(T) == typeof(bool))
        {
            int x = Unsafe.As<T, bool>(ref a) ? 1 : 0, y = Unsafe.As<T, bool>(ref b) ? 1 : 0, z = Unsafe.As<T, bool>(ref c) ? 1 : 0;
            bool qa = 0.5 * (x + z) - y != 0, qb = 0.5 * (z - x) != 0;
            bool value = w * (w * (qa ? 1 : 0) + (qb ? 1 : 0)) + y != 0;
            return Unsafe.As<bool, T>(ref value);
        }
        throw new NotSupportedException($"Interpolation is not defined for {typeof(T)}.");
    }
    internal static T JoinComponents(T a, T b, T c)
    {
        if (typeof(T) == typeof(Vec3d)) { var value = new Vec3d(Unsafe.As<T, Vec3d>(ref a).X, Unsafe.As<T, Vec3d>(ref b).Y, Unsafe.As<T, Vec3d>(ref c).Z); return Unsafe.As<Vec3d, T>(ref value); }
        if (typeof(T) == typeof(Vec3f)) { var value = new Vec3f(Unsafe.As<T, Vec3f>(ref a).X, Unsafe.As<T, Vec3f>(ref b).Y, Unsafe.As<T, Vec3f>(ref c).Z); return Unsafe.As<Vec3f, T>(ref value); }
        if (typeof(T) == typeof(Vec3i)) { var value = new Vec3i(Unsafe.As<T, Vec3i>(ref a).X, Unsafe.As<T, Vec3i>(ref b).Y, Unsafe.As<T, Vec3i>(ref c).Z); return Unsafe.As<Vec3i, T>(ref value); }
        throw new NotSupportedException("Staggered sampling requires Vec3d, Vec3f or Vec3i values.");
    }
}
