// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of tree/InternalNode.h at f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf.
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Util;

namespace Tedd.OpenVDB.Tree;

// The C++ value/child union is represented by an unmanaged value array and a sparse
// child table. This keeps GC scanning proportional to allocated children, not tiles.
internal sealed partial class InternalNode<T> where T : unmanaged
{
    internal readonly Coord Origin;
    internal readonly int Level;
    internal readonly int Log2Dim;
    internal readonly int ChildLog2Dim;
    internal readonly int TotalLog2Dim;
    internal readonly T[] Values;
    internal readonly NodeMask Active;
    // Mutations must go through AddChild/SetChild/RemoveChild so node-scoped traversals
    // detect changes to this direct table without invalidation from sibling subtrees.
    internal readonly Dictionary<int, object> Children = [];
    private long _directTopologyVersion;
    internal long DirectTopologyVersion => Volatile.Read(ref _directTopologyVersion);
    internal void AddChild(int offset, object child)
    {
        Children.Add(offset, child);
        Interlocked.Increment(ref _directTopologyVersion);
    }
    internal void SetChild(int offset, object child)
    {
        if (Children.TryGetValue(offset, out var previous) && ReferenceEquals(previous, child)) return;
        Children[offset] = child;
        Interlocked.Increment(ref _directTopologyVersion);
    }
    internal bool RemoveChild(int offset)
    {
        if (!Children.Remove(offset)) return false;
        Interlocked.Increment(ref _directTopologyVersion);
        return true;
    }

    internal InternalNode(int level, Coord origin, T value, bool active)
    {
        Level = level;
        Log2Dim = level == 1 ? 4 : 5;
        ChildLog2Dim = level == 1 ? 3 : 7;
        TotalLog2Dim = Log2Dim + ChildLog2Dim;
        Origin = VdbTree<T>.Align(origin, TotalLog2Dim);
        Values = GC.AllocateUninitializedArray<T>(1 << (3 * Log2Dim));
        Values.AsSpan().Fill(value); // Every element initialized before publication.
        Active = new NodeMask(Log2Dim, active);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int Offset(Coord c)
    {
        int mask = (1 << Log2Dim) - 1;
        return (((c.X >> ChildLog2Dim) & mask) << (2 * Log2Dim))
            | (((c.Y >> ChildLog2Dim) & mask) << Log2Dim)
            | ((c.Z >> ChildLog2Dim) & mask);
    }

    internal Coord ChildOrigin(int offset)
    {
        int mask = (1 << Log2Dim) - 1;
        return new(Origin.X + ((offset >> (2 * Log2Dim)) << ChildLog2Dim),
            Origin.Y + (((offset >> Log2Dim) & mask) << ChildLog2Dim),
            Origin.Z + ((offset & mask) << ChildLog2Dim));
    }

    internal bool Probe(Coord c, out T value, out LeafNode<T>? leaf, out int depth)
    {
        int offset = Offset(c);
        if (Children.TryGetValue(offset, out object? child))
        {
            if (child is InternalNode<T> node) return node.Probe(c, out value, out leaf, out depth);
            leaf = (LeafNode<T>)child;
            depth = 3;
            return leaf.ProbeValue(c, out value);
        }
        leaf = null;
        value = Values[offset];
        depth = 3 - Level;
        return Active.IsOn(offset);
    }

    internal object TouchChild(int offset)
    {
        if (Children.TryGetValue(offset, out object? child)) return child;
        Coord origin = ChildOrigin(offset);
        T value = Values[offset];
        bool active = Active.IsOn(offset);
        child = Level == 1 ? new LeafNode<T>(origin, value, active)
            : new InternalNode<T>(Level - 1, origin, value, active);
        AddChild(offset, child);
        Active.Set(offset, false); // Child and active-tile masks are disjoint.
        return child;
    }

    internal LeafNode<T> TouchLeaf(Coord c)
    {
        object child = TouchChild(Offset(c));
        return child is LeafNode<T> leaf ? leaf : ((InternalNode<T>)child).TouchLeaf(c);
    }

    internal void SetTile(int offset, T value, bool active)
    {
        RemoveChild(offset);
        Values[offset] = value;
        Active.Set(offset, active);
    }

    internal void AddTile(int level, Coord c, T value, bool active)
    {
        int offset = Offset(c);
        if (level == Level) SetTile(offset, value, active);
        else if (Level == 1)
        {
            var leaf = (LeafNode<T>)TouchChild(offset);
            if (active) leaf.SetValueOn(c, value); else leaf.SetValueOff(c, value);
        }
        else ((InternalNode<T>)TouchChild(offset)).AddTile(level, c, value, active);
    }

    internal void Fill(CoordBBox box, T value, bool active)
    {
        // The caller has already clipped the inclusive box to this node.
        int sx = (box.Min.X - Origin.X) >> ChildLog2Dim;
        int sy = (box.Min.Y - Origin.Y) >> ChildLog2Dim;
        int sz = (box.Min.Z - Origin.Z) >> ChildLog2Dim;
        int ex = (box.Max.X - Origin.X) >> ChildLog2Dim;
        int ey = (box.Max.Y - Origin.Y) >> ChildLog2Dim;
        int ez = (box.Max.Z - Origin.Z) >> ChildLog2Dim;
        for (int x = sx; x <= ex; x++)
            for (int y = sy; y <= ey; y++)
                for (int z = sz; z <= ez; z++)
                {
                    int offset = (x << (2 * Log2Dim)) | (y << Log2Dim) | z;
                    CoordBBox childBox = VdbTree<T>.NodeBounds(ChildOrigin(offset), ChildLog2Dim);
                    CoordBBox clipped = box.Intersection(childBox);
                    if (clipped.Min == childBox.Min && clipped.Max == childBox.Max)
                        SetTile(offset, value, active);
                    else
                    {
                        if (!Children.ContainsKey(offset) && Active.IsOn(offset) == active
                            && ValueEquality<T>.Equals(Values[offset], value)) continue;
                        object child = TouchChild(offset);
                        if (child is LeafNode<T> leaf) leaf.Fill(clipped, value, active);
                        else ((InternalNode<T>)child).Fill(clipped, value, active);
                    }
                }
    }

    internal void SetActiveRegion(CoordBBox box, bool active)
    {
        int sx = (box.Min.X - Origin.X) >> ChildLog2Dim;
        int sy = (box.Min.Y - Origin.Y) >> ChildLog2Dim;
        int sz = (box.Min.Z - Origin.Z) >> ChildLog2Dim;
        int ex = (box.Max.X - Origin.X) >> ChildLog2Dim;
        int ey = (box.Max.Y - Origin.Y) >> ChildLog2Dim;
        int ez = (box.Max.Z - Origin.Z) >> ChildLog2Dim;
        for (int x = sx; x <= ex; x++)
            for (int y = sy; y <= ey; y++)
                for (int z = sz; z <= ez; z++)
                {
                    int offset = (x << (2 * Log2Dim)) | (y << Log2Dim) | z;
                    CoordBBox bounds = VdbTree<T>.NodeBounds(ChildOrigin(offset), ChildLog2Dim);
                    CoordBBox clipped = box.Intersection(bounds);
                    bool hasChild = Children.TryGetValue(offset, out var child);
                    if (!hasChild)
                    {
                        if (Active.IsOn(offset) == active) continue;
                        if (clipped == bounds) { Active.Set(offset, active); continue; }
                        child = TouchChild(offset);
                    }
                    if (child is InternalNode<T> node) node.SetActiveRegion(clipped, active);
                    else
                    {
                        var leaf = (LeafNode<T>)child!;
                        foreach (Coord c in clipped) leaf.SetActiveState(c, active);
                    }
                }
    }

    internal static InternalNode<T> CopyTopology<TOther>(InternalNode<TOther> other, T value, bool allActive = false) where TOther : unmanaged
    {
        var copy = new InternalNode<T>(other.Level, other.Origin, value, allActive);
        if (!allActive) copy.Active.OrWith(other.Active);
        foreach ((int offset, object child) in other.Children)
        {
            copy.AddChild(offset, CopyChildTopology<TOther>(child, value, allActive));
            copy.Active.SetOff(offset);
        }
        return copy;
    }

    private static object CopyChildTopology<TOther>(object child, T value, bool allActive) where TOther : unmanaged
    {
        if (child is InternalNode<TOther> node) return CopyTopology(node, value, allActive);
        var source = (LeafNode<TOther>)child;
        var leaf = new LeafNode<T>(source.Origin, value, allActive);
        if (!allActive)
            for (int i = 0; i < LeafNode<T>.Size; i++) leaf.SetActiveState(i, source.IsValueOn(i));
        return leaf;
    }

    internal void SetAllActive(bool active)
    {
        Active.Fill(active);
        foreach ((int offset, object child) in Children)
        {
            Active.SetOff(offset);
            if (child is InternalNode<T> node) node.SetAllActive(active);
            else
            {
                var leaf = (LeafNode<T>)child;
                for (int i = 0; i < LeafNode<T>.Size; i++) leaf.SetActiveState(i, active);
            }
        }
    }

    internal void TopologyUnion<TOther>(InternalNode<TOther> other, bool preserveTiles) where TOther : unmanaged
    {
        for (int i = 0; i < Values.Length; i++)
        {
            bool hasThis = Children.TryGetValue(i, out object? target);
            if (other.Children.TryGetValue(i, out object? source))
            {
                if (hasThis) CombineChildTopology<TOther>(target!, source, 0, default, preserveTiles);
                else if (!preserveTiles || !Active.IsOn(i))
                {
                    AddChild(i, CopyChildTopology<TOther>(source, Values[i], Active.IsOn(i)));
                    Active.SetOff(i);
                }
            }
            else if (other.Active.IsOn(i))
            {
                if (hasThis) SetChildActive(target!, true);
                else Active.SetOn(i);
            }
        }
    }

    private static void SetChildActive(object target, bool active)
    {
        if (target is InternalNode<T> node) node.SetAllActive(active);
        else
        {
            var leaf = (LeafNode<T>)target;
            for (int j = 0; j < LeafNode<T>.Size; j++) leaf.SetActiveState(j, active);
        }
    }

    internal void TopologyRestrict<TOther>(InternalNode<TOther> other, T background, bool intersection) where TOther : unmanaged
    {
        for (int i = 0; i < Values.Length; i++)
        {
            bool hasThis = Children.TryGetValue(i, out object? target);
            bool hasOther = other.Children.TryGetValue(i, out object? source);
            if (hasThis)
            {
                if (hasOther) CombineChildTopology<TOther>(target!, source!, intersection ? 1 : 2, background, false);
                else if (other.Active.IsOn(i) != intersection) SetTile(i, background, false);
            }
            else if (Active.IsOn(i))
            {
                if (hasOther)
                {
                    if (intersection) AddChild(i, CopyChildTopology<TOther>(source!, Values[i], false));
                    else
                    {
                        target = TouchChild(i); // Inherit the active tile, then subtract the other topology.
                        CombineChildTopology<TOther>(target, source!, 2, background, false);
                    }
                    Active.SetOff(i);
                }
                else if (other.Active.IsOn(i) != intersection) Active.SetOff(i);
            }
        }
    }

    private static void CombineChildTopology<TOther>(object target, object source, int operation, T background, bool preserveTiles) where TOther : unmanaged
    {
        if (target is InternalNode<T> node)
        {
            if (operation == 0) node.TopologyUnion((InternalNode<TOther>)source, preserveTiles);
            else node.TopologyRestrict((InternalNode<TOther>)source, background, operation == 1);
        }
        else
        {
            var left = (LeafNode<T>)target;
            var right = (LeafNode<TOther>)source;
            for (int i = 0; i < LeafNode<T>.Size; i++)
            {
                bool a = left.IsValueOn(i), b = right.IsValueOn(i);
                left.SetActiveState(i, operation switch { 0 => a || b, 1 => a && b, _ => a && !b });
            }
        }
    }

    internal bool HasSameTopology<TOther>(InternalNode<TOther> other) where TOther : unmanaged
    {
        if (!Active.Equals(other.Active) || Children.Count != other.Children.Count) return false;
        foreach ((int offset, object child) in Children)
        {
            if (!other.Children.TryGetValue(offset, out object? source)) return false;
            if (child is InternalNode<T> node)
            {
                if (source is not InternalNode<TOther> otherNode || !node.HasSameTopology(otherNode)) return false;
            }
            else
            {
                if (source is not LeafNode<TOther> otherLeaf) return false;
                var leaf = (LeafNode<T>)child;
                for (int i = 0; i < LeafNode<T>.Size; i++) if (leaf.IsValueOn(i) != otherLeaf.IsValueOn(i)) return false;
            }
        }
        return true;
    }

    internal static InternalNode<T> CombineNodes(InternalNode<T>? a, VoxelState<T> tileA,
        InternalNode<T>? b, VoxelState<T> tileB, int level, Coord origin,
        Func<VoxelState<T>, VoxelState<T>, VoxelState<T>> operation)
    {
        var result = new InternalNode<T>(level, origin, default, false);
        for (int i = 0; i < result.Values.Length; i++)
        {
            object? childA = null, childB = null;
            a?.Children.TryGetValue(i, out childA);
            b?.Children.TryGetValue(i, out childB);
            VoxelState<T> valueA = a is null ? tileA : new(a.Values[i], a.Active.IsOn(i));
            VoxelState<T> valueB = b is null ? tileB : new(b.Values[i], b.Active.IsOn(i));
            if (childA is null && childB is null)
            {
                VoxelState<T> combined = operation(valueA, valueB);
                result.Values[i] = combined.Value;
                result.Active.Set(i, combined.IsActive);
            }
            else if (level == 1)
            {
                var leafA = (LeafNode<T>?)childA;
                var leafB = (LeafNode<T>?)childB;
                var leaf = new LeafNode<T>(result.ChildOrigin(i));
                for (int j = 0; j < LeafNode<T>.Size; j++)
                {
                    VoxelState<T> combined = operation(leafA is null ? valueA : new(leafA.GetValue(j), leafA.IsValueOn(j)),
                        leafB is null ? valueB : new(leafB.GetValue(j), leafB.IsValueOn(j)));
                    if (combined.IsActive) leaf.SetValueOn(j, combined.Value); else leaf.SetValueOff(j, combined.Value);
                }
                result.AddChild(i, leaf);
            }
            else result.AddChild(i, CombineNodes((InternalNode<T>?)childA, valueA,
                (InternalNode<T>?)childB, valueB, level - 1, result.ChildOrigin(i), operation));
        }
        return result;
    }

    internal void Prune()
    {
        foreach (int offset in Children.Keys.ToArray())
        {
            object child = Children[offset];
            T value;
            bool active;
            bool constant;
            if (child is LeafNode<T> leaf) constant = leaf.IsConstant(out value, out active);
            else
            {
                var node = (InternalNode<T>)child;
                node.Prune();
                constant = node.IsConstant(out value, out active);
            }
            if (constant) SetTile(offset, value, active);
        }
    }

    internal bool IsConstant(out T value, out bool active)
    {
        value = Values[0];
        active = Active.IsOn(0);
        if (Children.Count != 0 || (active ? !Active.IsOn() : !Active.IsOff())) return false;
        T[] values = Values;
        for (int i = 0; i < values.Length; i++)
            if (!ValueEquality<T>.Equals(value, values[i])) return false;
        return true;
    }

    internal InternalNode<T> DeepCopy()
    {
        var copy = new InternalNode<T>(Level, Origin, default, false);
        Values.AsSpan().CopyTo(copy.Values);
        for (int i = 0; i < Values.Length; i++) copy.Active.Set(i, Active.IsOn(i));
        foreach ((int offset, object child) in Children)
            copy.AddChild(offset, child is LeafNode<T> leaf ? leaf.DeepCopy() : ((InternalNode<T>)child).DeepCopy());
        return copy;
    }

    internal bool HasActiveValues()
    {
        if (!Active.IsOff()) return true;
        foreach (object child in Children.Values)
            if (child is LeafNode<T> leaf ? !leaf.IsEmpty : ((InternalNode<T>)child).HasActiveValues()) return true;
        return false;
    }

    internal ulong CountActive()
    {
        ulong count = checked((ulong)Active.CountOn() * (1UL << (3 * ChildLog2Dim)));
        foreach (object child in Children.Values)
            count = checked(count + (child is LeafNode<T> leaf ? (ulong)leaf.OnVoxelCount : ((InternalNode<T>)child).CountActive()));
        return count;
    }

    internal long CountLeaves()
    {
        long count = 0;
        foreach (object child in Children.Values)
            count = checked(count + (child is LeafNode<T> ? 1 : ((InternalNode<T>)child).CountLeaves()));
        return count;
    }

    internal IEnumerable<LeafNode<T>> Leaves()
    {
        foreach (int offset in Children.Keys.Order())
        {
            object child = Children[offset];
            if (child is LeafNode<T> leaf) yield return leaf;
            else foreach (var descendant in ((InternalNode<T>)child).Leaves()) yield return descendant;
        }
    }

    internal IEnumerable<VoxelValue<T>> EnumerateValues(ValueIterationMode mode)
    {
        for (int offset = 0; offset < Values.Length; offset++)
        {
            if (Children.TryGetValue(offset, out object? child))
            {
                if (child is InternalNode<T> node)
                {
                    foreach (var value in node.EnumerateValues(mode)) yield return value;
                }
                else
                {
                    var leaf = (LeafNode<T>)child;
                    for (int i = 0; i < LeafNode<T>.Size; i++)
                    {
                        bool on = leaf.IsValueOn(i);
                        if (!VdbTree<T>.Matches(mode, on)) continue;
                        Coord c = leaf.OffsetToGlobalCoord(i);
                        yield return new(new(c, c), leaf.GetValue(i), on, 0);
                    }
                }
            }
            else
            {
                bool on = Active.IsOn(offset);
                if (VdbTree<T>.Matches(mode, on))
                    yield return new(VdbTree<T>.NodeBounds(ChildOrigin(offset), ChildLog2Dim), Values[offset], on, Level);
            }
        }
    }
}
