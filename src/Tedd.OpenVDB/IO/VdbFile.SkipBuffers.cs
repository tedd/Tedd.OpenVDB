// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Native io/Compression.h readCompressedValues(nullptr) framing; no leaf payload decompression.
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.IO;

public static partial class VdbFile
{
    private static void SkipCompressedValues<T>(Input input, NodeMask active) where T : unmanaged
    {
        byte metadata = input.Byte(); bool maskCompressed = (input.Compression & VdbCompression.ActiveMask) != 0;
        if (metadata > 6 || (!maskCompressed && metadata != 6)) throw new InvalidDataException("Invalid active-mask compression metadata.");
        if (metadata is 2 or 4 or 5) input.Seek(input.Position + Unsafe.SizeOf<T>());
        if (metadata == 5) input.Seek(input.Position + Unsafe.SizeOf<T>());
        if (metadata is 3 or 4 or 5) input.Seek(input.Position + active.WordCount * 8L);
        int count = maskCompressed && metadata != 6 ? active.CountOn() : 512;
        // HalfWriter omits the whole count-zero chunk, while ordinary writeData retains its length.
        if (input.Half && count == 0) return;
        long bytes = count * (long)(input.Half ? HalfWidth<T>() : Unsafe.SizeOf<T>());
        if ((input.Compression & (VdbCompression.Zlib | VdbCompression.Blosc)) != 0)
        {
            long length = input.Int64();
            if (length <= 0) { if (length != -bytes) throw new InvalidDataException("Raw compressed-data fallback chunk has the wrong length."); }
            else
            {
                if (length > int.MaxValue || length > input.Options.MaxBytes - input.Position) throw new InvalidDataException("Compressed chunk exceeds the input byte limit.");
                bytes = length;
            }
        }
        if (bytes > input.Options.MaxBytes - input.Position || bytes > input.Limit - input.Position) throw new InvalidDataException("Skipped buffer exceeds the grid boundary.");
        input.Seek(input.Position + bytes);
    }
}