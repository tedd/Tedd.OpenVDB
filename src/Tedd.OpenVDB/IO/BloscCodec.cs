// Copyright (c) 2009-2018 Francesc Alted <francesc@blosc.org>
// Copyright (c) 2019-present Blosc Development Team <blosc@blosc.org>
// SPDX-License-Identifier: BSD-3-Clause
// Frame and shuffle port from c-blosc 1.21.6, commit 616f4b7343a8479f7e71dd3d7025bd92c9a6bbd0.
// See THIRD_PARTY_NOTICES.md for the retained license.
using System.Buffers;
using System.Buffers.Binary;
namespace Tedd.OpenVDB.IO;

internal static class BloscCodec
{
    private const int HeaderSize = 16;
    internal static int ValidateFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeaderSize) throw new InvalidDataException("Truncated Blosc header.");
        if (frame[0] != 2) throw new NotSupportedException("Only Blosc frame version 2 is supported.");
        int flags = frame[2], typeSize = frame[3];
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(frame[4..]);
        uint blockLength = BinaryPrimitives.ReadUInt32LittleEndian(frame[8..]);
        uint compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(frame[12..]);
        if ((flags & 8) != 0 || typeSize == 0 || compressedLength != frame.Length || length > Array.MaxLength || blockLength == 0)
            throw new InvalidDataException("Invalid Blosc header sizes or flags.");
        if ((flags & 2) != 0)
        {
            if (frame.Length != checked((int)length + HeaderSize)) throw new InvalidDataException("Invalid Blosc memcpy size.");
            return (int)length;
        }
        if (length == 0 || blockLength > length || blockLength > int.MaxValue)
            throw new InvalidDataException("Invalid Blosc block size.");
        int codec = flags >> 5;
        if (codec is not (0 or 1 or 2 or 3 or 4))
            throw new NotSupportedException($"Blosc compressor ID {codec} is unsupported; BloscLZ, LZ4, Snappy, zlib and Zstd are implemented.");
        if (frame[1] != 1) throw new NotSupportedException("Unsupported Blosc compressor format version.");
        int count = 1 + ((int)length - 1) / (int)blockLength;
        if (count > (frame.Length - HeaderSize) / 4) throw new InvalidDataException("Truncated Blosc block table.");
        int dataStart = checked(HeaderSize + count * 4);
        int[] boundaries = ArrayPool<int>.Shared.Rent(count);
        try
        {
            for (int index = 0; index < count; ++index)
            {
                uint start = BinaryPrimitives.ReadUInt32LittleEndian(frame[(HeaderSize + index * 4)..]);
                if (start < dataStart || start > frame.Length - 4) throw new InvalidDataException("Invalid Blosc block offset.");
                boundaries[index] = (int)start;
            }
            boundaries.AsSpan(0, count).Sort();
            if (boundaries[0] != dataStart) throw new InvalidDataException("Gap before the first Blosc block.");
            for (int index = 1; index < count; ++index)
                if (boundaries[index] == boundaries[index - 1]) throw new InvalidDataException("Duplicate Blosc block offset.");
        }
        finally
        {
            ArrayPool<int>.Shared.Return(boundaries);
        }
        return (int)length;
    }
    internal static void Decode(byte[] frame, Span<byte> destination, Action<long> reserve)
    {
        if (frame.Length < HeaderSize) throw new InvalidDataException("Truncated Blosc header.");
        if (frame[0] != 2) throw new NotSupportedException("Only Blosc frame version 2 is supported.");
        int flags = frame[2], typeSize = frame[3];
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(4));
        uint blockLength = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8));
        uint compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12));
        if ((flags & 8) != 0 || typeSize == 0 || compressedLength != frame.Length || length != destination.Length || blockLength == 0)
            throw new InvalidDataException("Invalid Blosc header sizes or flags.");
        if ((flags & 2) != 0)
        {
            // Native memcpy frames ignore both filter and codec flags.
            if (frame.Length != destination.Length + HeaderSize) throw new InvalidDataException("Invalid Blosc memcpy size.");
            frame.AsSpan(HeaderSize).CopyTo(destination); return;
        }
        if (destination.IsEmpty) throw new InvalidDataException("An empty Blosc frame must use memcpy storage.");
        if (blockLength > length || blockLength > int.MaxValue) throw new InvalidDataException("Invalid Blosc block size.");
        int codec = flags >> 5;
        if (codec is not (0 or 1 or 2 or 3 or 4)) throw new NotSupportedException($"Blosc compressor ID {codec} is unsupported; BloscLZ, LZ4, Snappy, zlib and Zstd are implemented.");
        if (frame[1] != 1) throw new NotSupportedException("Unsupported Blosc compressor format version.");
        int blockSize = (int)blockLength;
        int count = 1 + (destination.Length - 1) / blockSize;
        if (count > (frame.Length - HeaderSize) / 4) throw new InvalidDataException("Truncated Blosc block table.");
        int dataStart = checked(HeaderSize + count * 4);
        reserve(checked((long)count * 4));
        var boundaries = new int[count];
        for (int index = 0; index < count; ++index)
        {
            uint start = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(HeaderSize + index * 4));
            if (start < dataStart || start > frame.Length - 4) throw new InvalidDataException("Invalid Blosc block offset.");
            boundaries[index] = (int)start;
        }
        Array.Sort(boundaries);
        if (boundaries[0] != dataStart) throw new InvalidDataException("Gap before the first Blosc block.");
        for (int index = 1; index < boundaries.Length; ++index)
            if (boundaries[index] == boundaries[index - 1]) throw new InvalidDataException("Duplicate Blosc block offset.");
        bool shuffle = (flags & 1) != 0 && typeSize > 1;
        bool bitShuffle = !shuffle && (flags & 4) != 0;
        byte[]? temporary = null;
        if (shuffle || bitShuffle) { reserve(blockSize); temporary = new byte[blockSize]; }
        for (int index = 0, outputOffset = 0; index < count; ++index)
        {
            int size = System.Math.Min(blockSize, destination.Length - outputOffset);
            int sourceOffset = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(HeaderSize + index * 4));
            int boundary = Array.BinarySearch(boundaries, sourceOffset);
            int end = boundary + 1 == boundaries.Length ? frame.Length : boundaries[boundary + 1];
            Span<byte> output = destination.Slice(outputOffset, size);
            Span<byte> decoded = shuffle || bitShuffle ? temporary.AsSpan(0, size) : output;
            int splits = (flags & 16) == 0 && typeSize <= 16 && size / typeSize >= 128 && size == blockSize ? typeSize : 1;
            if (size % splits != 0) throw new InvalidDataException("Blosc split size does not divide the block.");
            int splitSize = size / splits;
            for (int split = 0; split < splits; ++split)
            {
                if (sourceOffset > end - 4) throw new InvalidDataException("Truncated Blosc split header.");
                uint compressed = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(sourceOffset)); sourceOffset += 4;
                if (compressed == 0 || compressed > end - sourceOffset) throw new InvalidDataException("Invalid Blosc split length.");
                int compressedSize = (int)compressed;
                Span<byte> target = decoded.Slice(split * splitSize, splitSize);
                if (compressedSize == splitSize) frame.AsSpan(sourceOffset, compressedSize).CopyTo(target);
                else if (codec == 0) BloscLzCodec.Decode(frame.AsSpan(sourceOffset, compressedSize), target);
                else if (codec == 1) Lz4BlockCodec.Decode(frame.AsSpan(sourceOffset, compressedSize), target);
                else if (codec == 2) SnappyBlockCodec.Decode(frame.AsSpan(sourceOffset, compressedSize), target);
                else if (codec == 3) VdbFile.DecompressZlib(frame, sourceOffset, compressedSize, target);
                else ZstdCodec.Decode(frame.AsSpan(sourceOffset, compressedSize), target, reserve);
                sourceOffset += compressedSize;
            }
            if (sourceOffset != end) throw new InvalidDataException("Blosc block lengths overlap or leave unused bytes.");
            if (shuffle) Unshuffle(decoded, output, typeSize);
            else if (bitShuffle) BitShuffleCodec.Unshuffle(decoded, output, typeSize);
            outputOffset += size;
        }
    }

    /// <summary>Reusable pooled storage for repeated Blosc frame encoding.</summary>
    /// <remarks>The returned span remains valid until the next encode operation or disposal.</remarks>
    internal sealed class EncoderWorkspace : IDisposable
    {
        private const int HashTableSize = 65536;
        private byte[]? _frame;
        private byte[]? _shuffled;
        private byte[]? _lz4;
        private int[]? _table;
        private bool _disposed;

        internal ReadOnlySpan<byte> Encode(ReadOnlySpan<byte> input)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (input.Length < 128) return WriteMemcpyFrame(input);
            int exclusiveMaximum = checked(input.Length + HeaderSize);
            int encodedLength = EncodeIfSmallerThan(input, exclusiveMaximum);
            return encodedLength == 0 ? WriteMemcpyFrame(input) : GetFrame(encodedLength);
        }

        internal int EncodeIfSmallerThan(scoped ReadOnlySpan<byte> input, int exclusiveMaximum)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);
            int maximumLength = exclusiveMaximum - 1;
            if (input.Length < 128 || maximumLength < 24) return 0;
            return TryEncodeCompressed(input, maximumLength, out int length) ? length : 0;
        }

        internal ReadOnlySpan<byte> GetFrame(int length)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_frame is null || length <= 0 || length > _frame.Length)
                throw new ArgumentOutOfRangeException(nameof(length));
            return _frame.AsSpan(0, length);
        }

        private bool TryEncodeCompressed(ReadOnlySpan<byte> input, int maximumLength, out int length)
        {
            const int typeSize = 4; // OpenVDB's native Blosc writer always shuffles as float-sized elements.
            bool split = input.Length % typeSize == 0 && input.Length / typeSize >= 128;
            int splits = split ? typeSize : 1, splitSize = input.Length / splits;
            EnsureByteBuffer(ref _frame, maximumLength);
            EnsureByteBuffer(ref _shuffled, input.Length);
            EnsureByteBuffer(ref _lz4, Lz4BlockCodec.MaximumEncodedLength(splitSize));
            _table ??= ArrayPool<int>.Shared.Rent(HashTableSize);

            Span<byte> shuffled = _shuffled.AsSpan(0, input.Length);
            Shuffle(input, shuffled, typeSize);
            Span<byte> frame = _frame.AsSpan(0, maximumLength);
            BinaryPrimitives.WriteInt32LittleEndian(frame[HeaderSize..], HeaderSize + 4);
            int destination = HeaderSize + 4;
            for (int index = 0; index < splits; ++index)
            {
                ReadOnlySpan<byte> source = shuffled.Slice(index * splitSize, splitSize);
                int packedLength = Lz4BlockCodec.Encode(source, _lz4, _table);
                ReadOnlySpan<byte> selected = packedLength < source.Length
                    ? _lz4.AsSpan(0, packedLength)
                    : source;
                if (destination > maximumLength - 4 ||
                    selected.Length > maximumLength - destination - 4)
                {
                    length = 0;
                    return false;
                }
                BinaryPrimitives.WriteInt32LittleEndian(frame[destination..], selected.Length);
                destination += 4;
                selected.CopyTo(frame[destination..]);
                destination += selected.Length;
            }
            WriteHeader(frame[..destination], input.Length, input.Length, split ? (byte)0x21 : (byte)0x31);
            length = destination;
            return true;
        }

        private ReadOnlySpan<byte> WriteMemcpyFrame(ReadOnlySpan<byte> input)
        {
            int length = checked(input.Length + HeaderSize);
            EnsureByteBuffer(ref _frame, length);
            Span<byte> frame = _frame.AsSpan(0, length);
            WriteHeader(frame, input.Length, System.Math.Max(input.Length, 1), 0x33);
            input.CopyTo(frame[HeaderSize..]);
            return frame;
        }

        private static void EnsureByteBuffer(ref byte[]? buffer, int length)
        {
            if (buffer is not null && buffer.Length >= length) return;
            byte[] replacement = ArrayPool<byte>.Shared.Rent(length);
            byte[]? previous = buffer;
            buffer = replacement;
            if (previous is not null) ArrayPool<byte>.Shared.Return(previous);
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (_table is not null) ArrayPool<int>.Shared.Return(_table);
            if (_lz4 is not null) ArrayPool<byte>.Shared.Return(_lz4);
            if (_shuffled is not null) ArrayPool<byte>.Shared.Return(_shuffled);
            if (_frame is not null) ArrayPool<byte>.Shared.Return(_frame);
            _table = null;
            _lz4 = null;
            _shuffled = null;
            _frame = null;
            _disposed = true;
        }
    }

    internal static byte[] Encode(ReadOnlySpan<byte> input)
    {
        using var workspace = new EncoderWorkspace();
        ReadOnlySpan<byte> encoded = workspace.Encode(input);
        byte[] result = GC.AllocateUninitializedArray<byte>(encoded.Length);
        encoded.CopyTo(result);
        return result;
    }
    private static void WriteHeader(Span<byte> output, int length, int blockSize, byte flags)
    {
        output[0] = 2; output[1] = 1; output[2] = flags; output[3] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(output[4..], length);
        BinaryPrimitives.WriteInt32LittleEndian(output[8..], blockSize);
        BinaryPrimitives.WriteInt32LittleEndian(output[12..], output.Length);
    }
    private static void Shuffle(ReadOnlySpan<byte> input, Span<byte> output, int typeSize)
    {
        int count = input.Length / typeSize;
        for (int component = 0; component < typeSize; ++component)
            for (int index = 0; index < count; ++index) output[component * count + index] = input[index * typeSize + component];
        input[(count * typeSize)..].CopyTo(output[(count * typeSize)..]);
    }
    private static void Unshuffle(ReadOnlySpan<byte> input, Span<byte> output, int typeSize)
    {
        int count = input.Length / typeSize;
        for (int index = 0; index < count; ++index)
            for (int component = 0; component < typeSize; ++component) output[index * typeSize + component] = input[component * count + index];
        input[(count * typeSize)..].CopyTo(output[(count * typeSize)..]);
    }
}
