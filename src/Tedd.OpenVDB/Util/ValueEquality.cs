// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

namespace Tedd.OpenVDB.Util;

/// <summary>Value equality for exact topology compression, retaining NaN payloads as voxels.</summary>
internal static class ValueEquality<T> where T : unmanaged
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Equals(T left, T right)
    {
        if (typeof(T) == typeof(float))
            return Unsafe.As<T, float>(ref left) == Unsafe.As<T, float>(ref right);
        if (typeof(T) == typeof(double))
            return Unsafe.As<T, double>(ref left) == Unsafe.As<T, double>(ref right);
        if (typeof(T) == typeof(Half))
            return Unsafe.As<T, Half>(ref left) == Unsafe.As<T, Half>(ref right);
        if (typeof(T) == typeof(Tedd.OpenVDB.Math.Vec3d))
        {
            var a = Unsafe.As<T, Tedd.OpenVDB.Math.Vec3d>(ref left);
            var b = Unsafe.As<T, Tedd.OpenVDB.Math.Vec3d>(ref right);
            return a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        }
        return EqualityComparer<T>.Default.Equals(left, right);
    }
}
