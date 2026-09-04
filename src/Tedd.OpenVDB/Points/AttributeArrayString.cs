// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of points/AttributeArrayString.h and AttributeArrayString.cc.
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Tedd.OpenVDB.Points;

/// <summary>Native string-table index codec with 32-bit unsigned storage.</summary>
public readonly struct StringCodec : IAttributeCodec<uint>
{
    public static string Name => "str";
    public static int StorageSize => sizeof(uint);
    public static void Encode(uint value, Span<byte> storage) => BinaryPrimitives.WriteUInt32LittleEndian(storage, value);
    public static uint Decode(ReadOnlySpan<byte> storage) => BinaryPrimitives.ReadUInt32LittleEndian(storage);
    public static bool StorageEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => Decode(left) == Decode(right);
}

/// <summary>Native truncated string-table index codec with 16-bit unsigned storage.</summary>
public readonly struct TruncatedStringCodec : IAttributeCodec<uint>
{
    public static string Name => "str_trnc";
    public static int StorageSize => sizeof(ushort);
    public static void Encode(uint value, Span<byte> storage) => BinaryPrimitives.WriteUInt16LittleEndian(storage, unchecked((ushort)value));
    public static uint Decode(ReadOnlySpan<byte> storage) => BinaryPrimitives.ReadUInt16LittleEndian(storage);
    public static bool StorageEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => Decode(left) == Decode(right);
}

/// <summary>Index attributes resolved through <c>string:N</c> descriptor metadata.</summary>
public sealed class StringAttributeArray : TypedAttributeArray<uint, StringCodec>
{
    private readonly int _maximumStorageBytes;

    public StringAttributeArray(
        int length = 1,
        int strideOrTotalSize = 1,
        bool constantStride = true,
        uint uniformIndex = 0,
        int maximumStorageBytes = 268_435_456)
        : base(length, strideOrTotalSize, constantStride, uniformIndex, maximumStorageBytes)
    {
        _maximumStorageBytes = maximumStorageBytes;
    }

    private StringAttributeArray(StringAttributeArray source)
        : base(
            source.Size,
            source.HasConstantStride ? source.Stride : source.DataSize,
            source.HasConstantStride,
            maximumStorageBytes: source._maximumStorageBytes)
    {
        _maximumStorageBytes = source._maximumStorageBytes;
        LoadSerializedStorage(source.GetStorageBytes().ToArray(), source.IsUniform, source.Flags);
    }

    public override StringAttributeArray DeepCopy() => new(this);

    public static bool IsString(AttributeArray array)
    {
        ArgumentNullException.ThrowIfNull(array);
        return array.Type == new AttributeType("uint32", StringCodec.Name);
    }
}

/// <summary>Snapshot mapping from string values to native one-based table indices.</summary>
public sealed class StringMetaCache
{
    private readonly Dictionary<string, uint> _cache = new(StringComparer.Ordinal);
    private readonly ReadOnlyDictionary<string, uint> _view;

    public StringMetaCache() => _view = new(_cache);
    public StringMetaCache(MetaMap metadata) : this() => Reset(metadata);

    public int Count => _cache.Count;
    public bool IsEmpty => _cache.Count == 0;
    public IReadOnlyDictionary<string, uint> Map => _view;

    public bool TryGetIndex(string value, out uint index)
    {
        ArgumentNullException.ThrowIfNull(value);
        return _cache.TryGetValue(value, out index);
    }

    public void Insert(string value, uint index)
    {
        ArgumentNullException.ThrowIfNull(value);
        _cache[value] = index;
    }

    public void Reset(MetaMap metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _cache.Clear();

        foreach (var pair in metadata)
        {
            if (!pair.Key.StartsWith("string:", StringComparison.Ordinal) ||
                !pair.Value.TryGetValue<string>(out string? value))
            {
                continue;
            }

            ReadOnlySpan<char> suffix = pair.Key.AsSpan(7);
            if (suffix.IsEmpty ||
                suffix[0] is '+' or '-' ||
                !ulong.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out ulong zeroBased) ||
                zeroBased >= uint.MaxValue)
            {
                throw new InvalidDataException($"Invalid string metadata index '{pair.Key}'.");
            }

            _cache[value] = checked((uint)zeroBased + 1);
        }
    }

    internal static string MetadataKey(uint index)
    {
        if (index == 0) throw new ArgumentOutOfRangeException(nameof(index));
        return "string:" + (index - 1).ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>Inserts string values into descriptor metadata using native string-index block allocation.</summary>
/// <remarks>
/// The allocation algorithm deliberately preserves OpenVDB 13.0.1 behavior for high index hints,
/// including subsequent reuse caused by its block-list update rules.
/// </remarks>
public sealed class StringMetaInserter
{
    private readonly record struct IndexBlock(uint Start, uint Size);

    private readonly MetaMap _metadata;
    private readonly StringMetaCache _cache = new();
    private readonly List<IndexBlock> _idBlocks = [];

    public StringMetaInserter(MetaMap metadata)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        ResetCache();
    }

    public bool HasKey(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return _cache.Map.ContainsKey(value);
    }

    public bool HasIndex(uint index) => index != 0 && _metadata.ContainsKey(StringMetaCache.MetadataKey(index));

    public uint Insert(string value, uint hint = 0)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_cache.TryGetIndex(value, out uint existing)) return existing;

        uint index = 1;
        bool canUseHint = false;
        if (hint > 0 && !HasIndex(hint))
        {
            index = hint;
            canUseHint = true;
        }

        int position = 0;
        for (; position < _idBlocks.Count; position++)
        {
            IndexBlock block = _idBlocks[position];
            uint end = unchecked(block.Start + block.Size);
            if (index < block.Start || index >= end) break;
            if (!canUseHint) index = end;
        }

        int previous;
        if (position == 0)
        {
            _idBlocks.Insert(0, new IndexBlock(1, 1));
            previous = 0;
            position = 1;
        }
        else
        {
            previous = position - 1;
            IndexBlock block = _idBlocks[previous];
            _idBlocks[previous] = block with { Size = unchecked(block.Size + 1) };
        }

        if (position < _idBlocks.Count)
        {
            IndexBlock prior = _idBlocks[previous];
            IndexBlock next = _idBlocks[position];
            if (unchecked(prior.Size + 1) == next.Start)
            {
                _idBlocks[previous] = prior with { Size = unchecked(prior.Size + next.Size) };
                _idBlocks.RemoveAt(position);
            }
        }

        if (index == 0) throw new OverflowException("No positive string metadata index remains.");
        _metadata.InsertMeta(StringMetaCache.MetadataKey(index), value);
        _cache.Insert(value, index);
        return index;
    }

    public void ResetCache()
    {
        _cache.Reset(_metadata);
        _idBlocks.Clear();
        if (_cache.IsEmpty) return;

        uint[] indices = [.. _cache.Map.Values];
        Array.Sort(indices);
        uint key = indices[0];
        uint size = 0;

        foreach (uint index in indices)
        {
            if (unchecked(key + size) != index)
            {
                if (size == 0) throw new InvalidDataException("String metadata index blocks are invalid.");
                _idBlocks.Add(new IndexBlock(key, size));
                size = 0;
                key = index;
            }

            size = unchecked(size + 1);
        }

        _idBlocks.Add(new IndexBlock(key, size));
    }
}

/// <summary>Resolves an encoded string index against live descriptor metadata.</summary>
public class StringAttributeHandle : IDisposable
{
    private readonly MetaMap _metadata;
    private AttributeHandle<uint, StringCodec>? _handle;

    protected AttributeHandle<uint, StringCodec> IndexHandle =>
        _handle ?? throw new ObjectDisposedException(nameof(StringAttributeHandle));

    public StringAttributeHandle(AttributeArray array, MetaMap metadata, bool preserveCompression = true)
    {
        ArgumentNullException.ThrowIfNull(array);
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        if (!StringAttributeArray.IsString(array))
        {
            throw new ArgumentException("Attribute array is not a native string index array.", nameof(array));
        }

        _handle = new AttributeHandle<uint, StringCodec>(array, preserveCompression);
    }

    public int Stride => IndexHandle.Stride;
    public int Size => IndexHandle.Size;
    public bool IsUniform => IndexHandle.IsUniform;
    public bool HasConstantStride => IndexHandle.HasConstantStride;
    public AttributeArray Array => IndexHandle.Array;

    public string Get(int index, int component = 0)
    {
        uint stringIndex = IndexHandle.Get(index, component);
        if (stringIndex == 0) return string.Empty;

        string key = StringMetaCache.MetadataKey(stringIndex);
        if (!_metadata.TryGetMetadata<string>(key, out string? result))
        {
            throw new KeyNotFoundException($"String attribute index {stringIndex} does not exist in metadata.");
        }

        return result;
    }

    protected void CheckAlive() => _ = IndexHandle;

    public virtual void Dispose()
    {
        _handle?.Dispose();
        _handle = null;
        GC.SuppressFinalize(this);
    }
}

/// <summary>Writes strings through a cache snapshot; call <see cref="ResetCache"/> after metadata changes.</summary>
public sealed class StringAttributeWriteHandle : StringAttributeHandle
{
    private readonly MetaMap _metadata;
    private readonly StringMetaCache _cache;
    private AttributeWriteHandle<uint, StringCodec>? _writer;

    private AttributeWriteHandle<uint, StringCodec> Writer =>
        _writer ?? throw new ObjectDisposedException(nameof(StringAttributeWriteHandle));

    public StringAttributeWriteHandle(AttributeArray array, MetaMap metadata, bool expand = true)
        : base(array, metadata, preserveCompression: false)
    {
        _metadata = metadata;
        _cache = new StringMetaCache(metadata);
        _writer = new AttributeWriteHandle<uint, StringCodec>(array, expand);
    }

    public bool Contains(string value)
    {
        CheckAlive();
        ArgumentNullException.ThrowIfNull(value);
        return value.Length == 0 || _cache.Map.ContainsKey(value);
    }

    public void ResetCache()
    {
        CheckAlive();
        _cache.Reset(_metadata);
    }

    public void Set(int index, string value) => Writer.Set(index, Index(value));
    public void Set(int index, int component, string value) => Writer.Set(index, component, Index(value));
    public void Expand(bool fill = true) => Writer.Expand(fill);
    public void Collapse() => Writer.Collapse(0);
    public void Collapse(string value) => Writer.Collapse(Index(value));
    public bool Compact() => Writer.Compact();
    public void Fill(string value) => Writer.Fill(Index(value));

    public override void Dispose()
    {
        _writer?.Dispose();
        _writer = null;
        base.Dispose();
    }

    private uint Index(string value)
    {
        CheckAlive();
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0) return 0;
        return _cache.TryGetIndex(value, out uint index)
            ? index
            : throw new KeyNotFoundException($"String '{value}' does not exist in metadata; insert it and reset the cache.");
    }
}
