// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.InteropServices;
using System.Text.Json;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbFileReaderNativeTests
{
    private static readonly string Folder = Environment.GetEnvironmentVariable("TEDD_OPENVDB_PARTIAL_FIXTURES") ?? Path.Combine(AppContext.BaseDirectory, "Fixtures", "file-partial");
    private static readonly JsonDocument Data = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "native-partial.json")));
    public static IEnumerable<object[]> Cases() => Data.RootElement.GetProperty("cases").EnumerateArray().Select((entry, i) => new object[] { i, entry.GetProperty("file").GetString()!, entry.GetProperty("operation").GetString()! });
    [Theory, MemberData(nameof(Cases))]
    public void NativeFileReadResultsMatch(int index, string file, string operation)
    {
        var fixture = Data.RootElement.GetProperty("cases")[index];
        using var reader = VdbFile.OpenRead(Path.Combine(Folder, file));
        Assert.Equal(!file.Contains("stream", StringComparison.Ordinal), reader.IsRandomAccess);
        if (operation == "names") { Assert.Equal(fixture.GetProperty("names").EnumerateArray().Select(x => x.GetString()), reader.GridNames); return; }
        string name = fixture.GetProperty("name").GetString()!;
        if (operation == "box" && fixture.GetProperty("grid").GetProperty("leaves").GetInt64() > 100_000)
        {
            Assert.Throws<InvalidDataException>(() => reader.ReadGrid(name, Bounds(Data.RootElement.GetProperty("boxes")[fixture.GetProperty("box").GetInt32()]))); return;
        }
        IGrid grid = operation switch
        {
            "metadata" => reader.ReadGridMetadata(name),
            "grid" => reader.ReadGrid(name),
            "all-metadata" => reader.ReadAllGridMetadata()[int.Parse(name)],
            "all" => reader.GetGrids()[int.Parse(name)],
            _ => reader.ReadGrid(name, Bounds(Data.RootElement.GetProperty("boxes")[fixture.GetProperty("box").GetInt32()]))
        };
        var expected = fixture.GetProperty("grid");
        Assert.Equal(expected.GetProperty("name").GetString(), grid.Name);
        Assert.Equal(expected.GetProperty("map").GetString(), grid.Transform.MapType);
        var metadata = expected.GetProperty("metadata").EnumerateArray().ToArray(); Assert.Equal(metadata.Length, grid.Metadata.Count);
        foreach (var item in metadata) { MetadataValue value = grid.Metadata[item[0].GetString()!]; Assert.Equal(item[1].GetString(), value.TypeName); Assert.Equal(item[2].GetString(), value.ToString()); }
        switch (grid)
        {
            case Grid<int> g: Check(expected, g); break;
            case Grid<float> g: Check(expected, g); break;
            case Grid<double> g: Check(expected, g); break;
            case Grid<long> g: Check(expected, g); break;
            case Grid<bool> g: Check(expected, g); break;
            case MaskGrid g: Check(expected, g.ToBooleanGrid()); break;
            default: throw new InvalidOperationException();
        }
    }
    private static BBoxd Bounds(JsonElement e) => new(Vector(e[0]), Vector(e[1]));
    private static Vec3d Vector(JsonElement e) => new(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble());
    private static Coord Coordinate(JsonElement e) => new(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32());
    private static ulong Hash<T>(ulong hash, T value) where T : unmanaged
    {
        ReadOnlySpan<T> input = new(in value); foreach (byte b in MemoryMarshal.AsBytes(input)) hash = unchecked((hash ^ b) * 1099511628211UL); return hash;
    }
    internal static void Check<T>(JsonElement expected, Grid<T> grid) where T : unmanaged
    {
        Assert.Equal(expected.GetProperty("active").GetUInt64(), grid.ActiveVoxelCount); Assert.Equal(expected.GetProperty("leaves").GetInt64(), grid.Tree.LeafCount);
        Assert.Equal(expected.GetProperty("empty").GetBoolean(), grid.IsEmpty); Assert.Equal(expected.GetProperty("background").GetDouble(), Convert.ToDouble(grid.Background));
        grid.TryGetActiveVoxelBoundingBox(out CoordBBox box); Assert.Equal(Coordinate(expected.GetProperty("bbox")[0]), box.Min); Assert.Equal(Coordinate(expected.GetProperty("bbox")[1]), box.Max);
        foreach (var sample in expected.GetProperty("samples").EnumerateArray())
        {
            Coord coordinate = Coordinate(sample[0]); Assert.Equal(sample[1].GetDouble(), Convert.ToDouble(grid.Tree.GetValue(coordinate)));
            Assert.Equal(sample[2].GetBoolean(), grid.Tree.IsValueOn(coordinate)); Assert.Equal(sample[3].GetInt32(), grid.Tree.GetValueDepth(coordinate));
        }
        ulong hash = 14695981039346656037UL, count = 0;
        foreach (var value in grid.Tree.EnumerateValues(ValueIterationMode.All))
        {
            Coord c = value.Bounds.Min; hash = Hash(hash, c.X); hash = Hash(hash, c.Y); hash = Hash(hash, c.Z); hash = Hash(hash, 3 - value.Level);
            hash = Hash(hash, (byte)(value.IsActive ? 1 : 0)); hash = Hash(hash, value.Value); ++count;
        }
        Assert.Equal(expected.GetProperty("values").GetUInt64(), count); Assert.Equal(expected.GetProperty("hash").GetString(), hash.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}