// Copyright 2011, Google Inc.
// SPDX-License-Identifier: BSD-3-Clause
// Managed raw Snappy block decoder from the official format_description.txt.
// Retained license in THIRD_PARTY_NOTICES.md. This is the raw block format used by Blosc, not framed Snappy.
using System.Buffers.Binary;
namespace Tedd.OpenVDB.IO;

internal static class SnappyBlockCodec
{
    internal static void Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        int source = 0, target = 0; uint declared = 0;
        for (int shift = 0; ; shift += 7)
        {
            if (source == input.Length) throw new InvalidDataException("Truncated Snappy size preamble.");
            byte next = input[source++];
            if (shift == 28 && (next & 0xf0) != 0) throw new InvalidDataException("Snappy size preamble exceeds UInt32.");
            declared |= (uint)(next & 127) << shift;
            if ((next & 128) == 0) break;
        }
        if (declared != output.Length) throw new InvalidDataException("Snappy declared output size mismatch.");
        while (source < input.Length)
        {
            byte tag = input[source++]; int kind = tag & 3, length;
            if (kind == 0)
            {
                uint encodedLength = (uint)(tag >> 2);
                if (encodedLength >= 60)
                {
                    int bytes = (int)encodedLength - 59;
                    if (bytes > input.Length - source) throw new InvalidDataException("Truncated Snappy literal length.");
                    encodedLength = 0;
                    for (int index = 0; index < bytes; ++index) encodedLength |= (uint)input[source++] << (8 * index);
                }
                if ((ulong)encodedLength + 1 > (ulong)(output.Length - target)) throw new InvalidDataException("Snappy literal exceeds output size.");
                length = (int)encodedLength + 1;
                if (length > input.Length - source) throw new InvalidDataException("Truncated Snappy literal.");
                input.Slice(source, length).CopyTo(output[target..]); source += length; target += length;
                continue;
            }
            uint distance;
            if (kind == 1)
            {
                if (source == input.Length) throw new InvalidDataException("Truncated Snappy short copy.");
                length = 4 + ((tag >> 2) & 7); distance = (uint)(((tag & 0xe0) << 3) | input[source++]);
            }
            else
            {
                int bytes = kind == 2 ? 2 : 4;
                if (bytes > input.Length - source) throw new InvalidDataException("Truncated Snappy copy offset.");
                length = 1 + (tag >> 2);
                distance = bytes == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(input[source..]) : BinaryPrimitives.ReadUInt32LittleEndian(input[source..]);
                source += bytes;
            }
            if (distance == 0 || distance > target || length > output.Length - target) throw new InvalidDataException("Invalid Snappy copy offset or length.");
            int offset = (int)distance;
            if (offset == 1) output.Slice(target, length).Fill(output[target - 1]);
            else if (offset >= length) output.Slice(target - offset, length).CopyTo(output[target..]);
            else for (int index = 0; index < length; ++index) output[target + index] = output[target + index - offset];
            target += length;
        }
        if (target != output.Length) throw new InvalidDataException("Snappy output size mismatch.");
    }
}
