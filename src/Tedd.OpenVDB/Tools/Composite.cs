// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Scalar numeric operations from tools/Composite.h; inputs use matching index spaces.
using System.Numerics;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

public static partial class Composite
{
    public static void CompSum<T>(VdbTree<T> a, VdbTree<T> b) where T : unmanaged, INumber<T>
        => a.Combine(b, static (T x, T y) => x + y);
    public static void CompMul<T>(VdbTree<T> a, VdbTree<T> b) where T : unmanaged, INumber<T>
        => a.Combine(b, static (T x, T y) => x * y);
    // std::min/max preserve the left operand on unordered floating-point comparisons.
    public static void CompMin<T>(VdbTree<T> a, VdbTree<T> b) where T : unmanaged, INumber<T>
        => a.Combine(b, static (T x, T y) => y < x ? y : x);
    public static void CompMax<T>(VdbTree<T> a, VdbTree<T> b) where T : unmanaged, INumber<T>
        => a.Combine(b, static (T x, T y) => x < y ? y : x);
    /// <summary>Copies active values from B into A while retaining B.</summary>
    public static void CompReplace<T>(VdbTree<T> a, VdbTree<T> b) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (ReferenceEquals(a, b)) return;
        a.TopologyUnion(b);
        foreach (var value in b.EnumerateValues()) a.Fill(value.Bounds, value.Value, true);
    }
}
