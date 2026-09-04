// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

internal static class ValueModifierArithmetic<T> where T : unmanaged
{
    internal static T Apply(T current, T candidate, int operation)
    {
        if (typeof(T) == typeof(bool))
        {
            bool a = Unsafe.As<T, bool>(ref current), b = Unsafe.As<T, bool>(ref candidate);
            bool value = operation is 0 or 3 ? a && b : a || b; return Unsafe.As<bool, T>(ref value);
        }
        if (typeof(T) == typeof(float) || typeof(T) == typeof(Vec2f) || typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec4f)) return Numeric<float>(current, candidate, operation);
        if (typeof(T) == typeof(double) || typeof(T) == typeof(Vec2d) || typeof(T) == typeof(Vec3d) || typeof(T) == typeof(Vec4d)) return Numeric<double>(current, candidate, operation);
        if (typeof(T) == typeof(int) || typeof(T) == typeof(Coord) || typeof(T) == typeof(Vec2i) || typeof(T) == typeof(Vec3i) || typeof(T) == typeof(Vec4i)) return Numeric<int>(current, candidate, operation);
        if (typeof(T) == typeof(long)) return Numeric<long>(current, candidate, operation);
        if (typeof(T) == typeof(Half)) return Numeric<Half>(current, candidate, operation);
        if (typeof(T) == typeof(sbyte)) return Numeric<sbyte>(current, candidate, operation);
        if (typeof(T) == typeof(byte)) return Numeric<byte>(current, candidate, operation);
        if (typeof(T) == typeof(short)) return Numeric<short>(current, candidate, operation);
        if (typeof(T) == typeof(ushort)) return Numeric<ushort>(current, candidate, operation);
        if (typeof(T) == typeof(uint)) return Numeric<uint>(current, candidate, operation);
        if (typeof(T) == typeof(ulong)) return Numeric<ulong>(current, candidate, operation);
        if (typeof(T) == typeof(nint)) return Numeric<nint>(current, candidate, operation);
        if (typeof(T) == typeof(nuint)) return Numeric<nuint>(current, candidate, operation);
        if (typeof(T) == typeof(Int128)) return Numeric<Int128>(current, candidate, operation);
        if (typeof(T) == typeof(UInt128)) return Numeric<UInt128>(current, candidate, operation);
        if (typeof(T) == typeof(decimal)) return Numeric<decimal>(current, candidate, operation);
        throw new NotSupportedException($"Voxel arithmetic is not defined for {typeof(T)}.");
    }
    private static T Numeric<TScalar>(T current, T candidate, int operation) where TScalar : unmanaged, INumber<TScalar>
    {
        ReadOnlySpan<TScalar> a = MemoryMarshal.Cast<T, TScalar>(MemoryMarshal.CreateReadOnlySpan(ref current, 1));
        ReadOnlySpan<TScalar> b = MemoryMarshal.Cast<T, TScalar>(MemoryMarshal.CreateReadOnlySpan(ref candidate, 1));
        if (operation < 2)
        {
            // Native cwiseLess/Greater uses lexicographic order and stops at the first unordered component.
            for (int i = 0; i < a.Length; i++) if (b[i] != a[i]) return (operation == 0 ? b[i] < a[i] : b[i] > a[i]) ? candidate : current;
            return current;
        }
        T result = default; Span<TScalar> values = MemoryMarshal.Cast<T, TScalar>(MemoryMarshal.CreateSpan(ref result, 1));
        for (int i = 0; i < values.Length; i++) values[i] = operation == 2 ? a[i] + b[i] : a[i] * b[i];
        return result;
    }
}