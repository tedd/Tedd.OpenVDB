// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using System.Runtime.InteropServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

public readonly record struct ValueRange<T>(T Minimum, T Maximum) where T : unmanaged;

public static partial class Count
{
    public static ValueRange<bool> MinMax(VdbTree<bool> tree) => Range<bool, BooleanOrder>(tree);
    public static ValueRange<Coord> MinMax(VdbTree<Coord> tree) => Range<Coord, VectorOrder<Coord, int>>(tree);
    public static ValueRange<Vec2i> MinMax(VdbTree<Vec2i> tree) => Range<Vec2i, VectorOrder<Vec2i, int>>(tree);
    public static ValueRange<Vec2f> MinMax(VdbTree<Vec2f> tree) => Range<Vec2f, VectorOrder<Vec2f, float>>(tree);
    public static ValueRange<Vec2d> MinMax(VdbTree<Vec2d> tree) => Range<Vec2d, VectorOrder<Vec2d, double>>(tree);
    public static ValueRange<Vec3i> MinMax(VdbTree<Vec3i> tree) => Range<Vec3i, VectorOrder<Vec3i, int>>(tree);
    public static ValueRange<Vec3f> MinMax(VdbTree<Vec3f> tree) => Range<Vec3f, VectorOrder<Vec3f, float>>(tree);
    public static ValueRange<Vec3d> MinMax(VdbTree<Vec3d> tree) => Range<Vec3d, VectorOrder<Vec3d, double>>(tree);
    public static ValueRange<Vec4i> MinMax(VdbTree<Vec4i> tree) => Range<Vec4i, VectorOrder<Vec4i, int>>(tree);
    public static ValueRange<Vec4f> MinMax(VdbTree<Vec4f> tree) => Range<Vec4f, VectorOrder<Vec4f, float>>(tree);
    public static ValueRange<Vec4d> MinMax(VdbTree<Vec4d> tree) => Range<Vec4d, VectorOrder<Vec4d, double>>(tree);
    private interface IOrder<T> where T : unmanaged
    {
        static abstract bool Less(T left, T right);
        static abstract bool Greater(T left, T right);
    }
    private readonly struct BooleanOrder : IOrder<bool>
    {
        public static bool Less(bool left, bool right) => !left && right;
        public static bool Greater(bool left, bool right) => left && !right;
    }
    // Native cwise ordering is lexicographic, and unordered components terminate comparison.
    private readonly struct VectorOrder<TVector, TScalar> : IOrder<TVector>
        where TVector : unmanaged where TScalar : unmanaged, INumber<TScalar>
    {
        public static bool Less(TVector left, TVector right) => Compare(left, right, false);
        public static bool Greater(TVector left, TVector right) => Compare(left, right, true);
        private static bool Compare(TVector left, TVector right, bool greater)
        {
            ReadOnlySpan<TScalar> a = MemoryMarshal.Cast<TVector, TScalar>(MemoryMarshal.CreateReadOnlySpan(ref left, 1));
            ReadOnlySpan<TScalar> b = MemoryMarshal.Cast<TVector, TScalar>(MemoryMarshal.CreateReadOnlySpan(ref right, 1));
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return greater ? a[i] > b[i] : a[i] < b[i];
            return false;
        }
    }
    private static ValueRange<T> Range<T, TOrder>(VdbTree<T> tree) where T : unmanaged where TOrder : struct, IOrder<T>
    {
        ArgumentNullException.ThrowIfNull(tree); T min = default, max = default; bool seen = false;
        new DynamicNodeManager<T>(tree).ForEachTopDown((node, index) =>
        {
            foreach (var value in node.EnumerateValues(ValueIterationMode.Active))
            {
                T current = value.Value;
                if (!seen) { min = max = current; seen = true; continue; }
                if (TOrder.Less(current, min)) min = current; if (TOrder.Greater(current, max)) max = current;
            }
            return true;
        });
        return new(min, max);
    }
}
