// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB;

public sealed partial record MetadataValue
{
    public bool TryGetValue<T>(out T value)
    {
        if (Value is T typed) { value = typed; return true; }
        if (_value is Coord coordinate && typeof(T) == typeof(Vec3i)) { value = (T)(object)new Vec3i(coordinate.X, coordinate.Y, coordinate.Z); return true; }
        value = default!; return false;
    }
    public MetadataValue DeepCopy() => new(_value switch
    {
        IRegisteredMetadata custom => custom.Copy(),
        OpaqueMetadata opaque => OpaqueMetadata.FromUnknown(opaque.TypeName, opaque.Data),
        _ => _value
    });
    /// <summary>Returns a copy of the native little-endian payload without its size prefix.</summary>
    public byte[] ToByteArray() { var result = new byte[MetadataWire.Size(_value)]; MetadataWire.Encode(_value, result); return result; }
    /// <summary>Native metadata equality compares type names and exact encoded bytes, including signed zero and NaN payloads.</summary>
    public bool Equals(MetadataValue? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null || TypeName != other.TypeName) return false;
        int size = MetadataWire.Size(_value); if (size != MetadataWire.Size(other._value)) return false;
        if (_value is string a && other._value is string b) return a == b;
        Span<byte> aBuffer = stackalloc byte[128], bBuffer = stackalloc byte[128];
        ReadOnlySpan<byte> left = Payload(_value, aBuffer), right = Payload(other._value, bBuffer);
        return left.SequenceEqual(right);
    }
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(TypeName, StringComparer.Ordinal);
        Span<byte> buffer = stackalloc byte[128]; foreach (byte value in Payload(_value, buffer)) hash.Add(value);
        return hash.ToHashCode();
    }
    private static ReadOnlySpan<byte> Payload(object value, Span<byte> buffer)
    {
        if (value is OpaqueMetadata opaque) return opaque.Data;
        if (value is IRegisteredMetadata custom) return custom.Data;
        int size = MetadataWire.Size(value);
        Span<byte> target = size <= buffer.Length ? buffer[..size] : new byte[size];
        MetadataWire.Encode(value, target); return target;
    }
    /// <summary>Native Metadata.asBool: false for numeric/vector/matrix zero, empty strings and empty opaque payloads.</summary>
    public bool AsBool() => _value switch
    {
        bool value => value,
        int value => value != 0,
        long value => value != 0,
        Half value => value != (Half)0,
        float value => value != 0,
        double value => value != 0,
        string value => value.Length != 0,
        Coord value => value != default,
        Vec2i value => value != default,
        Vec2f value => value != default,
        Vec2d value => value != default,
        Vec3f value => value != default,
        Vec3d value => value != default,
        Vec4i value => value != default,
        Vec4f value => value != default,
        Vec4d value => value != default,
        Mat4f value => Nonzero(value),
        Mat4d value => Nonzero(value),
        OpaqueMetadata value => !value.Data.IsEmpty,
        IRegisteredMetadata value => value.AsBool(),
        _ => throw new InvalidOperationException("Unknown metadata representation.")
    };
    private static bool Nonzero(Mat4d value) { for (int row = 0; row < 4; ++row) for (int column = 0; column < 4; ++column) if (value[row, column] != 0) return true; return false; }
    private static bool Nonzero(Mat4f value) { for (int row = 0; row < 4; ++row) for (int column = 0; column < 4; ++column) if (value[row, column] != 0) return true; return false; }
    private static string Number(double value, string format = "G6")
    {
        if (double.IsNaN(value)) return BitConverter.DoubleToInt64Bits(value) < 0 ? "-nan" : "nan";
        if (double.IsPositiveInfinity(value)) return "inf";
        if (double.IsNegativeInfinity(value)) return "-inf";
        return value.ToString(format, System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant();
    }
    private static string Vector(params double[] values) => "[" + string.Join(", ", values.Select(value => Number(value))) + "]";
    private static string Matrix(Mat4d value)
    {
        var rows = new string[4];
        for (int row = 0; row < 4; ++row) rows[row] = "[" + string.Join(", ", Enumerable.Range(0, 4).Select(column => Number(value[row, column], "F6"))) + "]";
        return "[" + string.Join(",\n ", rows) + "]";
    }
    private static string Matrix(Mat4f value)
    {
        var rows = new string[4];
        for (int row = 0; row < 4; ++row) rows[row] = "[" + string.Join(", ", Enumerable.Range(0, 4).Select(column => Number(value[row, column], "F6"))) + "]";
        return "[" + string.Join(",\n ", rows) + "]";
    }
    private static string FormatNative(object value) => value switch
    {
        bool scalar => scalar ? "true" : "false",
        Half scalar => Number((float)scalar),
        float scalar => Number(scalar),
        double scalar => Number(scalar),
        Coord vector => $"[{vector.X}, {vector.Y}, {vector.Z}]",
        Vec2i vector => $"[{vector.X}, {vector.Y}]",
        Vec4i vector => $"[{vector.X}, {vector.Y}, {vector.Z}, {vector.W}]",
        Vec2f vector => Vector(vector.X, vector.Y),
        Vec2d vector => Vector(vector.X, vector.Y),
        Vec3f vector => Vector(vector.X, vector.Y, vector.Z),
        Vec3d vector => Vector(vector.X, vector.Y, vector.Z),
        Vec4f vector => Vector(vector.X, vector.Y, vector.Z, vector.W),
        Vec4d vector => Vector(vector.X, vector.Y, vector.Z, vector.W),
        Mat4f matrix => Matrix(matrix),
        Mat4d matrix => Matrix(matrix),
        OpaqueMetadata opaque => opaque.Data.IsEmpty ? string.Empty : "<binary data>",
        IRegisteredMetadata custom => custom.Format(),
        IFormattable scalar => scalar.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString()!
    };
}
