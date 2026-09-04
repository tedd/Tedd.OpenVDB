// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Specialized point-index leaf storage from tools/PointIndexGrid.h, composed with the managed 5/4/3 tree.
using System.Collections.ObjectModel;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.Tools;

/// <summary>Immutable point-index leaf: 512 cumulative voxel offsets and an independently owned index array.</summary>
public sealed class PointIndexLeafNode
{
    internal readonly LeafNode<uint> OffsetLeaf;
    internal readonly uint[] IndexArray;
    internal PointIndexLeafNode(LeafNode<uint> leaf, uint[] indices) { OffsetLeaf = leaf; IndexArray = indices; }
    /// <summary>Creates a canonical point leaf by copying monotone prefix offsets and point indices.</summary>
    public PointIndexLeafNode(Coord origin, ReadOnlySpan<uint> offsets, ReadOnlySpan<uint> indices)
    {
        if (offsets.Length != 512) throw new ArgumentException("A point-index leaf has 512 prefix offsets.", nameof(offsets));
        OffsetLeaf = new(origin); IndexArray = indices.ToArray(); uint previous = 0;
        for (int i = 0; i < 512; ++i) { uint end = offsets[i]; if (end < previous || end > (uint)indices.Length) throw new ArgumentException("Prefix offsets must be monotone and within the index array.", nameof(offsets)); OffsetLeaf.SetValueOnly(i, end); OffsetLeaf.SetActiveState(i, end > previous); previous = end; }
        if (previous != (uint)indices.Length) throw new ArgumentException("The last prefix offset must equal the point-index count.", nameof(offsets));
    }
    public const int Log2Dim = 3, Dim = 8, Size = 512;
    public Coord Origin => OffsetLeaf.Origin;
    public CoordBBox Bounds => OffsetLeaf.Bounds;
    public ReadOnlySpan<uint> Offsets => OffsetLeaf.Values;
    public ReadOnlySpan<uint> Indices => IndexArray;
    public NodeMask ValueMask => OffsetLeaf.ValueMask;
    public int PointCount => IndexArray.Length;
    public int OnVoxelCount => OffsetLeaf.OnVoxelCount;
    public uint GetValue(int offset) => OffsetLeaf.GetValue(offset);
    public uint GetValue(Coord coordinate) => OffsetLeaf.GetValue(coordinate);
    public bool IsValueOn(int offset) => OffsetLeaf.IsValueOn(offset);
    public bool IsValueOn(Coord coordinate) => OffsetLeaf.IsValueOn(coordinate);
    public ReadOnlySpan<uint> GetIndices(Coord coordinate) => GetIndices(LeafNode<uint>.CoordToOffset(coordinate));
    public ReadOnlySpan<uint> GetIndices(int offset)
    { if (!OffsetLeaf.IsValueOn(offset)) return []; int start = offset == 0 ? 0 : checked((int)OffsetLeaf.GetValue(offset - 1)); return IndexArray.AsSpan(start, checked((int)OffsetLeaf.GetValue(offset)) - start); }
    public bool IsEmpty(CoordBBox box)
    { foreach (Coord c in Bounds.Intersection(box)) if (GetIndices(c).Length != 0) return false; return true; }
    public PointIndexLeafNode DeepCopy() => new(OffsetLeaf.DeepCopy(), (uint[])IndexArray.Clone());
    internal (int Start, int Count) Range(int offset) { int start = offset == 0 ? 0 : checked((int)OffsetLeaf.GetValue(offset - 1)); return (start, checked((int)OffsetLeaf.GetValue(offset)) - start); }
}

/// <summary>Read-only specialized point-index topology using OpenVDB's 5/4/3 hierarchy.</summary>
public sealed class PointIndexTree
{
    private readonly VdbTree<uint> _offsets;
    private readonly Dictionary<Coord, PointIndexLeafNode> _lookup;
    private readonly PointIndexLeafNode[] _spatial;
    private readonly ReadOnlyCollection<PointIndexLeafNode> _leaves;
    internal PointIndexTree(IEnumerable<PointIndexLeafNode> leaves)
    {
        _offsets = new(); _lookup = [];
        foreach (var leaf in leaves) { _lookup.Add(leaf.Origin, leaf); _offsets.AddLeaf(leaf.OffsetLeaf); }
        _leaves = Array.AsReadOnly(_offsets.EnumerateLeaves().Select(x => _lookup[x.Origin]).ToArray());
        _spatial = _leaves.OrderBy(x => x.Origin).ToArray();
    }
    public IReadOnlyList<PointIndexLeafNode> Leaves => _leaves;
    public int LeafCount => _leaves.Count;
    public int RootEntryCount => _offsets.Entries.Count;
    public ulong ActiveVoxelCount => _offsets.ActiveVoxelCount;
    public ulong PointCount => _leaves.Aggregate(0ul, (sum, x) => sum + (ulong)x.PointCount);
    public bool IsEmpty => _offsets.IsEmpty;
    public long[] NodeCount() => _offsets.NodeCount();
    public uint GetValue(Coord coordinate) => _offsets.GetValue(coordinate);
    public bool IsValueOn(Coord coordinate) => _offsets.IsValueOn(coordinate);
    public int GetValueDepth(Coord coordinate) => _offsets.GetValueDepth(coordinate);
    public PointIndexLeafNode? ProbeLeaf(Coord coordinate) => _lookup.GetValueOrDefault(coordinate.AlignDown(3));
    public IEnumerable<VoxelValue<uint>> EnumerateValues(ValueIterationMode mode = ValueIterationMode.Active) => _offsets.EnumerateValues(mode);
    public VdbTree<uint> CopyOffsetTree() => _offsets.DeepCopy();
    public PointIndexTree DeepCopy() => new(_leaves.Select(x => x.DeepCopy()));
    internal IEnumerable<PointIndexLeafNode> SearchLeaves(CoordBBox region, CancellationToken token)
    {
        if (region.IsEmpty) yield break; int minimum = region.Min.X & ~7, maximum = region.Max.X & ~7;
        int lo = 0, hi = _spatial.Length; while (lo < hi) { int mid = lo + (hi - lo) / 2; if (_spatial[mid].Origin.X < minimum) lo = mid + 1; else hi = mid; }
        for (int i = lo; i < _spatial.Length && _spatial[i].Origin.X <= maximum; ++i) { token.ThrowIfCancellationRequested(); var leaf = _spatial[i]; if (leaf.Bounds.Intersects(region)) yield return leaf; }
    }
}

/// <summary>Point-index grid with exclusive specialized-leaf ownership and a mutable native index/world transform.</summary>
/// <remarks>This specialized representation is not a scalar Grid&lt;uint&gt;. CopyOffsetGrid returns an independent diagnostic copy; standard volume-grid I/O does not encode the separate point-index arrays.</remarks>
public sealed class PointIndexGrid : IGrid
{
    private readonly Dictionary<string, MetadataValue> _metadata = [];
    private readonly ReadOnlyDictionary<string, MetadataValue> _metadataView;
    private Transform _transform;
    internal PointIndexGrid(PointIndexTree tree, Transform transform) { Tree = tree; _transform = transform; _metadataView = new(_metadata); }
    public PointIndexTree Tree { get; }
    public Transform Transform { get => _transform; set { ArgumentNullException.ThrowIfNull(value); _transform = value; } }
    public Type ValueType => typeof(uint);
    public IReadOnlyDictionary<string, MetadataValue> Metadata => _metadataView;
    public string Name { get => _metadata.TryGetValue(Grid<uint>.NameMetadataKey, out var value) ? value.ToString() : string.Empty; set { ArgumentNullException.ThrowIfNull(value); SetMetadata(Grid<uint>.NameMetadataKey, value); } }
    public GridClass GridClass { get => _metadata.TryGetValue(Grid<uint>.ClassMetadataKey, out var value) && value.Value is string text ? Grid<uint>.ParseGridClass(text) : GridClass.Unknown; set => SetMetadata(Grid<uint>.ClassMetadataKey, Grid<uint>.GridClassToString(value)); }
    public void SetMetadata(string name, object value) => SetMetadata(name, new MetadataValue(value));
    public void SetMetadata(string name, MetadataValue value) { ArgumentException.ThrowIfNullOrEmpty(name); ArgumentNullException.ThrowIfNull(value); _metadata[name] = value; }
    public bool RemoveMetadata(string name) => _metadata.Remove(name);
    public TValue GetMetadata<TValue>(string name) => _metadata[name].GetValue<TValue>();
    public bool TryGetMetadata<TValue>(string name, out TValue value) { if (_metadata.TryGetValue(name, out var item)) { try { value = item.GetValue<TValue>(); return true; } catch (InvalidCastException) { } } value = default!; return false; }
    public ulong ActiveVoxelCount => Tree.ActiveVoxelCount;
    public ulong PointCount => Tree.PointCount;
    public static PointIndexGrid Create(IPointSource points, double voxelSize = 1, CancellationToken cancellationToken = default) => Create(points, Transform.CreateLinearTransform(voxelSize), cancellationToken);
    public static PointIndexGrid Create(IPointSource points, Transform transform, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points); ArgumentNullException.ThrowIfNull(transform); cancellationToken.ThrowIfCancellationRequested(); Transform copy = transform.Copy();
        var partition = PointPartitioner.Create(points, copy, recordVoxelOffsets: true, cancellationToken: cancellationToken); var leaves = new PointIndexLeafNode[partition.Count];
        for (int bucket = 0; bucket < partition.Count; ++bucket)
        {
            cancellationToken.ThrowIfCancellationRequested(); ReadOnlySpan<int> indices = partition.Indices(bucket); uint[] histogram = new uint[512]; foreach (int index in indices) ++histogram[partition.VoxelOffsets[index]];
            var leaf = new LeafNode<uint>(partition.Origin(bucket)); uint count = 0;
            for (int offset = 0; offset < 512; ++offset) { uint n = histogram[offset]; if (n > 0) { histogram[offset] = count; count += n; leaf.SetActiveState(offset, true); } leaf.SetValueOnly(offset, count); }
            uint[] ordered = GC.AllocateUninitializedArray<uint>(indices.Length); foreach (int index in indices) ordered[histogram[partition.VoxelOffsets[index]]++] = (uint)index; leaves[bucket] = new(leaf, ordered);
        }
        cancellationToken.ThrowIfCancellationRequested(); return new(new(leaves), copy);
    }
    public bool IsValidPartition(IPointSource points, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points); cancellationToken.ThrowIfCancellationRequested(); if ((ulong)points.Count != PointCount) return false;
        foreach (var leaf in Tree.Leaves) for (int offset = 0; offset < 512; ++offset) if (leaf.IsValueOn(offset))
        { cancellationToken.ThrowIfCancellationRequested(); Coord coordinate = leaf.OffsetLeaf.OffsetToGlobalCoord(offset); foreach (uint index in leaf.GetIndices(offset)) { if (index >= (uint)points.Count) return false; Vec3d position = points.GetPosition((int)index); if (!position.IsFinite || coordinate != Transform.WorldToIndexCellCentered(position)) return false; } }
        return true;
    }
    public static PointIndexGrid GetValid(IPointSource points, PointIndexGrid grid, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(grid); return grid.IsValidPartition(points, cancellationToken) ? grid : Create(points, grid.Transform, cancellationToken); }
    public PointIndexGrid DeepCopy() { var copy = new PointIndexGrid(Tree.DeepCopy(), Transform.Copy()); foreach (var pair in _metadata) copy._metadata.Add(pair.Key, pair.Value.DeepCopy()); return copy; }
    public Grid<uint> CopyOffsetGrid() { var copy = new Grid<uint>(Tree.CopyOffsetTree()) { Transform = Transform.Copy() }; foreach (var pair in _metadata) copy.SetMetadata(pair.Key, pair.Value.DeepCopy()); return copy; }
    internal void RemapIndices(ReadOnlySpan<int> mapping) { foreach (var leaf in Tree.Leaves) for (int i = 0; i < leaf.IndexArray.Length; ++i) leaf.IndexArray[i] = checked((uint)mapping[(int)leaf.IndexArray[i]]); }
}
