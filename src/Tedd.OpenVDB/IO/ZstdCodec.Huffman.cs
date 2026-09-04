// Copyright (c) Meta Platforms, Inc. and affiliates.
// SPDX-License-Identifier: BSD-3-Clause
using System.Numerics;
namespace Tedd.OpenVDB.IO;

internal static partial class ZstdCodec
{
    private static int DecodeLiterals(ReadOnlySpan<byte> input, ref int source, Context context, int maximum)
    {
        if (input.IsEmpty) throw Corrupt("missing literals");
        int kind = input[0] & 3, sizeCode = (input[0] >> 2) & 3, length, compressed;
        if (kind < 2)
        {
            int bytes = sizeCode switch { 1 => 2, 3 => 3, _ => 1 };
            uint header = (uint)Take(input, ref source, bytes); length = (int)(header >> (bytes == 1 ? 3 : 4));
            if (length > maximum || length > context.Literals.Length) throw Corrupt("literal size exceeds output");
            if (kind == 1) context.Literals.AsSpan(0, length).Fill((byte)Take(input, ref source, 1));
            else
            {
                if (length > input.Length - source) throw Corrupt("truncated literals"); input.Slice(source, length).CopyTo(context.Literals); source += length;
            }
            return length;
        }
        if (input.Length < 5) throw Corrupt("truncated compressed literals header");
        int headerSize = sizeCode < 2 ? 3 : sizeCode == 2 ? 4 : 5;
        ulong fields = Take(input, ref source, headerSize);
        int sizeBits = sizeCode < 2 ? 10 : sizeCode == 2 ? 14 : 18;
        length = (int)((fields >> 4) & ((1UL << sizeBits) - 1)); compressed = (int)(fields >> (4 + sizeBits));
        if (length > maximum || length > context.Literals.Length || compressed > input.Length - source || (sizeCode != 0 && length < 6)) throw Corrupt("invalid compressed literals size");
        ReadOnlySpan<byte> packed = input.Slice(source, compressed); source += compressed;
        if (kind == 2) packed = packed[ReadHuffmanTable(packed, context)..];
        else if (context.HuffmanLog == 0) throw Corrupt("repeated Huffman table is absent");
        if (sizeCode == 0) DecodeHuffman(packed, context.Literals.AsSpan(0, length), context);
        else
        {
            int position = 0;
            int first = (int)Take(packed, ref position, 2), second = (int)Take(packed, ref position, 2), third = (int)Take(packed, ref position, 2);
            int segment = (length + 3) / 4;
            if (first + second + third > packed.Length - position) throw Corrupt("Huffman jump table");
            DecodeHuffman(packed.Slice(position, first), context.Literals.AsSpan(0, segment), context); position += first;
            DecodeHuffman(packed.Slice(position, second), context.Literals.AsSpan(segment, segment), context); position += second;
            DecodeHuffman(packed.Slice(position, third), context.Literals.AsSpan(segment * 2, segment), context); position += third;
            DecodeHuffman(packed[position..], context.Literals.AsSpan(segment * 3, length - segment * 3), context);
        }
        return length;
    }
    private static int ReadHuffmanTable(ReadOnlySpan<byte> input, Context context)
    {
        if (input.IsEmpty) throw Corrupt("missing Huffman weights");
        Span<byte> weights = stackalloc byte[256]; Span<int> ranks = stackalloc int[13]; ranks.Clear();
        int header = input[0], count, consumed;
        if (header >= 128)
        {
            count = header - 127; consumed = 1 + (count + 1) / 2;
            if (consumed > input.Length) throw Corrupt("truncated Huffman weights");
            for (int index = 0; index < count; ++index) weights[index] = (byte)((input[1 + index / 2] >> ((index & 1) == 0 ? 4 : 0)) & 15);
        }
        else
        {
            consumed = 1 + header;
            if (consumed > input.Length) throw Corrupt("truncated compressed Huffman weights");
            ReadOnlySpan<byte> compressed = input.Slice(1, header); Span<short> counts = stackalloc short[256];
            int tableBytes = ReadCounts(compressed, counts, 6, out int log); BuildFse(context.Weights, counts, log);
            var bits = new ReverseBits(compressed[tableBytes..]);
            int first = (int)bits.Read(log), second = (int)bits.Read(log); count = 0;
            while (true)
            {
                if (count > 253) throw Corrupt("too many Huffman weights");
                FseEntry entry = context.Weights.Entries[first]; weights[count++] = entry.Symbol;
                if (entry.Bits > bits.Remaining) { weights[count++] = context.Weights.Entries[second].Symbol; break; }
                first = entry.Next + (int)bits.Read(entry.Bits);
                if (count > 253) throw Corrupt("too many Huffman weights");
                entry = context.Weights.Entries[second]; weights[count++] = entry.Symbol;
                if (entry.Bits > bits.Remaining) { weights[count++] = context.Weights.Entries[first].Symbol; break; }
                second = entry.Next + (int)bits.Read(entry.Bits);
            }
        }
        int total = 0;
        for (int index = 0; index < count; ++index)
        {
            int weight = weights[index]; if (weight > 12) throw Corrupt("excessive Huffman weight"); ++ranks[weight]; total += (1 << weight) >> 1;
        }
        if (total == 0) throw Corrupt("empty Huffman tree");
        int tableLog = BitOperations.Log2((uint)total) + 1;
        if (tableLog > 12) throw Corrupt("excessive Huffman table");
        int missing = (1 << tableLog) - total;
        if ((missing & (missing - 1)) != 0) throw Corrupt("incomplete Huffman tree");
        int lastWeight = BitOperations.Log2((uint)missing) + 1; weights[count++] = (byte)lastWeight; ++ranks[lastWeight];
        if (ranks[1] < 2 || (ranks[1] & 1) != 0) throw Corrupt("invalid Huffman rank distribution");
        int position = 0;
        for (int weight = 1; weight <= tableLog; ++weight)
            for (int symbol = 0; symbol < count; ++symbol)
                if (weights[symbol] == weight)
                {
                    int repetitions = 1 << (weight - 1);
                    context.Huffman.AsSpan(position, repetitions).Fill((ushort)(symbol | ((tableLog + 1 - weight) << 8))); position += repetitions;
                }
        if (position != 1 << tableLog) throw Corrupt("invalid Huffman lookup size");
        context.HuffmanLog = tableLog; return consumed;
    }
    private static void DecodeHuffman(ReadOnlySpan<byte> input, Span<byte> output, Context context)
    {
        var bits = new ReverseBits(input);
        for (int index = 0; index < output.Length; ++index)
        {
            ushort entry = context.Huffman[bits.Peek(context.HuffmanLog)]; int count = entry >> 8;
            if (count == 0) throw Corrupt("empty Huffman lookup"); bits.Read(count); output[index] = (byte)entry;
        }
        if (bits.Remaining != 0) throw Corrupt("unused Huffman bits");
    }
}
