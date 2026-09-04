// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of points/AttributeSet.h and points/AttributeSet.cc.
namespace Tedd.OpenVDB.Points;

public sealed record AttributeSetReadOptions
{
    public int MaximumAttributes { get; init; } = 1_000_000;
    public int MaximumNameBytes { get; init; } = 1_048_576;
    public AttributeReadOptions AttributeOptions { get; init; } = new();
    public IO.VdbReadOptions VdbOptions { get; init; } = new();
}

/// <summary>Ordered collection of uniquely named point-attribute arrays.</summary>
/// <remarks>Shallow copies share descriptor and array storage. Mutating access performs copy-on-write.
/// Sharing counts track AttributeSet owners; raw references returned to callers are outside that count.</remarks>
public sealed class AttributeSet : IDisposable, IEquatable<AttributeSet>
{
    private sealed class SharedAttribute(AttributeArray value)
    {
        internal AttributeArray Value = value;
        internal int Owners = 1;
    }

    private AttributeDescriptor _descriptor;
    private List<SharedAttribute?> _attributes;
    private readonly AttributeArrayRegistry _registry;
    private AttributeStreamMetadata[]? _pendingMetadata;
    private bool _disposed;

    public AttributeSet() : this(new AttributeDescriptor(), 1, AttributeArrayRegistry.CreateDefault()) { }

    public AttributeSet(AttributeDescriptor descriptor, int arrayLength = 1, AttributeArrayRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor); ArgumentOutOfRangeException.ThrowIfNegative(arrayLength);
        _descriptor = descriptor; _registry = registry ?? AttributeArrayRegistry.CreateDefault();
        _attributes = new List<SharedAttribute?>(descriptor.Size);
        foreach (var entry in descriptor.Entries())
        {
            MetadataValue? defaultValue = descriptor.Metadata.GetMetadata("default:" + entry.Name);
            _attributes.Add(new(_registry.Create(entry.Type, arrayLength, metadata: defaultValue)));
        }
    }

    /// <summary>Creates a resized set with the same shared descriptor, shapes and default values.</summary>
    public AttributeSet(AttributeSet source, int arrayLength)
    {
        ArgumentNullException.ThrowIfNull(source); source.CheckDisposed(); ArgumentOutOfRangeException.ThrowIfNegative(arrayLength);
        _descriptor = source._descriptor; _registry = source._registry;
        _attributes = new List<SharedAttribute?>(source._attributes.Count);
        for (int position = 0; position < source._attributes.Count; position++)
        {
            SharedAttribute cell = source.RequireCell(position);
            AttributeArray existing = cell.Value;
            int strideOrTotalSize = existing.HasConstantStride ? existing.Stride : existing.DataSize;
            string name = source.NameAt(position);
            var array = _registry.Create(_descriptor.Type(position), arrayLength, strideOrTotalSize, existing.HasConstantStride,
                _descriptor.Metadata.GetMetadata("default:" + name));
            array.IsHidden = existing.IsHidden; array.IsTransient = existing.IsTransient;
            _attributes.Add(new(array));
        }
    }

    /// <summary>Creates a native-style shallow copy sharing the descriptor and arrays.</summary>
    public AttributeSet(AttributeSet source)
    {
        ArgumentNullException.ThrowIfNull(source); source.CheckDisposed();
        _descriptor = source._descriptor; _registry = source._registry;
        _attributes = new List<SharedAttribute?>(source._attributes.Count);
        foreach (SharedAttribute? cell in source._attributes)
        {
            if (cell is not null) Interlocked.Increment(ref cell.Owners);
            _attributes.Add(cell);
        }
    }

    private AttributeSet(AttributeDescriptor descriptor, AttributeArrayRegistry registry, List<SharedAttribute?> attributes)
    {
        _descriptor = descriptor; _registry = registry; _attributes = attributes;
    }

    ~AttributeSet() => ReleaseAll();

    public AttributeDescriptor Descriptor { get { CheckDisposed(); return _descriptor; } }
    public int Size { get { CheckDisposed(); return _attributes.Count; } }
    public long EstimateMemoryBytes
    {
        get
        {
            CheckDisposed();
            long bytes = 64 + _descriptor.EstimateMemoryBytes() + _attributes.Count * 16L;
            foreach (var cell in _attributes) if (cell is not null) bytes += cell.Value.StorageByteCount;
            return bytes;
        }
    }

    public int Find(string name) { CheckDisposed(); return _descriptor.Find(name); }

    public AttributeArray? GetConst(string name)
    {
        int position = Find(name);
        return position == AttributeDescriptor.InvalidPosition ? null : GetConst(position);
    }

    public AttributeArray? GetConst(int position)
    {
        CheckDisposed();
        if ((uint)position >= (uint)_attributes.Count) throw new ArgumentOutOfRangeException(nameof(position));
        return _attributes[position]?.Value;
    }

    public AttributeArray? Get(string name)
    {
        int position = Find(name);
        return position == AttributeDescriptor.InvalidPosition ? null : Get(position);
    }

    public AttributeArray? Get(int position)
    {
        CheckDisposed(); MakeUnique(position); return _attributes[position]?.Value;
    }

    public int Replace(string name, AttributeArray attribute)
    {
        int position = Find(name);
        return position == AttributeDescriptor.InvalidPosition ? AttributeDescriptor.InvalidPosition : Replace(position, attribute);
    }

    public int Replace(int position, AttributeArray attribute)
    {
        CheckDisposed(); ArgumentNullException.ThrowIfNull(attribute);
        if ((uint)position >= (uint)_attributes.Count) throw new ArgumentOutOfRangeException(nameof(position));
        if (attribute.Type != _descriptor.Type(position)) return AttributeDescriptor.InvalidPosition;
        Release(_attributes[position]); _attributes[position] = new(attribute); return position;
    }

    public bool IsShared(int position)
    {
        CheckDisposed();
        SharedAttribute cell = RequireCell(position);
        return Volatile.Read(ref cell.Owners) != 1;
    }

    public void MakeUnique(int position)
    {
        CheckDisposed();
        SharedAttribute? cell = PositionCell(position);
        if (cell is null) throw new InvalidOperationException("AttributeSet contains an unsafe removed array.");
        if (Volatile.Read(ref cell.Owners) == 1) return;
        AttributeArray copy = cell.Value.DeepCopy();
        Interlocked.Decrement(ref cell.Owners);
        _attributes[position] = new(copy);
    }

    public AttributeArray AppendAttribute(string name, AttributeType type, int strideOrTotalSize = 1,
        bool constantStride = true, MetadataValue? defaultValue = null)
    {
        CheckDisposed();
        AttributeDescriptor replacement = _descriptor.DuplicateAppend(name, type);
        if (defaultValue is not null) replacement.SetDefaultValue(name, defaultValue);
        int position = replacement.Find(name);
        return AppendAttribute(_descriptor, replacement, position, strideOrTotalSize, constantStride, defaultValue);
    }

    public AttributeArray AppendAttribute(AttributeDescriptor expected, AttributeDescriptor replacement, int position,
        int strideOrTotalSize = 1, bool constantStride = true, MetadataValue? defaultValue = null)
    {
        CheckDisposed(); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(replacement);
        if (!_descriptor.Equals(expected)) throw new InvalidOperationException("Cannot append attributes because descriptors do not match.");
        if (replacement.Size != _descriptor.Size + 1) throw new ArgumentException("Replacement descriptor must append exactly one attribute.", nameof(replacement));
        AttributeType type = replacement.Type(position);
        int arrayLength = _attributes.Count == 0 ? 1 : RequireCell(0).Value.Size;
        AttributeArray array = _registry.Create(type, arrayLength, strideOrTotalSize, constantStride, defaultValue);
        _descriptor = replacement; _attributes.Add(new(array)); return array;
    }

    public AttributeArray? RemoveAttribute(string name)
    {
        int position = Find(name);
        return position == AttributeDescriptor.InvalidPosition ? null : RemoveAttribute(position);
    }

    public AttributeArray? RemoveAttribute(int position)
    {
        CheckDisposed();
        if ((uint)position >= (uint)_attributes.Count) return null;
        SharedAttribute cell = RequireCell(position);
        AttributeDescriptor replacement = _descriptor.DuplicateDrop([position]);
        AttributeArray value = cell.Value;
        _attributes.RemoveAt(position);
        _descriptor = replacement;
        Release(cell);
        return value;
    }

    public AttributeArray? RemoveAttributeUnsafe(int position)
    {
        CheckDisposed();
        if ((uint)position >= (uint)_attributes.Count) return null;
        SharedAttribute? cell = _attributes[position];
        if (cell is null) return null;
        _attributes[position] = null; Release(cell); return cell.Value;
    }

    public void DropAttributes(IEnumerable<int> positions)
    {
        CheckDisposed(); ArgumentNullException.ThrowIfNull(positions);
        int[] dropped = NormalizePositions(positions);
        if (dropped.Length == 0) return;
        AttributeDescriptor replacement = _descriptor.DuplicateDrop(dropped);
        DropAttributes(dropped, _descriptor, replacement);
    }

    public void DropAttributes(IEnumerable<int> positions, AttributeDescriptor expected, AttributeDescriptor replacement)
    {
        CheckDisposed(); ArgumentNullException.ThrowIfNull(positions); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(replacement);
        int[] dropped = NormalizePositions(positions);
        if (dropped.Length == 0) return;
        if (!_descriptor.Equals(expected)) throw new InvalidOperationException("Cannot drop attributes because descriptors do not match.");
        if (replacement.Size != _attributes.Count - dropped.Length) throw new ArgumentException("Replacement descriptor size does not match retained arrays.", nameof(replacement));
        HashSet<int> set = dropped.ToHashSet();
        var retained = new List<SharedAttribute?>(_attributes.Count - dropped.Length);
        for (int position = 0; position < _attributes.Count; position++)
        {
            if (set.Contains(position)) Release(_attributes[position]);
            else retained.Add(_attributes[position]);
        }

        _descriptor = replacement; _descriptor.PruneUnusedDefaultValues(); _attributes = retained;
    }

    public void RenameAttributes(AttributeDescriptor expected, AttributeDescriptor replacement)
    {
        CheckDisposed(); ArgumentNullException.ThrowIfNull(expected); ArgumentNullException.ThrowIfNull(replacement);
        if (!_descriptor.Equals(expected)) throw new InvalidOperationException("Cannot rename attributes because descriptors do not match.");
        if (replacement.Size != _attributes.Count) throw new ArgumentException("Replacement descriptor size differs.", nameof(replacement));
        _descriptor = replacement;
    }

    public void ReorderAttributes(AttributeDescriptor replacement)
    {
        CheckDisposed(); ArgumentNullException.ThrowIfNull(replacement);
        if (_descriptor.Equals(replacement)) { _descriptor = replacement; return; }
        if (!_descriptor.HasSameAttributes(replacement))
            throw new InvalidOperationException("Cannot reorder attributes because descriptors do not contain the same attributes.");
        var reordered = new SharedAttribute?[replacement.Size];
        foreach (var pair in _descriptor.Map) reordered[replacement.Find(pair.Key)] = _attributes[pair.Value];
        _attributes = [.. reordered]; _descriptor = replacement;
    }

    public void ResetDescriptor(AttributeDescriptor replacement, bool allowMismatchingDescriptors = false)
    {
        CheckDisposed(); ArgumentNullException.ThrowIfNull(replacement);
        if (!allowMismatchingDescriptors && !_descriptor.Equals(replacement))
            throw new InvalidOperationException("Cannot swap a nonmatching descriptor.");
        _descriptor = replacement;
    }

    public int GroupOffset(string group) { CheckDisposed(); return _descriptor.GroupOffset(group); }
    public int GroupOffset(GroupIndex index) { CheckDisposed(); return _descriptor.GroupOffset(index); }
    public GroupIndex GroupIndex(string group) { CheckDisposed(); return _descriptor.GroupIndex(group); }
    public GroupIndex GroupIndex(int offset) { CheckDisposed(); return _descriptor.GroupIndex(offset); }

    public GroupHandle GetGroupHandle(string group)
    {
        CheckDisposed();
        GroupIndex index = _descriptor.GroupIndex(group);
        var storage = RequireCell(index.AttributeIndex).Value as TypedAttributeArray<byte, GroupCodec> ??
            throw new InvalidOperationException("Descriptor group index does not reference group storage.");
        return new GroupHandle(storage, index.BitOffset);
    }

    public GroupWriteHandle GetGroupWriteHandle(string group)
    {
        CheckDisposed();
        GroupIndex index = _descriptor.GroupIndex(group);
        MakeUnique(index.AttributeIndex);
        var storage = RequireCell(index.AttributeIndex).Value as TypedAttributeArray<byte, GroupCodec> ??
            throw new InvalidOperationException("Descriptor group index does not reference group storage.");
        return new GroupWriteHandle(storage, index.BitOffset);
    }
    public IReadOnlyList<int> GroupAttributeIndices()
    {
        CheckDisposed();
        var positions = new List<int>();
        for (int position = 0; position < _attributes.Count; position++)
            if (_attributes[position]?.Value is TypedAttributeArray<byte, GroupCodec>) positions.Add(position);
        return positions;
    }

    public void Write(Stream stream, bool outputTransient = false, bool compress = false)
    {
        CheckDisposed(); ArgumentNullException.ThrowIfNull(stream);
        int[] positions = PersistentPositions(outputTransient);
        AttributeDescriptor descriptor = positions.Length == _attributes.Count ? _descriptor :
            _descriptor.DuplicateDrop(Enumerable.Range(0, _attributes.Count).Except(positions));
        List<byte[]> encodings = Encode(positions, compress);
        descriptor.Write(stream);
        WriteParts(stream, encodings, metadata: true);
        WriteParts(stream, encodings, metadata: false);
    }

    public void WriteDescriptor(Stream stream, bool outputTransient = false)
    {
        CheckDisposed(); ArgumentNullException.ThrowIfNull(stream);
        int[] positions = PersistentPositions(outputTransient);
        (positions.Length == _attributes.Count ? _descriptor :
            _descriptor.DuplicateDrop(Enumerable.Range(0, _attributes.Count).Except(positions))).Write(stream);
    }

    public void WriteMetadata(Stream stream, bool outputTransient = false, bool compress = false)
    {
        CheckDisposed(); WriteParts(stream, Encode(PersistentPositions(outputTransient), compress), metadata: true);
    }

    public void WriteAttributes(Stream stream, bool outputTransient = false, bool compress = false)
    {
        CheckDisposed(); WriteParts(stream, Encode(PersistentPositions(outputTransient), compress), metadata: false);
    }

    public static AttributeSet Read(Stream stream, AttributeArrayRegistry? registry = null, AttributeSetReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream); options ??= new(); registry ??= AttributeArrayRegistry.CreateDefault();
        AttributeDescriptor descriptor = AttributeDescriptor.Read(stream, options.VdbOptions, options.MaximumAttributes, options.MaximumNameBytes);
        var metadata = new AttributeStreamMetadata[descriptor.Size];
        for (int position = 0; position < metadata.Length; position++)
            metadata[position] = AttributeStreams.ReadMetadata(stream, options.AttributeOptions);
        var attributes = new List<SharedAttribute?>(descriptor.Size);
        for (int position = 0; position < descriptor.Size; position++)
            attributes.Add(new(AttributeStreams.ReadBuffers(stream, descriptor.Type(position), registry, metadata[position], options.AttributeOptions)));
        return new(descriptor, registry, attributes);
    }

    public void ReadFrom(Stream stream, AttributeSetReadOptions? options = null)
    {
        CheckDisposed();
        using AttributeSet replacement = Read(stream, _registry, options);
        ReleaseAll();
        _descriptor = replacement._descriptor;
        _attributes = replacement._attributes;
        replacement._attributes = [];
    }

    public void ReadDescriptor(Stream stream, AttributeSetReadOptions? options = null)
    {
        CheckDisposed(); options ??= new();
        _descriptor = AttributeDescriptor.Read(stream, options.VdbOptions, options.MaximumAttributes, options.MaximumNameBytes);
        ReleaseAll(); _attributes = [];
        _pendingMetadata = null;
    }

    public void ReadMetadata(Stream stream, AttributeSetReadOptions? options = null)
    {
        CheckDisposed(); options ??= new();
        var metadata = new AttributeStreamMetadata[_descriptor.Size];
        for (int position = 0; position < metadata.Length; position++)
            metadata[position] = AttributeStreams.ReadMetadata(stream, options.AttributeOptions);
        ReleaseAll(); _attributes = Enumerable.Repeat<SharedAttribute?>(null, _descriptor.Size).ToList();
        _pendingMetadata = metadata;
    }

    public void ReadAttributes(Stream stream, AttributeSetReadOptions? options = null)
    {
        CheckDisposed(); options ??= new();
        AttributeStreamMetadata[] metadata = _pendingMetadata ?? throw new InvalidOperationException("ReadMetadata must precede ReadAttributes.");
        var loaded = new List<SharedAttribute?>(_descriptor.Size);
        try
        {
            for (int position = 0; position < _descriptor.Size; position++)
                loaded.Add(new(AttributeStreams.ReadBuffers(stream, _descriptor.Type(position), _registry, metadata[position], options.AttributeOptions)));
        }
        catch
        {
            foreach (var cell in loaded) Release(cell);
            throw;
        }
        ReleaseAll(); _attributes = loaded; _pendingMetadata = null;
    }

    public bool Equals(AttributeSet? other)
    {
        CheckDisposed();
        if (other is null || other._disposed || !_descriptor.Equals(other._descriptor) || _attributes.Count != other._attributes.Count) return false;
        for (int position = 0; position < _attributes.Count; position++)
        {
            AttributeArray? left = _attributes[position]?.Value, right = other._attributes[position]?.Value;
            if (left is null || right is null) { if (!ReferenceEquals(left, right)) return false; }
            else if (!left.NativeEquals(right)) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is AttributeSet other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_descriptor, _attributes.Count);

    public void Dispose()
    {
        if (_disposed) return;
        ReleaseAll(); _disposed = true; GC.SuppressFinalize(this);
    }

    private int[] PersistentPositions(bool outputTransient) =>
        Enumerable.Range(0, _attributes.Count).Where(position => outputTransient || !RequireCell(position).Value.IsTransient).ToArray();

    private List<byte[]> Encode(int[] positions, bool compress)
    {
        var encodings = new List<byte[]>(positions.Length);
        foreach (int position in positions)
        {
            using var buffer = new MemoryStream();
            AttributeStreams.Write(buffer, RequireCell(position).Value, compress, outputTransient: true);
            encodings.Add(buffer.ToArray());
        }
        return encodings;
    }

    private static void WriteParts(Stream stream, IEnumerable<byte[]> encodings, bool metadata)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite) throw new ArgumentException("Stream is not writable.", nameof(stream));
        foreach (byte[] encoding in encodings)
        {
            if (encoding.Length < 14) throw new InvalidDataException("Encoded attribute is truncated.");
            int header = (encoding[9] & 1) != 0 ? 18 : 14;
            stream.Write(metadata ? encoding.AsSpan(0, header) : encoding.AsSpan(header));
        }
    }

    private int[] NormalizePositions(IEnumerable<int> positions)
    {
        int[] values = positions.Distinct().Order().ToArray();
        foreach (int position in values)
            if ((uint)position >= (uint)_attributes.Count) throw new ArgumentOutOfRangeException(nameof(positions));
        return values;
    }

    private string NameAt(int position)
    {
        foreach (var pair in _descriptor.Map) if (pair.Value == position) return pair.Key;
        throw new InvalidOperationException("Descriptor has no name for the requested position.");
    }

    private SharedAttribute RequireCell(int position) =>
        PositionCell(position) ?? throw new InvalidOperationException("AttributeSet contains an unsafe removed array.");

    private SharedAttribute? PositionCell(int position)
    {
        if ((uint)position >= (uint)_attributes.Count) throw new ArgumentOutOfRangeException(nameof(position));
        return _attributes[position];
    }

    private void CheckDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void ReleaseAll()
    {
        foreach (var cell in _attributes) Release(cell);
        _attributes.Clear();
    }

    private static void Release(SharedAttribute? cell)
    {
        if (cell is not null) Interlocked.Decrement(ref cell.Owners);
    }
}
