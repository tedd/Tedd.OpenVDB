// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tree;

/// <summary>A live reference to a root, internal node or leaf, with subtree-scoped operations.</summary>
/// <remarks>Node references and direct iterators survive edits in sibling subtrees. Direct iterators and their value handles invalidate when this node is detached or its direct child table changes. Descendant edits that preserve that table remain valid. Root iterators retain tree-wide invalidation. This does not permit concurrent edits within the same node.</remarks>
public readonly struct TreeNode<T> where T : unmanaged
{
    private readonly VdbTree<T>? _tree;
    private readonly InternalNode<T>? _node;
    private readonly LeafNode<T>? _leaf;
    internal TreeNode(VdbTree<T> tree, InternalNode<T>? node = null, LeafNode<T>? leaf = null)
    { _tree = tree; _node = node; _leaf = leaf; Level = leaf is not null ? 0 : node?.Level ?? 3; }
    public int Level { get; }
    public int Depth => 3 - Level;
    public bool IsRoot => Level == 3;
    public bool IsLeaf => Level == 0;
    public bool IsValid => _tree is not null && (IsRoot || _tree!.ContainsNode(_node, _leaf));
    public Coord Origin { get { Validate(); return _leaf?.Origin ?? _node?.Origin ?? Coord.Zero; } }
    public CoordBBox Bounds { get { Validate(); return _leaf?.Bounds ?? (_node is null ? CoordBBox.Infinite : VdbTree<T>.NodeBounds(_node.Origin, _node.TotalLog2Dim)); } }
    public int ChildCount { get { Validate(); return _leaf is not null ? 0 : _node?.Children.Count ?? _tree!.Entries.Values.Count(entry => entry.Child is not null); } }
    public int TableSize { get { Validate(); return _leaf is not null ? LeafNode<T>.Size : _node?.Values.Length ?? _tree!.Entries.Count; } }
    public int ValueCount => TableSize - ChildCount;
    public int ActiveValueCount { get { Validate(); return _leaf?.OnVoxelCount ?? _node?.Active.CountOn() ?? _tree!.Entries.Values.Count(entry => entry.Child is null && entry.Active); } }
    public LeafNode<T>? Leaf { get { Validate(); return _leaf; } }
    public T GetValue(Coord coordinate) { ValidateCoordinate(coordinate); return _tree!.GetValue(coordinate); }
    public bool IsValueOn(Coord coordinate) { ValidateCoordinate(coordinate); return _tree!.IsValueOn(coordinate); }

    /// <summary>Materializes a leaf strictly within this node's subtree.</summary>
    public LeafNode<T> TouchLeaf(Coord coordinate)
    {
        ValidateCoordinate(coordinate);
        if (_leaf is not null) return _leaf;
        if (_node is null) return _tree!.TouchLeaf(coordinate);
        var existing = _tree!.ProbeLeaf(coordinate);
        if (existing is not null) return existing;
        _tree!.InvalidateAccessors();
        return _node.TouchLeaf(coordinate);
    }
    /// <summary>Replaces a descendant tile. The level cannot exceed this node's level.</summary>
    public void AddTile(int level, Coord coordinate, T value, bool active)
    {
        ValidateCoordinate(coordinate);
        if ((uint)level > (uint)Level) throw new ArgumentOutOfRangeException(nameof(level));
        if (_leaf is not null) { if (active) _leaf.SetValueOn(coordinate, value); else _leaf.SetValueOff(coordinate, value); }
        else if (_node is null) _tree!.AddTile(level, coordinate, value, active);
        else { _tree!.InvalidateAccessors(); _node.AddTile(level, coordinate, value, active); }
    }
    /// <summary>Enumerates direct children in native table order, without visiting descendants.</summary>
    public IEnumerable<TreeNode<T>> EnumerateChildren()
    {
        Validate(); long version = DirectVersion;
        if (_node is not null)
        {
            foreach (int offset in _node.Children.Keys.Order())
            {
                CheckVersion(version); object child = _node.Children[offset];
                yield return child is InternalNode<T> inner ? new(_tree!, inner) : new(_tree!, leaf: (LeafNode<T>)child);
            }
        }
        else if (_leaf is null)
        {
            foreach (Coord key in _tree!.Entries.Keys.Order())
            { CheckVersion(version); if (_tree!.Entries[key].Child is { } child) yield return new(_tree!, child); }
        }
        CheckVersion(version);
    }
    /// <summary>Enumerates values owned directly by this node; child slots are omitted.</summary>
    public IEnumerable<TreeValue<T>> EnumerateValues(ValueIterationMode mode = ValueIterationMode.Active)
    {
        Validate(); if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        long version = DirectVersion;
        if (_leaf is not null)
        {
            for (int offset = 0; offset < LeafNode<T>.Size; offset++)
            { CheckVersion(version); if (Matches(mode, _leaf.IsValueOn(offset))) yield return new(_tree!, _leaf.OffsetToGlobalCoord(offset), null, _leaf, offset, 0, nodeLocal: true); }
        }
        else if (_node is not null)
        {
            for (int offset = 0; offset < _node.Values.Length; offset++)
            { CheckVersion(version); if (!_node.Children.ContainsKey(offset) && Matches(mode, _node.Active.IsOn(offset))) yield return new(_tree!, _node.ChildOrigin(offset), _node, null, offset, Level, nodeLocal: true); }
        }
        else
        {
            foreach (Coord key in _tree!.Entries.Keys.Order())
            { CheckVersion(version); var entry = _tree!.Entries[key]; if (entry.Child is null && Matches(mode, entry.Active)) yield return new(_tree!, key, null, null, 0, 3); }
        }
        CheckVersion(version);
    }
    private static bool Matches(ValueIterationMode mode, bool active) => mode == ValueIterationMode.All || (mode == ValueIterationMode.Active) == active;
    private long DirectVersion => _node?.DirectTopologyVersion ?? (_leaf is null ? _tree!.TopologyVersion : 0);
    private void CheckVersion(long version)
    {
        Validate();
        if (DirectVersion != version) throw new InvalidOperationException("The node's direct topology changed during enumeration.");
    }
    private void ValidateCoordinate(Coord coordinate) { Validate(); if (!Bounds.Contains(coordinate)) throw new ArgumentOutOfRangeException(nameof(coordinate), "Coordinate is outside this node."); }
    private void Validate() { if (!IsValid) throw new InvalidOperationException("The node reference is uninitialized or detached from its tree."); }
}

public sealed partial class VdbTree<T> where T : unmanaged
{
    public TreeNode<T> Root => new(this);
    /// <summary>Finds an allocated node at the requested level; tiles do not count as nodes.</summary>
    public TreeNode<T>? ProbeNode(Coord coordinate, int level)
    {
        if ((uint)level > 3) throw new ArgumentOutOfRangeException(nameof(level));
        if (level == 3) return Root;
        if (!Entries.TryGetValue(Align(coordinate, 12), out var entry) || entry.Child is null) return null;
        var node = entry.Child;
        while (node.Level > level)
        {
            if (!node.Children.TryGetValue(node.Offset(coordinate), out var child)) return null;
            if (child is LeafNode<T> leaf) return level == 0 ? new(this, leaf: leaf) : null;
            node = (InternalNode<T>)child;
        }
        return new(this, node);
    }
    internal bool ContainsNode(InternalNode<T>? expected, LeafNode<T>? leaf)
    {
        Coord origin = expected?.Origin ?? leaf!.Origin;
        if (!Entries.TryGetValue(Align(origin, 12), out var entry) || entry.Child is null) return false;
        var node = entry.Child;
        if (expected?.Level == 2) return ReferenceEquals(node, expected);
        if (!node.Children.TryGetValue(node.Offset(origin), out var child)) return false;
        if (expected is not null) return ReferenceEquals(child, expected);
        node = (InternalNode<T>)child;
        return node.Children.TryGetValue(node.Offset(origin), out child) && ReferenceEquals(child, leaf);
    }
    /// <summary>Depth-first pre-order traversal of allocated nodes, including the root by default.</summary>
    public IEnumerable<TreeNode<T>> EnumerateNodes(int minDepth = 0, int maxDepth = 3)
    {
        if ((uint)minDepth > 3) throw new ArgumentOutOfRangeException(nameof(minDepth));
        if (maxDepth < minDepth || maxDepth > 3) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        long version = TopologyVersion;
        foreach (var node in Visit(Root)) yield return node;
        IEnumerable<TreeNode<T>> Visit(TreeNode<T> node)
        {
            if (version != TopologyVersion) throw new InvalidOperationException("Tree topology changed during node traversal.");
            if (node.Depth >= minDepth) yield return node;
            if (node.Depth < maxDepth) foreach (var child in node.EnumerateChildren()) foreach (var descendant in Visit(child)) yield return descendant;
            if (version != TopologyVersion) throw new InvalidOperationException("Tree topology changed during node traversal.");
        }
    }
}
