// Copyright (c) 2021 The Blosc Developers <blosc@blosc.org>
// Copyright (C) 2005-2020 Ariya Hidayat <ariya.hidayat@gmail.com>
// SPDX-License-Identifier: BSD-3-Clause AND MIT
// Bounded port of c-blosc 1.21.6 blosclz_decompress; retained licenses in THIRD_PARTY_NOTICES.md.
namespace Tedd.OpenVDB.IO;

internal static class BloscLzCodec
{
    internal static void Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        if (input.IsEmpty) throw new InvalidDataException("Empty BloscLZ block.");
        int source = 1, target = 0, control = input[0] & 31;
        while (true)
        {
            if (control < 32)
            {
                int literals = control + 1;
                if (literals > input.Length - source || literals > output.Length - target) throw new InvalidDataException("Truncated or oversized BloscLZ literal.");
                input.Slice(source, literals).CopyTo(output[target..]); source += literals; target += literals;
                if (source == input.Length) break;
                control = input[source++];
            }
            else
            {
                int length = (control >> 5) - 1, distance = (control & 31) << 8;
                int maximum = output.Length - target - 3;
                if (length > maximum) throw new InvalidDataException("BloscLZ match exceeds output size.");
                if (length == 6)
                {
                    byte extra;
                    do
                    {
                        if (source >= input.Length - 1) throw new InvalidDataException("Truncated BloscLZ extended length.");
                        extra = input[source++];
                        if (extra > maximum - length) throw new InvalidDataException("BloscLZ extended length exceeds output size.");
                        length += extra;
                    } while (extra == 255);
                }
                else if (source >= input.Length - 1) throw new InvalidDataException("Truncated BloscLZ match.");
                int low = input[source++]; length += 3;
                if (low == 255 && distance == (31 << 8))
                {
                    if (source >= input.Length - 1) throw new InvalidDataException("Truncated BloscLZ far offset.");
                    distance = (input[source] << 8) + input[source + 1] + 8192; source += 2;
                }
                else distance += low + 1;
                if (distance > target) throw new InvalidDataException("BloscLZ offset precedes its output buffer.");
                // Native BloscLZ requires another control byte after a match. It does
                // not emit the match if the input ends here, so such blocks fail the size check.
                if (source == input.Length) throw new InvalidDataException("BloscLZ match has no following sequence.");
                control = input[source++];
                if (distance == 1) output.Slice(target, length).Fill(output[target - 1]);
                else if (distance >= length) output.Slice(target - distance, length).CopyTo(output[target..]);
                else for (int index = 0; index < length; ++index) output[target + index] = output[target + index - distance];
                target += length;
            }
        }
        if (target != output.Length) throw new InvalidDataException("BloscLZ output size mismatch.");
    }
}
