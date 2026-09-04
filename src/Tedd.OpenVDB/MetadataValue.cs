// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB;

public sealed partial record MetadataValue
{
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
        IFormattable scalar => scalar.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString()!
    };
}
