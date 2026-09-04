// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed value traversal corresponding to tree/TreeIterator.h.
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tree;

/// <summary>A live mutable reference to one stored tile or voxel.</summary>
/// <remarks>Tree-wide traversal handles invalidate on any structural tree change. Handles from TreeNode direct traversal invalidate only on node detachment or changes to that node's direct child table. Value and activity edits preserve topology.</remarks>
public readonly struct TreeValue<T> where T : unmanaged
{
    private readonly VdbTree<T>? _tree;
    private readonly InternalNode<T>? _node;
    private readonly LeafNode<T>? _leaf;
    private readonly Coord _coordinate;
    private readonly int _offset;
    private readonly long _version;
    private readonly bool _nodeLocal;
    internal TreeValue(VdbTree<T> tree, Coord coordinate, InternalNode<T>? node, LeafNode<T>? leaf, int offset, int level, bool nodeLocal = false)
    {
        _tree = tree; _coordinate = coordinate; _node = node; _leaf = leaf; _offset = offset; Level = level;
        _nodeLocal = nodeLocal && level < 3;
        _version = _nodeLocal ? node?.DirectTopologyVersion ?? 0 : tree.TopologyVersion;
    }
    public int Level { get; }
    public int Depth => 3 - Level;
    public bool IsVoxel => Level == 0;
    public bool IsValid => _tree is not null && (_nodeLocal
        ? (_node is null || _version == _node.DirectTopologyVersion) && _tree.ContainsNode(_node, _leaf)
        : _version == _tree.TopologyVersion);
    public Coord Coordinate { get { Validate(); return _coordinate; } }
    public CoordBBox Bounds { get { Validate(); return VdbTree<T>.NodeBounds(_coordinate, Level switch { 0 => 0, 1 => 3, 2 => 7, _ => 12 }); } }
    public ulong VoxelCount => 1UL << (Level switch { 0 => 0, 1 => 9, 2 => 21, _ => 36 });
    public T Value
    {
        get { Validate(); return _leaf is not null ? _leaf.GetValue(_offset) : _node is not null ? _node.Values[_offset] : _tree!.Entries[_coordinate].Value; }
        set => SetValue(value);
    }
    public bool IsActive
    {
        get { Validate(); return _leaf is not null ? _leaf.IsValueOn(_offset) : _node is not null ? _node.Active.IsOn(_offset) : _tree!.Entries[_coordinate].Active; }
        set => SetActiveState(value);
    }
    /// <summary>Sets the value while preserving activity, matching the actual native iterator implementation.</summary>
    public void SetValue(T value) => SetValueOnly(value);
    public void SetValueOn(T value)
    {
        Validate();
        if (_leaf is not null) _leaf.SetValueOn(_offset, value);
        else if (_node is not null) { _node.Values[_offset] = value; _node.Active.SetOn(_offset); }
        else _tree!.Entries[_coordinate] = new(value, true);
    }
    /// <summary>Changes only the stored value, without changing activity.</summary>
    public void SetValueOnly(T value)
    {
        Validate();
        if (_leaf is not null) _leaf.SetValueOnly(_offset, value);
        else if (_node is not null) _node.Values[_offset] = value;
        else { var entry = _tree!.Entries[_coordinate]; _tree.Entries[_coordinate] = new(value, entry.Active); }
    }
    public void SetActiveState(bool active)
    {
        Validate();
        if (_leaf is not null) _leaf.SetActiveState(_offset, active);
        else if (_node is not null) _node.Active.Set(_offset, active);
        else { var entry = _tree!.Entries[_coordinate]; _tree.Entries[_coordinate] = new(entry.Value, active); }
    }
    public void SetValueOff() => SetActiveState(false);
    public void ModifyValue(Func<T, T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation); T result = operation(Value); if (IsVoxel) SetValueOn(result); else SetValueOnly(result);
    }
    public VoxelValue<T> Snapshot() => new(Bounds, Value, IsActive, Level);
    private void Validate() { if (!IsValid) throw new InvalidOperationException("The value handle is uninitialized, detached, or its traversal topology changed."); }
}

public sealed partial class VdbTree<T> where T : unmanaged
{
    /// <summary>Traverses mutable stored values without expanding tiles. Depth zero is the root, three is a leaf.</summary>
    public IEnumerable<TreeValue<T>> EnumerateValueHandles(ValueIterationMode mode = ValueIterationMode.Active, int minDepth = 0, int maxDepth = 3)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (minDepth < 0 || minDepth > 3) throw new ArgumentOutOfRangeException(nameof(minDepth));
        if (maxDepth < minDepth || maxDepth > 3) throw new ArgumentOutOfRangeException(nameof(maxDepth));
        long version = TopologyVersion;
        foreach (Coord key in Entries.Keys.Order())
        {
            ValidateVersion(); var entry = Entries[key];
            if (entry.Child is null)
            {
                if (minDepth == 0 && Matches(mode, entry.Active)) yield return new(this, key, null, null, 0, 3);
            }
            else if (maxDepth > 0) foreach (var item in Visit(entry.Child)) yield return item;
        }
        ValidateVersion();
        IEnumerable<TreeValue<T>> Visit(InternalNode<T> node)
        {
            int depth = 3 - node.Level;
            for (int i = 0; i < node.Values.Length; i++)
            {
                ValidateVersion();
                if (node.Children.TryGetValue(i, out var child))
                {
                    if (depth == maxDepth) continue;
                    if (child is InternalNode<T> inner) { foreach (var item in Visit(inner)) yield return item; }
                    else
                    {
                        var leaf = (LeafNode<T>)child;
                        for (int offset = 0; offset < LeafNode<T>.Size; offset++)
                        { ValidateVersion(); if (Matches(mode, leaf.IsValueOn(offset))) yield return new(this, leaf.OffsetToGlobalCoord(offset), null, leaf, offset, 0); }
                    }
                }
                else if (depth >= minDepth && Matches(mode, node.Active.IsOn(i))) yield return new(this, node.ChildOrigin(i), node, null, i, node.Level);
            }
        }
        void ValidateVersion() { if (version != TopologyVersion) throw new InvalidOperationException("Tree topology changed during traversal."); }
    }
}
