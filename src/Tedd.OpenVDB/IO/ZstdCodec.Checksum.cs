// Copyright (c) Yann Collet - Meta Platforms, Inc.
// SPDX-License-Identifier: BSD-3-Clause
// XXH64 checksum algorithm from Zstd's bundled xxhash.c, seed zero.
using System.Buffers.Binary;
using System.Numerics;
namespace Tedd.OpenVDB.IO;

internal static partial class ZstdCodec
{
    private static ulong XxHash64(ReadOnlySpan<byte> input)
    {
        const ulong prime1 = 11400714785074694791UL, prime2 = 14029467366897019727UL, prime3 = 1609587929392839161UL, prime4 = 9650029242287828579UL, prime5 = 2870177450012600261UL;
        static ulong Round(ulong accumulator, ulong value) => unchecked(BitOperations.RotateLeft(accumulator + value * prime2, 31) * prime1);
        static ulong Merge(ulong accumulator, ulong value) => unchecked((accumulator ^ Round(0, value)) * prime1 + prime4);
        unchecked
        {
            int position = 0; ulong hash;
            if (input.Length >= 32)
            {
                ulong first = prime1 + prime2, second = prime2, third = 0, fourth = 0UL - prime1;
                int last = input.Length - 32;
                do
                {
                    first = Round(first, BinaryPrimitives.ReadUInt64LittleEndian(input[position..])); second = Round(second, BinaryPrimitives.ReadUInt64LittleEndian(input[(position + 8)..]));
                    third = Round(third, BinaryPrimitives.ReadUInt64LittleEndian(input[(position + 16)..])); fourth = Round(fourth, BinaryPrimitives.ReadUInt64LittleEndian(input[(position + 24)..])); position += 32;
                } while (position <= last);
                hash = BitOperations.RotateLeft(first, 1) + BitOperations.RotateLeft(second, 7) + BitOperations.RotateLeft(third, 12) + BitOperations.RotateLeft(fourth, 18);
                hash = Merge(Merge(Merge(Merge(hash, first), second), third), fourth);
            }
            else hash = prime5;
            hash += (ulong)input.Length;
            while (position <= input.Length - 8)
            {
                hash ^= Round(0, BinaryPrimitives.ReadUInt64LittleEndian(input[position..])); hash = BitOperations.RotateLeft(hash, 27) * prime1 + prime4; position += 8;
            }
            if (position <= input.Length - 4) { hash ^= BinaryPrimitives.ReadUInt32LittleEndian(input[position..]) * prime1; hash = BitOperations.RotateLeft(hash, 23) * prime2 + prime3; position += 4; }
            while (position < input.Length) { hash ^= input[position++] * prime5; hash = BitOperations.RotateLeft(hash, 11) * prime1; }
            hash ^= hash >> 33; hash *= prime2; hash ^= hash >> 29; hash *= prime3; return hash ^ (hash >> 32);
        }
    }
}
