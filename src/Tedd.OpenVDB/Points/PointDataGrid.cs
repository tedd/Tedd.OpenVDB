// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of points/PointDataGrid.h and points/PointConversion.h.
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;

namespace Tedd.OpenVDB.Points;

/// <summary>Distinct signed 32-bit marker for OpenVDB's ptdataidx32 tree value type.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct PointDataIndex32(int Value)
{
    public static explicit operator PointDataIndex32(int value) => new(value);
    public static implicit operator int(PointDataIndex32 value) => value.Value;
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Selects point ranges by voxel activity.</summary>
public enum PointDataIndexMode { All, Active, Inactive }

/// <summary>A half-open range into a leaf's attribute arrays.</summary>
public readonly record struct PointDataRange(int Start, int End)
{
    public int Count => checked(End - Start);
    public bool IsEmpty => Start == End;
}

/// <summary>A specialized point-data leaf with cumulative point offsets and copy-on-write attributes.</summary>
/// <remarks>Offset mutation is separate from validation, as in OpenVDB. Validate manually assembled leaves before publication.</remarks>
public sealed class PointDataLeafNode
{
    internal readonly LeafNode<uint> PrefixLeaf;
    private AttributeSet _attributes;

    public const int Log2Dim = 3, Dim = 8, Size = 512;

    public PointDataLeafNode(Coord origin, bool active = false)
    {
        PrefixLeaf = new LeafNode<uint>(origin, 0, active);
        _attributes = new AttributeSet();
    }

    internal PointDataLeafNode(LeafNode<uint> prefixLeaf, AttributeSet attributes)
    {
        PrefixLeaf = prefixLeaf ?? throw new ArgumentNullException(nameof(prefixLeaf));
        _attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public Coord Origin => PrefixLeaf.Origin;
    public CoordBBox Bounds => PrefixLeaf.Bounds;
    public ReadOnlySpan<uint> Offsets => PrefixLeaf.Values;
    public NodeMask ValueMask => PrefixLeaf.ValueMask;
    public int OnVoxelCount => PrefixLeaf.OnVoxelCount;
    public int OffVoxelCount => PrefixLeaf.OffVoxelCount;
    public bool IsEmpty => PrefixLeaf.IsEmpty;
    public int AttributeCount => _attributes.Size;
    public AttributeDescriptor Descriptor => _attributes.Descriptor;
    public int PointCount => CheckedPointIndex(PrefixLeaf.GetValue(Size - 1));
    public long OnPointCount => CountPoints(true);
    public long OffPointCount => CountPoints(false);

    public uint GetValue(int offset) => PrefixLeaf.GetValue(offset);
    public uint GetValue(Coord coordinate) => PrefixLeaf.GetValue(coordinate);
    public bool IsValueOn(int offset) => PrefixLeaf.IsValueOn(offset);
    public bool IsValueOn(Coord coordinate) => PrefixLeaf.IsValueOn(coordinate);

    public PointDataRange GetPointRange(Coord coordinate) => GetPointRange(LeafNode<uint>.CoordToOffset(coordinate));

    public PointDataRange GetPointRange(int offset)
    {
        if ((uint)offset >= Size) throw new ArgumentOutOfRangeException(nameof(offset));
        int start = offset == 0 ? 0 : CheckedPointIndex(PrefixLeaf.GetValue(offset - 1));
        int end = CheckedPointIndex(PrefixLeaf.GetValue(offset));
        return new(start, end);
    }

    public int GetVoxelOffsetForPoint(int pointIndex)
    {
        if ((uint)pointIndex >= (uint)PointCount) throw new ArgumentOutOfRangeException(nameof(pointIndex));
        uint target = checked((uint)pointIndex + 1u);
        ReadOnlySpan<uint> offsets = PrefixLeaf.Values;
        int low = 0, high = Size;
        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            if (offsets[middle] < target) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    public IEnumerable<int> EnumerateIndices(PointDataIndexMode mode = PointDataIndexMode.All)
    {
        if (mode == PointDataIndexMode.All)
        {
            int count = PointCount;
            for (int index = 0; index < count; ++index) yield return index;
            yield break;
        }
        bool active = mode == PointDataIndexMode.Active;
        for (int offset = 0; offset < Size; ++offset)
        {
            if (PrefixLeaf.IsValueOn(offset) != active) continue;
            PointDataRange range = GetPointRange(offset);
            for (int index = range.Start; index < range.End; ++index) yield return index;
        }
    }

    public IEnumerable<int> EnumerateIndices(Coord coordinate)
    {
        PointDataRange range = GetPointRange(coordinate);
        for (int index = range.Start; index < range.End; ++index) yield return index;
    }

    public AttributeArray GetAttributeArray(int position) =>
        _attributes.Get(position) ?? throw new InvalidOperationException("The attribute array was removed unsafely.");
    public AttributeArray GetAttributeArray(string name) =>
        _attributes.Get(name) ?? throw new KeyNotFoundException($"Attribute '{name}' was not found.");
    public AttributeArray GetConstAttributeArray(int position) =>
        _attributes.GetConst(position) ?? throw new InvalidOperationException("The attribute array was removed unsafely.");
    public AttributeArray GetConstAttributeArray(string name) =>
        _attributes.GetConst(name) ?? throw new KeyNotFoundException($"Attribute '{name}' was not found.");
    public bool HasAttribute(int position) => (uint)position < (uint)_attributes.Size;
    public bool HasAttribute(string name) => _attributes.Find(name) != AttributeDescriptor.InvalidPosition;

    public void InitializeAttributes(AttributeDescriptor descriptor, int arrayLength, AttributeArrayRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayLength);
        if (descriptor.Size != 1 || descriptor.Find("P") == AttributeDescriptor.InvalidPosition ||
            descriptor.ValueType(0) != "vec3s")
            throw new ArgumentException("Initialization requires exactly one Vec3f position attribute named P.", nameof(descriptor));
        var replacement = new AttributeSet(descriptor, arrayLength, registry);
        AttributeSet previous = _attributes;
        _attributes = replacement;
        previous.Dispose();
    }

    public AttributeSet StealAttributeSet()
    {
        AttributeSet previous = _attributes;
        _attributes = new AttributeSet();
        return previous;
    }

    /// <summary>Transfers ownership of <paramref name="attributes"/> to this leaf.</summary>
    public void ReplaceAttributeSet(AttributeSet attributes, bool allowMismatchingDescriptors = false)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if (!allowMismatchingDescriptors && !_attributes.Descriptor.Equals(attributes.Descriptor))
            throw new InvalidOperationException("Attribute set descriptors are not equal.");
        AttributeSet previous = _attributes;
        _attributes = attributes;
        previous.Dispose();
    }

    public void ClearAttributes(bool updateValueMask = true)
    {
        var replacement = new AttributeSet(_attributes, 0);
        AttributeSet previous = _attributes;
        _attributes = replacement;
        previous.Dispose();
        for (int offset = 0; offset < Size; ++offset) PrefixLeaf.SetValueOnly(offset, 0);
        if (updateValueMask) SetValuesOff();
    }

    public AttributeArray AppendAttribute(AttributeDescriptor expected, AttributeDescriptor replacement, int position,
        int strideOrTotalSize = 1, bool constantStride = true, MetadataValue? defaultValue = null) =>
        _attributes.AppendAttribute(expected, replacement, position, strideOrTotalSize, constantStride, defaultValue);
    public void DropAttributes(IEnumerable<int> positions, AttributeDescriptor expected, AttributeDescriptor replacement) =>
        _attributes.DropAttributes(positions, expected, replacement);
    public void ReorderAttributes(AttributeDescriptor replacement) => _attributes.ReorderAttributes(replacement);
    public void RenameAttributes(AttributeDescriptor expected, AttributeDescriptor replacement) => _attributes.RenameAttributes(expected, replacement);
    public void ResetDescriptor(AttributeDescriptor replacement) => _attributes.ResetDescriptor(replacement);

    public void CompactAttributes()
    {
        for (int position = 0; position < _attributes.Size; ++position) GetAttributeArray(position).Compact();
    }

    public GroupHandle GroupHandle(GroupIndex index)
    {
        if (GetConstAttributeArray(index.AttributeIndex) is not TypedAttributeArray<byte, GroupCodec> array)
            throw new InvalidOperationException("The group index does not reference a group attribute.");
        return new GroupHandle(array, index.BitOffset);
    }
    public GroupHandle GroupHandle(string name) => GroupHandle(_attributes.GroupIndex(name));

    public GroupWriteHandle GroupWriteHandle(GroupIndex index)
    {
        if (GetAttributeArray(index.AttributeIndex) is not TypedAttributeArray<byte, GroupCodec> array)
            throw new InvalidOperationException("The group index does not reference a group attribute.");
        return new GroupWriteHandle(array, index.BitOffset);
    }
    public GroupWriteHandle GroupWriteHandle(string name) => GroupWriteHandle(_attributes.GroupIndex(name));

    public long GroupPointCount(string groupName)
    {
        if (!_attributes.Descriptor.HasGroup(groupName)) return 0;
        GroupHandle handle = GroupHandle(groupName);
        long count = 0;
        foreach (int index in EnumerateIndices()) if (handle.Get(index)) ++count;
        return count;
    }
    public void SetOffsets(ReadOnlySpan<uint> offsets, bool updateValueMask = true)
    {
        if (offsets.Length != Size) throw new ArgumentException("A point-data leaf requires 512 prefix offsets.", nameof(offsets));
        for (int offset = 0; offset < Size; ++offset)
            if (offsets[offset] > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(offsets), "PointDataIndex32 cannot exceed Int32.MaxValue.");
        for (int offset = 0; offset < Size; ++offset) PrefixLeaf.SetValueOnly(offset, offsets[offset]);
        if (updateValueMask) UpdateValueMask();
    }

    public void ValidateOffsets()
    {
        ReadOnlySpan<uint> offsets = PrefixLeaf.Values;
        for (int offset = 1; offset < Size; ++offset)
            if (offsets[offset - 1] > offsets[offset])
                throw new InvalidOperationException("Voxel offsets are not monotonically increasing.");
        for (int position = 1; position < _attributes.Size; ++position)
            if (GetConstAttributeArray(position - 1).Size != GetConstAttributeArray(position).Size)
                throw new InvalidOperationException("Attribute arrays have inconsistent lengths.");
        if (_attributes.Size != 0 && PointCount != GetConstAttributeArray(0).Size)
            throw new InvalidOperationException("The final voxel offset does not match the attribute-array length.");
    }

    public void UpdateValueMask()
    {
        uint start = 0;
        for (int offset = 0; offset < Size; ++offset)
        {
            uint end = PrefixLeaf.GetValue(offset);
            PrefixLeaf.SetActiveState(offset, end > start);
            start = end;
        }
    }

    public void SetOffsetOn(int offset, uint value)
    {
        ValidatePointIndex(value, nameof(value));
        PrefixLeaf.SetValueOnly(offset, value);
        PrefixLeaf.SetActiveState(offset, true);
    }
    public void SetOffsetOnly(int offset, uint value)
    {
        ValidatePointIndex(value, nameof(value));
        PrefixLeaf.SetValueOnly(offset, value);
    }
    public void SetActiveState(int offset, bool active) => PrefixLeaf.SetActiveState(offset, active);
    public void SetActiveState(Coord coordinate, bool active) => PrefixLeaf.SetActiveState(coordinate, active);
    public void SetValueOn(int offset) => PrefixLeaf.SetActiveState(offset, true);
    public void SetValueOn(Coord coordinate) => PrefixLeaf.SetActiveState(coordinate, true);
    public void SetValueOff(int offset) => PrefixLeaf.SetActiveState(offset, false);
    public void SetValueOff(Coord coordinate) => PrefixLeaf.SetActiveState(coordinate, false);
    public void SetValuesOn() { for (int offset = 0; offset < Size; ++offset) PrefixLeaf.SetActiveState(offset, true); }
    public void SetValuesOff() { for (int offset = 0; offset < Size; ++offset) PrefixLeaf.SetActiveState(offset, false); }

    public void Fill(CoordBBox box, uint value, bool active)
    {
        if (value != 0) throw new InvalidOperationException("Point-data prefix values cannot be filled with nonzero data.");
        foreach (Coord coordinate in Bounds.Intersection(box)) PrefixLeaf.SetActiveState(coordinate, active);
    }
    public void Fill(uint value, bool active)
    {
        if (value != 0) throw new InvalidOperationException("Point-data prefix values cannot be filled with nonzero data.");
        if (active) SetValuesOn(); else SetValuesOff();
    }

    public PointDataLeafNode DeepCopy() => new(PrefixLeaf.DeepCopy(), new AttributeSet(_attributes));
    public bool NativeEquals(PointDataLeafNode? other) =>
        other is not null && Origin == other.Origin && PrefixLeaf.Values.SequenceEqual(other.PrefixLeaf.Values) &&
        PrefixLeaf.ValueMask.Equals(other.PrefixLeaf.ValueMask) && _attributes.Equals(other._attributes);
    internal AttributeSet AttributeSetReference => _attributes;

    private long CountPoints(bool active)
    {
        if (active && PrefixLeaf.IsEmpty) return 0;
        if (!active && PrefixLeaf.OnVoxelCount == Size) return 0;
        if (active && PrefixLeaf.OnVoxelCount == Size) return PointCount;
        if (!active && PrefixLeaf.IsEmpty) return PointCount;
        long count = 0;
        for (int offset = 0; offset < Size; ++offset)
            if (PrefixLeaf.IsValueOn(offset) == active) count += GetPointRange(offset).Count;
        return count;
    }

    private static int CheckedPointIndex(uint value)
    {
        if (value > int.MaxValue) throw new InvalidOperationException("PointDataIndex32 exceeds Int32.MaxValue.");
        return (int)value;
    }
    private static void ValidatePointIndex(uint value, string parameterName)
    {
        if (value > int.MaxValue) throw new ArgumentOutOfRangeException(parameterName, "PointDataIndex32 cannot exceed Int32.MaxValue.");
    }
}

/// <summary>Specialized PointData topology with exclusive ownership of coupled prefix leaves and attributes.</summary>
public sealed partial class PointDataTree
{
    private sealed class TopologyState
    {
        internal readonly VdbTree<uint> PrefixTree;
        internal readonly Dictionary<Coord, PointDataLeafNode> Lookup;
        internal readonly ReadOnlyCollection<PointDataLeafNode> Leaves;

        internal TopologyState(IEnumerable<PointDataLeafNode> leaves)
        {
            ArgumentNullException.ThrowIfNull(leaves);
            PrefixTree = new VdbTree<uint>();
            Lookup = [];
            foreach (PointDataLeafNode leaf in leaves)
            {
                ArgumentNullException.ThrowIfNull(leaf);
                if (!Lookup.TryAdd(leaf.Origin, leaf))
                    throw new ArgumentException("Duplicate point-data leaf origin.", nameof(leaves));
                PrefixTree.AddLeaf(leaf.PrefixLeaf);
            }
            Leaves = Array.AsReadOnly(PrefixTree.EnumerateLeaves().Select(leaf => Lookup[leaf.Origin]).ToArray());
        }
    }

    private TopologyState _topology;
    private VdbTree<uint> _prefixTree => Volatile.Read(ref _topology).PrefixTree;
    private Dictionary<Coord, PointDataLeafNode> _lookup => Volatile.Read(ref _topology).Lookup;
    private ReadOnlyCollection<PointDataLeafNode> _leaves => Volatile.Read(ref _topology).Leaves;

    internal PointDataTree(IEnumerable<PointDataLeafNode> leaves)
    {
        _topology = new TopologyState(leaves);
    }

    /// <summary>Atomically publishes a fully validated replacement topology.</summary>
    internal void ReplaceLeaves(IEnumerable<PointDataLeafNode> leaves)
    {
        var replacement = new TopologyState(leaves);
        Interlocked.Exchange(ref _topology, replacement);
    }

    public IReadOnlyList<PointDataLeafNode> Leaves => _leaves;
    public int LeafCount => _leaves.Count;
    public int RootEntryCount => _prefixTree.RootEntryCount;
    public bool IsEmpty => _prefixTree.IsEmpty;
    public ulong ActiveVoxelCount => _prefixTree.ActiveVoxelCount;
    public ulong PointCount => _leaves.Aggregate(0ul, static (sum, leaf) => checked(sum + (ulong)leaf.PointCount));
    public ulong OnPointCount => _leaves.Aggregate(0ul, static (sum, leaf) => checked(sum + (ulong)leaf.OnPointCount));
    public ulong OffPointCount => _leaves.Aggregate(0ul, static (sum, leaf) => checked(sum + (ulong)leaf.OffPointCount));
    public AttributeDescriptor? Descriptor => _leaves.Count == 0 ? null : _leaves[0].Descriptor;
    public long[] NodeCount() => _prefixTree.NodeCount();
    public uint GetValue(Coord coordinate) => _prefixTree.GetValue(coordinate);
    public bool IsValueOn(Coord coordinate) => _prefixTree.IsValueOn(coordinate);
    public int GetValueDepth(Coord coordinate) => _prefixTree.GetValueDepth(coordinate);
    public PointDataLeafNode? ProbeLeaf(Coord coordinate) => _lookup.GetValueOrDefault(coordinate.AlignDown(3));
    public VdbTree<uint> CopyOffsetTree() => _prefixTree.DeepCopy();
    public PointDataTree DeepCopy() => new(_leaves.Select(static leaf => leaf.DeepCopy()));

    public AttributeDescriptor? MakeDescriptorUnique()
    {
        if (_leaves.Count == 0) return null;
        AttributeDescriptor first = _leaves[0].Descriptor;
        for (int index = 1; index < _leaves.Count; ++index)
            if (!first.Equals(_leaves[index].Descriptor))
                throw new InvalidOperationException("Point-data leaf descriptors do not match.");
        var replacement = new AttributeDescriptor(first);
        foreach (PointDataLeafNode leaf in _leaves) leaf.ResetDescriptor(replacement);
        return replacement;
    }

    public void CompactAttributes() { foreach (PointDataLeafNode leaf in _leaves) leaf.CompactAttributes(); }
    public void SetStreamingMode(bool enabled = true)
    {
        foreach (PointDataLeafNode leaf in _leaves)
            for (int position = 0; position < leaf.AttributeCount; ++position)
                leaf.GetAttributeArray(position).IsStreaming = enabled;
    }

    /// <summary>Touches eager managed attributes in native sequential prefetch order.</summary>
    public void Prefetch(bool position = true, bool otherAttributes = true)
    {
        if (_leaves.Count == 0) return;
        int positionIndex = _leaves[0].Descriptor.Find("P");
        if (position && positionIndex != AttributeDescriptor.InvalidPosition)
            foreach (PointDataLeafNode leaf in _leaves) _ = leaf.GetConstAttributeArray(positionIndex).GetStorageBytes().Length;
        if (!otherAttributes) return;
        for (int attribute = 0; attribute < _leaves[0].AttributeCount; ++attribute)
        {
            if (attribute == positionIndex) continue;
            foreach (PointDataLeafNode leaf in _leaves) _ = leaf.GetConstAttributeArray(attribute).GetStorageBytes().Length;
        }
    }
}
/// <summary>In-memory PointDataGrid composition matching OpenVDB's ptdataidx32 5/4/3 topology.</summary>
/// <remarks>OpenVDB paged multipass PointDataGrid archive framing is not supported yet.</remarks>
public sealed partial class PointDataGrid : IGrid
{
    private readonly Dictionary<string, MetadataValue> _metadata = new(StringComparer.Ordinal);
    private readonly ReadOnlyDictionary<string, MetadataValue> _metadataView;
    private Transform _transform;

    internal PointDataGrid(PointDataTree tree, Transform transform)
    {
        Tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _transform = transform ?? throw new ArgumentNullException(nameof(transform));
        _metadataView = new(_metadata);
    }

    public PointDataTree Tree { get; }
    public Type ValueType => typeof(PointDataIndex32);
    public string NativeTreeType => "Tree_ptdataidx32_5_4_3";
    public bool IsEmpty => Tree.IsEmpty;
    public ulong ActiveVoxelCount => Tree.ActiveVoxelCount;
    public ulong PointCount => Tree.PointCount;
    public ulong OnPointCount => Tree.OnPointCount;
    public ulong OffPointCount => Tree.OffPointCount;
    public IReadOnlyDictionary<string, MetadataValue> Metadata => _metadataView;
    public Transform Transform { get => _transform; set { ArgumentNullException.ThrowIfNull(value); _transform = value; } }
    public string Name
    {
        get => _metadata.TryGetValue(Grid<int>.NameMetadataKey, out MetadataValue? value) ? value.ToString() : string.Empty;
        set { ArgumentNullException.ThrowIfNull(value); SetMetadata(Grid<int>.NameMetadataKey, value); }
    }
    public GridClass GridClass
    {
        get => _metadata.TryGetValue(Grid<int>.ClassMetadataKey, out MetadataValue? value) && value.Value is string text
            ? Grid<int>.ParseGridClass(text) : GridClass.Unknown;
        set => SetMetadata(Grid<int>.ClassMetadataKey, Grid<int>.GridClassToString(value));
    }
    public void SetMetadata(string name, object value) => SetMetadata(name, new MetadataValue(value));
    public void SetMetadata(string name, MetadataValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        _metadata[name] = value;
    }
    public bool RemoveMetadata(string name) => _metadata.Remove(name);
    public TValue GetMetadata<TValue>(string name) => _metadata[name].GetValue<TValue>();
    public bool TryGetMetadata<TValue>(string name, out TValue value)
    {
        if (_metadata.TryGetValue(name, out MetadataValue? item))
        {
            try { value = item.GetValue<TValue>(); return true; } catch (InvalidCastException) { }
        }
        value = default!;
        return false;
    }

    public static PointDataGrid Create(IPointSource positions, double voxelSize = 1,
        MetadataValue? positionDefaultValue = null, CancellationToken cancellationToken = default) =>
        Create(positions, Transform.CreateLinearTransform(voxelSize), positionDefaultValue, cancellationToken);
    public static PointDataGrid Create(IPointSource positions, Transform transform,
        MetadataValue? positionDefaultValue = null, CancellationToken cancellationToken = default) =>
        Create<NullCodec<Vec3f>>(positions, transform, positionDefaultValue, cancellationToken);
    public static PointDataGrid Create<TCodec>(IPointSource positions, Transform transform,
        MetadataValue? positionDefaultValue = null, CancellationToken cancellationToken = default)
        where TCodec : unmanaged, IAttributeCodec<Vec3f>
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(transform);
        PointIndexGrid pointIndexGrid = PointIndexGrid.Create(positions, transform, cancellationToken);
        return Create<TCodec>(pointIndexGrid, positions, transform, positionDefaultValue, cancellationToken);
    }

    public static PointDataGrid Create<TCodec>(PointIndexGrid pointIndexGrid, IPointSource positions, Transform transform,
        MetadataValue? positionDefaultValue = null, CancellationToken cancellationToken = default)
        where TCodec : unmanaged, IAttributeCodec<Vec3f>
    {
        ArgumentNullException.ThrowIfNull(pointIndexGrid);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(transform);
        cancellationToken.ThrowIfCancellationRequested();
        if (positions.Count < 0)
            throw new ArgumentException("The position source reported a negative count.", nameof(positions));

        var registry = AttributeArrayRegistry.CreateDefault();
        var positionType = new AttributeType("vec3s", TCodec.Name);
        if (!registry.IsRegistered(positionType)) registry.Register<Vec3f, TCodec>();
        AttributeDescriptor descriptor = AttributeDescriptor.Create(positionType);
        if (positionDefaultValue is not null) descriptor.SetDefaultValue("P", positionDefaultValue);

        IReadOnlyList<PointIndexLeafNode> sourceLeaves = pointIndexGrid.Tree.Leaves;
        var leaves = new PointDataLeafNode[sourceLeaves.Count];
        for (int leafIndex = 0; leafIndex < sourceLeaves.Count; ++leafIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PointIndexLeafNode sourceLeaf = sourceLeaves[leafIndex];
            var attributes = new AttributeSet(descriptor, sourceLeaf.PointCount, registry);
            var positionArray = attributes.Get(0) as TypedAttributeArray<Vec3f, TCodec>
                ?? throw new InvalidOperationException("The P attribute registry returned an incompatible array.");
            positionArray.Expand();
            int targetIndex = 0;
            for (int offset = 0; offset < PointDataLeafNode.Size; ++offset)
            {
                (int start, int count) = sourceLeaf.Range(offset);
                if (count == 0) continue;
                Vec3d center = sourceLeaf.OffsetLeaf.OffsetToGlobalCoord(offset).AsVec3d();
                int end = checked(start + count);
                for (int index = start; index < end; ++index)
                {
                    uint sourceIndex = sourceLeaf.IndexArray[index];
                    if (sourceIndex >= (uint)positions.Count)
                        throw new ArgumentException("The point-index grid references a position outside the source.", nameof(pointIndexGrid));
                    Vec3d local = transform.WorldToIndex(positions.GetPosition((int)sourceIndex)) - center;
                    positionArray.SetUnsafe(targetIndex++, new Vec3f((float)local.X, (float)local.Y, (float)local.Z));
                }
            }
            if (targetIndex != sourceLeaf.PointCount)
                throw new InvalidOperationException("The point-index topology contains points in inactive voxels.");
            leaves[leafIndex] = new PointDataLeafNode(sourceLeaf.OffsetLeaf.DeepCopy(), attributes);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new PointDataGrid(new PointDataTree(leaves), transform.Copy());
    }

    public Vec3d GetPosition(PointDataLeafNode leaf, int pointIndex)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        int voxelOffset = leaf.GetVoxelOffsetForPoint(pointIndex);
        Coord voxel = leaf.PrefixLeaf.OffsetToGlobalCoord(voxelOffset);
        if (leaf.GetConstAttributeArray("P") is not AttributeArray<Vec3f> positions)
            throw new InvalidOperationException("The P attribute is not a Vec3f array.");
        Vec3f local = positions.Get(pointIndex);
        return Transform.IndexToWorld(voxel.AsVec3d() + new Vec3d(local.X, local.Y, local.Z));
    }

    public IEnumerable<Vec3d> EnumeratePositions(PointDataIndexMode mode = PointDataIndexMode.All)
    {
        foreach (PointDataLeafNode leaf in Tree.Leaves)
            foreach (int pointIndex in leaf.EnumerateIndices(mode))
                yield return GetPosition(leaf, pointIndex);
    }

    public PointDataGrid DeepCopy()
    {
        var copy = new PointDataGrid(Tree.DeepCopy(), Transform.Copy());
        foreach (var pair in _metadata) copy._metadata.Add(pair.Key, pair.Value.DeepCopy());
        return copy;
    }
    public Grid<uint> CopyOffsetGrid()
    {
        var copy = new Grid<uint>(Tree.CopyOffsetTree()) { Transform = Transform.Copy() };
        foreach (var pair in _metadata) copy.SetMetadata(pair.Key, pair.Value.DeepCopy());
        return copy;
    }
}
