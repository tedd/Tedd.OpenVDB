// Copyright (c) 2009-2018 Francesc Alted <francesc@blosc.org>
// Copyright (c) 2019-present Blosc Development Team <blosc@blosc.org>
// SPDX-License-Identifier: BSD-3-Clause
// Frame and shuffle port from c-blosc 1.21.6, commit 616f4b7343a8479f7e71dd3d7025bd92c9a6bbd0.
// See THIRD_PARTY_NOTICES.md for the retained license.
using System.Buffers.Binary;
namespace Tedd.OpenVDB.IO;

internal static class BloscCodec
{
    private const int HeaderSize = 16;
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

    internal static byte[] Encode(ReadOnlySpan<byte> input)
    {
        const int typeSize = 4; // OpenVDB's native Blosc writer always shuffles as float-sized elements.
        if (input.Length < 128) return MemcpyFrame(input);
        var shuffled = new byte[input.Length]; Shuffle(input, shuffled, typeSize);
        bool split = input.Length % typeSize == 0 && input.Length / typeSize >= 128;
        int splits = split ? typeSize : 1, splitSize = input.Length / splits;
        using var output = new MemoryStream(input.Length + 64); using var writer = new BinaryWriter(output);
        writer.Write(new byte[HeaderSize]); writer.Write(20); // One block, beginning immediately after its offset.
        for (int index = 0; index < splits; ++index)
        {
            ReadOnlySpan<byte> source = shuffled.AsSpan(index * splitSize, splitSize);
            byte[] packed = Lz4BlockCodec.Encode(source);
            if (packed.Length < source.Length) { writer.Write(packed.Length); writer.Write(packed); }
            else { writer.Write(source.Length); writer.Write(source); }
        }
        if (output.Length >= input.Length + HeaderSize) return MemcpyFrame(input);
        byte[] result = output.ToArray(); WriteHeader(result, input.Length, input.Length, split ? (byte)0x21 : (byte)0x31);
        return result;
    }
    private static byte[] MemcpyFrame(ReadOnlySpan<byte> input)
    {
        var result = new byte[checked(input.Length + HeaderSize)];
        WriteHeader(result, input.Length, System.Math.Max(input.Length, 1), 0x33);
        input.CopyTo(result.AsSpan(HeaderSize)); return result;
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
