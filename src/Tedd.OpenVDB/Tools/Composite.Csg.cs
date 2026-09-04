// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of tools/Composite.h CSG entry points and tools/Merge.h CSG node operators.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;

namespace Tedd.OpenVDB.Tools;

public static partial class Composite
{
    /// <summary>Replaces A by its level-set union with B, stealing selected B subtrees.</summary>
    /// <remarks>Overlapping or unused B nodes can remain, matching the pinned upstream implementation.</remarks>
    public static void CsgUnion<T>(VdbTree<T> a, VdbTree<T> b, bool prune = true, bool pruneCancelledTiles = false)
        where T : unmanaged, IFloatingPointIeee754<T> => Csg(a, b, CsgOperation.Union, prune, pruneCancelledTiles);

    public static void CsgIntersection<T>(VdbTree<T> a, VdbTree<T> b, bool prune = true, bool pruneCancelledTiles = false)
        where T : unmanaged, IFloatingPointIeee754<T> => Csg(a, b, CsgOperation.Intersection, prune, pruneCancelledTiles);

    public static void CsgDifference<T>(VdbTree<T> a, VdbTree<T> b, bool prune = true, bool pruneCancelledTiles = false)
        where T : unmanaged, IFloatingPointIeee754<T> => Csg(a, b, CsgOperation.Difference, prune, pruneCancelledTiles);

    public static void CsgUnion<T>(Grid<T> a, Grid<T> b, bool prune = true, bool pruneCancelledTiles = false)
        where T : unmanaged, IFloatingPointIeee754<T>
    { ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); CsgUnion(a.Tree, b.Tree, prune, pruneCancelledTiles); }

    public static void CsgIntersection<T>(Grid<T> a, Grid<T> b, bool prune = true, bool pruneCancelledTiles = false)
        where T : unmanaged, IFloatingPointIeee754<T>
    { ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); CsgIntersection(a.Tree, b.Tree, prune, pruneCancelledTiles); }

    public static void CsgDifference<T>(Grid<T> a, Grid<T> b, bool prune = true, bool pruneCancelledTiles = false)
        where T : unmanaged, IFloatingPointIeee754<T>
    { ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b); CsgDifference(a.Tree, b.Tree, prune, pruneCancelledTiles); }

    private enum CsgOperation { Union, Intersection, Difference }

    private static void Csg<T>(VdbTree<T> a, VdbTree<T> b, CsgOperation operation, bool prune, bool cancelled)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(a); ArgumentNullException.ThrowIfNull(b);
        if (ReferenceEquals(a, b)) throw new ArgumentException("CSG inputs must be distinct trees.", nameof(b));
        if (!(a.Background > T.Zero) || !(-a.Background < T.Zero)) throw new ArgumentException("A must have a positive exterior background.", nameof(a));
        if (!(b.Background > T.Zero) || !(-b.Background < T.Zero)) throw new ArgumentException("B must have a positive exterior background.", nameof(b));
        a.InvalidateAccessors(); b.InvalidateAccessors();
        if (operation == CsgOperation.Difference) DifferenceRoot(a, b, prune && cancelled);
        else UnionIntersectionRoot(a, b, operation, prune && cancelled);
        if (prune) Prune.PruneLevelSet(a);
    }

    private static int TileFlags<T>(T value, bool active) where T : IFloatingPointIeee754<T> =>
        (active ? 1 : 0) | (value < T.Zero ? 2 : value > T.Zero ? 4 : 0);

    private static void UnionIntersectionRoot<T>(VdbTree<T> a, VdbTree<T> b, CsgOperation operation, bool cancelled)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var union = operation == CsgOperation.Union;
        var insideFlag = union ? 2 : 4;
        var outsideFlag = union ? 4 : 2;
        var inside = union ? -a.Background : a.Background;
        var outside = -inside;
        Prune.EraseBackgroundTiles(a);
        if (!union)
            foreach (var key in a.Entries.Keys.ToArray()) if (!b.Entries.ContainsKey(key)) a.Entries.Remove(key);
        var tiles = new Dictionary<Coord, int>();
        foreach (var (key, entry) in a.Entries)
            if (entry.Child is null) tiles.Add(key, TileFlags(entry.Value, entry.Active));
        foreach (var (key, entry) in b.Entries)
        {
            if (entry.Child is not null) continue;
            var flag = TileFlags(entry.Value, entry.Active);
            if (!tiles.TryGetValue(key, out var old)) tiles.Add(key, flag);
            else if ((old & outsideFlag) != 0 && (flag & insideFlag) != 0) tiles[key] = flag;
        }
        foreach (var (key, flag) in tiles)
            if ((flag & insideFlag) != 0 && (union || a.Entries.ContainsKey(key))) a.Entries[key] = new(inside, (flag & 1) != 0);
        var children = new HashSet<Coord>(a.Entries.Where(static entry => entry.Value.Child is not null).Select(static entry => entry.Key));
        var recurse = new List<Coord>();
        foreach (var (key, source) in b.Entries.Where(static entry => entry.Value.Child is not null).ToArray())
        {
            if (!union && !a.Entries.ContainsKey(key)) continue;
            if (children.Contains(key)) { recurse.Add(key); continue; }
            // Equality intentionally excludes active inside tiles, matching Merge.h's root flags.
            if (tiles.TryGetValue(key, out var flag) && flag == insideFlag) continue;
            var child = source.Child!;
            b.Entries[key] = new(b.Background, false);
            ResetCsgBackground(child, b.Background, a.Background);
            a.Entries[key] = new(default, false, child);
            children.Add(key);
        }
        foreach (var (key, flag) in tiles)
            if ((flag & outsideFlag) != 0 && !children.Contains(key) && (union || a.Entries.ContainsKey(key)))
                a.Entries[key] = new(outside, (flag & 1) != 0);
        Prune.EraseBackgroundTiles(a);
        foreach (var key in recurse)
            if (a.Entries.TryGetValue(key, out var left) && left.Child is not null && b.Entries.TryGetValue(key, out var right) && right.Child is not null)
                UnionIntersectionNode(left.Child, right.Child, a.Background, b.Background, operation, cancelled);
    }

    private static void UnionIntersectionNode<T>(InternalNode<T> a, InternalNode<T> b, T background, T otherBackground,
        CsgOperation operation, bool cancelled) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var union = operation == CsgOperation.Union;
        var inside = union ? -background : background;
        var outside = -inside;
        var valid = new NodeMask(a.Log2Dim);
        var invalid = new NodeMask(a.Log2Dim);
        for (var offset = 0; offset < a.Values.Length; ++offset)
        {
            if (a.Children.ContainsKey(offset)) continue;
            var value = a.Values[offset];
            if (union ? value < T.Zero : value > T.Zero) valid.SetOn(offset);
            else if (union ? value > T.Zero : value < T.Zero) invalid.SetOn(offset);
        }
        for (var offset = 0; offset < b.Values.Length; ++offset)
        {
            if (b.Children.ContainsKey(offset) || valid.IsOn(offset)) continue;
            if (union ? b.Values[offset] < T.Zero : b.Values[offset] > T.Zero)
            { a.SetTile(offset, inside, b.Active.IsOn(offset)); valid.SetOn(offset); }
        }
        var recurse = new List<int>();
        foreach (var (offset, child) in b.Children.ToArray())
        {
            if (valid.IsOn(offset)) b.SetTile(offset, outside, false);
            else if (invalid.IsOn(offset))
            {
                b.SetTile(offset, otherBackground, false);
                ResetCsgBackground<T>(child, otherBackground, background);
                a.SetChild(offset, child); a.Active.SetOff(offset);
            }
            else if (a.Children.ContainsKey(offset)) recurse.Add(offset);
        }
        foreach (var offset in recurse)
        {
            var left = a.Children[offset]; var right = b.Children[offset];
            if (left is LeafNode<T> leaf) MergeCsgLeaves(leaf, (LeafNode<T>)right, background, operation, cancelled);
            else UnionIntersectionNode((InternalNode<T>)left, (InternalNode<T>)right, background, otherBackground, operation, cancelled);
        }
    }

    private static void DifferenceRoot<T>(VdbTree<T> a, VdbTree<T> b, bool cancelled)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        Prune.EraseBackgroundTiles(a);
        var flags = new Dictionary<Coord, int>();
        foreach (var (key, entry) in a.Entries)
        {
            if (entry.Child is not null) flags.Add(key, 4);
            else if (entry.Value < T.Zero) flags.Add(key, 2 | (entry.Active ? 1 : 0));
        }
        foreach (var (key, entry) in b.Entries)
            if (entry.Child is null && entry.Value < T.Zero && flags.ContainsKey(key)) a.Entries[key] = new(a.Background, entry.Active);
        var recurse = new List<Coord>();
        foreach (var (key, entry) in b.Entries.Where(static entry => entry.Value.Child is not null).ToArray())
        {
            if (!flags.TryGetValue(key, out var flag)) continue;
            if ((flag & 4) != 0) recurse.Add(key);
            else if ((flag & 2) != 0)
            {
                var child = entry.Child!;
                b.Entries[key] = new(b.Background, false);
                ResetCsgBackground(child, b.Background, a.Background); NegateCsgNode<T>(child);
                a.Entries[key] = new(default, false, child);
            }
        }
        Prune.EraseBackgroundTiles(a);
        foreach (var key in recurse)
            if (a.Entries.TryGetValue(key, out var left) && left.Child is not null && b.Entries.TryGetValue(key, out var right) && right.Child is not null)
                DifferenceNode(left.Child, right.Child, a.Background, b.Background, cancelled);
    }

    private static void DifferenceNode<T>(InternalNode<T> a, InternalNode<T> b, T background, T otherBackground, bool cancelled)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var inside = new NodeMask(a.Log2Dim);
        for (var offset = 0; offset < a.Values.Length; ++offset)
            if (!a.Children.ContainsKey(offset) && a.Values[offset] < T.Zero) inside.SetOn(offset);
        for (var offset = 0; offset < b.Values.Length; ++offset)
            if (!b.Children.ContainsKey(offset) && b.Values[offset] < T.Zero && (inside.IsOn(offset) || a.Children.ContainsKey(offset)))
                a.SetTile(offset, background, b.Active.IsOn(offset));
        var recurse = new List<int>();
        foreach (var (offset, child) in b.Children.ToArray())
        {
            if (inside.IsOn(offset))
            {
                b.SetTile(offset, otherBackground, false);
                ResetCsgBackground<T>(child, otherBackground, background); NegateCsgNode<T>(child);
                a.SetChild(offset, child); a.Active.SetOff(offset);
            }
            else if (a.Children.ContainsKey(offset)) recurse.Add(offset);
        }
        foreach (var offset in recurse)
        {
            var left = a.Children[offset]; var right = b.Children[offset];
            if (left is LeafNode<T> leaf) MergeCsgLeaves(leaf, (LeafNode<T>)right, background, CsgOperation.Difference, cancelled);
            else DifferenceNode((InternalNode<T>)left, (InternalNode<T>)right, background, otherBackground, cancelled);
        }
    }

    private static void MergeCsgLeaves<T>(LeafNode<T> a, LeafNode<T> b, T background, CsgOperation operation, bool cancelled)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        var allCancelled = cancelled;
        for (var offset = 0; offset < LeafNode<T>.Size; ++offset)
        {
            var oldValue = a.GetValue(offset);
            var sourceValue = b.GetValue(offset);
            if (allCancelled) allCancelled = operation == CsgOperation.Difference ? oldValue == sourceValue : oldValue == -sourceValue;
            var candidate = operation == CsgOperation.Difference ? -sourceValue : sourceValue;
            if (operation == CsgOperation.Union ? candidate < oldValue : oldValue < candidate)
            { a.SetValueOnly(offset, candidate); a.SetActiveState(offset, b.IsValueOn(offset)); }
        }
        if (allCancelled) a.Fill(operation == CsgOperation.Union ? -background : background, false);
    }

    private static void ResetCsgBackground<T>(object child, T oldBackground, T newBackground)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (oldBackground == newBackground) return;
        if (child is LeafNode<T> leaf) leaf.ResetBackground(oldBackground, newBackground, -oldBackground, -newBackground);
        else ((InternalNode<T>)child).ResetBackground(oldBackground, newBackground, -oldBackground, -newBackground);
    }

    private static void NegateCsgNode<T>(object child) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (child is LeafNode<T> leaf)
        {
            for (var offset = 0; offset < LeafNode<T>.Size; ++offset) leaf.SetValueOnly(offset, -leaf.GetValue(offset));
        }
        else
        {
            var node = (InternalNode<T>)child;
            for (var offset = 0; offset < node.Values.Length; ++offset)
                if (node.Children.TryGetValue(offset, out var next)) NegateCsgNode<T>(next);
                else node.Values[offset] = -node.Values[offset];
        }
    }
}
