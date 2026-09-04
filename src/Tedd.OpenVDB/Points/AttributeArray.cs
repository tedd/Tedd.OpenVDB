// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of points/AttributeArray.h and AttributeArray.cc.
using System.Runtime.CompilerServices;
namespace Tedd.OpenVDB.Points;

public readonly record struct AttributeType(string ValueType, string Codec);
public readonly record struct AttributeCopyIndex(int Source, int Target);
[Flags]
public enum AttributeFlags : byte { None = 0, Transient = 1, Hidden = 2, ConstantStride = 8, Streaming = 16, PartialRead = 32 }

/// <summary>In-memory point-attribute storage. Mutations require exclusive ownership.</summary>
public abstract class AttributeArray
{
    public abstract AttributeType Type { get; }
    public abstract int Size { get; }
    public abstract int Stride { get; }
    public abstract int DataSize { get; }
    public abstract int ValueTypeSize { get; }
    public abstract int StorageTypeSize { get; }
    public abstract bool ValueTypeIsFloatingPoint { get; }
    public abstract bool ValueTypeIsVector { get; }
    public abstract bool ValueTypeIsQuaternion { get; }
    public abstract bool ValueTypeIsMatrix { get; }
    public virtual bool ValueTypeIsClass => ValueTypeIsVector || ValueTypeIsQuaternion || ValueTypeIsMatrix;
    public string ValueType => Type.ValueType;
    public string CodecType => Type.Codec;
    public AttributeFlags Flags { get; protected set; }
    public bool HasConstantStride => (Flags & AttributeFlags.ConstantStride) != 0;
    public bool IsHidden { get => Flag(AttributeFlags.Hidden); set => SetFlag(AttributeFlags.Hidden, value); }
    public bool IsTransient { get => Flag(AttributeFlags.Transient); set => SetFlag(AttributeFlags.Transient, value); }
    public bool IsStreaming { get => Flag(AttributeFlags.Streaming); set => SetFlag(AttributeFlags.Streaming, value); }
    public abstract bool IsUniform { get; }
    /// <summary>Encoded payload size, excluding CLR object and array overhead.</summary>
    public abstract int StorageByteCount { get; }
    public bool IsDataLoaded => true;
    public void LoadData() { }
    public abstract void Expand(bool fill = true);
    public abstract void Collapse();
    public abstract bool Compact();
    public abstract AttributeArray DeepCopy();
    /// <summary>Native equality, including its comparison of only Size storage values for nonuniform strided arrays.</summary>
    public abstract bool NativeEquals(AttributeArray? other);
    /// <summary>Compares all encoded values and array shape, without the native strided-tail omission.</summary>
    public abstract bool StorageEquals(AttributeArray? other);
    public abstract ReadOnlySpan<byte> GetStorageBytes();
    internal abstract Span<byte> MutableStorageBytes();
    internal abstract void LoadSerializedStorage(byte[] storage, bool uniform, AttributeFlags flags);
    private bool Flag(AttributeFlags flag) => (Flags & flag) != 0;
    private void SetFlag(AttributeFlags flag, bool value) => Flags = value ? Flags | flag : Flags & ~flag;
    /// <summary>Copies encoded stride blocks; codecs are not converted. Source and destination must have compatible storage.</summary>
    /// <remarks>Uniform source strides repeat the uniform encoded value. Invalid native buffer overreads are rejected.
    /// Sequential self-copy observes prior writes; completed writes can remain after a later invalid index.</remarks>
    public void CopyValues(AttributeArray source, IEnumerable<AttributeCopyIndex> indices, bool compact = true)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(indices);
        if (StorageTypeSize != source.StorageTypeSize || Stride != source.Stride || ValueTypeIsFloatingPoint != source.ValueTypeIsFloatingPoint)
            throw new ArgumentException("Encoded element sizes, strides and numeric categories must match.", nameof(source));
        Expand();
        int width = checked(StorageTypeSize * Stride);
        foreach (var pair in indices)
        {
            // Variable-stride native copying has zero byte width; retain its checked no-op semantics.
            int sourceIndex = source.IsUniform ? 0 : pair.Source;
            if ((uint)sourceIndex >= (uint)source.DataSize || (uint)pair.Target >= (uint)DataSize) throw new ArgumentOutOfRangeException(nameof(indices));
            if (width == 0) continue;
            long offset = (long)pair.Target * width;
            if (offset + width > StorageByteCount) throw new ArgumentOutOfRangeException(nameof(indices));
            Span<byte> target = MutableStorageBytes().Slice((int)offset, width);
            if (source.IsUniform)
            {
                var uniform = source.GetStorageBytes();
                for (int i = 0; i < Stride; i++) uniform.CopyTo(target.Slice(i * StorageTypeSize, StorageTypeSize));
            }
            else
            {
                long sourceOffset = (long)sourceIndex * width;
                if (sourceOffset + width > source.StorageByteCount) throw new ArgumentOutOfRangeException(nameof(indices));
                source.GetStorageBytes().Slice((int)sourceOffset, width).CopyTo(target);
            }
        }
        if (compact) Compact();
    }
}
public abstract class AttributeArray<T> : AttributeArray where T : unmanaged
{
    public abstract T Get(int index);
    public abstract void Set(int index, T value);
    public abstract T GetUnsafe(int index);
    public abstract void SetUnsafe(int index, T value);
    public abstract void Collapse(T value);
    public abstract void Fill(T value);
    public T this[int index] { get => Get(index); set => Set(index, value); }
}

/// <summary>Uniform-or-expanded encoded attributes with constant or variable stride.</summary>
/// <remarks>Length zero normalizes to one. Set always expands uniform storage, even for an unchanged value.
/// Expand(false) provides zero-initialized encoded storage instead of exposing native uninitialized bytes.
/// Unsafe accessors retain managed bounds checks; SetUnsafe updates the uniform value without expanding it.</remarks>
public class TypedAttributeArray<T, TCodec> : AttributeArray<T> where T : unmanaged where TCodec : unmanaged, IAttributeCodec<T>
{
    private byte[] _storage;
    private bool _uniform = true;
    private readonly int _strideOrTotalSize;
    private readonly int _maximumStorageBytes;
    public override AttributeType Type => new(AttributeValueTraits<T>.Name, TCodec.Name);
    public override int Size { get; }
    public override int Stride => HasConstantStride ? _strideOrTotalSize : 0;
    public override int DataSize { get; }
    public override int ValueTypeSize => Unsafe.SizeOf<T>();
    public override int StorageTypeSize => TCodec.StorageSize;
    public override bool ValueTypeIsFloatingPoint => AttributeValueTraits<T>.Floating;
    public override bool ValueTypeIsClass => !typeof(T).IsPrimitive && typeof(T) != typeof(Half);
    public override bool ValueTypeIsVector => AttributeValueTraits<T>.Vector;
    public override bool ValueTypeIsQuaternion => AttributeValueTraits<T>.Quaternion;
    public override bool ValueTypeIsMatrix => AttributeValueTraits<T>.Matrix;
    public override bool IsUniform => _uniform;
    public override int StorageByteCount => _storage.Length;
    public TypedAttributeArray(int length = 1, int strideOrTotalSize = 1, bool constantStride = true, T uniformValue = default, int maximumStorageBytes = 268_435_456)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length); ArgumentOutOfRangeException.ThrowIfNegative(strideOrTotalSize);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumStorageBytes);
        if (constantStride && strideOrTotalSize == 0) throw new ArgumentOutOfRangeException(nameof(strideOrTotalSize), "A constant stride must be at least one.");
        if (!constantStride && strideOrTotalSize < length) throw new ArgumentOutOfRangeException(nameof(strideOrTotalSize), "Variable storage must have at least one value per element.");
        Size = System.Math.Max(1, length); _strideOrTotalSize = System.Math.Max(1, strideOrTotalSize);
        DataSize = constantStride ? checked(Size * _strideOrTotalSize) : _strideOrTotalSize;
        Flags = constantStride ? AttributeFlags.ConstantStride : AttributeFlags.None; _maximumStorageBytes = maximumStorageBytes;
        int width = TCodec.StorageSize;
        if (width <= 0 || width > maximumStorageBytes) throw new ArgumentOutOfRangeException(nameof(maximumStorageBytes));
        _storage = new byte[width]; TCodec.Encode(uniformValue, _storage);
    }
    private TypedAttributeArray(TypedAttributeArray<T, TCodec> source)
    {
        Size = source.Size; DataSize = source.DataSize; _strideOrTotalSize = source._strideOrTotalSize; _maximumStorageBytes = source._maximumStorageBytes;
        Flags = source.Flags; _uniform = source._uniform; _storage = (byte[])source._storage.Clone();
    }
    public override TypedAttributeArray<T, TCodec> DeepCopy() => new(this);
    public static TypedAttributeArray<T, TCodec> Create(int length, int strideOrTotalSize = 1, bool constantStride = true, MetadataValue? metadata = null, int maximumStorageBytes = 268_435_456)
        => new(length, strideOrTotalSize, constantStride, metadata is not null && metadata.TryGetValue<T>(out var value) ? value : default, maximumStorageBytes);
    public override T Get(int index) => GetUnsafe(index);
    public override T GetUnsafe(int index)
    {
        Validate(index); int width = TCodec.StorageSize; var bytes = _storage;
        return TCodec.Decode(bytes.AsSpan(_uniform ? 0 : index * width, width));
    }
    public override void Set(int index, T value) { Validate(index); Expand(); SetUnsafe(index, value); }
    public override void SetUnsafe(int index, T value)
    {
        Validate(index); int width = TCodec.StorageSize; var bytes = _storage;
        TCodec.Encode(value, bytes.AsSpan(_uniform ? 0 : index * width, width));
    }
    public override void Expand(bool fill = true)
    {
        if (!_uniform) return;
        int width = TCodec.StorageSize; long required = (long)DataSize * width;
        if (required > _maximumStorageBytes || required > Array.MaxLength) throw new InvalidOperationException("Attribute expansion exceeds its storage budget.");
        var expanded = new byte[(int)required];
        if (fill)
        {
            // Doubling copies retain exact encoded bits while using the runtime's vectorized copy implementation.
            _storage.CopyTo(expanded, 0);
            for (int populated = width; populated < expanded.Length;)
            {
                int count = System.Math.Min(populated, expanded.Length - populated);
                expanded.AsSpan(0, count).CopyTo(expanded.AsSpan(populated, count)); populated += count;
            }
        }
        _storage = expanded; _uniform = false;
    }
    public override void Collapse() => Collapse(default);
    public override void Collapse(T value)
    {
        if (!_uniform) { var uniform = new byte[TCodec.StorageSize]; TCodec.Encode(value, uniform); _storage = uniform; _uniform = true; }
        else TCodec.Encode(value, _storage);
    }
    public override void Fill(T value)
    {
        int width = TCodec.StorageSize; TCodec.Encode(value, _storage.AsSpan(0, width));
        for (int populated = width; populated < _storage.Length;)
        {
            int count = System.Math.Min(populated, _storage.Length - populated);
            _storage.AsSpan(0, count).CopyTo(_storage.AsSpan(populated, count)); populated += count;
        }
    }
    public override bool Compact()
    {
        if (_uniform) return true;
        T first = GetUnsafe(0);
        for (int i = 1; i < DataSize; i++) if (!AttributeValueTraits<T>.ExactlyEqual(GetUnsafe(i), first)) return false;
        Collapse(first); return true;
    }
    public override bool NativeEquals(AttributeArray? other) => Equal(other, native: true);
    public override bool StorageEquals(AttributeArray? other) => Equal(other, native: false);
    private bool Equal(AttributeArray? other, bool native)
    {
        if (other is not TypedAttributeArray<T, TCodec> array || Size != array.Size || _strideOrTotalSize != array._strideOrTotalSize || Flags != array.Flags || _uniform != array._uniform) return false;
        int width = TCodec.StorageSize, count = _uniform ? 1 : native ? Size : DataSize;
        for (int i = 0; i < count; i++) if (!TCodec.StorageEquals(_storage.AsSpan(i * width, width), array._storage.AsSpan(i * width, width))) return false;
        return true;
    }
    public override ReadOnlySpan<byte> GetStorageBytes() => _storage;
    internal override Span<byte> MutableStorageBytes() => _storage;
    internal override void LoadSerializedStorage(byte[] storage, bool uniform, AttributeFlags flags)
    {
        if (storage.Length != (long)(uniform ? 1 : DataSize) * TCodec.StorageSize) throw new InvalidDataException("Encoded attribute storage has the wrong length.");
        if (typeof(T) == typeof(bool) && typeof(TCodec) == typeof(NullCodec<bool>))
            foreach (byte value in storage) if (value > 1) throw new InvalidDataException("Boolean attribute storage must contain zero or one.");
        _storage = storage; _uniform = uniform; Flags = flags;
    }
    private void Validate(int index) { if ((uint)index >= (uint)DataSize) throw new ArgumentOutOfRangeException(nameof(index)); }
}
public sealed class TypedAttributeArray<T>(int length = 1, int strideOrTotalSize = 1, bool constantStride = true, T uniformValue = default, int maximumStorageBytes = 268_435_456)
    : TypedAttributeArray<T, NullCodec<T>>(length, strideOrTotalSize, constantStride, uniformValue, maximumStorageBytes) where T : unmanaged;
