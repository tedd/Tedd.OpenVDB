// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Util;

/// <summary>Native lexicographic vector ordering for leaf median operations.</summary>
internal sealed class ValueComparer<T> : IComparer<T> where T : unmanaged
{
    internal static readonly IComparer<T> Default =
        typeof(T) == typeof(Vec2d) || typeof(T) == typeof(Vec3d) || typeof(T) == typeof(Vec3f) ||
        typeof(T) == typeof(Vec3i) || typeof(T) == typeof(Vec4d) ? new ValueComparer<T>() : Comparer<T>.Default;

    public int Compare(T left, T right)
    {
        if (typeof(T) == typeof(Vec2d))
        {
            var a = Unsafe.As<T, Vec2d>(ref left); var b = Unsafe.As<T, Vec2d>(ref right);
            if (a.X != b.X) return CompareComponent(a.X, b.X);
            return CompareComponent(a.Y, b.Y);
        }
        if (typeof(T) == typeof(Vec3d))
        {
            var a = Unsafe.As<T, Vec3d>(ref left); var b = Unsafe.As<T, Vec3d>(ref right);
            if (a.X != b.X) return CompareComponent(a.X, b.X);
            if (a.Y != b.Y) return CompareComponent(a.Y, b.Y);
            return CompareComponent(a.Z, b.Z);
        }
        if (typeof(T) == typeof(Vec3f))
        {
            var a = Unsafe.As<T, Vec3f>(ref left); var b = Unsafe.As<T, Vec3f>(ref right);
            if (a.X != b.X) return CompareComponent(a.X, b.X);
            if (a.Y != b.Y) return CompareComponent(a.Y, b.Y);
            return CompareComponent(a.Z, b.Z);
        }
        if (typeof(T) == typeof(Vec3i))
        {
            var a = Unsafe.As<T, Vec3i>(ref left); var b = Unsafe.As<T, Vec3i>(ref right);
            if (a.X != b.X) return a.X.CompareTo(b.X);
            if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
            return a.Z.CompareTo(b.Z);
        }
        if (typeof(T) == typeof(Vec4d))
        {
            var a = Unsafe.As<T, Vec4d>(ref left); var b = Unsafe.As<T, Vec4d>(ref right);
            if (a.X != b.X) return CompareComponent(a.X, b.X);
            if (a.Y != b.Y) return CompareComponent(a.Y, b.Y);
            if (a.Z != b.Z) return CompareComponent(a.Z, b.Z);
            return CompareComponent(a.W, b.W);
        }
        return Comparer<T>.Default.Compare(left, right);
    }
    // Native cwiseLessThan stops at the first unequal component, including unordered NaN.
    private static int CompareComponent(double left, double right) => left < right ? -1 : left > right ? 1 : 0;
}
