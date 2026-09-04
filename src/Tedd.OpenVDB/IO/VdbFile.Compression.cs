// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Port of io/Compression.h (MaskCompress/readCompressedValues/writeCompressedValues)
// and io/Compression.cc (zipToStream/unzipFromStream).
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tedd.OpenVDB.Util;
using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.IO;

[Flags]
public enum VdbCompression : uint
{
    None = 0,
    Zlib = 1,
    ActiveMask = 2,
    Blosc = 4
}

public sealed record VdbWriteOptions
{
    public VdbCompression Compression { get; init; } = VdbCompression.None;
    /// <summary>Null honors Boolean is_saved_as_half_float metadata; an explicit value overrides it without mutating the grid.</summary>
    public bool? SaveFloatAsHalf { get; init; }
    public bool EnableInstancing { get; init; } = true;
}

public static partial class VdbFile
{
    private static VdbCompression ValidateCompression(VdbCompression compression)
    {
        if ((compression & ~(VdbCompression.Zlib | VdbCompression.ActiveMask | VdbCompression.Blosc)) != 0)
            throw new NotSupportedException("Unknown VDB compression flags are unsupported.");
        return compression;
    }

    private static T Negate<T>(T value) where T : unmanaged
    {
        if (typeof(T) == typeof(int)) { int result = unchecked(-Unsafe.As<T, int>(ref value)); return Unsafe.As<int, T>(ref result); }
        if (typeof(T) == typeof(float)) { float result = -Unsafe.As<T, float>(ref value); return Unsafe.As<float, T>(ref result); }
        if (typeof(T) == typeof(long)) { long result = unchecked(-Unsafe.As<T, long>(ref value)); return Unsafe.As<long, T>(ref result); }
        if (typeof(T) == typeof(bool)) { bool result = !Unsafe.As<T, bool>(ref value); return Unsafe.As<bool, T>(ref result); }
        if (typeof(T) == typeof(Vec3i)) { var result = -Unsafe.As<T, Vec3i>(ref value); return Unsafe.As<Vec3i, T>(ref result); }
        if (typeof(T) == typeof(Vec3f)) { var result = -Unsafe.As<T, Vec3f>(ref value); return Unsafe.As<Vec3f, T>(ref result); }
        if (typeof(T) == typeof(Vec3d)) { var result = -Unsafe.As<T, Vec3d>(ref value); return Unsafe.As<Vec3d, T>(ref result); }
        double d = -Unsafe.As<T, double>(ref value); return Unsafe.As<double, T>(ref d);
    }
    private static Half ToHalf<T>(T value) where T : unmanaged
    {
        // OpenVDB RealToHalf<double> explicitly passes through float before half.
        float converted = typeof(T) == typeof(float) ? Unsafe.As<T, float>(ref value) : (float)Unsafe.As<T, double>(ref value);
        return (Half)converted;
    }
    private static T FromHalf<T>(Half value) where T : unmanaged
    {
        float converted = (float)value;
        if (typeof(T) == typeof(float)) return Unsafe.As<float, T>(ref converted);
        double d = converted; return Unsafe.As<double, T>(ref d);
    }
    private static T QuantizeHalf<T>(T value) where T : unmanaged
    {
        Span<byte> bytes = stackalloc byte[6]; EncodeHalf(bytes, value); return DecodeHalf<T>(bytes);
    }

    private static void ReadCompressedValues<T>(Input input, Span<T> destination, NodeMask active, T background) where T : unmanaged
    {
        byte metadata = input.Byte();
        bool maskCompressed = (input.Compression & VdbCompression.ActiveMask) != 0;
        if (metadata > 6 || (!maskCompressed && metadata != 6))
            throw new InvalidDataException("Invalid active-mask compression metadata.");
        T inactive0 = metadata == 0 ? background : Negate(background), inactive1 = background;
        if (metadata is 2 or 4 or 5) inactive0 = input.Scalar<T>();
        if (metadata == 5) inactive1 = input.Scalar<T>();
        NodeMask? selection = metadata is 3 or 4 or 5 ? ReadMask(input, active.Log2Dim) : null;
        int count = maskCompressed && metadata != 6 ? active.CountOn() : destination.Length;
        if (count == destination.Length)
        {
            ReadValueData(input, destination);
            return;
        }
        input.Reserve((long)count * Unsafe.SizeOf<T>());
        var values = new T[count];
        ReadValueData(input, values.AsSpan());
        for (int index = 0, stored = 0; index < destination.Length; ++index)
            destination[index] = active.IsOn(index) ? values[stored++] : selection?.IsOn(index) == true ? inactive1 : inactive0;
    }

    private static void ReadValueData<T>(Input input, Span<T> destination) where T : unmanaged
    {
        if (!input.Half && BitConverter.IsLittleEndian && (input.Compression & (VdbCompression.Zlib | VdbCompression.Blosc)) == 0)
        {
            input.Exact(MemoryMarshal.AsBytes(destination)); ValidateBooleans(destination); return;
        }
        // Unlike ordinary writeData(count=0), native HalfWriter skips the entire chunk.
        if (input.Half && destination.IsEmpty) return;
        int width = input.Half ? HalfWidth<T>() : Unsafe.SizeOf<T>();
        int byteCount = checked(destination.Length * width);
        input.Reserve(byteCount);
        var bytes = new byte[byteCount];
        ReadData(input, bytes);
        if (input.Half)
        {
            for (int index = 0; index < destination.Length; ++index)
                destination[index] = DecodeHalf<T>(bytes.AsSpan(index * width, width));
        }
        else if (BitConverter.IsLittleEndian) { bytes.AsSpan().CopyTo(MemoryMarshal.AsBytes(destination)); ValidateBooleans(destination); }
        else
        {
            for (int index = 0; index < destination.Length; ++index)
            {
                ReadOnlySpan<byte> scalar = bytes.AsSpan(index * width, width);
                destination[index] = DecodeScalar<T>(scalar);
            }
        }
    }
    private static void ValidateBooleans<T>(Span<T> values) where T : unmanaged
    {
        if (typeof(T) != typeof(bool)) return;
        foreach (byte value in MemoryMarshal.AsBytes(values)) if (value > 1) throw new InvalidDataException("Boolean scalar is not zero or one.");
    }
    private static void ReadData(Input input, Span<byte> destination)
    {
        if ((input.Compression & (VdbCompression.Zlib | VdbCompression.Blosc)) == 0) { input.Exact(destination); return; }
        long length = input.Int64();
        if (length <= 0)
        {
            if (length != -(long)destination.Length) throw new InvalidDataException("Raw compressed-data fallback chunk has the wrong length.");
            input.Exact(destination); return;
        }
        if (length > input.Options.MaxBytes - input.Position || length > int.MaxValue)
            throw new InvalidDataException("Compressed chunk exceeds the input byte limit.");
        // Resource accounting occurs before allocation; decompressed size comes from the fixed node layout.
        byte[] compressed = input.Bytes((int)length);
        if ((input.Compression & VdbCompression.Blosc) != 0)
        {
            if (BloscCodec.ValidateFrame(compressed) != destination.Length) throw new InvalidDataException("Blosc chunk has the wrong decoded size.");
            BloscCodec.Decode(compressed, destination, input.Reserve);
        }
        else DecompressZlib(compressed, 0, compressed.Length, destination);
    }
    internal static void DecompressZlib(byte[] compressed, int offset, int length, Span<byte> destination)
    {
        ReadOnlySpan<byte> data = compressed.AsSpan(offset, length);
        if (data.Length < 6 || (data[0] & 15) != 8 || (data[0] >> 4) > 7 ||
            ((data[0] << 8) | data[1]) % 31 != 0 || (data[1] & 32) != 0)
            throw new InvalidDataException("Invalid zlib header or unsupported preset dictionary.");
        using var source = new MemoryStream(compressed, offset, length, writable: false);
        using var inflater = new ZLibStream(source, CompressionMode.Decompress);
        inflater.ReadExactly(destination);
        if (inflater.ReadByte() != -1) throw new InvalidDataException("Zlib chunk expands beyond its declared node size.");
        if (BinaryPrimitives.ReadUInt32BigEndian(data[^4..]) != Adler32(destination))
            throw new InvalidDataException("Zlib checksum mismatch or truncated trailer.");
    }
    private static uint Adler32(ReadOnlySpan<byte> bytes)
    {
        uint low = 1, high = 0;
        foreach (byte value in bytes) { low = (low + value) % 65521; high = (high + low) % 65521; }
        return (high << 16) | low;
    }

    private static void WriteCompressedValues<T>(BinaryWriter output, ReadOnlySpan<T> values, NodeMask active,
        NodeMask? children, T background, VdbCompression compression, bool half) where T : unmanaged
    {
        byte metadata = 6;
        T inactive0 = background, inactive1 = background;
        if ((compression & VdbCompression.ActiveMask) != 0)
        {
            int unique = 0;
            foreach (int index in active.OffIndices)
            {
                if (children?.IsOn(index) == true) continue;
                T value = values[index];
                if ((unique > 0 && ValueEquality<T>.Equals(value, inactive0)) || (unique > 1 && ValueEquality<T>.Equals(value, inactive1))) continue;
                if (unique == 0) inactive0 = value;
                else if (unique == 1) inactive1 = value;
                if (++unique == 3) break;
            }
            metadata = unique switch
            {
                0 => 0,
                1 when ValueEquality<T>.Equals(inactive0, background) => 0,
                1 when ValueEquality<T>.Equals(inactive0, Negate(background)) => 1,
                1 => 2,
                2 => 5,
                _ => 6
            };
            if (unique == 2 && (ValueEquality<T>.Equals(inactive0, background) || ValueEquality<T>.Equals(inactive1, background)))
            {
                if (ValueEquality<T>.Equals(inactive0, background)) (inactive0, inactive1) = (inactive1, inactive0);
                metadata = ValueEquality<T>.Equals(inactive0, Negate(background)) ? (byte)3 : (byte)4;
            }
        }
        output.Write(metadata);
        if (metadata is 2 or 4 or 5) WriteScalar(output, half ? QuantizeHalf(inactive0) : inactive0);
        if (metadata == 5) WriteScalar(output, half ? QuantizeHalf(inactive1) : inactive1);
        if (metadata is 3 or 4 or 5)
        {
            var selection = new NodeMask(active.Log2Dim);
            foreach (int index in active.OffIndices)
                if (ValueEquality<T>.Equals(values[index], inactive1)) selection.SetOn(index);
            WriteMask(output, selection);
        }
        if (metadata == 6) { WriteValueData(output, values, compression, half); return; }
        var packed = new T[active.CountOn()];
        int offset = 0;
        foreach (int index in active.OnIndices) packed[offset++] = values[index];
        WriteValueData(output, packed.AsSpan(), compression, half);
    }
    private static void WriteValueData<T>(BinaryWriter output, ReadOnlySpan<T> values, VdbCompression compression, bool half) where T : unmanaged
    {
        if (half && values.IsEmpty) return;
        if (!half && BitConverter.IsLittleEndian && (compression & (VdbCompression.Zlib | VdbCompression.Blosc)) == 0)
        {
            output.Write(MemoryMarshal.AsBytes(values)); return;
        }
        byte[] bytes;
        if (half)
        {
            int width = HalfWidth<T>(); bytes = new byte[checked(values.Length * width)];
            for (int index = 0; index < values.Length; ++index)
                EncodeHalf(bytes.AsSpan(index * width, width), values[index]);
        }
        else if (BitConverter.IsLittleEndian) bytes = MemoryMarshal.AsBytes(values).ToArray();
        else
        {
            using var buffer = new MemoryStream(); using var writer = new BinaryWriter(buffer);
            foreach (T value in values) WriteScalar(writer, value);
            bytes = buffer.ToArray();
        }
        if ((compression & (VdbCompression.Zlib | VdbCompression.Blosc)) == 0) { output.Write(bytes); return; }
        if ((compression & VdbCompression.Blosc) != 0)
        {
            byte[] frame = BloscCodec.Encode(bytes); output.Write((long)frame.Length); output.Write(frame); return;
        }
        using var packed = new MemoryStream();
        using (var zipper = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true)) zipper.Write(bytes);
        if (packed.Length < bytes.Length)
        {
            output.Write(packed.Length); output.Write(packed.GetBuffer().AsSpan(0, (int)packed.Length));
        }
        else { output.Write(-(long)bytes.Length); output.Write(bytes); }
    }
}
