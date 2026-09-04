// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.Tree;

/// <summary>
/// A leaf-caching accessor. Keep one per thread; never mutate a tree concurrently.
/// Tile replacement, pruning and clearing invalidate cached nodes automatically.
/// The cache holds a managed object, never a raw pointer across a GC safepoint.
/// </summary>
public sealed class ValueAccessor<T> where T : unmanaged
{
    private readonly VdbTree<T> _tree;
    private LeafNode<T>? _leaf;
    private long _version;
    public ValueAccessor(VdbTree<T> tree) => _tree = tree ?? throw new ArgumentNullException(nameof(tree));
    public VdbTree<T> Tree => _tree;
    public void Clear() { _leaf = null; _version = _tree.TopologyVersion; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private LeafNode<T>? Cached(Coord coordinate)
    {
        var leaf = _leaf;
        if (_version != _tree.TopologyVersion) { Clear(); return null; }
        return leaf is not null && leaf.Origin == VdbTree<T>.Align(coordinate, 3) ? leaf : null;
    }

    public bool ProbeValue(Coord coordinate, out T value)
    {
        var leaf = Cached(coordinate);
        if (leaf is not null) return leaf.ProbeValue(coordinate, out value);
        bool active = _tree.Probe(coordinate, out value, out _leaf, out _);
        _version = _tree.TopologyVersion;
        return active;
    }
    public T GetValue(Coord coordinate) { ProbeValue(coordinate, out T value); return value; }
    public bool IsValueOn(Coord coordinate) => ProbeValue(coordinate, out _);
    public T this[Coord coordinate] { get => GetValue(coordinate); set => SetValueOn(coordinate, value); }
    public void SetValueOn(Coord coordinate, T value)
    {
        var leaf = Cached(coordinate);
        if (leaf is not null) leaf.SetValueOn(coordinate, value);
        else { _tree.SetValueOn(coordinate, value); Cache(coordinate); }
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
        _tree.Probe(coordinate, out _, out _leaf, out _);
        _version = _tree.TopologyVersion;
    }
}
