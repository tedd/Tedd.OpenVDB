// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of points/AttributeArray.h codecs.
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.Points;

/// <summary>Statically dispatched attribute codecs operate on one encoded value.</summary>
public interface IAttributeCodec<T> where T : unmanaged
{
    static abstract string Name { get; }
    static abstract int StorageSize { get; }
    static abstract void Encode(T value, Span<byte> storage);
    static abstract T Decode(ReadOnlySpan<byte> storage);
    static abstract bool StorageEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right);
}
public readonly struct NullCodec<T> : IAttributeCodec<T> where T : unmanaged
{
    public static string Name => "null";
    public static int StorageSize => Unsafe.SizeOf<T>();
    public static void Encode(T value, Span<byte> storage) => MemoryMarshal.Write(storage, in value);
    public static T Decode(ReadOnlySpan<byte> storage)
    {
        if (typeof(T) == typeof(bool) && storage[0] > 1) throw new InvalidDataException("Boolean attribute storage must contain zero or one.");
        return MemoryMarshal.Read<T>(storage);
    }
    public static bool StorageEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => AttributeValueTraits<T>.ExactlyEqual(Decode(left), Decode(right));
}
public readonly struct TruncateCodec<T> : IAttributeCodec<T> where T : unmanaged
{
    public static string Name => "trnc";
    private static bool Float => typeof(T) == typeof(float) || typeof(T) == typeof(Vec3f);
    public static int StorageSize => typeof(T) == typeof(float) || typeof(T) == typeof(int) ? 2 :
        typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec3i) ? 6 : throw new NotSupportedException("TruncateCodec supports float, int32, vec3s and vec3i.");
    public static void Encode(T value, Span<byte> storage)
    {
        int count = StorageSize / 2;
        if (Float)
        {
            var values = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
            for (int i = 0; i < count; i++) BinaryPrimitives.WriteUInt16LittleEndian(storage.Slice(i * 2, 2), BitConverter.HalfToUInt16Bits((Half)values[i]));
        }
        else
        {
            var values = MemoryMarshal.Cast<T, int>(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
            for (int i = 0; i < count; i++) BinaryPrimitives.WriteInt16LittleEndian(storage.Slice(i * 2, 2), unchecked((short)values[i]));
        }
    }
    public static T Decode(ReadOnlySpan<byte> storage)
    {
        int count = StorageSize / 2; T value = default;
        if (Float)
        {
            var values = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateSpan(ref value, 1));
            for (int i = 0; i < count; i++) values[i] = (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(storage.Slice(i * 2, 2)));
        }
        else
        {
            var values = MemoryMarshal.Cast<T, int>(MemoryMarshal.CreateSpan(ref value, 1));
            for (int i = 0; i < count; i++) values[i] = BinaryPrimitives.ReadInt16LittleEndian(storage.Slice(i * 2, 2));
        }
        return value;
    }
    public static bool StorageEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => AttributeValueTraits<T>.ExactlyEqual(Decode(left), Decode(right));
}
public interface IFixedPointRange { static abstract string Name { get; } static abstract double Offset { get; } }
public readonly struct PositionRange : IFixedPointRange { public static string Name => "fxpt"; public static double Offset => .5; }
public readonly struct UnitRange : IFixedPointRange { public static string Name => "ufxpt"; public static double Offset => 0; }
public readonly struct FixedPoint8Codec<T, TRange> : IAttributeCodec<T> where T : unmanaged where TRange : unmanaged, IFixedPointRange
{
    public static string Name => TRange.Name + "8";
    public static int StorageSize => FixedPointAttribute<T, TRange>.Components;
    public static void Encode(T value, Span<byte> storage) => FixedPointAttribute<T, TRange>.Encode(value, storage, 1);
    public static T Decode(ReadOnlySpan<byte> storage) => FixedPointAttribute<T, TRange>.Decode(storage, 1);
    public static bool StorageEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left[..StorageSize].SequenceEqual(right[..StorageSize]);
}
public readonly struct FixedPoint16Codec<T, TRange> : IAttributeCodec<T> where T : unmanaged where TRange : unmanaged, IFixedPointRange
{
    public static string Name => TRange.Name + "16";
    public static int StorageSize => FixedPointAttribute<T, TRange>.Components * 2;
    public static void Encode(T value, Span<byte> storage) => FixedPointAttribute<T, TRange>.Encode(value, storage, 2);
    public static T Decode(ReadOnlySpan<byte> storage) => FixedPointAttribute<T, TRange>.Decode(storage, 2);
    public static bool StorageEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left[..StorageSize].SequenceEqual(right[..StorageSize]);
}
internal static class FixedPointAttribute<T, TRange> where T : unmanaged where TRange : unmanaged, IFixedPointRange
{
    private static bool Float => typeof(T) == typeof(float) || typeof(T) == typeof(Vec3f);
    internal static int Components => typeof(T) == typeof(float) || typeof(T) == typeof(double) ? 1 :
        typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec3d) ? 3 : throw new NotSupportedException("Fixed-point codecs support float, double, vec3s and vec3d.");
    internal static void Encode(T value, Span<byte> storage, int width)
    {
        int count = Components, max = width == 1 ? byte.MaxValue : ushort.MaxValue;
        if (storage.Length < count * width) throw new ArgumentException("Storage buffer is too short.", nameof(storage));
        if (Float)
        {
            var values = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
            for (int i = 0; i < count; i++)
            {
                float shifted = values[i] + (float)TRange.Offset;
                if (float.IsNaN(shifted)) throw new ArgumentOutOfRangeException(nameof(value), "Native fixed-point conversion of NaN is undefined.");
                int encoded = shifted < 0 ? 0 : shifted >= 1 ? max : (int)(shifted * max);
                Write(storage, i, width, encoded);
            }
        }
        else
        {
            var values = MemoryMarshal.Cast<T, double>(MemoryMarshal.CreateReadOnlySpan(ref value, 1));
            for (int i = 0; i < count; i++)
            {
                double shifted = values[i] + TRange.Offset;
                if (double.IsNaN(shifted)) throw new ArgumentOutOfRangeException(nameof(value), "Native fixed-point conversion of NaN is undefined.");
                int encoded = shifted < 0 ? 0 : shifted >= 1 ? max : (int)(shifted * max);
                Write(storage, i, width, encoded);
            }
        }
    }
    internal static T Decode(ReadOnlySpan<byte> storage, int width)
    {
        int count = Components, max = width == 1 ? byte.MaxValue : ushort.MaxValue; T value = default;
        if (storage.Length < count * width) throw new ArgumentException("Storage buffer is too short.", nameof(storage));
        if (Float)
        {
            var values = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateSpan(ref value, 1));
            for (int i = 0; i < count; i++) values[i] = (float)Read(storage, i, width) / max - (float)TRange.Offset;
        }
        else
        {
            var values = MemoryMarshal.Cast<T, double>(MemoryMarshal.CreateSpan(ref value, 1));
            for (int i = 0; i < count; i++) values[i] = (double)Read(storage, i, width) / max - TRange.Offset;
        }
        return value;
    }
    private static int Read(ReadOnlySpan<byte> storage, int index, int width) => width == 1 ? storage[index] : BinaryPrimitives.ReadUInt16LittleEndian(storage.Slice(index * 2, 2));
    private static void Write(Span<byte> storage, int index, int width, int value) { if (width == 1) storage[index] = (byte)value; else BinaryPrimitives.WriteUInt16LittleEndian(storage.Slice(index * 2, 2), (ushort)value); }
}
public readonly struct UnitVecCodec<T> : IAttributeCodec<T> where T : unmanaged
{
    public static string Name => "uvec";
    public static int StorageSize => typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec3d) ? 2 : throw new NotSupportedException("UnitVecCodec supports vec3s and vec3d.");
    public static void Encode(T value, Span<byte> storage)
    {
        _ = StorageSize;
        ushort encoded = typeof(T) == typeof(Vec3f) ? QuantizedUnitVec.Pack(Unsafe.As<T, Vec3f>(ref value)) : QuantizedUnitVec.Pack(Unsafe.As<T, Vec3d>(ref value));
        BinaryPrimitives.WriteUInt16LittleEndian(storage, encoded);
    }
    public static T Decode(ReadOnlySpan<byte> storage)
    {
        _ = StorageSize; Vec3f decoded = QuantizedUnitVec.Unpack(BinaryPrimitives.ReadUInt16LittleEndian(storage));
        if (typeof(T) == typeof(Vec3f)) return Unsafe.As<Vec3f, T>(ref decoded);
        Vec3d wide = new(decoded.X, decoded.Y, decoded.Z); return Unsafe.As<Vec3d, T>(ref wide);
    }
    public static bool StorageEquals(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left[..StorageSize].SequenceEqual(right[..StorageSize]);
}
internal static class AttributeValueTraits<T> where T : unmanaged
{
    internal static string Name =>
        typeof(T) == typeof(bool) ? "bool" : typeof(T) == typeof(Half) ? "half" : typeof(T) == typeof(float) ? "float" : typeof(T) == typeof(double) ? "double" :
        typeof(T) == typeof(sbyte) ? "int8" : typeof(T) == typeof(byte) ? "uint8" : typeof(T) == typeof(short) ? "int16" : typeof(T) == typeof(ushort) ? "uint16" :
        typeof(T) == typeof(int) ? "int32" : typeof(T) == typeof(uint) ? "uint32" : typeof(T) == typeof(long) ? "int64" :
        typeof(T) == typeof(Vec2i) ? "vec2i" : typeof(T) == typeof(Vec2f) ? "vec2s" : typeof(T) == typeof(Vec2d) ? "vec2d" :
        typeof(T) == typeof(Vec3i) ? "vec3i" : typeof(T) == typeof(Vec3f) ? "vec3s" : typeof(T) == typeof(Vec3d) ? "vec3d" :
        typeof(T) == typeof(Vec4i) ? "vec4i" : typeof(T) == typeof(Vec4f) ? "vec4s" : typeof(T) == typeof(Vec4d) ? "vec4d" :
        typeof(T) == typeof(Mat3d) ? "mat3d" : typeof(T) == typeof(Mat4f) ? "mat4s" : typeof(T) == typeof(Mat4d) ? "mat4d" :
        typeof(T) == typeof(Quatf) ? "quats" : typeof(T) == typeof(Quatd) ? "quatd" : typeof(T).FullName!;
    internal static bool Floating => typeof(T) == typeof(Half) || FloatComponents || DoubleComponents;
    internal static bool Vector => Name.StartsWith("vec", StringComparison.Ordinal);
    internal static bool Matrix => Name.StartsWith("mat", StringComparison.Ordinal);
    internal static bool Quaternion => Name.StartsWith("quat", StringComparison.Ordinal);
    private static bool FloatComponents => typeof(T) == typeof(float) || typeof(T) == typeof(Vec2f) || typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec4f) || typeof(T) == typeof(Mat4f) || typeof(T) == typeof(Quatf);
    private static bool DoubleComponents => typeof(T) == typeof(double) || typeof(T) == typeof(Vec2d) || typeof(T) == typeof(Vec3d) || typeof(T) == typeof(Vec4d) || typeof(T) == typeof(Mat3d) || typeof(T) == typeof(Mat4d) || typeof(T) == typeof(Quatd);
    internal static bool ExactlyEqual(T left, T right)
    {
        if (FloatComponents)
        {
            var a = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateReadOnlySpan(ref left, 1)); var b = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateReadOnlySpan(ref right, 1));
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true;
        }
        if (DoubleComponents)
        {
            var a = MemoryMarshal.Cast<T, double>(MemoryMarshal.CreateReadOnlySpan(ref left, 1)); var b = MemoryMarshal.Cast<T, double>(MemoryMarshal.CreateReadOnlySpan(ref right, 1));
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false; return true;
        }
        return ValueEquality<T>.Equals(left, right);
    }
}
