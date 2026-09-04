// Copyright (C) 2011-2020, Yann Collet.
// SPDX-License-Identifier: BSD-2-Clause
// Managed LZ4 block format implementation based on bundled lz4 1.9.4.
// See THIRD_PARTY_NOTICES.md for the retained license.
using System.Buffers;
using System.Buffers.Binary;
namespace Tedd.OpenVDB.IO;

internal static class Lz4BlockCodec
{
    private const int HashTableSize = 65536;

    internal static void Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        int source = 0, target = 0; bool matched = false;
        while (source < input.Length)
        {
            byte token = input[source++];
            int literals = ReadLength(input, ref source, token >> 4, output.Length - target);
            if (literals > input.Length - source) throw new InvalidDataException("Truncated LZ4 literals.");
            input.Slice(source, literals).CopyTo(output[target..]); source += literals; target += literals;
            if (source == input.Length)
            {
                if (target != output.Length || (matched && literals < 5)) throw new InvalidDataException("Invalid LZ4 final literals or output size.");
                return;
            }
            if (source > input.Length - 2 || target > output.Length - 12) throw new InvalidDataException("Truncated LZ4 match or invalid final match position.");
            int distance = BinaryPrimitives.ReadUInt16LittleEndian(input[source..]); source += 2;
            if (distance == 0 || distance > target) throw new InvalidDataException("LZ4 match offset is outside the decoded window.");
            int match = ReadLength(input, ref source, token & 15, output.Length - target - 4) + 4;
            // Overlap is intentional: an offset of one repeats the preceding byte.
            for (int index = 0; index < match; ++index) output[target + index] = output[target + index - distance];
            target += match; matched = true;
        }
        throw new InvalidDataException("LZ4 block has no final literal sequence.");
    }
    private static int ReadLength(ReadOnlySpan<byte> input, ref int offset, int length, int maximum)
    {
        if (length > maximum) throw new InvalidDataException("LZ4 sequence exceeds its output buffer.");
        if (length != 15) return length;
        byte next;
        do
        {
            if (offset == input.Length) throw new InvalidDataException("Truncated LZ4 extended length.");
            next = input[offset++];
            if (next > maximum - length) throw new InvalidDataException("LZ4 extended length exceeds its output buffer.");
            length += next;
        } while (next == 255);
        return length;
    }

    internal static int MaximumEncodedLength(int inputLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputLength);
        return checked(inputLength + inputLength / 255 + 16);
    }

    internal static int Encode(ReadOnlySpan<byte> input, Span<byte> output, Span<int> table)
    {
        int maximumLength = MaximumEncodedLength(input.Length);
        if (output.Length < maximumLength) throw new ArgumentException("The LZ4 output buffer is too small.", nameof(output));
        if (table.Length < HashTableSize) throw new ArgumentException("The LZ4 hash table is too small.", nameof(table));
        table[..HashTableSize].Fill(-1);
        int source = 0, anchor = 0, destination = 0;
        while (source <= input.Length - 12)
        {
            uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(input[source..]);
            int hash = (int)(unchecked(sequence * 2654435761u) >> 16);
            int match = table[hash]; table[hash] = source;
            if (match < 0 || source - match > 65535 || BinaryPrimitives.ReadUInt32LittleEndian(input[match..]) != sequence) { ++source; continue; }
            int length = 4;
            while (source + length < input.Length - 5 && input[match + length] == input[source + length]) ++length;
            int literals = source - anchor, matchLength = length - 4;
            output[destination++] = (byte)((System.Math.Min(literals, 15) << 4) | System.Math.Min(matchLength, 15));
            if (literals >= 15) WriteLength(output, ref destination, literals - 15);
            input.Slice(anchor, literals).CopyTo(output[destination..]); destination += literals;
            BinaryPrimitives.WriteUInt16LittleEndian(output[destination..], (ushort)(source - match)); destination += 2;
            if (matchLength >= 15) WriteLength(output, ref destination, matchLength - 15);
            source += length; anchor = source;
        }
        int last = input.Length - anchor;
        output[destination++] = (byte)(System.Math.Min(last, 15) << 4);
        if (last >= 15) WriteLength(output, ref destination, last - 15);
        input[anchor..].CopyTo(output[destination..]); destination += last;
        return destination;
    }

    internal static byte[] Encode(ReadOnlySpan<byte> input)
    {
        int maximumLength = MaximumEncodedLength(input.Length);
        byte[] output = ArrayPool<byte>.Shared.Rent(maximumLength);
        int[] table = ArrayPool<int>.Shared.Rent(HashTableSize);
        try
        {
            int length = Encode(input, output, table);
            byte[] result = GC.AllocateUninitializedArray<byte>(length);
            output.AsSpan(0, length).CopyTo(result);
            return result;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(table);
            ArrayPool<byte>.Shared.Return(output);
        }
    }
    private static void WriteLength(Span<byte> output, ref int offset, int length)
    {
        while (length >= 255) { output[offset++] = 255; length -= 255; }
        output[offset++] = (byte)length;
    }
}
