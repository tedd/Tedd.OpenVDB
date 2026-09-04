// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of Composite.h BuildPrimarySegment / BuildSecondarySegment.
using System.Numerics;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tools;

public static partial class Composite
{
    /// <summary>Builds a leaf-based level-set union without modifying either input.</summary>
    /// <remarks>Matches serial upstream execution: tiles guide selection, leaves are copied, and internal signs are flood-filled.
    /// With unequal backgrounds, native parallel segment merges can normalize inactive values differently.</remarks>
    public static VdbTree<T> CsgUnionCopy<T>(VdbTree<T> a, VdbTree<T> b)
        where T : unmanaged, IFloatingPointIeee754<T> => CsgCopy(a, b, CsgOperation.Union);

    /// <summary>Builds a leaf-based level-set intersection without modifying either input.</summary>
    public static VdbTree<T> CsgIntersectionCopy<T>(VdbTree<T> a, VdbTree<T> b)
        where T : unmanaged, IFloatingPointIeee754<T> => CsgCopy(a, b, CsgOperation.Intersection);

    /// <summary>Builds a leaf-based level-set difference without modifying either input.</summary>
    public static VdbTree<T> CsgDifferenceCopy<T>(VdbTree<T> a, VdbTree<T> b)
        where T : unmanaged, IFloatingPointIeee754<T> => CsgCopy(a, b, CsgOperation.Difference);

    public static Grid<T> CsgUnionCopy<T>(Grid<T> a, Grid<T> b)
        where T : unmanaged, IFloatingPointIeee754<T> => CsgGridCopy(a, b, CsgOperation.Union);

    public static Grid<T> CsgIntersectionCopy<T>(Grid<T> a, Grid<T> b)
        where T : unmanaged, IFloatingPointIeee754<T> => CsgGridCopy(a, b, CsgOperation.Intersection);

    public static Grid<T> CsgDifferenceCopy<T>(Grid<T> a, Grid<T> b)
        where T : unmanaged, IFloatingPointIeee754<T> => CsgGridCopy(a, b, CsgOperation.Difference);

    private static Grid<T> CsgGridCopy<T>(Grid<T> a, Grid<T> b, CsgOperation operation)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b);
        var output = new Grid<T>(CsgCopy(a.Tree, b.Tree, operation)) { Transform = a.Transform.Copy() };
        foreach (var (key, value) in a.Metadata) output.SetMetadata(key, value);
        return output;
    }

    private static VdbTree<T> CsgCopy<T>(VdbTree<T> a, VdbTree<T> b, CsgOperation operation)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b);
        var output = new VdbTree<T>(a.Background);
        // Checking every leaf directly is equivalent to upstream's internal-node batching:
        // when no matching lower node exists, all its leaves see the same uniform RHS tile.
        foreach (var left in a.EnumerateLeaves())
        {
            var right = b.ProbeLeaf(left.Origin);
            if (right is not null)
            {
                var result = left.DeepCopy();
                MergeCsgLeaves(result, right, a.Background, operation, false);
                output.AddLeaf(result);
            }
            else
            {
                var inside = b.GetValue(left.Origin) < T.Zero;
                if (operation == CsgOperation.Intersection ? inside : !inside) output.AddLeaf(left.DeepCopy());
            }
        }
        foreach (var right in b.EnumerateLeaves())
        {
            if (a.ProbeLeaf(right.Origin) is not null) continue;
            var inside = a.GetValue(right.Origin) < T.Zero;
            if (operation == CsgOperation.Union ? !inside : inside)
            {
                var result = right.DeepCopy();
                if (operation == CsgOperation.Difference) NegateCsgNode<T>(result);
                output.AddLeaf(result);
            }
        }
        SignedFloodFill.Apply(output, minLevel: 1);
        return output;
    }
}
