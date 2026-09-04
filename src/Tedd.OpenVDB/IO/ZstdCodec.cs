// Copyright (c) Meta Platforms, Inc. and affiliates.
// SPDX-License-Identifier: BSD-3-Clause
// Bounded managed port of Zstd 1.5.6 frame, block, FSE and Huffman decoding.
// See THIRD_PARTY_NOTICES.md and reference/ZSTD_VERIFICATION.md.
using System.Buffers.Binary;
using System.Numerics;
namespace Tedd.OpenVDB.IO;

internal static partial class ZstdCodec
{
    private const int MaximumBlock = 128 * 1024;
    private static InvalidDataException Corrupt(string detail) => new("Invalid Zstd data: " + detail);
    private static ulong Take(ReadOnlySpan<byte> input, ref int position, int bytes)
    {
        if (bytes > input.Length - position) throw Corrupt("truncated field");
        ulong value = 0; for (int index = 0; index < bytes; ++index) value |= (ulong)input[position++] << (index * 8); return value;
    }
    internal static void Decode(ReadOnlySpan<byte> input, Span<byte> output, Action<long> reserve)
    {
        if (input.IsEmpty) throw Corrupt("missing frame");
        int source = 0, target = 0; Context? context = null;
        while (source < input.Length)
        {
            uint magic = (uint)Take(input, ref source, 4);
            if ((magic & 0xfffffff0) == 0x184d2a50)
            {
                uint skip = (uint)Take(input, ref source, 4);
                if (skip > input.Length - source) throw Corrupt("skippable frame length"); source += (int)skip; continue;
            }
            if (magic != 0xfd2fb528) throw new NotSupportedException("Unsupported Zstd frame magic or legacy format.");
            int descriptor = (int)Take(input, ref source, 1);
            if ((descriptor & 8) != 0) throw Corrupt("reserved frame flag");
            bool single = (descriptor & 32) != 0, checksum = (descriptor & 4) != 0;
            ulong window = 0;
            if (!single)
            {
                int code = (int)Take(input, ref source, 1), log = 10 + (code >> 3);
                if (log > 31) throw new NotSupportedException("Zstd windows above the native 64-bit maximum are unsupported.");
                window = 1UL << log; window += (window >> 3) * (uint)(code & 7);
            }
            int dictionaryBytes = (descriptor & 3) switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 };
            if (Take(input, ref source, dictionaryBytes) != 0) throw new NotSupportedException("Dictionary-dependent Zstd frames are unsupported.");
            int sizeCode = descriptor >> 6;
            int sizeBytes = sizeCode switch { 0 => single ? 1 : 0, 1 => 2, 2 => 4, _ => 8 };
            ulong? declared = sizeBytes == 0 ? null : Take(input, ref source, sizeBytes);
            if (sizeCode == 1) declared += 256;
            if (declared > (ulong)(output.Length - target)) throw Corrupt("content size exceeds destination");
            if (single) window = declared!.Value;
            int frameStart = target, blockMaximum = (int)System.Math.Min(window, MaximumBlock);
            context?.Reset(); bool last;
            do
            {
                uint header = (uint)Take(input, ref source, 3); last = (header & 1) != 0;
                int kind = (int)((header >> 1) & 3), size = (int)(header >> 3);
                if (kind == 3 || size > blockMaximum) throw Corrupt("reserved block type or excessive block length");
                if (kind is 0 or 1)
                {
                    if (size > output.Length - target) throw Corrupt("block exceeds destination");
                    if (kind == 0)
                    {
                        if (size > input.Length - source) throw Corrupt("truncated raw block"); input.Slice(source, size).CopyTo(output[target..]); source += size;
                    }
                    else output.Slice(target, size).Fill((byte)Take(input, ref source, 1));
                    target += size;
                }
                else
                {
                    if (size > input.Length - source) throw Corrupt("truncated compressed block");
                    if (context is null)
                    {
                        int literals = System.Math.Min(output.Length, MaximumBlock);
                        reserve(checked(literals + 4L * (512 + 512 + 256 + 64) + 2L * 4096 + 512));
                        context = new Context(literals);
                    }
                    target += DecodeBlock(input.Slice(source, size), output, target, frameStart, window, blockMaximum, context); source += size;
                }
                if (declared is ulong known && (ulong)(target - frameStart) > known) throw Corrupt("frame exceeds declared content size");
            } while (!last);
            if (declared is ulong expected && expected != (ulong)(target - frameStart)) throw Corrupt("frame content size mismatch");
            if (checksum && (uint)Take(input, ref source, 4) != (uint)XxHash64(output.Slice(frameStart, target - frameStart))) throw Corrupt("checksum mismatch");
        }
        if (target != output.Length) throw Corrupt("decoded size mismatch");
    }
    private sealed class Context(int literalCapacity)
    {
        internal readonly byte[] Literals = new byte[literalCapacity];
        internal readonly FseTable LiteralLengths = new(512), MatchLengths = new(512), Offsets = new(256), Weights = new(64);
        internal readonly ushort[] Huffman = new ushort[4096];
        internal int HuffmanLog;
        internal uint Repeat0 = 1, Repeat1 = 4, Repeat2 = 8;
        internal void Reset() { HuffmanLog = 0; LiteralLengths.Valid = MatchLengths.Valid = Offsets.Valid = false; Repeat0 = 1; Repeat1 = 4; Repeat2 = 8; }
    }
    private static int DecodeBlock(ReadOnlySpan<byte> input, Span<byte> output, int start, int frameStart, ulong window, int maximum, Context context)
    {
        int source = 0, target = start, literalPosition = 0;
        int literalCount = DecodeLiterals(input, ref source, context, System.Math.Min(maximum, output.Length - start));
        int count = (int)Take(input, ref source, 1);
        if (count == 255) count = 0x7f00 + (int)Take(input, ref source, 2);
        else if (count > 127) count = ((count - 128) << 8) + (int)Take(input, ref source, 1);
        if (count > (output.Length - start) / 3 || count > maximum / 3) throw Corrupt("excessive sequence count");
        if (count != 0)
        {
            int modes = (int)Take(input, ref source, 1);
            if ((modes & 3) != 0) throw Corrupt("reserved sequence flags");
            SetSequenceTable(context.LiteralLengths, modes >> 6, input, ref source, LiteralNorm, 6, 35, 9);
            SetSequenceTable(context.Offsets, (modes >> 4) & 3, input, ref source, OffsetNorm, 5, 31, 8);
            SetSequenceTable(context.MatchLengths, (modes >> 2) & 3, input, ref source, MatchNorm, 6, 52, 9);
            var bits = new ReverseBits(input[source..]);
            int literalState = (int)bits.Read(context.LiteralLengths.Log), offsetState = (int)bits.Read(context.Offsets.Log), matchState = (int)bits.Read(context.MatchLengths.Log);
            for (int sequence = 0; sequence < count; ++sequence)
            {
                FseEntry ll = context.LiteralLengths.Entries[literalState], ml = context.MatchLengths.Entries[matchState], of = context.Offsets.Entries[offsetState];
                uint literalLength = LiteralBase[ll.Symbol], matchLength = MatchBase[ml.Symbol], offset;
                int offsetBits = of.Symbol;
                if (offsetBits > 1)
                {
                    offset = ((1U << offsetBits) - 3) + bits.Read(offsetBits);
                    context.Repeat2 = context.Repeat1; context.Repeat1 = context.Repeat0; context.Repeat0 = offset;
                }
                else if (offsetBits == 0)
                {
                    offset = literalLength == 0 ? context.Repeat1 : context.Repeat0;
                    context.Repeat1 = literalLength == 0 ? context.Repeat0 : context.Repeat1; context.Repeat0 = offset;
                }
                else
                {
                    uint code = 1U + (literalLength == 0 ? 1U : 0U) + bits.Read(1);
                    offset = code switch { 1 => context.Repeat1, 2 => context.Repeat2, _ => context.Repeat0 - 1 };
                    if (code != 1) context.Repeat2 = context.Repeat1;
                    context.Repeat1 = context.Repeat0; context.Repeat0 = offset;
                }
                matchLength += bits.Read(MatchBits[ml.Symbol]); literalLength += bits.Read(LiteralBits[ll.Symbol]);
                if (literalLength > literalCount - literalPosition || literalLength > output.Length - target) throw Corrupt("literal sequence exceeds buffer");
                context.Literals.AsSpan(literalPosition, (int)literalLength).CopyTo(output[target..]); literalPosition += (int)literalLength; target += (int)literalLength;
                if (offset == 0 || offset > target - frameStart || offset > window || matchLength > output.Length - target || matchLength > maximum - (target - start)) throw Corrupt("invalid match reference or length");
                int distance = (int)offset, length = (int)matchLength;
                if (distance == 1) output.Slice(target, length).Fill(output[target - 1]);
                else if (distance >= length) output.Slice(target - distance, length).CopyTo(output[target..]);
                else for (int index = 0; index < length; ++index) output[target + index] = output[target + index - distance];
                target += length;
                if (sequence + 1 < count)
                {
                    literalState = ll.Next + (int)bits.Read(ll.Bits); matchState = ml.Next + (int)bits.Read(ml.Bits); offsetState = of.Next + (int)bits.Read(of.Bits);
                }
            }
            if (bits.Remaining != 0) throw Corrupt("unused sequence bits");
        }
        else if (source != input.Length) throw Corrupt("unused sequence bytes");
        int tail = literalCount - literalPosition;
        if (tail > output.Length - target || tail > maximum - (target - start)) throw Corrupt("literal tail exceeds output");
        context.Literals.AsSpan(literalPosition, tail).CopyTo(output[target..]); return target + tail - start;
    }
}
