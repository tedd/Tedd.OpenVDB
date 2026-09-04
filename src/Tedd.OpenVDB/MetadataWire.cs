// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB;

/// <summary>Native metadata payloads, independent of archive framing.</summary>
internal static class MetadataWire
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    internal static readonly string[] BuiltinNames = ["bool", "int32", "int64", "half", "float", "double", "string", "vec2i", "vec2s", "vec2d", "vec3i", "vec3s", "vec3d", "vec4i", "vec4s", "vec4d", "mat4s", "mat4d"];
    internal static int? FixedSize(string name) => name switch
    {
        "bool" => 1,
        "half" => 2,
        "int32" or "float" => 4,
        "int64" or "double" or "vec2i" or "vec2s" => 8,
        "vec3i" or "vec3s" => 12,
        "vec2d" or "vec4i" or "vec4s" => 16,
        "vec3d" => 24,
        "vec4d" => 32,
        "mat4s" => 64,
        "mat4d" => 128,
        "string" => null,
        _ => throw new NotSupportedException($"Unknown builtin metadata type '{name}'.")
    };
    internal static object Decode(string name, ReadOnlySpan<byte> payload)
    {
        if (FixedSize(name) is int size && size != payload.Length) throw new InvalidDataException("Metadata size disagrees with its type.");
        return name switch
        {
            "bool" => payload[0] switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Boolean metadata is not zero or one.") },
            "half" => Read<Half>(payload, 2),
            "int32" => Read<int>(payload, 4),
            "int64" => Read<long>(payload, 8),
            "float" => Read<float>(payload, 4),
            "double" => Read<double>(payload, 8),
            "string" => Utf8.GetString(payload),
            "vec2i" => Read<Vec2i>(payload, 4),
            "vec2s" => Read<Vec2f>(payload, 4),
            "vec2d" => Read<Vec2d>(payload, 8),
            "vec3i" => Read<Coord>(payload, 4),
            "vec3s" => Read<Vec3f>(payload, 4),
            "vec3d" => Read<Vec3d>(payload, 8),
            "vec4i" => Read<Vec4i>(payload, 4),
            "vec4s" => Read<Vec4f>(payload, 4),
            "vec4d" => Read<Vec4d>(payload, 8),
            "mat4s" => Read<Mat4f>(payload, 4),
            "mat4d" => Read<Mat4d>(payload, 8),
            _ => throw new NotSupportedException()
        };
    }
    internal static int Size(object value) => value switch
    {
        string text => Utf8.GetByteCount(text),
        OpaqueMetadata opaque => opaque.Data.Length,
        IRegisteredMetadata custom => custom.Data.Length,
        bool => 1,
        Half => 2,
        int or float => 4,
        long or double or Vec2i or Vec2f => 8,
        Coord or Vec3f => 12,
        Vec2d or Vec4i or Vec4f => 16,
        Vec3d => 24,
        Vec4d => 32,
        Mat4f => 64,
        Mat4d => 128,
        _ => throw new NotSupportedException("Unknown metadata representation.")
    };
    internal static void Encode(object value, Span<byte> output)
    {
        if (output.Length != Size(value)) throw new ArgumentException("Metadata payload length mismatch.", nameof(output));
        switch (value)
        {
            case string v: Utf8.GetBytes(v, output); break;
            case OpaqueMetadata v: v.Data.CopyTo(output); break;
            case IRegisteredMetadata v: v.Data.CopyTo(output); break;
            case bool v: output[0] = v ? (byte)1 : (byte)0; break;
            case Half v: Write(output, v, 2); break;
            case int v: Write(output, v, 4); break;
            case long v: Write(output, v, 8); break;
            case float v: Write(output, v, 4); break;
            case double v: Write(output, v, 8); break;
            case Vec2i v: Write(output, v, 4); break;
            case Vec2f v: Write(output, v, 4); break;
            case Vec2d v: Write(output, v, 8); break;
            case Coord v: Write(output, v, 4); break;
            case Vec3f v: Write(output, v, 4); break;
            case Vec3d v: Write(output, v, 8); break;
            case Vec4i v: Write(output, v, 4); break;
            case Vec4f v: Write(output, v, 4); break;
            case Vec4d v: Write(output, v, 8); break;
            case Mat4f v: Write(output, v, 4); break;
            case Mat4d v: Write(output, v, 8); break;
            default: throw new NotSupportedException();
        }
    }
    private static T Read<T>(ReadOnlySpan<byte> bytes, int width) where T : unmanaged
    {
        if (bytes.Length != Unsafe.SizeOf<T>()) throw new InvalidDataException("Metadata layout size mismatch.");
        if (BitConverter.IsLittleEndian) return MemoryMarshal.Read<T>(bytes);
        Span<byte> native = stackalloc byte[Unsafe.SizeOf<T>()]; bytes.CopyTo(native); Swap(native, width); return MemoryMarshal.Read<T>(native);
    }
    private static void Write<T>(Span<byte> output, T value, int width) where T : unmanaged
    {
        if (output.Length != Unsafe.SizeOf<T>()) throw new InvalidDataException("Metadata layout size mismatch.");
        MemoryMarshal.Write(output, in value); if (!BitConverter.IsLittleEndian) Swap(output, width);
    }
    private static void Swap(Span<byte> values, int width) { for (int offset = 0; offset < values.Length; offset += width) values.Slice(offset, width).Reverse(); }
}

// Native std::string orders valid UTF-8 names by scalar value. UTF-16 ordinal ordering
// differs for supplementary characters relative to BMP code points above U+D7FF.
internal sealed class MetadataNameComparer : IComparer<string>
{
    internal static readonly MetadataNameComparer Instance = new();
    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1; if (right is null) return 1;
        var a = left.EnumerateRunes().GetEnumerator(); var b = right.EnumerateRunes().GetEnumerator();
        while (true)
        {
            bool hasA = a.MoveNext(), hasB = b.MoveNext();
            if (!hasA || !hasB) return hasA != hasB ? hasA.CompareTo(hasB) : StringComparer.Ordinal.Compare(left, right);
            int order = a.Current.Value.CompareTo(b.Current.Value); if (order != 0) return order;
        }
    }
}
