// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of tree/RootNode.h, Tree.h and ValueAccessor.h.
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Util;

namespace Tedd.OpenVDB.Tree;

public readonly record struct VoxelState<T>(T Value, bool IsActive) where T : unmanaged;

public enum ValueIterationMode { Active, Inactive, All }

/// <summary>A stored tile or voxel. Bounds are inclusive; level zero is one voxel.</summary>
public readonly record struct VoxelValue<T>(CoordBBox Bounds, T Value, bool IsActive, int Level) where T : unmanaged
{
    public bool IsVoxel => Level == 0;
    public ulong VoxelCount => Bounds.Volume;
    public Coord Coordinate => Bounds.Min;
}

/// <summary>
/// Managed OpenVDB Tree4&lt;T,5,4,3&gt;: an unbounded signed-coordinate root map,
/// 32³ upper tables, 16³ lower tables and 8³ leaves. Active state is independent
/// of stored value. Reads may run concurrently only while the tree is not mutated.
/// </summary>
public sealed class VdbTree<T> where T : unmanaged
{
    internal readonly Dictionary<Coord, RootEntry> Entries = [];
    internal long TopologyVersion { get; private set; }
    internal readonly record struct RootEntry(T Value, bool Active, InternalNode<T>? Child = null);
    public const int Depth = 4;
    public const int LeafLog2Dim = 3;
    public const int LowerLog2Dim = 4;
    public const int UpperLog2Dim = 5;
    public T Background { get; private set; }
    public bool IsEmpty
    {
        get
        {
            foreach (RootEntry entry in Entries.Values)
                if (entry.Child is null ? entry.Active : entry.Child.HasActiveValues()) return false;
            return true;
        }
    }
    public int RootEntryCount => Entries.Count;
    public long LeafCount => Entries.Values.Sum(e => e.Child?.CountLeaves() ?? 0);
    public ulong ActiveVoxelCount
    {
        get
        {
            ulong count = 0;
            foreach (RootEntry entry in Entries.Values)
                count = checked(count + (entry.Child?.CountActive() ?? (entry.Active ? 1UL << 36 : 0)));
            return count;
        }
    }
    public ulong ActiveLeafVoxelCount
    {
        get
        {
            ulong count = 0;
            foreach (var leaf in EnumerateLeaves()) count = checked(count + (ulong)leaf.OnVoxelCount);
            return count;
        }
    }
    public ulong ActiveTileCount => (ulong)EnumerateValues().LongCount(v => !v.IsVoxel);

    public VdbTree(T background = default) => Background = background;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Coord Align(Coord c, int log2Dim)
    {
        int mask = ~((1 << log2Dim) - 1);
        return new(c.X & mask, c.Y & mask, c.Z & mask);
    }

    internal static CoordBBox NodeBounds(Coord origin, int log2Dim)
    {
        int edge = (1 << log2Dim) - 1;
        return new(origin, new(origin.X + edge, origin.Y + edge, origin.Z + edge));
    }
    internal static bool Matches(ValueIterationMode mode, bool on) => mode == ValueIterationMode.All
        || (mode == ValueIterationMode.Active ? on : !on);

    internal bool Probe(Coord c, out T value, out LeafNode<T>? leaf, out int depth)
    {
        if (!Entries.TryGetValue(Align(c, 12), out RootEntry entry))
        {
            value = Background; leaf = null; depth = -1; return false;
        }
        if (entry.Child is not null) return entry.Child.Probe(c, out value, out leaf, out depth);
        value = entry.Value; leaf = null; depth = 0; return entry.Active;
    }

    public T GetValue(Coord coordinate) { Probe(coordinate, out T value, out _, out _); return value; }
    public T GetValue(int x, int y, int z) => GetValue(new(x, y, z));
    public T this[Coord coordinate] { get => GetValue(coordinate); set => SetValueOn(coordinate, value); }
    public bool ProbeValue(Coord coordinate, out T value) => Probe(coordinate, out value, out _, out _);
    public bool IsValueOn(Coord coordinate) => Probe(coordinate, out _, out _, out _);
    public bool IsValueOff(Coord coordinate) => !IsValueOn(coordinate);
    public int GetValueDepth(Coord coordinate) { Probe(coordinate, out _, out _, out int depth); return depth; }
    public LeafNode<T>? ProbeLeaf(Coord coordinate) { Probe(coordinate, out _, out var leaf, out _); return leaf; }

    private InternalNode<T> TouchUpper(Coord coordinate)
    {
        Coord key = Align(coordinate, 12);
        if (Entries.TryGetValue(key, out RootEntry entry))
        {
            if (entry.Child is not null) return entry.Child;
        }
        else entry = new(Background, false);
        var node = new InternalNode<T>(2, key, entry.Value, entry.Active);
        Entries[key] = new(default, false, node);
        return node;
    }

    /// <summary>Materializes a leaf while preserving the inherited tile value and state.</summary>
    public LeafNode<T> TouchLeaf(Coord coordinate) => TouchUpper(coordinate).TouchLeaf(coordinate);

    public void SetValue(Coord coordinate, T value) => SetValueOn(coordinate, value);
    public void SetValueOn(Coord coordinate, T value) => Set(coordinate, value, true);
    public void SetValueOff(Coord coordinate, T value) => Set(coordinate, value, false);
    public void SetValueOn(Coord coordinate) => SetActiveState(coordinate, true);
    public void SetValueOff(Coord coordinate) => SetActiveState(coordinate, false);

    private void Set(Coord coordinate, T value, bool active)
    {
        bool oldActive = Probe(coordinate, out T oldValue, out var leaf, out _);
        if (leaf is null && oldActive == active && ValueEquality<T>.Equals(oldValue, value)) return;
        leaf ??= TouchLeaf(coordinate);
        if (active) leaf.SetValueOn(coordinate, value); else leaf.SetValueOff(coordinate, value);
    }

    public void SetValueOnly(Coord coordinate, T value)
    {
        // RootNode::setValueOnly creates an upper node even for an absent background write.
        if (!Entries.ContainsKey(Align(coordinate, 12))) TouchUpper(coordinate);
        Probe(coordinate, out T oldValue, out var leaf, out _);
        if (leaf is null && ValueEquality<T>.Equals(oldValue, value)) return;
        (leaf ?? TouchLeaf(coordinate)).SetValueOnly(coordinate, value);
    }

    public void SetActiveState(Coord coordinate, bool active)
    {
        bool oldActive = Probe(coordinate, out _, out var leaf, out _);
        if (leaf is null && oldActive == active) return;
        (leaf ?? TouchLeaf(coordinate)).SetActiveState(coordinate, active);
    }

    /// <summary>Replaces the containing tile: 0=voxel, 1=8³, 2=128³, 3=4096³.</summary>
    public void AddTile(int level, Coord coordinate, T value, bool active)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 3);
        InvalidateAccessors();
        if (level == 3) Entries[Align(coordinate, 12)] = new(value, active);
        else TouchUpper(coordinate).AddTile(level, coordinate, value, active);
    }

    /// <summary>Fills an inclusive box, retaining large fully covered regions as tiles.</summary>
    public void Fill(CoordBBox box, T value, bool active = true)
    {
        if (box.IsEmpty) return;
        InvalidateAccessors();
        Coord start = Align(box.Min, 12);
        // Int64 loop variables prevent wraparound at int.MaxValue.
        for (long x = start.X; x <= box.Max.X; x += 4096)
        for (long y = start.Y; y <= box.Max.Y; y += 4096)
        for (long z = start.Z; z <= box.Max.Z; z += 4096)
        {
            Coord key = new((int)x, (int)y, (int)z);
            CoordBBox bounds = NodeBounds(key, 12);
            CoordBBox clipped = box.Intersection(bounds);
            if (clipped.Min == bounds.Min && clipped.Max == bounds.Max) Entries[key] = new(value, active);
            else TouchUpper(key).Fill(clipped, value, active);
        }
    }

    /// <summary>Writes a z-contiguous dense box. The complete span length is checked before mutation.</summary>
    public void CopyFromDense(CoordBBox box, ReadOnlySpan<T> values, bool active = true)
    {
        ulong volume = box.Volume;
        if (volume != (ulong)values.Length) throw new ArgumentException("Span length must equal the box volume.", nameof(values));
        if (box.IsEmpty) return;
        // A caller can supply a span from a live leaf in this same tree. Snapshot
        // overlapping input before any write, so shifted transfers have memmove semantics.
        foreach (var leaf in EnumerateLeaves())
            if (values.Overlaps(leaf.Values)) { values = values.ToArray(); break; }
        int index = 0;
        var accessor = GetAccessor();
        foreach (Coord c in box)
        {
            if (active) accessor.SetValueOn(c, values[index++]);
            else accessor.SetValueOff(c, values[index++]);
        }
    }

    public void CopyToDense(CoordBBox box, Span<T> destination)
    {
        ulong volume = box.Volume;
        if (volume != (ulong)destination.Length) throw new ArgumentException("Span length must equal the box volume.", nameof(destination));
        int index = 0;
        var accessor = GetAccessor();
        foreach (Coord c in box) destination[index++] = accessor.GetValue(c);
    }

    /// <summary>Changes only the active state inside an inclusive box, preserving all values.</summary>
    public void SetActiveState(CoordBBox box, bool active)
    {
        if (box.IsEmpty) return;
        InvalidateAccessors();
        Coord start = Align(box.Min, 12);
        for (long x = start.X; x <= box.Max.X; x += 4096)
        for (long y = start.Y; y <= box.Max.Y; y += 4096)
        for (long z = start.Z; z <= box.Max.Z; z += 4096)
        {
            Coord key = new((int)x, (int)y, (int)z);
            CoordBBox bounds = NodeBounds(key, 12);
            CoordBBox clipped = box.Intersection(bounds);
            if (!Entries.TryGetValue(key, out RootEntry entry)) entry = new(Background, false);
            if (entry.Child is null)
            {
                if (entry.Active == active) continue;
                if (clipped == bounds) { Entries[key] = new(entry.Value, active); continue; }
            }
            TouchUpper(key).SetActiveRegion(clipped, active);
        }
    }

    /// <summary>Unions active states and child topology, retaining this tree's values.</summary>
    public void TopologyUnion<TOther>(VdbTree<TOther> other, bool preserveTiles = false) where TOther : unmanaged
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other)) return;
        InvalidateAccessors();
        foreach ((Coord key, var source) in other.Entries)
        {
            bool exists = Entries.TryGetValue(key, out RootEntry target);
            if (source.Child is not null)
            {
                if (!exists) Entries.Add(key, new(default, false, InternalNode<T>.CopyTopology(source.Child, Background)));
                else if (target.Child is not null) target.Child.TopologyUnion(source.Child, preserveTiles);
                else if (!preserveTiles || !target.Active)
                    Entries[key] = new(default, false, InternalNode<T>.CopyTopology(source.Child, target.Value, target.Active));
            }
            else if (source.Active)
            {
                if (!exists) Entries.Add(key, new(Background, true));
                else if (target.Child is not null) target.Child.SetAllActive(true);
                else if (!target.Active) Entries[key] = new(target.Value, true);
            }
        }
    }

    /// <summary>Intersects topology. Branches covered by inactive tiles are removed, as in OpenVDB.</summary>
    public void TopologyIntersection<TOther>(VdbTree<TOther> other) where TOther : unmanaged
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other)) return;
        InvalidateAccessors();
        foreach (Coord key in Entries.Keys.ToArray())
        {
            RootEntry target = Entries[key];
            bool exists = other.Entries.TryGetValue(key, out var source);
            if (target.Child is not null)
            {
                if (!exists || (source.Child is null && !source.Active)) Entries.Remove(key);
                else if (source.Child is not null) target.Child.TopologyRestrict(source.Child, Background, true);
            }
            else if (target.Active)
            {
                if (!exists || (source.Child is null && !source.Active)) Entries[key] = new(target.Value, false);
                else if (source.Child is not null)
                    Entries[key] = new(default, false, InternalNode<T>.CopyTopology(source.Child, target.Value));
            }
        }
    }

    /// <summary>Subtracts topology. Branches covered by active tiles are removed, as in OpenVDB.</summary>
    public void TopologyDifference<TOther>(VdbTree<TOther> other) where TOther : unmanaged
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other))
        {
            // A snapshot preserves source traversal while matching native per-node semantics.
            TopologyDifference(other.DeepCopy());
            return;
        }
        InvalidateAccessors();
        foreach ((Coord key, var source) in other.Entries)
        {
            if (!Entries.TryGetValue(key, out RootEntry target)) continue;
            if (source.Child is not null)
            {
                if (target.Child is not null) target.Child.TopologyRestrict(source.Child, Background, false);
                else if (target.Active)
                {
                    var node = new InternalNode<T>(2, key, target.Value, true);
                    node.TopologyRestrict(source.Child, Background, false);
                    Entries[key] = new(default, false, node);
                }
            }
            else if (source.Active)
            {
                if (target.Child is not null) Entries.Remove(key);
                else if (target.Active) Entries[key] = new(target.Value, false);
            }
        }
    }

    /// <summary>Compares node topology and masks, ignoring values and implicit/background root tiles.</summary>
    public bool HasSameTopology<TOther>(VdbTree<TOther> other) where TOther : unmanaged
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other)) return true;
        var unmatched = new HashSet<Coord>(other.Entries.Keys);
        foreach ((Coord key, RootEntry target) in Entries)
        {
            if (target.Child is null && !target.Active && ValueEquality<T>.Equals(target.Value, Background)) continue;
            if (!other.Entries.TryGetValue(key, out var source)) return false;
            if (target.Child is not null)
            {
                if (source.Child is null || !target.Child.HasSameTopology(source.Child)) return false;
            }
            else if (source.Child is not null || target.Active != source.Active) return false;
            unmatched.Remove(key);
        }
        foreach (Coord key in unmatched)
        {
            var source = other.Entries[key];
            if (source.Child is not null || source.Active || !ValueEquality<TOther>.Equals(source.Value, other.Background)) return false;
        }
        return true;
    }
    /// <summary>Retains values inside the box; replaces everything outside by inactive background.</summary>
    public void Clip(CoordBBox box)
    {
        InvalidateAccessors();
        if (box.IsEmpty) { Entries.Clear(); return; }
        var result = new VdbTree<T>(Background);
        foreach (var value in EnumerateValues(ValueIterationMode.All))
        {
            CoordBBox clipped = value.Bounds.Intersection(box);
            if (clipped.IsEmpty) continue;
            if (!value.IsActive && ValueEquality<T>.Equals(value.Value, Background)) continue;
            result.Fill(clipped, value.Value, value.IsActive);
        }
        result.Prune();
        Entries.Clear();
        foreach (var item in result.Entries) Entries.Add(item.Key, item.Value);
    }

    /// <summary>
    /// Sparse per-value combination. Replaces this tree and empties other after success,
    /// matching OpenVDB combine ownership semantics. A throwing callback leaves both inputs intact.
    /// The callback must not mutate either input. Implicit background always remains inactive.
    /// </summary>
    public void Combine(VdbTree<T> other, Func<VoxelState<T>, VoxelState<T>, VoxelState<T>> operation, bool prune = false)
    {
        ArgumentNullException.ThrowIfNull(other);
        ArgumentNullException.ThrowIfNull(operation);
        if (ReferenceEquals(this, other)) throw new ArgumentException("The input trees must be distinct.", nameof(other));
        var result = CombineCopy(this, other, operation, prune);
        InvalidateAccessors();
        Entries.Clear();
        Background = result.Background;
        foreach (var item in result.Entries) Entries.Add(item.Key, item.Value);
        other.Clear();
    }

    public void Combine(VdbTree<T> other, Func<T, T, T> operation, bool prune = false)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Combine(other, (a, b) => new VoxelState<T>(operation(a.Value, b.Value), a.IsActive || b.IsActive), prune);
    }

    public static VdbTree<T> CombineCopy(VdbTree<T> a, VdbTree<T> b,
        Func<VoxelState<T>, VoxelState<T>, VoxelState<T>> operation, bool prune = false)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(operation);
        var result = new VdbTree<T>();
        var keys = new HashSet<Coord>(a.Entries.Keys);
        keys.UnionWith(b.Entries.Keys);
        foreach (Coord key in keys.Order())
        {
            if (!a.Entries.TryGetValue(key, out RootEntry left)) left = new(a.Background, false);
            if (!b.Entries.TryGetValue(key, out RootEntry right)) right = new(b.Background, false);
            VoxelState<T> tileA = new(left.Value, left.Active), tileB = new(right.Value, right.Active);
            if (left.Child is null && right.Child is null)
            {
                VoxelState<T> combined = operation(tileA, tileB);
                result.Entries.Add(key, new(combined.Value, combined.IsActive));
            }
            else
            {
                var node = InternalNode<T>.CombineNodes(left.Child, tileA, right.Child, tileB, 2, key, operation);
                if (prune) node.Prune();
                result.Entries.Add(key, new(default, false, node));
            }
        }
        result.Background = operation(new(a.Background, false), new(b.Background, false)).Value;
        return result;
    }

    public void Prune()
    {
        InvalidateAccessors();
        foreach (Coord key in Entries.Keys.ToArray())
        {
            RootEntry entry = Entries[key];
            if (entry.Child is not null)
            {
                entry.Child.Prune();
                if (entry.Child.IsConstant(out T value, out bool active)) Entries[key] = entry = new(value, active);
            }
            if (entry.Child is null && !entry.Active && ValueEquality<T>.Equals(entry.Value, Background)) Entries.Remove(key);
        }
    }

    public void Clear() { InvalidateAccessors(); Entries.Clear(); }

    /// <summary>Changes only the implicit root background. Existing tiles and voxels retain their values.</summary>
    public void SetBackground(T value) { Background = value; InvalidateAccessors(); }

    public VdbTree<T> DeepCopy()
    {
        var result = new VdbTree<T>(Background);
        foreach ((Coord key, RootEntry entry) in Entries)
            result.Entries.Add(key, new(entry.Value, entry.Active, entry.Child?.DeepCopy()));
        return result;
    }

    public ValueAccessor<T> GetAccessor() => new(this);
    internal void InvalidateAccessors() => TopologyVersion = unchecked(TopologyVersion + 1);

    /// <summary>Enumerates stored values (tiles once), not every voxel represented by a tile.</summary>
    public IEnumerable<VoxelValue<T>> EnumerateValues(ValueIterationMode mode = ValueIterationMode.Active)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        foreach (Coord key in Entries.Keys.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z))
        {
            RootEntry entry = Entries[key];
            if (entry.Child is not null)
            {
                foreach (var value in entry.Child.EnumerateValues(mode)) yield return value;
            }
            else if (Matches(mode, entry.Active)) yield return new(NodeBounds(key, 12), entry.Value, entry.Active, 3);
        }
    }

    /// <summary>Expands active tiles; callers should bound enumeration of large volumes.</summary>
    public IEnumerable<(Coord Coordinate, T Value)> EnumerateActiveVoxels()
    {
        foreach (var value in EnumerateValues())
        foreach (Coord coordinate in value.Bounds) yield return (coordinate, value.Value);
    }

    public IEnumerable<LeafNode<T>> EnumerateLeaves()
    {
        foreach (Coord key in Entries.Keys.OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z))
            if (Entries[key].Child is { } child)
                foreach (var leaf in child.Leaves()) yield return leaf;
    }

    public bool TryGetActiveVoxelBoundingBox(out CoordBBox box)
    {
        box = CoordBBox.Empty;
        foreach (var value in EnumerateValues()) box = box.Expand(value.Bounds);
        return !box.IsEmpty;
    }
}
