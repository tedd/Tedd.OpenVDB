// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Restricted managed specialization of Tree4<ValueMask,5,4,3>.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB;

/// <summary>A topology grid with one bit per leaf voxel representing both value and activity.</summary>
/// <remarks>This exposes a restricted mask API, not all native MaskTree overloads. Internal/root tiles
/// retain native Boolean value/activity fields. Exporting a Boolean snapshot makes a deep copy.</remarks>
public sealed class MaskGrid : IGrid
{
    internal Grid<bool> Storage { get; }
    public MaskGrid(bool background = false) { Storage = new Grid<bool>(background); }
    internal MaskGrid(Grid<bool> storage) { Storage = storage; }
    public Type ValueType => typeof(bool);
    public string Name { get => Storage.Name; set => Storage.Name = value; }
    public Transform Transform { get => Storage.Transform; set => Storage.Transform = value; }
    public IReadOnlyDictionary<string, MetadataValue> Metadata => Storage.Metadata;
    public ulong ActiveVoxelCount => Storage.ActiveVoxelCount;
    public long LeafCount => Storage.Tree.LeafCount;
    public bool Background => Storage.Background;
    public bool IsEmpty => Storage.IsEmpty;
    public bool GetValue(Coord coordinate) => Storage.Tree.GetValue(coordinate);
    public bool IsValueOn(Coord coordinate) => Storage.Tree.IsValueOn(coordinate);
    public int GetValueDepth(Coord coordinate) => Storage.Tree.GetValueDepth(coordinate);
    public void SetMetadata(string name, object value) => Storage.SetMetadata(name, value);
    public void SetMetadata(string name, MetadataValue value) => Storage.SetMetadata(name, value);
    public bool RemoveMetadata(string name) => Storage.RemoveMetadata(name);
    public TValue GetMetadata<TValue>(string name) => Storage.GetMetadata<TValue>(name);
    public void SetValue(Coord coordinate, bool value)
    {
        var leaf = Storage.Tree.ProbeLeaf(coordinate);
        if (leaf is null)
        {
            leaf = Storage.Tree.TouchLeaf(coordinate);
            // Native LeafNode<ValueMask> merges the inherited tile value and active state.
            // A mask leaf stores that single bit as both its voxel value and activity.
            for (int index = 0; index < LeafNode<bool>.Size; ++index)
            {
                bool inherited = leaf.GetValue(index) || leaf.IsValueOn(index);
                if (inherited) leaf.SetValueOn(index, true); else leaf.SetValueOff(index, false);
            }
        }
        if (value) leaf.SetValueOn(coordinate, true); else leaf.SetValueOff(coordinate, false);
    }
    public void SetValueOn(Coord coordinate) => SetValue(coordinate, true);
    public void SetValueOff(Coord coordinate) => SetValue(coordinate, false);
    public void SetActiveState(Coord coordinate, bool active) => SetValue(coordinate, active);
    public void AddTile(int level, Coord coordinate, bool value)
    {
        if (level == 0) { SetValue(coordinate, value); return; }
        Storage.Tree.AddTile(level, coordinate, value, value);
    }
    public void Clear() => Storage.Tree.Clear();
    public void Prune() => Storage.Tree.Prune();
    public bool TryGetActiveVoxelBoundingBox(out CoordBBox box) => Storage.TryGetActiveVoxelBoundingBox(out box);
    public Grid<bool> ToBooleanGrid() => Storage.DeepCopy();
    public MaskGrid DeepCopy() => new(Storage.DeepCopy());
    /// <summary>Copies a Boolean grid whose leaf values already equal their activity; incompatible leaves are rejected.</summary>
    public static MaskGrid FromBooleanGrid(Grid<bool> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var view = new MaskGrid(source); view.ValidateLeafValues(); return view.DeepCopy();
    }
    internal void ValidateLeafValues()
    {
        foreach (var leaf in Storage.Tree.EnumerateLeaves())
            for (int index = 0; index < LeafNode<bool>.Size; ++index)
                if (leaf.GetValue(index) != leaf.IsValueOn(index)) throw new InvalidDataException("Mask leaf values must equal their activity bits.");
    }
}
