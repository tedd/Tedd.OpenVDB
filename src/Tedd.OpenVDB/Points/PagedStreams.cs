// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of points/StreamCompression.h and StreamCompression.cc.
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using Tedd.OpenVDB.IO;

namespace Tedd.OpenVDB.Points;

public sealed record PagedReadOptions
{
    public int MaximumPageBytes { get; init; } = 268_435_456;
    public int MaximumEncodedPageBytes { get; init; } = 268_435_456;
    public long MaximumTotalAllocationBytes { get; init; } = 536_870_912;
    public CancellationToken CancellationToken { get; init; }
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumPageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumEncodedPageBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumTotalAllocationBytes);
        CancellationToken.ThrowIfCancellationRequested();
    }
}

/// <summary>A shared eager point-data page decoded from native split header/data passes.</summary>
public sealed class Page
{
    private readonly object _gate = new();
    private byte[]? _data;
    private ExceptionDispatchInfo? _loadFailure;
    internal Page(int compressedBytes, int uncompressedBytes)
    {
        CompressedBytes = compressedBytes; UncompressedBytes = uncompressedBytes;
    }
    public int CompressedBytes { get; }
    public int UncompressedBytes { get; }
    public bool IsLoaded => Volatile.Read(ref _data) is not null;
    internal void ReadBuffers(Stream stream, PagedReadOptions options, Action<long> reserve)
    {
        lock (_gate)
        {
            int encodedBytes = CompressedBytes > 0 ? CompressedBytes : checked(-CompressedBytes);
            if (encodedBytes > options.MaximumEncodedPageBytes || encodedBytes > Array.MaxLength) throw new InvalidDataException("Encoded page exceeds its budget.");
            if (_data is not null)
            {
                SkipExactly(stream, encodedBytes, options.CancellationToken);
                return;
            }
            _loadFailure?.Throw();
            try
            {
                reserve(encodedBytes);
                byte[] encoded = GC.AllocateUninitializedArray<byte>(encodedBytes);
                ReadExactly(stream, encoded, options.CancellationToken);
                if (CompressedBytes < 0)
                {
                    if (encodedBytes != UncompressedBytes) throw new InvalidDataException("Raw page size is inconsistent.");
                    _data = encoded;
                    return;
                }
                uint decodedLength = checked((uint)BloscCodec.ValidateFrame(encoded));
                bool padded = decodedLength == 128 && UncompressedBytes <= 128;
                if ((!padded && decodedLength != UncompressedBytes) || decodedLength > options.MaximumPageBytes || decodedLength > Array.MaxLength)
                    throw new InvalidDataException("Compressed page size is inconsistent.");
                reserve(decodedLength);
                byte[] decoded = GC.AllocateUninitializedArray<byte>((int)decodedLength);
                BloscCodec.Decode(encoded, decoded, reserve);
                if (decoded.Length == UncompressedBytes) _data = decoded;
                else { reserve(UncompressedBytes); _data = decoded.AsSpan(0, UncompressedBytes).ToArray(); }
            }
            catch (Exception exception)
            {
                _loadFailure = ExceptionDispatchInfo.Capture(exception);
                throw;
            }
        }
    }
    internal void EnsureLoaded()
    {
        if (Volatile.Read(ref _data) is not null) return;
        lock (_gate)
        {
            _loadFailure?.Throw();
            if (_data is null) throw new InvalidOperationException("Page buffers have not been read.");
        }
    }
    internal void Copy(int index, Span<byte> destination)
    {
        byte[]? data = Volatile.Read(ref _data);
        if (data is null)
        {
            lock (_gate)
            {
                _loadFailure?.Throw();
                data = _data ?? throw new InvalidOperationException("Page buffers have not been read.");
            }
        }
        if (index < 0 || index > data.Length - destination.Length) throw new InvalidDataException("Page handle exceeds its page.");
        data.AsSpan(index, destination.Length).CopyTo(destination);
    }
    private static void SkipExactly(Stream stream, int byteCount, CancellationToken token)
    {
        Span<byte> scratch = stackalloc byte[4096];
        while (byteCount != 0)
        {
            int count = System.Math.Min(byteCount, scratch.Length);
            ReadExactly(stream, scratch[..count], token);
            byteCount -= count;
        }
    }
    private static void ReadExactly(Stream stream, Span<byte> destination, CancellationToken token)
    {
        while (!destination.IsEmpty)
        {
            token.ThrowIfCancellationRequested();
            int count = stream.Read(destination[..System.Math.Min(65536, destination.Length)]);
            if (count == 0) throw new EndOfStreamException();
            destination = destination[count..];
        }
    }
}

/// <summary>A range within one shared point-data page.</summary>
public sealed class PageHandle
{
    internal PageHandle(Page page, int index, int size) { Page = page; Index = index; Size = size; }
    public Page Page { get; }
    public int Index { get; }
    public int Size { get; }
    public byte[] Read()
    {
        Page.EnsureLoaded();
        byte[] result = GC.AllocateUninitializedArray<byte>(Size);
        Page.Copy(Index, result); return result;
    }
    public void Read(Span<byte> destination)
    {
        if (destination.Length != Size) throw new ArgumentException("Destination length must equal the handle size.", nameof(destination));
        Page.Copy(Index, destination);
    }
    public PageHandle Copy() => new(Page, Index, Size);
}

/// <summary>Reads OpenVDB's page headers and page buffers in two coordinated passes.</summary>
/// <remarks>Without a mapped-file owner, native delayed reads fall back to eager loading; this managed stream has the same eager behavior.</remarks>
public sealed class PagedInputStream
{
    private readonly Stream _stream;
    private readonly PagedReadOptions _options;
    private int _byteIndex;
    private int _uncompressedBytes;
    private Page? _page;
    private long _allocated;

    public PagedInputStream(Stream stream, PagedReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead) throw new ArgumentException("Stream is not readable.", nameof(stream));
        _stream = stream; _options = options ?? new(); _options.Validate();
    }

    public PageHandle CreateHandle(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);
        _options.CancellationToken.ThrowIfCancellationRequested();
        if (_byteIndex == _uncompressedBytes)
        {
            Span<byte> integer = stackalloc byte[4]; ReadExactly(integer);
            int compressed = BinaryPrimitives.ReadInt32LittleEndian(integer);
            if (compressed == 0 || compressed == int.MinValue) throw new InvalidDataException("Page header contains an invalid size.");
            int uncompressed;
            if (compressed > 0)
            {
                ReadExactly(integer); uncompressed = BinaryPrimitives.ReadInt32LittleEndian(integer);
                if (uncompressed <= 0) throw new InvalidDataException("Compressed page has an invalid decoded size.");
            }
            else uncompressed = -compressed;
            if (uncompressed > _options.MaximumPageBytes) throw new InvalidDataException("Page exceeds its decoded-size budget.");
            _page = new(compressed, uncompressed); _uncompressedBytes = uncompressed; _byteIndex = 0;
        }
        if (_page is null || byteCount > _uncompressedBytes - _byteIndex)
            throw new InvalidDataException("Page handle crosses a native page boundary.");
        var handle = new PageHandle(_page, _byteIndex, byteCount);
        _byteIndex += byteCount;
        return handle;
    }

    public void Read(PageHandle handle, int byteCount, bool delayed = true)
    {
        ArgumentNullException.ThrowIfNull(handle);
        if (byteCount != handle.Size) throw new ArgumentException("Read size differs from the page handle.", nameof(byteCount));
        _options.CancellationToken.ThrowIfCancellationRequested();
        Page page = handle.Page;
        int expectedIndex = _byteIndex == _uncompressedBytes ? 0 : _byteIndex;
        if (handle.Index != expectedIndex) throw new InvalidDataException("Page handles were not replayed in size-pass order.");
        if (_byteIndex == _uncompressedBytes)
        {
            page.ReadBuffers(_stream, _options, Reserve);
            _uncompressedBytes = page.UncompressedBytes;
            _byteIndex = 0; _page = page;
        }
        else if (!ReferenceEquals(_page, page))
            throw new InvalidDataException("Page handles were not replayed in size-pass order.");
        _byteIndex = checked(_byteIndex + byteCount);
        if (_byteIndex > _uncompressedBytes) throw new InvalidDataException("Page reads exceed the decoded page.");
        _ = delayed;
    }

    public bool AtPageBoundary => _byteIndex == _uncompressedBytes;

    private void Reserve(long count)
    {
        if (count < 0 || count > _options.MaximumTotalAllocationBytes - _allocated)
            throw new InvalidDataException("Paged decoding exceeds its allocation budget.");
        _allocated += count; _options.CancellationToken.ThrowIfCancellationRequested();
    }

    private void ReadExactly(Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            int count = _stream.Read(destination);
            if (count == 0) throw new EndOfStreamException();
            destination = destination[count..];
        }
    }
}

/// <summary>Buffers blocks into one-mebibyte OpenVDB point-data pages.</summary>
/// <remarks>Size-only and data passes must receive identical block lengths and ordering. Stream ownership remains with the caller.</remarks>
public sealed class PagedOutputStream : IDisposable
{
    public const int PageSize = 1024 * 1024;
    private readonly Stream _stream;
    private byte[]? _buffer;
    private BloscCodec.EncoderWorkspace? _encoder;
    private int _bytes;
    private bool _disposed;

    public PagedOutputStream(Stream stream, bool sizeOnly = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite) throw new ArgumentException("Stream is not writable.", nameof(stream));
        _stream = stream; SizeOnly = sizeOnly;
    }

    public bool SizeOnly { get; }

    public void Write(ReadOnlySpan<byte> source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (source.IsEmpty) return;
        if (source.Length > PageSize)
        {
            Flush();
            CompressAndWrite(source);
            return;
        }
        if (source.Length > PageSize - _bytes) Flush();
        _buffer ??= ArrayPool<byte>.Shared.Rent(PageSize);
        source.CopyTo(_buffer.AsSpan(_bytes));
        _bytes += source.Length;
    }

    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_bytes == 0) return;
        CompressAndWrite(_buffer.AsSpan(0, _bytes));
        _bytes = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_buffer is not null) ArrayPool<byte>.Shared.Return(_buffer);
        _encoder?.Dispose();
        _buffer = null; _encoder = null; _bytes = 0; _disposed = true;
    }

    private void CompressAndWrite(ReadOnlySpan<byte> source)
    {
        Span<byte> padded = stackalloc byte[128];
        int compressedBytes = 0;
        if (source.Length > 48)
        {
            scoped ReadOnlySpan<byte> input = source;
            if (source.Length < padded.Length)
            {
                padded.Clear();
                source.CopyTo(padded);
                input = padded;
            }
            _encoder ??= new BloscCodec.EncoderWorkspace();
            compressedBytes = _encoder.EncodeIfSmallerThan(input, source.Length);
        }

        if (SizeOnly)
        {
            Span<byte> header = stackalloc byte[8];
            if (compressedBytes == 0)
            {
                BinaryPrimitives.WriteInt32LittleEndian(header, -source.Length);
                _stream.Write(header[..4]);
            }
            else
            {
                BinaryPrimitives.WriteInt32LittleEndian(header, compressedBytes);
                BinaryPrimitives.WriteInt32LittleEndian(header[4..], source.Length);
                _stream.Write(header);
            }
        }
        else if (compressedBytes == 0) _stream.Write(source);
        else _stream.Write(_encoder!.GetFrame(compressedBytes));
    }
}
