// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of points/AttributeArray.h and points/StreamCompression.cc.
using System.Buffers.Binary;
using Tedd.OpenVDB.IO;
namespace Tedd.OpenVDB.Points;

public sealed record AttributeReadOptions
{
    public int MaximumStorageBytes { get; init; } = 268_435_456;
    public int MaximumEncodedBytes { get; init; } = 268_435_456;
    public long MaximumTotalAllocationBytes { get; init; } = 536_870_912;
    public CancellationToken CancellationToken { get; init; }
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumStorageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumEncodedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumTotalAllocationBytes);
        CancellationToken.ThrowIfCancellationRequested();
    }
}
public sealed record AttributeStreamMetadata(AttributeFlags Flags, int Size, int StrideOrTotalSize, byte SerializationFlags, int EncodedBytes)
{
    public bool IsUniform => (SerializationFlags & 2) != 0;
    public bool IsPaged => (SerializationFlags & 8) != 0;
    public bool HasConstantStride => (Flags & AttributeFlags.ConstantStride) != 0;
}

/// <summary>Native standalone attribute metadata and buffer serialization with raw or Blosc storage.</summary>
/// <remarks>The encoded length excludes the optional stride field and the nonuniform compression marker.
/// Type names and codecs belong to the surrounding attribute descriptor, not this stream.
/// Stream ownership remains with the caller. Paged and delayed buffers use a separate native layout and are rejected here.</remarks>
public static class AttributeStreams
{
    public static void Write(Stream stream, AttributeArray array, bool compress = false, bool outputTransient = false)
    {
        ArgumentNullException.ThrowIfNull(stream); ArgumentNullException.ThrowIfNull(array);
        if (!stream.CanWrite) throw new ArgumentException("Stream is not writable.", nameof(stream));
        if (array.IsTransient && !outputTransient) return;
        if ((array.Flags & AttributeFlags.PartialRead) != 0) throw new InvalidOperationException("Cannot serialize partially loaded attributes.");
        ReadOnlySpan<byte> storage = array.GetStorageBytes(); byte[]? encoded = null;
        if (compress && !array.IsUniform && storage.Length > 48)
        {
            ReadOnlySpan<byte> input = storage;
            if (storage.Length < 128) { var padded = new byte[128]; storage.CopyTo(padded); input = padded; }
            byte[] candidate = BloscCodec.Encode(input);
            if (candidate.Length < storage.Length) encoded = candidate;
        }
        int length = encoded?.Length ?? storage.Length; bool strided = array.Stride != 1;
        byte serializationFlags = (byte)((strided ? 1 : 0) | (array.IsUniform ? 2 : 0));
        Span<byte> header = stackalloc byte[18];
        BinaryPrimitives.WriteUInt64LittleEndian(header, checked((ulong)length + 6)); header[8] = (byte)array.Flags; header[9] = serializationFlags;
        BinaryPrimitives.WriteUInt32LittleEndian(header[10..], (uint)array.Size);
        if (strided) BinaryPrimitives.WriteUInt32LittleEndian(header[14..], (uint)(array.HasConstantStride ? array.Stride : array.DataSize));
        stream.Write(header[..(strided ? 18 : 14)]);
        if (!array.IsUniform) stream.WriteByte(encoded is null ? (byte)0 : (byte)1);
        if (encoded is null) stream.Write(storage); else stream.Write(encoded);
    }
    public static TypedAttributeArray<T, NullCodec<T>> Read<T>(Stream stream, AttributeReadOptions? options = null) where T : unmanaged
        => Read<T, NullCodec<T>>(stream, options);
    public static TypedAttributeArray<T, TCodec> Read<T, TCodec>(Stream stream, AttributeReadOptions? options = null)
        where T : unmanaged where TCodec : unmanaged, IAttributeCodec<T>
    {
        options ??= new(); var metadata = ReadMetadata(stream, options);
        return ReadBuffers<T, TCodec>(stream, metadata, options);
    }
    public static AttributeStreamMetadata ReadMetadata(Stream stream, AttributeReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream); options ??= new(); options.Validate();
        if (!stream.CanRead) throw new ArgumentException("Stream is not readable.", nameof(stream));
        Span<byte> header = stackalloc byte[14]; ReadExactly(stream, header, options.CancellationToken);
        ulong declared = BinaryPrimitives.ReadUInt64LittleEndian(header);
        if (declared < 6 || declared - 6 > (ulong)options.MaximumEncodedBytes) throw new InvalidDataException("Attribute payload size is invalid or exceeds its budget.");
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(header[10..]); uint stride = 1;
        byte serialized = header[9];
        if ((serialized & 0xF0) != 0) throw new NotSupportedException("Unknown attribute serialization flags.");
        if ((serialized & 1) != 0) { Span<byte> value = stackalloc byte[4]; ReadExactly(stream, value, options.CancellationToken); stride = BinaryPrimitives.ReadUInt32LittleEndian(value); }
        if (size == 0 || size > int.MaxValue || stride == 0 || stride > int.MaxValue) throw new InvalidDataException("Attribute shape is not representable.");
        var result = new AttributeStreamMetadata((AttributeFlags)header[8], (int)size, (int)stride, serialized, (int)(declared - 6));
        ValidateMetadata(result, options); return result;
    }
    public static TypedAttributeArray<T, TCodec> ReadBuffers<T, TCodec>(Stream stream, AttributeStreamMetadata metadata, AttributeReadOptions? options = null)
        where T : unmanaged where TCodec : unmanaged, IAttributeCodec<T>
    {
        ArgumentNullException.ThrowIfNull(stream); ArgumentNullException.ThrowIfNull(metadata); options ??= new(); options.Validate(); ValidateMetadata(metadata, options);
        if (metadata.IsPaged) throw new NotSupportedException("Paged attribute buffers require a paged stream.");
        int width = TCodec.StorageSize;
        long dataSize = metadata.HasConstantStride ? (long)metadata.Size * metadata.StrideOrTotalSize : metadata.StrideOrTotalSize;
        long bytes = (metadata.IsUniform ? 1 : dataSize) * width;
        if (width <= 0 || dataSize > int.MaxValue || bytes > options.MaximumStorageBytes || bytes > Array.MaxLength) throw new InvalidDataException("Decoded attribute storage exceeds its budget.");
        int expected = (int)bytes; long allocated = 0;
        void Reserve(long count)
        {
            if (count < 0 || count > options.MaximumTotalAllocationBytes - allocated) throw new InvalidDataException("Attribute decoding exceeds its allocation budget.");
            allocated += count; options.CancellationToken.ThrowIfCancellationRequested();
        }
        int compression = 0;
        if (!metadata.IsUniform) { Span<byte> marker = stackalloc byte[1]; ReadExactly(stream, marker, options.CancellationToken); compression = marker[0]; if (compression > 1) throw new InvalidDataException("Unknown attribute compression marker."); }
        if (compression == 0 && metadata.EncodedBytes != expected) throw new InvalidDataException("Raw attribute payload size does not match its shape.");
        Reserve(metadata.EncodedBytes); byte[] payload = new byte[metadata.EncodedBytes]; ReadExactly(stream, payload, options.CancellationToken);
        byte[] storage;
        if (compression == 0) storage = payload;
        else
        {
            uint decodedLength = checked((uint)BloscCodec.ValidateFrame(payload));
            bool padded = decodedLength == 128 && expected <= 128;
            if ((!padded && decodedLength != expected) || decodedLength > options.MaximumStorageBytes || decodedLength > Array.MaxLength) throw new InvalidDataException("Attribute Blosc frame size does not match its shape or budget.");
            Reserve(decodedLength); var decoded = new byte[(int)decodedLength]; BloscCodec.Decode(payload, decoded, Reserve);
            if (decoded.Length == expected) storage = decoded;
            else { Reserve(expected); storage = decoded.AsSpan(0, expected).ToArray(); }
        }
        Reserve(width);
        var array = new TypedAttributeArray<T, TCodec>(metadata.Size, metadata.StrideOrTotalSize, metadata.HasConstantStride, maximumStorageBytes: options.MaximumStorageBytes);
        array.LoadSerializedStorage(storage, metadata.IsUniform, metadata.Flags & ~AttributeFlags.PartialRead);
        return array;
    }
    /// <summary>Reads buffers using a runtime descriptor type and a scoped factory registry.</summary>
    public static AttributeArray ReadBuffers(Stream stream, AttributeType type, AttributeArrayRegistry registry, AttributeStreamMetadata metadata, AttributeReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream); ArgumentNullException.ThrowIfNull(registry); ArgumentNullException.ThrowIfNull(metadata);
        options ??= new(); options.Validate(); ValidateMetadata(metadata, options);
        if (metadata.IsPaged) throw new NotSupportedException("Paged attribute buffers require a paged stream.");
        AttributeArray array = registry.Create(type, metadata.Size, metadata.StrideOrTotalSize, metadata.HasConstantStride);
        int width = array.StorageTypeSize;
        long dataSize = metadata.HasConstantStride ? (long)metadata.Size * metadata.StrideOrTotalSize : metadata.StrideOrTotalSize;
        long bytes = (metadata.IsUniform ? 1 : dataSize) * width;
        if (width <= 0 || dataSize > int.MaxValue || bytes > options.MaximumStorageBytes || bytes > Array.MaxLength)
            throw new InvalidDataException("Decoded attribute storage exceeds its budget.");
        int expected = (int)bytes; long allocated = 0;
        void Reserve(long count)
        {
            if (count < 0 || count > options.MaximumTotalAllocationBytes - allocated)
                throw new InvalidDataException("Attribute decoding exceeds its allocation budget.");
            allocated += count; options.CancellationToken.ThrowIfCancellationRequested();
        }
        int compression = 0;
        if (!metadata.IsUniform)
        {
            Span<byte> marker = stackalloc byte[1]; ReadExactly(stream, marker, options.CancellationToken);
            compression = marker[0]; if (compression > 1) throw new InvalidDataException("Unknown attribute compression marker.");
        }
        if (compression == 0 && metadata.EncodedBytes != expected)
            throw new InvalidDataException("Raw attribute payload size does not match its shape.");
        Reserve(metadata.EncodedBytes); byte[] payload = new byte[metadata.EncodedBytes]; ReadExactly(stream, payload, options.CancellationToken);
        byte[] storage;
        if (compression == 0) storage = payload;
        else
        {
            uint decodedLength = checked((uint)BloscCodec.ValidateFrame(payload));
            bool padded = decodedLength == 128 && expected <= 128;
            if ((!padded && decodedLength != expected) || decodedLength > options.MaximumStorageBytes || decodedLength > Array.MaxLength)
                throw new InvalidDataException("Attribute Blosc frame size does not match its shape or budget.");
            Reserve(decodedLength); var decoded = new byte[(int)decodedLength]; BloscCodec.Decode(payload, decoded, Reserve);
            if (decoded.Length == expected) storage = decoded;
            else { Reserve(expected); storage = decoded.AsSpan(0, expected).ToArray(); }
        }
        array.LoadSerializedStorage(storage, metadata.IsUniform, metadata.Flags & ~AttributeFlags.PartialRead);
        return array;
    }
    private static void ValidateMetadata(AttributeStreamMetadata metadata, AttributeReadOptions options)
    {
        if (metadata.Size < 1 || metadata.StrideOrTotalSize < 1 || (!metadata.HasConstantStride && metadata.StrideOrTotalSize < metadata.Size) ||
            metadata.EncodedBytes < 0 || metadata.EncodedBytes > options.MaximumEncodedBytes) throw new InvalidDataException("Invalid attribute metadata.");
        if ((metadata.SerializationFlags & 0xF0) != 0) throw new NotSupportedException("Unknown attribute serialization flags.");
        if ((metadata.SerializationFlags & 1) == 0 && metadata.StrideOrTotalSize != 1) throw new InvalidDataException("Unstrided metadata contains a stride.");
    }
    private static void ReadExactly(Stream stream, Span<byte> destination, CancellationToken cancellationToken)
    {
        while (!destination.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = stream.Read(destination[..System.Math.Min(65536, destination.Length)]);
            if (count == 0) throw new EndOfStreamException();
            destination = destination[count..];
        }
    }
}
