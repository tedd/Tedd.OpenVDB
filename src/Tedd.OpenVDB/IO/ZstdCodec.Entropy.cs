// Copyright (c) Meta Platforms, Inc. and affiliates.
// SPDX-License-Identifier: BSD-3-Clause
using System.Numerics;
namespace Tedd.OpenVDB.IO;

internal static partial class ZstdCodec
{
    private readonly record struct FseEntry(ushort Next, byte Bits, byte Symbol);
    private sealed class FseTable(int capacity)
    {
        internal readonly FseEntry[] Entries = new FseEntry[capacity];
        internal int Log;
        internal bool Valid;
    }
    private static ReadOnlySpan<short> LiteralNorm => [4, 3, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 1, 1, 1, 1, 1, -1, -1, -1, -1];
    private static ReadOnlySpan<short> MatchNorm => [1, 4, 3, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1, -1, -1];
    private static ReadOnlySpan<short> OffsetNorm => [1, 1, 1, 1, 1, 1, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, -1, -1, -1, -1, -1];
    private static ReadOnlySpan<byte> LiteralBits => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 4, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
    private static ReadOnlySpan<byte> MatchBits => [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 3, 3, 4, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
    private static ReadOnlySpan<uint> LiteralBase => [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 18, 20, 22, 24, 28, 32, 40, 48, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536];
    private static ReadOnlySpan<uint> MatchBase => [3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 37, 39, 41, 43, 47, 51, 59, 67, 83, 99, 131, 259, 515, 1027, 2051, 4099, 8195, 16387, 32771, 65539];
    private static void SetSequenceTable(FseTable table, int mode, ReadOnlySpan<byte> input, ref int source, ReadOnlySpan<short> defaults, int defaultLog, int maxSymbol, int maxLog)
    {
        switch (mode)
        {
            case 0: BuildFse(table, defaults, defaultLog); break;
            case 1:
                int symbol = (int)Take(input, ref source, 1); if (symbol > maxSymbol) throw Corrupt("RLE sequence symbol");
                table.Entries[0] = new(0, 0, (byte)symbol); table.Log = 0; table.Valid = true; break;
            case 2:
                Span<short> counts = stackalloc short[256];
                int used = ReadCounts(input[source..], counts[..(maxSymbol + 1)], maxLog, out int log);
                source += used; BuildFse(table, counts[..(maxSymbol + 1)], log); break;
            case 3: if (!table.Valid) throw Corrupt("repeated sequence table is absent"); break;
        }
    }
    private static int ReadCounts(ReadOnlySpan<byte> input, Span<short> counts, int maxLog, out int log)
    {
        counts.Clear(); var bits = new ForwardBits(input); log = (int)bits.Read(4) + 5;
        if (log > maxLog) throw Corrupt("FSE table logarithm");
        int remaining = (1 << log) + 1, threshold = 1 << log, bitCount = log + 1, symbol = 0; bool previousZero = false;
        while (remaining > 1 && symbol < counts.Length)
        {
            if (previousZero)
            {
                uint skip; do { skip = bits.Read(2); if (skip > counts.Length - symbol) throw Corrupt("FSE zero run"); symbol += (int)skip; } while (skip == 3);
                if (symbol >= counts.Length) throw Corrupt("FSE zero run exhausted symbols");
            }
            int maximum = 2 * threshold - 1 - remaining, count;
            if (bits.Peek(bitCount - 1) < maximum) count = (int)bits.Read(bitCount - 1);
            else { count = (int)bits.Read(bitCount); if (count >= threshold) count -= maximum; }
            --count; remaining -= count < 0 ? -count : count; counts[symbol++] = (short)count; previousZero = count == 0;
            if (remaining < threshold && remaining > 1) { bitCount = BitOperations.Log2((uint)remaining) + 1; threshold = 1 << (bitCount - 1); }
        }
        if (remaining != 1) throw Corrupt("FSE probabilities do not sum to table size");
        return (bits.Position + 7) / 8;
    }
    private static void BuildFse(FseTable table, ReadOnlySpan<short> counts, int log)
    {
        int size = 1 << log, high = size - 1, sum = 0;
        if (size > table.Entries.Length) throw Corrupt("FSE table exceeds storage");
        Span<ushort> next = stackalloc ushort[256]; Span<byte> symbols = stackalloc byte[512]; next.Clear();
        for (int symbol = 0; symbol < counts.Length; ++symbol)
        {
            int count = counts[symbol]; if (count < -1 || count > size || sum > size - System.Math.Abs(count)) throw Corrupt("invalid FSE probability"); sum += System.Math.Abs(count);
            if (count == -1) { symbols[high--] = (byte)symbol; next[symbol] = 1; } else next[symbol] = (ushort)count;
        }
        if (sum != size) throw Corrupt("invalid FSE probability total");
        int step = (size >> 1) + (size >> 3) + 3, position = 0;
        for (int symbol = 0; symbol < counts.Length; ++symbol)
            for (int index = 0; index < counts[symbol]; ++index)
            {
                symbols[position] = (byte)symbol; position = (position + step) & (size - 1);
                while (position > high) position = (position + step) & (size - 1);
            }
        if (position != 0) throw Corrupt("invalid FSE distribution");
        for (int index = 0; index < size; ++index)
        {
            byte symbol = symbols[index]; int state = next[symbol]++, bits = log - BitOperations.Log2((uint)state);
            table.Entries[index] = new((ushort)((state << bits) - size), (byte)bits, symbol);
        }
        table.Log = log; table.Valid = true;
    }
    private ref struct ForwardBits(ReadOnlySpan<byte> input)
    {
        private readonly ReadOnlySpan<byte> _input = input;
        internal int Position;
        internal uint Peek(int count) => Extract(_input, Position, System.Math.Min(count, _input.Length * 8 - Position));
        internal uint Read(int count)
        {
            if (count > _input.Length * 8 - Position) throw Corrupt("truncated forward bitstream"); uint value = Peek(count); Position += count; return value;
        }
    }
    private ref struct ReverseBits
    {
        private readonly ReadOnlySpan<byte> _input;
        internal int Remaining;
        internal ReverseBits(ReadOnlySpan<byte> input)
        {
            if (input.IsEmpty || input[^1] == 0 || input.Length > MaximumBlock) throw Corrupt("invalid reverse bitstream marker or size");
            _input = input; Remaining = (input.Length - 1) * 8 + BitOperations.Log2(input[^1]);
        }
        internal uint Peek(int count)
        {
            int available = System.Math.Min(count, Remaining);
            return Extract(_input, Remaining - available, available) << (count - available);
        }
        internal uint Read(int count)
        {
            if (count > Remaining) throw Corrupt("truncated reverse bitstream"); uint value = Peek(count); Remaining -= count; return value;
        }
    }
    private static uint Extract(ReadOnlySpan<byte> input, int position, int count)
    {
        uint value = 0; int shift = 0;
        while (count > 0)
        {
            int bits = System.Math.Min(8 - (position & 7), count); value |= (uint)((input[position >> 3] >> (position & 7)) & ((1 << bits) - 1)) << shift;
            position += bits; shift += bits; count -= bits;
        }
        return value;
    }
}
