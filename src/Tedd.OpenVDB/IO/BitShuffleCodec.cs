// Copyright (c) 2014 Kiyoshi Masui (kiyo@physics.ubc.ca)
// Copyright (c) 2019-present Blosc Development Team <blosc@blosc.org>
// SPDX-License-Identifier: MIT AND BSD-3-Clause
// Fused bounded port of c-blosc bitshuffle-generic.c inverse transpose and shuffle.c remainder rules.
// Retained licenses in THIRD_PARTY_NOTICES.md.
namespace Tedd.OpenVDB.IO;

internal static class BitShuffleCodec
{
    internal static void Unshuffle(ReadOnlySpan<byte> input, Span<byte> output, int typeSize)
    {
        if (typeSize <= 0 || input.Length != output.Length) throw new InvalidDataException("Invalid bitshuffle dimensions.");
        int elements = input.Length / typeSize;
        // Native c-blosc copies the complete block unchanged when its element count
        // is not divisible by eight. It does not shuffle only the aligned prefix.
        if ((elements & 7) != 0) { input.CopyTo(output); return; }
        int rowBytes = elements / 8;
        for (int component = 0; component < typeSize; ++component)
        {
            for (int group = 0; group < rowBytes; ++group)
            {
                ulong bits = 0;
                for (int plane = 0; plane < 8; ++plane) bits |= (ulong)input[(component * 8 + plane) * rowBytes + group] << (plane * 8);
                ulong temporary = (bits ^ (bits >> 7)) & 0x00aa00aa00aa00aaUL; bits ^= temporary ^ (temporary << 7);
                temporary = (bits ^ (bits >> 14)) & 0x0000cccc0000ccccUL; bits ^= temporary ^ (temporary << 14);
                temporary = (bits ^ (bits >> 28)) & 0x00000000f0f0f0f0UL; bits ^= temporary ^ (temporary << 28);
                for (int element = 0; element < 8; ++element) output[(group * 8 + element) * typeSize + component] = (byte)(bits >> (element * 8));
            }
        }
        int tail = elements * typeSize; input[tail..].CopyTo(output[tail..]);
    }
}
