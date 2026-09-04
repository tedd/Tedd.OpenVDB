// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/SignedFloodFill.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tools;

/// <summary>Propagates narrow-band level-set signs through inactive voxels and tiles.</summary>
/// <remarks>Input surfaces must form closed narrow bands, as required by OpenVDB's scanline algorithm.</remarks>
public static class SignedFloodFill
{
    public static void Apply<T>(VdbTree<T> tree, int minLevel = 0) where T : unmanaged, INumber<T>, ISignedNumber<T>
    {
        ArgumentNullException.ThrowIfNull(tree);
        WithValues(tree, tree.Background, -tree.Background, minLevel);
    }

    /// <summary>Uses absolute exterior width and negative absolute interior width without changing activity.</summary>
    public static void WithValues<T>(VdbTree<T> tree, T outsideWidth, T insideWidth, int minLevel = 0)
        where T : unmanaged, INumber<T>, ISignedNumber<T>
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentOutOfRangeException.ThrowIfNegative(minLevel);
        if (minLevel > 3) return;
        var outside = T.Abs(outsideWidth);
        var inside = -T.Abs(insideWidth);
        tree.InvalidateAccessors();
        foreach (var entry in tree.Entries.Values)
            if (entry.Child is not null) FillNode(entry.Child, outside, inside, minLevel);
        var children = tree.Entries.Where(static entry => entry.Value.Child is not null).OrderBy(static entry => entry.Key).ToArray();
        // Native root filling returns early when there are no children, including tile-only trees.
        if (children.Length == 0) return;
        for (var index = 1; index < children.Length; ++index)
        {
            var a = children[index - 1];
            var b = children[index];
            if (a.Key.X != b.Key.X || a.Key.Y != b.Key.Y || (long)b.Key.Z - a.Key.Z == 4096) continue;
            if (!(Prune.LastValue<T>(a.Value.Child!) < T.Zero) || !(Prune.FirstValue<T>(b.Value.Child!) < T.Zero)) continue;
            for (long z = (long)a.Key.Z + 4096; z < b.Key.Z; z += 4096)
                tree.Entries[new Coord(a.Key.X, a.Key.Y, (int)z)] = new(inside, false);
        }
        tree.SetBackground(outside);
    }

    private static void FillNode<T>(InternalNode<T> node, T outside, T inside, int minLevel)
        where T : unmanaged, INumber<T>, ISignedNumber<T>
    {
        foreach (var child in node.Children.Values)
        {
            if (child is InternalNode<T> internalNode) FillNode(internalNode, outside, inside, minLevel);
            else if (minLevel == 0) FillLeaf((LeafNode<T>)child, outside, inside);
        }
        if (node.Level < minLevel) return;
        if (node.Children.Count == 0)
        {
            node.Values.AsSpan().Fill(node.Values[0] < T.Zero ? inside : outside);
            return;
        }
        var first = node.Children.Keys.Min();
        var xInside = Prune.FirstValue<T>(node.Children[first]) < T.Zero;
        var dimension = 1 << node.Log2Dim;
        for (var x = 0; x < dimension; ++x)
        {
            var x00 = x << (2 * node.Log2Dim);
            if (node.Children.TryGetValue(x00, out var xChild)) xInside = Prune.LastValue<T>(xChild) < T.Zero;
            var yInside = xInside;
            for (var y = 0; y < dimension; ++y)
            {
                var xy0 = x00 + (y << node.Log2Dim);
                if (node.Children.TryGetValue(xy0, out var yChild)) yInside = Prune.LastValue<T>(yChild) < T.Zero;
                var zInside = yInside;
                for (var z = 0; z < dimension; ++z)
                {
                    var offset = xy0 + z;
                    if (node.Children.TryGetValue(offset, out var child)) zInside = Prune.LastValue<T>(child) < T.Zero;
                    else node.Values[offset] = zInside ? inside : outside;
                }
            }
        }
    }

    private static void FillLeaf<T>(LeafNode<T> leaf, T outside, T inside) where T : unmanaged, INumber<T>, ISignedNumber<T>
    {
        var mask = leaf.ValueMask;
        var first = mask.FindFirstOn();
        if (first == LeafNode<T>.Size)
        {
            leaf.Fill(leaf.GetValue(0) < T.Zero ? inside : outside);
            return;
        }
        var xInside = leaf.GetValue(first) < T.Zero;
        for (var x = 0; x < LeafNode<T>.Dim; ++x)
        {
            var x00 = x << (2 * LeafNode<T>.Log2Dim);
            if (mask.IsOn(x00)) xInside = leaf.GetValue(x00) < T.Zero;
            var yInside = xInside;
            for (var y = 0; y < LeafNode<T>.Dim; ++y)
            {
                var xy0 = x00 + (y << LeafNode<T>.Log2Dim);
                if (mask.IsOn(xy0)) yInside = leaf.GetValue(xy0) < T.Zero;
                var zInside = yInside;
                for (var z = 0; z < LeafNode<T>.Dim; ++z)
                {
                    var offset = xy0 + z;
                    if (mask.IsOn(offset)) zInside = leaf.GetValue(offset) < T.Zero;
                    else leaf.SetValueOnly(offset, zInside ? inside : outside);
                }
            }
        }
    }
}
