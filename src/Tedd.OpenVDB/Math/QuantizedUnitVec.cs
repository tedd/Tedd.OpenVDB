// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/QuantizedUnitVec.h.
using System.Numerics;
using System.Runtime.InteropServices;

namespace Tedd.OpenVDB.Math;

/// <summary>OpenVDB's 16-bit unit-vector encoding, preserving its exact native normalization table.</summary>
/// <remarks>Three sign bits accompany thirteen component bits. Pack accepts finite vectors; inputs
/// whose normalization produces nonfinite integer conversions throw instead of invoking native
/// undefined behavior. A zero vector packs as zero, which decodes to positive Z, not to zero.
/// Unpack defines a value for every possible 16-bit word, including words not produced by Pack.</remarks>
public static partial class QuantizedUnitVec
{
    public static ushort Pack(Vec3f vector)
    {
        float x = vector.X, y = vector.Y, z = vector.Z;
        if (x == 0 && y == 0 && z == 0) return 0;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(vector), "Packed vector components must be finite.");
        ushort data = 0;
        if (x < 0) { data |= 0x8000; x = -x; }
        if (y < 0) { data |= 0x4000; y = -y; }
        if (z < 0) { data |= 0x2000; z = -z; }
        float weight = 126f / (x + y + z);
        float scaledX = x * weight, scaledY = y * weight;
        if (!float.IsFinite(scaledX) || !float.IsFinite(scaledY)) throw new ArgumentOutOfRangeException(nameof(vector), "Packed vector normalization is not representable.");
        ushort xbits = (ushort)scaledX, ybits = (ushort)scaledY;
        if (xbits > 63) { xbits = (ushort)(127 - xbits); ybits = (ushort)(127 - ybits); }
        return (ushort)(data | (xbits << 7) | ybits);
    }
    public static ushort Pack(Vec3d vector)
    {
        double x = vector.X, y = vector.Y, z = vector.Z;
        if (x == 0 && y == 0 && z == 0) return 0;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(vector), "Packed vector components must be finite.");
        ushort data = 0;
        if (x < 0) { data |= 0x8000; x = -x; }
        if (y < 0) { data |= 0x4000; y = -y; }
        if (z < 0) { data |= 0x2000; z = -z; }
        double weight = 126.0 / (x + y + z);
        double scaledX = x * weight, scaledY = y * weight;
        if (!double.IsFinite(scaledX) || !double.IsFinite(scaledY)) throw new ArgumentOutOfRangeException(nameof(vector), "Packed vector normalization is not representable.");
        ushort xbits = (ushort)scaledX, ybits = (ushort)scaledY;
        if (xbits > 63) { xbits = (ushort)(127 - xbits); ybits = (ushort)(127 - ybits); }
        return (ushort)(data | (xbits << 7) | ybits);
    }
    public static Vec3f Unpack(ushort data)
    {
        float weight = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(NormalizationWeightBytes.Slice((data & 0x1FFF) * sizeof(float), sizeof(float)));
        int xbits = (data & 0x1F80) >> 7, ybits = data & 0x007F;
        if (xbits + ybits > 126) { xbits = 127 - xbits; ybits = 127 - ybits; }
        float x = xbits * weight, y = ybits * weight, z = (126 - xbits - ybits) * weight;
        if ((data & 0x8000) != 0) x = -x;
        if ((data & 0x4000) != 0) y = -y;
        if ((data & 0x2000) != 0) z = -z;
        return new(x, y, z);
    }
    public static ushort FlipSignBits(ushort data) => (ushort)(data ^ 0xE000);
    public static void FlipSignBits(ref ushort data) => data ^= 0xE000;

    /// <summary>Encodes into the first input.Length output elements. Buffers must not overlap.</summary>
    public static void Pack(ReadOnlySpan<Vec3f> input, Span<ushort> output)
    {
        CheckBuffers(input, output); for (int i = 0; i < input.Length; ++i) output[i] = Pack(input[i]);
    }
    public static void Pack(ReadOnlySpan<Vec3d> input, Span<ushort> output)
    {
        CheckBuffers(input, output); for (int i = 0; i < input.Length; ++i) output[i] = Pack(input[i]);
    }
    public static void Unpack(ReadOnlySpan<ushort> input, Span<Vec3f> output)
    {
        CheckBuffers(input, output); for (int i = 0; i < input.Length; ++i) output[i] = Unpack(input[i]);
    }
    public static void FlipSignBits(Span<ushort> data)
    {
        int i = 0;
        if (System.Numerics.Vector.IsHardwareAccelerated)
        {
            int width = System.Numerics.Vector<ushort>.Count, last = data.Length - width;
            var mask = new System.Numerics.Vector<ushort>(0xE000);
            for (; i <= last; i += width)
            {
                var values = new System.Numerics.Vector<ushort>(data.Slice(i, width));
                (values ^ mask).CopyTo(data.Slice(i, width));
            }
        }
        for (; i < data.Length; ++i) data[i] ^= 0xE000;
    }
    private static void CheckBuffers<TInput, TOutput>(ReadOnlySpan<TInput> input, Span<TOutput> output) where TInput : struct where TOutput : struct
    {
        if (output.Length < input.Length) throw new ArgumentException("Output span is too short.", nameof(output));
        if (MemoryMarshal.AsBytes(input).Overlaps(MemoryMarshal.AsBytes(output[..input.Length]))) throw new ArgumentException("Input and output spans must not overlap.", nameof(output));
    }
}
