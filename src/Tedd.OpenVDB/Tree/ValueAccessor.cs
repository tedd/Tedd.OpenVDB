// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Util;

namespace Tedd.OpenVDB.Tree;

/// <summary>
/// A leaf-caching accessor. Keep one per thread; never mutate a tree concurrently.
/// Tile replacement, pruning and clearing invalidate cached nodes automatically.
/// The caches hold managed references, never raw pointers across GC safepoints.
/// Call Clear to release cached references immediately when the accessor will be idle.
/// </summary>
public sealed class ValueAccessor<T> where T : unmanaged
{
    private readonly VdbTree<T> _tree;
    private LeafNode<T>? _leaf;
    [InlineArray(64)]
    private struct LeafCache
    {
        private LeafNode<T>? _first;
    }
    private LeafCache _leaves;
    private long _version;
    public ValueAccessor(VdbTree<T> tree) => _tree = tree ?? throw new ArgumentNullException(nameof(tree));
    public VdbTree<T> Tree => _tree;
    public void Clear()
    {
        _leaf = null;
        _leaves = default;
        _version = _tree.TopologyVersion;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LeafNode<T>? Cached(Coord coordinate)
    {
        if (_version != _tree.TopologyVersion) { Clear(); return null; }
        var leaf = _leaf;
        if (leaf is null) return null;
        var origin = leaf.Origin;
        return (((coordinate.X ^ origin.X) | (coordinate.Y ^ origin.Y) | (coordinate.Z ^ origin.Z)) & -8) == 0
            ? leaf : null;
    }

    public bool ProbeValue(Coord coordinate, out T value)
    {
        var leaf = Cached(coordinate) ?? CachedSecondary(coordinate);
        if (leaf is not null) return leaf.ProbeValue(coordinate, out value);
        return Resolve(coordinate, out value);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetValue(Coord coordinate)
    {
        var leaf = Cached(coordinate);
        return leaf is not null ? leaf.GetValue(coordinate) : GetValueUncached(coordinate);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private T GetValueUncached(Coord coordinate)
    {
        var leaf = CachedSecondary(coordinate);
        if (leaf is not null) return leaf.GetValue(coordinate);
        Resolve(coordinate, out T value);
        return value;
    }
    public bool IsValueOn(Coord coordinate) => ProbeValue(coordinate, out _);
    public T this[Coord coordinate] { get => GetValue(coordinate); set => SetValueOn(coordinate, value); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetValueOn(Coord coordinate, T value)
    {
        var leaf = Cached(coordinate);
        if (leaf is not null) leaf.SetValueOn(coordinate, value);
        else SetValueOnUncached(coordinate, value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void SetValueOnUncached(Coord coordinate, T value)
    {
        var leaf = CachedSecondary(coordinate);
        if (leaf is not null) { leaf.SetValueOn(coordinate, value); return; }
        bool active = Resolve(coordinate, out T previous);
        leaf = _leaf;
        if (leaf is null)
        {
            if (active && ValueEquality<T>.Equals(previous, value)) return;
            // Equality may invoke user code and replace topology. TouchLeaf itself
            // only adds nodes, so references valid before it remain valid afterward.
            if (_version != _tree.TopologyVersion) Clear();
            leaf = _tree.TouchLeaf(coordinate);
            _leaf = leaf;
            _leaves[CacheSlot(coordinate)] = leaf;
            _version = _tree.TopologyVersion;
        }
        leaf.SetValueOn(coordinate, value);
    }
    public void SetValueOff(Coord coordinate, T value)
    {
        var leaf = Cached(coordinate);
        if (leaf is not null) leaf.SetValueOff(coordinate, value);
        else { _tree.SetValueOff(coordinate, value); Cache(coordinate); }
    }
    public void SetValueOnly(Coord coordinate, T value)
    {
        var leaf = Cached(coordinate);
        if (leaf is not null) leaf.SetValueOnly(coordinate, value);
        else { _tree.SetValueOnly(coordinate, value); Cache(coordinate); }
    }
    public void SetActiveState(Coord coordinate, bool active)
    {
        var leaf = Cached(coordinate);
        if (leaf is not null) leaf.SetActiveState(coordinate, active);
        else { _tree.SetActiveState(coordinate, active); Cache(coordinate); }
    }
    public void SetValueOn(Coord coordinate) => SetActiveState(coordinate, true);
    public void SetValueOff(Coord coordinate) => SetActiveState(coordinate, false);
    private void Cache(Coord coordinate)
    {
        if (_version != _tree.TopologyVersion) Clear();
        if (CachedSecondary(coordinate) is null) Resolve(coordinate, out _);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LeafNode<T>? CachedSecondary(Coord coordinate)
    {
        var leaf = _leaves[CacheSlot(coordinate)];
        if (leaf is null || leaf.Origin != VdbTree<T>.Align(coordinate, 3)) return null;
        _leaf = leaf;
        return leaf;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CacheSlot(Coord coordinate) =>
        ((coordinate.X >> 3) & 3) << 4 | ((coordinate.Y >> 3) & 3) << 2 | ((coordinate.Z >> 3) & 3);

    // Full origin checks reject slot collisions. A topology-version change clears
    // every cached leaf before reuse; no dictionary slot or raw address escapes.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool Resolve(Coord coordinate, out T value)
    {
        bool active = _tree.Probe(coordinate, out value, out _leaf, out _);
        if (_leaf is { } leaf) _leaves[CacheSlot(coordinate)] = leaf;
        return active;
    }
}
