// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbMetadataTests
{
    private sealed record Fixture(string Name, string Type, string Payload, string Text, bool AsBool);
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "metadata");
    private static readonly Dictionary<string, Fixture> Fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(Folder, "native-metadata.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.ToDictionary(x => x.Name);
    private static readonly VdbArchive Archive = Load(0);
    private static VdbArchive Load(int compression) { using var input = File.OpenRead(Path.Combine(Folder, $"native-{compression}.vdb")); return VdbFile.ReadArchive(input); }
    public static IEnumerable<object[]> Cases() => Fixtures.Keys.Select(name => new object[] { name });
    [Theory, MemberData(nameof(Cases))]
    public void NativeBuiltinTypeBooleanAndTextMatch(string name)
    {
        var expected = Fixtures[name]; MetadataValue actual = Archive.Grids[0].Metadata[name];
        Assert.Equal(expected.Type, actual.TypeName); Assert.Equal(expected.AsBool, actual.AsBool()); Assert.Equal(Encoding.UTF8.GetString(Convert.FromHexString(expected.Text)), actual.ToString());
        Assert.Equal(expected.Payload, Convert.ToHexStringLower(Payload(actual.Value)));
    }
    private static byte[] Payload(object value)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        switch (value)
        {
            case bool v: writer.Write(v); break;
            case int v: writer.Write(v); break;
            case long v: writer.Write(v); break;
            case Half v: writer.Write(BitConverter.HalfToInt16Bits(v)); break;
            case float v: writer.Write(v); break;
            case double v: writer.Write(v); break;
            case string v: writer.Write(Encoding.UTF8.GetBytes(v)); break;
            case Coord v: writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); break;
            case Vec2i v: writer.Write(v.X); writer.Write(v.Y); break;
            case Vec2f v: writer.Write(v.X); writer.Write(v.Y); break;
            case Vec2d v: writer.Write(v.X); writer.Write(v.Y); break;
            case Vec3f v: writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); break;
            case Vec3d v: writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); break;
            case Vec4i v: writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); writer.Write(v.W); break;
            case Vec4f v: writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); writer.Write(v.W); break;
            case Vec4d v: writer.Write(v.X); writer.Write(v.Y); writer.Write(v.Z); writer.Write(v.W); break;
            case Mat4d v: for (int row = 0; row < 4; ++row) for (int column = 0; column < 4; ++column) writer.Write(v[row, column]); break;
            case Mat4f v: for (int row = 0; row < 4; ++row) for (int column = 0; column < 4; ++column) writer.Write(v[row, column]); break;
            case OpaqueMetadata v: writer.Write(v.Data); break;
            default: throw new InvalidDataException(value.GetType().Name);
        }
        return stream.ToArray();
    }
    [Theory, InlineData(0), InlineData(4), InlineData(6)]
    public void NativeArchiveMetadataRoundTrips(int compression)
    {
        VdbArchive expected = Load(compression); using var bytes = new MemoryStream(); VdbFile.WriteArchive(bytes, expected, options: new() { Compression = (VdbCompression)compression });
        string? exported = Environment.GetEnvironmentVariable("TEDD_OPENVDB_METADATA_OUTPUT_DIR");
        if (exported is not null) { Directory.CreateDirectory(exported); File.WriteAllBytes(Path.Combine(exported, $"managed-{compression}.vdb"), bytes.ToArray()); }
        bytes.Position = 0; VdbArchive actual = VdbFile.ReadArchive(bytes);
        foreach (var pair in expected.Grids[0].Metadata) Assert.Equal(Payload(pair.Value.Value), Payload(actual.Grids[0].Metadata[pair.Key].Value));
        foreach (var pair in expected.Metadata) Assert.Equal(Payload(pair.Value.Value), Payload(actual.Metadata[pair.Key].Value));
    }
    [Fact]
    public void MetadataTextUsesInvariantNativeFormatting()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("nb-NO"); Assert.Equal("[1.25, -2.5]", new MetadataValue(new Vec2f(1.25f, -2.5f)).ToString()); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void NewNumericTypesHaveNativeContiguousSizes()
    {
        Assert.Equal(8, Marshal.SizeOf<Vec2i>()); Assert.Equal(8, Marshal.SizeOf<Vec2f>());
        Assert.Equal(16, Marshal.SizeOf<Vec4i>()); Assert.Equal(16, Marshal.SizeOf<Vec4f>()); Assert.Equal(64, Marshal.SizeOf<Mat4f>());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Vec2f()[2]); Assert.Throws<ArgumentOutOfRangeException>(() => new Vec4i()[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Mat4f.Identity[4, 0]);
        var nan = Mat4f.Identity with { M00 = float.NaN }; Assert.False(nan.Equals(nan));
        Assert.Equal(Mat4f.Zero, Mat4f.Zero with { M00 = -0.0f });
    }
}
