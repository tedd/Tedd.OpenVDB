// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.Tree;

/// <summary>OpenVDB merge policies. Merging consumes the source tree.</summary>
public enum MergePolicy { ActiveStates, Nodes, ActiveStatesAndNodes }

public sealed partial class VdbTree<T> where T : unmanaged
{
    /// <summary>Updates the root background and, optionally, matching inactive values of either sign.</summary>
    public void SetBackground(T value, bool updateChildNodes)
    {
        if (!updateChildNodes) { SetBackground(value); return; }
        T old = Background;
        if (ValueEquality<T>.Equals(old, value)) return;
        T oldNegative = ValueArithmetic<T>.Negative(old), newNegative = ValueArithmetic<T>.Negative(value);
        InvalidateAccessors();
        foreach (Coord key in Entries.Keys.ToArray())
        {
            RootEntry entry = Entries[key];
            if (entry.Child is not null) entry.Child.ResetBackground(old, value, oldNegative, newNegative);
            else if (!entry.Active) Entries[key] = new(ValueArithmetic<T>.Reset(entry.Value, old, value, oldNegative, newNegative), false);
        }
        Background = value;
    }
    internal static void ValidateMergePolicy(MergePolicy policy)
    {
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
    }
    /// <summary>Merges values and/or nodes using the upstream ownership and tile precedence rules.</summary>
    /// <remarks>Leaves transferred from other become owned by this tree. Other is emptied.</remarks>
    public void Merge(VdbTree<T> other, MergePolicy policy = MergePolicy.ActiveStatesAndNodes)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other)) throw new ArgumentException("A tree cannot consume itself.", nameof(other));
        ValidateMergePolicy(policy);
        bool sameBackground = ValueEquality<T>.Equals(Background, other.Background);
        T negative = sameBackground ? default : ValueArithmetic<T>.Negative(Background);
        T otherNegative = sameBackground ? default : ValueArithmetic<T>.Negative(other.Background);
        InvalidateAccessors();
        other.InvalidateAccessors();
        foreach ((Coord key, RootEntry source) in other.Entries)
        {
            bool exists = Entries.TryGetValue(key, out RootEntry target);
            if (source.Child is not null)
            {
                if (target.Child is not null)
                {
                    // RootNode passes (otherBackground, background), in this order, to child merge.
                    target.Child.Merge(source.Child, policy, other.Background, Background, otherNegative, negative);
                }
                else if (!exists || policy != MergePolicy.ActiveStates || !target.Active)
                {
                    source.Child.ResetBackground(other.Background, Background, otherNegative, negative);
                    Entries[key] = new(default, false, source.Child);
                    if (exists && policy == MergePolicy.ActiveStatesAndNodes && target.Active) source.Child.MergeActiveTile(target.Value);
                }
            }
            else if (source.Active && policy != MergePolicy.Nodes)
            {
                if (!exists || (target.Child is null && !target.Active)) Entries[key] = source;
                else if (target.Child is not null)
                {
                    if (policy == MergePolicy.ActiveStates) Entries[key] = source;
                    else target.Child.MergeActiveTile(source.Value);
                }
            }
        }
        other.Entries.Clear();
    }
    /// <summary>Inserts the supplied leaf by reference, replacing the existing leaf or tile.</summary>
    /// <remarks>The caller must transfer ownership; inserting the same leaf into multiple trees aliases values.</remarks>
    public void AddLeaf(LeafNode<T> leaf)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        InvalidateAccessors();
        TouchUpper(leaf.Origin).AddLeaf(leaf);
    }
    /// <summary>Detaches a leaf and replaces its slot with a tile. Returns null when no leaf exists.</summary>
    public LeafNode<T>? StealLeaf(Coord coordinate, T value = default, bool active = false)
    {
        if (!Entries.TryGetValue(Align(coordinate, 12), out RootEntry entry) || entry.Child is null) return null;
        LeafNode<T>? leaf = entry.Child.StealLeaf(coordinate, value, active);
        if (leaf is not null) InvalidateAccessors();
        return leaf;
    }
    public LeafNode<T>[] StealLeaves(T value = default, bool active = false)
    {
        var leaves = EnumerateLeaves().ToArray();
        foreach (var leaf in leaves) StealLeaf(leaf.Origin, value, active);
        return leaves;
    }
    /// <summary>Fills the box using actual leaf buffers, including uniform inactive regions.</summary>
    public void DenseFill(CoordBBox box, T value, bool active = true)
    {
        if (box.IsEmpty) return;
        Coord start = Align(box.Min, 3);
        for (long x = start.X; x <= box.Max.X; x += 8)
            for (long y = start.Y; y <= box.Max.Y; y += 8)
                for (long z = start.Z; z <= box.Max.Z; z += 8)
                {
                    var leaf = TouchLeaf(new((int)x, (int)y, (int)z));
                    leaf.Fill(box.Intersection(leaf.Bounds), value, active);
                }
    }
    /// <summary>Replaces active tiles by leaves; inactive tiles remain sparse.</summary>
    /// <param name="maximumNewLeaves">Optional allocation budget, checked before any mutation.</param>
    public void VoxelizeActiveTiles(ulong maximumNewLeaves = ulong.MaxValue)
    {
        var tiles = EnumerateValues().Where(v => !v.IsVoxel).ToArray();
        ulong required = 0;
        foreach (var tile in tiles) required = checked(required + (tile.VoxelCount >> 9));
        if (required > maximumNewLeaves) throw new InvalidOperationException($"Voxelization requires {required} new leaves; limit is {maximumNewLeaves}.");
        foreach (var tile in tiles) DenseFill(tile.Bounds, tile.Value, true);
    }
    public void ModifyValue(Coord coordinate, Func<T, T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        T result = operation(GetValue(coordinate));
        SetValueOn(coordinate, result);
    }
    public void ModifyValueAndActiveState(Coord coordinate, Func<VoxelState<T>, VoxelState<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        bool active = ProbeValue(coordinate, out T value);
        var result = operation(new(value, active));
        if (!Entries.ContainsKey(Align(coordinate, 12))) { InvalidateAccessors(); TouchUpper(coordinate); }
        Set(coordinate, result.Value, result.IsActive);
    }
    /// <summary>Node counts indexed by level: leaf, lower, upper, root.</summary>
    public long[] NodeCount()
    {
        var counts = new long[Depth];
        counts[3] = 1;
        foreach (RootEntry entry in Entries.Values) entry.Child?.CountNodes(counts);
        return counts;
    }
    public ulong InactiveLeafVoxelCount
    {
        get { ulong count = 0; foreach (var leaf in EnumerateLeaves()) count = checked(count + (ulong)leaf.OffVoxelCount); return count; }
    }
    /// <summary>Inactive voxels represented by nodes and non-background root tiles, as tools::countInactiveVoxels.</summary>
    public ulong InactiveVoxelCount
    {
        get
        {
            ulong count = 0;
            foreach (RootEntry entry in Entries.Values)
            {
                if (entry.Child is not null) count = checked(count + (1UL << 36) - entry.Child.CountActive());
                else if (!entry.Active && !ValueArithmetic<T>.ApproximatelyEqual(entry.Value, Background)) count = checked(count + (1UL << 36));
            }
            return count;
        }
    }
    /// <summary>Bounds of whole leaves containing active voxels and all active tiles.</summary>
    public bool TryGetLeafBoundingBox(out CoordBBox bounds)
    {
        bounds = CoordBBox.Empty;
        foreach (var leaf in EnumerateLeaves()) if (!leaf.IsEmpty) bounds = bounds.Expand(leaf.Bounds);
        foreach (var value in EnumerateValues()) if (!value.IsVoxel) bounds = bounds.Expand(value.Bounds);
        return !bounds.IsEmpty;
    }
    /// <summary>Range from the lexicographically first to last root key, following RootNode::getIndexRange.</summary>
    public CoordBBox GetIndexRange()
    {
        if (Entries.Count == 0) return new(new(0, 0, 0), new(0, 0, 0));
        Coord min = Entries.Keys.Min(), max = Entries.Keys.Max();
        return new(min, NodeBounds(max, 12).Max);
    }
}
