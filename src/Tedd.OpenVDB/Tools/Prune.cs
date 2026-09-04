// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/Prune.h.
using System.Numerics;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;

namespace Tedd.OpenVDB.Tools;

/// <summary>Bottom-up sparse tree pruning corresponding to OpenVDB tools/Prune.h.</summary>
/// <remarks>Root background tiles use native approximate equality; NaN root tile values consequently compare as background.</remarks>
public static class Prune
{
    /// <summary>Replaces exactly uniform nodes with tiles and removes background root tiles.</summary>
    public static void Apply<T>(VdbTree<T> tree) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree);
        tree.Prune();
        EraseBackgroundTiles(tree);
    }

    /// <summary>Replaces uniform-state nodes whose value range is within tolerance by their lower median.</summary>
    public static void Apply<T>(VdbTree<T> tree, T tolerance) where T : unmanaged, INumber<T> =>
        PruneNumeric(tree, tolerance, includeLeaves: true);

    /// <summary>Prunes internal nodes only. Leaf nodes are never replaced.</summary>
    public static void PruneTiles<T>(VdbTree<T> tree) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree);
        tree.InvalidateAccessors();
        foreach (var key in tree.Entries.Keys.ToArray())
        {
            var entry = tree.Entries[key];
            if (entry.Child is not null)
            {
                PruneExactInternal(entry.Child);
                if (entry.Child.IsConstant(out var value, out var active)) tree.Entries[key] = new(value, active);
            }
        }
        EraseBackgroundTiles(tree);
    }

    public static void PruneTiles<T>(VdbTree<T> tree, T tolerance) where T : unmanaged, INumber<T> =>
        PruneNumeric(tree, tolerance, includeLeaves: false);

    /// <summary>Replaces fully inactive child nodes with the tree background.</summary>
    public static void PruneInactive<T>(VdbTree<T> tree) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree);
        PruneInactiveWithValue(tree, tree.Background);
    }

    /// <summary>Replaces fully inactive child nodes with the specified value; existing tiles retain their values.</summary>
    public static void PruneInactiveWithValue<T>(VdbTree<T> tree, T value) where T : unmanaged =>
        PruneInactiveCore(tree, _ => value);

    /// <summary>Prunes fully inactive level-set nodes using the first value's sign and symmetric background widths.</summary>
    public static void PruneLevelSet<T>(VdbTree<T> tree) where T : unmanaged, INumber<T>, ISignedNumber<T>
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (tree.Background < T.Zero) throw new ArgumentException("The background cannot be negative.", nameof(tree));
        PruneInactiveCore(tree, first => first < T.Zero ? -tree.Background : tree.Background);
    }

    /// <summary>Prunes fully inactive level-set nodes to the supplied exterior/interior widths.</summary>
    public static void PruneLevelSet<T>(VdbTree<T> tree, T outsideWidth, T insideWidth)
        where T : unmanaged, INumber<T>, ISignedNumber<T>
    {
        if (outsideWidth < T.Zero) throw new ArgumentOutOfRangeException(nameof(outsideWidth));
        if (!(insideWidth < T.Zero)) throw new ArgumentOutOfRangeException(nameof(insideWidth));
        PruneInactiveCore(tree, first => first < T.Zero ? insideWidth : outsideWidth);
    }

    private static void PruneExactInternal<T>(InternalNode<T> node) where T : unmanaged
    {
        foreach (var (offset, child) in node.Children.ToArray())
        {
            if (child is not InternalNode<T> internalNode) continue;
            PruneExactInternal(internalNode);
            if (internalNode.IsConstant(out var value, out var active)) node.SetTile(offset, value, active);
        }
    }

    private static void PruneNumeric<T>(VdbTree<T> tree, T tolerance, bool includeLeaves) where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (!T.IsFinite(tolerance) || tolerance < T.Zero) throw new ArgumentOutOfRangeException(nameof(tolerance));
        tree.InvalidateAccessors();
        foreach (var key in tree.Entries.Keys.ToArray())
        {
            var entry = tree.Entries[key];
            if (entry.Child is null) continue;
            PruneNumericNode(entry.Child, tolerance, includeLeaves);
            if (TryUniform(entry.Child, tolerance, out var value, out var active)) tree.Entries[key] = new(value, active);
        }
        EraseBackgroundTiles(tree);
    }

    private static void PruneNumericNode<T>(InternalNode<T> node, T tolerance, bool includeLeaves) where T : unmanaged, INumber<T>
    {
        foreach (var (offset, child) in node.Children.ToArray())
        {
            if (child is InternalNode<T> internalNode) PruneNumericNode(internalNode, tolerance, includeLeaves);
            else if (!includeLeaves) continue;
            if (TryUniform<T>(child, tolerance, out var value, out var active)) node.SetTile(offset, value, active);
        }
    }

    private static bool TryUniform<T>(object child, T tolerance, out T value, out bool active) where T : unmanaged, INumber<T>
    {
        ReadOnlySpan<T> values;
        if (child is LeafNode<T> leaf)
        {
            value = leaf.GetValue(0);
            active = leaf.IsDense;
            if (!active && !leaf.IsEmpty) return false;
            values = leaf.Values;
        }
        else
        {
            var node = (InternalNode<T>)child;
            value = node.Values[0];
            active = false;
            if (node.Children.Count != 0 || !node.Active.IsConstant(out active)) return false;
            values = node.Values;
        }
        var minimum = values[0];
        var maximum = minimum;
        if (T.IsNaN(minimum)) return false;
        foreach (var current in values)
        {
            if (T.IsNaN(current)) return false;
            if (current < minimum) minimum = current;
            if (current > maximum) maximum = current;
            if (ValueEquality<T>.Equals(minimum, maximum)) continue;
            if (typeof(T) == typeof(Half))
            {
                if (float.CreateChecked(maximum) - float.CreateChecked(minimum) > float.CreateChecked(tolerance)) return false;
                continue;
            }
            try { if (checked(maximum - minimum) > tolerance) return false; }
            catch (OverflowException) { return false; }
        }
        if (ValueEquality<T>.Equals(minimum, maximum)) { value = minimum; return true; }
        // Upstream uses nth_element at (size-1)/2, i.e. the lower median for even-sized nodes.
        value = LowerMedian(values.ToArray().AsSpan());
        return true;
    }

    private static T LowerMedian<T>(Span<T> values) where T : INumber<T>
    {
        var target = (values.Length - 1) >> 1;
        var left = 0;
        var right = values.Length - 1;
        while (left < right)
        {
            var pivot = values[left + ((right - left) >> 1)];
            var i = left;
            var j = right;
            while (i <= j)
            {
                while (values[i] < pivot) ++i;
                while (values[j] > pivot) --j;
                if (i > j) break;
                (values[i], values[j]) = (values[j], values[i]);
                ++i; --j;
            }
            if (target <= j) right = j;
            else if (target >= i) left = i;
            else break;
        }
        return values[target];
    }

    private static void PruneInactiveCore<T>(VdbTree<T> tree, Func<T, T> replacement) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree);
        tree.InvalidateAccessors();
        foreach (var key in tree.Entries.Keys.ToArray())
        {
            var entry = tree.Entries[key];
            if (entry.Child is null) continue;
            PruneInactiveNode(entry.Child, replacement);
            if (IsInactive<T>(entry.Child)) tree.Entries[key] = new(replacement(FirstValue<T>(entry.Child)), false);
        }
        EraseBackgroundTiles(tree);
    }

    private static void PruneInactiveNode<T>(InternalNode<T> node, Func<T, T> replacement) where T : unmanaged
    {
        foreach (var (offset, child) in node.Children.ToArray())
        {
            if (child is InternalNode<T> internalNode) PruneInactiveNode(internalNode, replacement);
            if (IsInactive<T>(child)) node.SetTile(offset, replacement(FirstValue<T>(child)), false);
        }
    }

    private static bool IsInactive<T>(object child) where T : unmanaged => child is LeafNode<T> leaf
        ? leaf.IsEmpty : ((InternalNode<T>)child).Children.Count == 0 && ((InternalNode<T>)child).Active.IsOff();

    internal static T FirstValue<T>(object child) where T : unmanaged
    {
        if (child is LeafNode<T> leaf) return leaf.GetValue(0);
        var node = (InternalNode<T>)child;
        return node.Children.TryGetValue(0, out var first) ? FirstValue<T>(first) : node.Values[0];
    }

    internal static T LastValue<T>(object child) where T : unmanaged
    {
        if (child is LeafNode<T> leaf) return leaf.GetValue(LeafNode<T>.Size - 1);
        var node = (InternalNode<T>)child;
        var last = node.Values.Length - 1;
        return node.Children.TryGetValue(last, out var value) ? LastValue<T>(value) : node.Values[last];
    }

    internal static void EraseBackgroundTiles<T>(VdbTree<T> tree) where T : unmanaged
    {
        foreach (var key in tree.Entries.Keys.ToArray())
        {
            var entry = tree.Entries[key];
            if (entry.Child is null && !entry.Active && ValueArithmetic<T>.ApproximatelyEqual(entry.Value, tree.Background)) tree.Entries.Remove(key);
        }
    }
}
