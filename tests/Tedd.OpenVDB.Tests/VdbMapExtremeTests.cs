// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbMapExtremeTests
{
    private static readonly JsonDocument Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "maps", "native-maps-extreme.json")));
    public static IEnumerable<object[]> Cases() => Fixture.RootElement.GetProperty("cases").EnumerateArray().Select((c, i) => new object[] { i, c.GetProperty("type").GetString()! });
    [Theory, MemberData(nameof(Cases))]
    public void FiniteScaleMayHaveInfiniteNativeDeterminant(int index, string type)
    {
        var c = Fixture.RootElement.GetProperty("cases")[index];
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.RootElement.GetProperty("commit").GetString());
        Vec3d scale = Vector(c.GetProperty("scale")), translation = new(.25, -.5, .75);
        MapBase map = type switch
        {
            nameof(ScaleMap) => new ScaleMap(scale),
            nameof(ScaleTranslateMap) => new ScaleTranslateMap(scale, translation),
            nameof(UniformScaleMap) => new UniformScaleMap(scale.X),
            nameof(UniformScaleTranslateMap) => new UniformScaleTranslateMap(scale.X, translation),
            _ => throw new InvalidDataException()
        };
        Assert.Equal(Number(c.GetProperty("determinant")), map.Determinant); Assert.Equal(Vector(c.GetProperty("voxelSize")), map.VoxelSize);
        Assert.Equal(map.Determinant, map.Copy().Determinant);
        foreach (var sample in c.GetProperty("samples").EnumerateArray())
        {
            var point = Vector(sample.GetProperty("point"));
            EqualBits(Vector(sample.GetProperty("mapped")), map.ApplyMap(point)); EqualBits(Vector(sample.GetProperty("inverse")), map.ApplyInverseMap(point));
        }
        var transform = Transform.FromMap(map, simplify: false); using var bytes = new MemoryStream(); transform.Write(bytes);
        Assert.Equal(c.GetProperty("wire").GetString(), Convert.ToHexStringLower(bytes.ToArray()));
        bytes.Position = 0; var loaded = new Transform(); loaded.Read(bytes); Assert.Equal(type, loaded.MapType); Assert.Equal(map.VoxelSize, loaded.VoxelSize);
        using var written = new MemoryStream(); loaded.Write(written); Assert.Equal(bytes.ToArray(), written.ToArray());
    }
    private static double Number(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : e.GetString() switch
    {
        "Infinity" => double.PositiveInfinity,
        "-Infinity" => double.NegativeInfinity,
        _ => double.NaN
    };
    private static Vec3d Vector(JsonElement e) => new(Number(e[0]), Number(e[1]), Number(e[2]));
    private static void EqualBits(Vec3d expected, Vec3d actual)
    {
        for (int i = 0; i < 3; i++) Assert.Equal(BitConverter.DoubleToInt64Bits(expected[i]), BitConverter.DoubleToInt64Bits(actual[i]));
    }
}
