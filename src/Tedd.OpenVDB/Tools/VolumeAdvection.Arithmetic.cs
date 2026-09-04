// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Native-width arithmetic from tools/VolumeAdvect.h and math/Math.h.
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

internal static class VolumeAdvectionArithmetic<T> where T : unmanaged
{
    internal static int Components => typeof(T) == typeof(Vec2d) ? 2 : typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec3d) || typeof(T) == typeof(Vec3i) ? 3 : typeof(T) == typeof(Vec4d) ? 4 : 1;
    private static bool Single => typeof(T) == typeof(float) || typeof(T) == typeof(Vec3f);
    private static bool Integral => typeof(T) == typeof(int) || typeof(T) == typeof(Vec3i);
    private static bool HalfPrecision => typeof(T) == typeof(Half);
    internal static void Validate()
    {
        if (!Single && !Integral && !HalfPrecision && typeof(T) != typeof(double) && typeof(T) != typeof(Vec2d) && typeof(T) != typeof(Vec3d) && typeof(T) != typeof(Vec4d))
            throw new NotSupportedException("Volume advection supports Float, Double, Half, Int32, Vec2d, Vec3f, Vec3d, Vec3i and Vec4d values.");
    }
    // All accepted vectors have sequential homogeneous component storage. The component loops below
    // stay within the statically selected vector size; unknown T is rejected before reaching them.
    private static double Get(ref T value, int component)
    {
        if (Single) return Unsafe.Add(ref Unsafe.As<T, float>(ref value), component);
        if (Integral) return Unsafe.Add(ref Unsafe.As<T, int>(ref value), component);
        if (HalfPrecision) return (float)Unsafe.As<T, Half>(ref value);
        return Unsafe.Add(ref Unsafe.As<T, double>(ref value), component);
    }
    private static void Set(ref T value, int component, double result)
    {
        if (Single) Unsafe.Add(ref Unsafe.As<T, float>(ref value), component) = (float)result;
        else if (Integral) Unsafe.Add(ref Unsafe.As<T, int>(ref value), component) = unchecked((int)result);
        else if (HalfPrecision) Unsafe.As<T, Half>(ref value) = (Half)(float)result;
        else Unsafe.Add(ref Unsafe.As<T, double>(ref value), component) = result;
    }
    internal static T Correct(T forward, T initial, T backward, bool bfecc, bool singleVelocity)
    {
        T result = default;
        for (int component = 0; component < Components; ++component)
        {
            double a = Get(ref forward, component), b = Get(ref initial, component), c = Get(ref backward, component), value;
            if (bfecc)
            {
                if (singleVelocity && (Single || Integral || HalfPrecision)) value = 0.5f * (3f * (float)b - (float)c);
                else value = 0.5 * (3.0 * b - c);
            }
            else
            {
                double difference = Integral ? unchecked((int)b - (int)c) : Single || HalfPrecision ? (float)b - (float)c : b - c;
                if (singleVelocity && (Single || Integral || HalfPrecision)) value = (float)a + 0.5f * (float)difference;
                else value = a + 0.5 * difference;
            }
            Set(ref result, component, value);
        }
        return result;
    }
    internal static bool Less(T a, T b)
    {
        // Current native cwiseLessThan is lexicographic, despite VolumeAdvect.h's stale ANY-component comment.
        for (int component = 0; component < Components - 1; ++component)
        {
            double x = Get(ref a, component), y = Get(ref b, component); if (x != y) return x < y;
        }
        return Get(ref a, Components - 1) < Get(ref b, Components - 1);
    }
    internal static T Clamp(T value, T minimum, T maximum)
    {
        for (int component = 0; component < Components; ++component)
        {
            double v = Get(ref value, component), min = Get(ref minimum, component), max = Get(ref maximum, component);
            // Native vector extrema can produce reversed per-component bounds. Preserve the source ternary.
            Set(ref value, component, v > min ? v < max ? v : max : min);
        }
        return value;
    }
    internal static bool IsBackground(T value, T background)
    {
        double delta = typeof(T) == typeof(float) ? 1e-5f : typeof(T) == typeof(double) ? 1e-9 : HalfPrecision ? 0.00390625 : 0;
        for (int component = 0; component < Components; ++component)
        {
            double a = Get(ref value, component), b = Get(ref background, component);
            double difference = Integral ? unchecked((int)a - (int)b) : Single || HalfPrecision ? (float)a - (float)b : a - b;
            double absolute = System.Math.Abs(difference);
            if (absolute > delta) return false; // Vec2/3/4 isApproxEqual overloads test every component.
        }
        return true;
    }
}
