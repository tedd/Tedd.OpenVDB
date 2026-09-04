// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.Tree;

internal sealed partial class InternalNode<T> where T : unmanaged
{
    internal void ResetBackground(T oldBackground, T newBackground, T oldNegative, T newNegative)
    {
        if (ValueEquality<T>.Equals(oldBackground, newBackground)) return;
        var values = Values;
        for (int offset = 0; offset < values.Length; offset++)
        {
            if (Children.TryGetValue(offset, out var child)) ResetChild(child, oldBackground, newBackground, oldNegative, newNegative);
            else if (Active.IsOff(offset)) values[offset] = ValueArithmetic<T>.Reset(values[offset], oldBackground, newBackground, oldNegative, newNegative);
        }
    }
    private static void ResetChild(object child, T oldBackground, T newBackground, T oldNegative, T newNegative)
    {
        if (child is LeafNode<T> leaf) leaf.ResetBackground(oldBackground, newBackground, oldNegative, newNegative);
        else ((InternalNode<T>)child).ResetBackground(oldBackground, newBackground, oldNegative, newNegative);
    }
    internal void MergeActiveTile(T value)
    {
        var values = Values;
        for (int offset = 0; offset < values.Length; offset++)
        {
            if (Children.TryGetValue(offset, out var child))
            {
                if (child is LeafNode<T> leaf) leaf.MergeActiveTile(value);
                else ((InternalNode<T>)child).MergeActiveTile(value);
            }
            else if (Active.IsOff(offset)) { values[offset] = value; Active.SetOn(offset); }
        }
    }
    internal void Merge(InternalNode<T> other, MergePolicy policy, T background, T otherBackground, T negative, T otherNegative)
    {
        for (int offset = 0; offset < Values.Length; offset++)
        {
            bool hasChild = Children.TryGetValue(offset, out var target);
            if (other.Children.TryGetValue(offset, out var source))
            {
                if (hasChild)
                {
                    if (target is LeafNode<T> leaf) leaf.Merge((LeafNode<T>)source, policy);
                    else ((InternalNode<T>)target!).Merge((InternalNode<T>)source, policy, background, otherBackground, negative, otherNegative);
                }
                else if (policy != MergePolicy.ActiveStates || Active.IsOff(offset))
                {
                    bool wasActive = Active.IsOn(offset);
                    T value = Values[offset];
                    other.Children.Remove(offset);
                    Children[offset] = source;
                    Active.SetOff(offset);
                    ResetChild(source, otherBackground, background, otherNegative, negative);
                    if (policy == MergePolicy.ActiveStatesAndNodes && wasActive)
                    {
                        if (source is LeafNode<T> leaf) leaf.MergeActiveTile(value);
                        else ((InternalNode<T>)source).MergeActiveTile(value);
                    }
                }
            }
            else if (policy != MergePolicy.Nodes && other.Active.IsOn(offset))
            {
                T value = other.Values[offset];
                if (policy == MergePolicy.ActiveStates)
                {
                    if (Active.IsOff(offset)) SetTile(offset, value, true);
                }
                else if (hasChild)
                {
                    if (target is LeafNode<T> leaf) leaf.MergeActiveTile(value);
                    else ((InternalNode<T>)target!).MergeActiveTile(value);
                }
                else if (Active.IsOff(offset)) { Values[offset] = value; Active.SetOn(offset); }
            }
        }
    }
    internal void AddLeaf(LeafNode<T> leaf)
    {
        int offset = Offset(leaf.Origin);
        if (Level == 1) { Children[offset] = leaf; Active.SetOff(offset); }
        else ((InternalNode<T>)TouchChild(offset)).AddLeaf(leaf);
    }
    internal LeafNode<T>? StealLeaf(Coord coordinate, T value, bool active)
    {
        int offset = Offset(coordinate);
        if (!Children.TryGetValue(offset, out var child)) return null;
        if (child is InternalNode<T> node) return node.StealLeaf(coordinate, value, active);
        SetTile(offset, value, active);
        return (LeafNode<T>)child;
    }
    internal void CountNodes(Span<long> counts)
    {
        counts[Level]++;
        foreach (object child in Children.Values)
            if (child is LeafNode<T>) counts[0]++; else ((InternalNode<T>)child).CountNodes(counts);
    }
}
