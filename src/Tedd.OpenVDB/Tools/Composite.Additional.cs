// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Additional scalar and leaf compositing entry points from tools/Composite.h.
using System.Numerics;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tools;

public static partial class Composite
{
    /// <summary>Divides A by B and consumes B. Integral division by zero saturates, with zero/zero returning zero.</summary>
    public static void CompDiv<T>(VdbTree<T> a, VdbTree<T> b) where T : unmanaged, INumber<T>, IMinMaxValue<T>
        => a.Combine(b, static (T x, T y) =>
        {
            // INumber's division distinguishes integer arithmetic from floating/decimal arithmetic.
            if (T.One / T.CreateChecked(2) == T.Zero && y == T.Zero)
                return x == T.Zero ? T.Zero : x > T.Zero ? T.MaxValue : -T.MaxValue;
            return x / y;
        });

    public static void CompSum(VdbTree<bool> a, VdbTree<bool> b) => a.Combine(b, static (bool x, bool y) => x || y);
    public static void CompMul(VdbTree<bool> a, VdbTree<bool> b) => a.Combine(b, static (bool x, bool y) => x && y);
    public static void CompMin(VdbTree<bool> a, VdbTree<bool> b) => a.Combine(b, static (bool x, bool y) => x && y);
    public static void CompMax(VdbTree<bool> a, VdbTree<bool> b) => a.Combine(b, static (bool x, bool y) => x || y);
    public static void CompDiv(VdbTree<bool> a, VdbTree<bool> b) => a.Combine(b, static (bool x, bool _) => x);

    public static void CompSum<T>(Grid<T> a, Grid<T> b) where T : unmanaged, INumber<T> { ValidateGrids(a, b); CompSum(a.Tree, b.Tree); }
    public static void CompMul<T>(Grid<T> a, Grid<T> b) where T : unmanaged, INumber<T> { ValidateGrids(a, b); CompMul(a.Tree, b.Tree); }
    public static void CompMin<T>(Grid<T> a, Grid<T> b) where T : unmanaged, INumber<T> { ValidateGrids(a, b); CompMin(a.Tree, b.Tree); }
    public static void CompMax<T>(Grid<T> a, Grid<T> b) where T : unmanaged, INumber<T> { ValidateGrids(a, b); CompMax(a.Tree, b.Tree); }
    public static void CompDiv<T>(Grid<T> a, Grid<T> b) where T : unmanaged, INumber<T>, IMinMaxValue<T> { ValidateGrids(a, b); CompDiv(a.Tree, b.Tree); }
    public static void CompReplace<T>(Grid<T> a, Grid<T> b) where T : unmanaged { ValidateGrids(a, b); CompReplace(a.Tree, b.Tree); }
    public static void CompSum(Grid<bool> a, Grid<bool> b) { ValidateGrids(a, b); CompSum(a.Tree, b.Tree); }
    public static void CompMul(Grid<bool> a, Grid<bool> b) { ValidateGrids(a, b); CompMul(a.Tree, b.Tree); }
    public static void CompMin(Grid<bool> a, Grid<bool> b) { ValidateGrids(a, b); CompMin(a.Tree, b.Tree); }
    public static void CompMax(Grid<bool> a, Grid<bool> b) { ValidateGrids(a, b); CompMax(a.Tree, b.Tree); }
    public static void CompDiv(Grid<bool> a, Grid<bool> b) { ValidateGrids(a, b); CompDiv(a.Tree, b.Tree); }

    /// <summary>Moves source leaves into the destination, composites overlapping active source voxels, and clears the source.</summary>
    /// <remarks>Tiles are ignored. New destination leaves retain all stored source values and active states.
    /// For overlapping leaves, the operation receives destination then source values; null copies the source value.
    /// Inputs must be distinct; custom operations must not mutate either tree.</remarks>
    public static void CompActiveLeafVoxels<T>(VdbTree<T> source, VdbTree<T> destination, Func<T, T, T>? operation = null) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(destination);
        if (ReferenceEquals(source, destination)) throw new ArgumentException("Leaf transfer requires distinct trees.", nameof(destination));
        var leaves = source.StealLeaves();
        source.Clear();
        foreach (var sourceLeaf in leaves)
        {
            var target = destination.ProbeLeaf(sourceLeaf.Origin);
            if (target is null) { destination.AddLeaf(sourceLeaf); continue; }
            foreach (var offset in sourceLeaf.ValueMask.OnIndices)
            {
                var value = sourceLeaf.GetValue(offset);
                target.SetValueOn(offset, operation is null ? value : operation(target.GetValue(offset), value));
            }
        }
    }

    private static void ValidateGrids<T>(Grid<T> a, Grid<T> b) where T : unmanaged
    { ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); }
}
