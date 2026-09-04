// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.IO;

public static partial class VdbFile
{
    private static bool SupportsHalf(Type type) => type == typeof(float) || type == typeof(double) || type == typeof(Vec3f) || type == typeof(Vec3d);
    private static int HalfWidth<T>() where T : unmanaged => typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec3d) ? 6 : 2;
    private static T DecodeScalar<T>(ReadOnlySpan<byte> bytes) where T : unmanaged
    {
        if (typeof(T) == typeof(bool))
        {
            bool value = bytes[0] switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Boolean scalar is not zero or one.") };
            return Unsafe.As<bool, T>(ref value);
        }
        if (BitConverter.IsLittleEndian) return MemoryMarshal.Read<T>(bytes);
        Span<byte> native = stackalloc byte[Unsafe.SizeOf<T>()]; bytes.CopyTo(native);
        int component = typeof(T) == typeof(double) || typeof(T) == typeof(long) || typeof(T) == typeof(Vec3d) ? 8 : 4;
        for (int offset = 0; offset < native.Length; offset += component) native.Slice(offset, component).Reverse();
        return MemoryMarshal.Read<T>(native);
    }
    private static void EncodeScalar<T>(Span<byte> bytes, T value) where T : unmanaged
    {
        MemoryMarshal.Write(bytes, in value);
        if (BitConverter.IsLittleEndian || typeof(T) == typeof(bool)) return;
        int component = typeof(T) == typeof(double) || typeof(T) == typeof(long) || typeof(T) == typeof(Vec3d) ? 8 : 4;
        for (int offset = 0; offset < bytes.Length; offset += component) bytes.Slice(offset, component).Reverse();
    }
    private static T DecodeHalf<T>(ReadOnlySpan<byte> bytes) where T : unmanaged
    {
        if (typeof(T) == typeof(Vec3f))
        {
            var value = new Vec3f(DecodeHalf<float>(bytes), DecodeHalf<float>(bytes[2..]), DecodeHalf<float>(bytes[4..]));
            return Unsafe.As<Vec3f, T>(ref value);
        }
        if (typeof(T) == typeof(Vec3d))
        {
            var value = new Vec3d(DecodeHalf<double>(bytes), DecodeHalf<double>(bytes[2..]), DecodeHalf<double>(bytes[4..]));
            return Unsafe.As<Vec3d, T>(ref value);
        }
        return FromHalf<T>(BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes)));
    }
    private static void EncodeHalf<T>(Span<byte> bytes, T value) where T : unmanaged
    {
        if (typeof(T) == typeof(Vec3f))
        {
            var vector = Unsafe.As<T, Vec3f>(ref value); EncodeHalf(bytes, vector.X); EncodeHalf(bytes[2..], vector.Y); EncodeHalf(bytes[4..], vector.Z);
        }
        else if (typeof(T) == typeof(Vec3d))
        {
            var vector = Unsafe.As<T, Vec3d>(ref value); EncodeHalf(bytes, vector.X); EncodeHalf(bytes[2..], vector.Y); EncodeHalf(bytes[4..], vector.Z);
        }
        else BinaryPrimitives.WriteUInt16LittleEndian(bytes, BitConverter.HalfToUInt16Bits(ToHalf(value)));
    }
}
