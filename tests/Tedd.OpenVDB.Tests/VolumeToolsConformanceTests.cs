using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class VolumeToolsConformanceTests
{
    private static readonly Lazy<Dictionary<string, JsonElement>> Fixtures = new(() =>
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-volume-tools.json")));
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", document.RootElement.GetProperty("upstreamCommit").GetString());
        return document.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(static entry => entry.GetProperty("name").GetString()!, static entry => entry.Clone());
    });

    public static IEnumerable<object[]> NativeCases() => Fixtures.Value.Keys.Select(static name => new object[] { name });

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void PruningFloodFillAndMorphologyMatchPinnedNativeOpenVdb(string name)
    {
        var fixture = Fixtures.Value[name];
        var tree = new VdbTree<float>(fixture.GetProperty("background").GetSingle());
        foreach (var initial in fixture.GetProperty("initial").EnumerateArray())
        {
            var coordinate = Coordinate(initial.GetProperty("min"));
            var value = initial.GetProperty("value").GetSingle();
            var active = initial.GetProperty("active").GetBoolean();
            switch (initial.GetProperty("op").GetString())
            {
                case "fill": tree.Fill(new CoordBBox(coordinate, Coordinate(initial.GetProperty("max"))), value, active); break;
                case "tile": tree.AddTile(initial.GetProperty("level").GetInt32(), coordinate, value, active); break;
                case "touch": tree.TouchLeaf(coordinate); break;
                default: if (active) tree.SetValueOn(coordinate, value); else tree.SetValueOff(coordinate, value); break;
            }
        }
        var tolerance = fixture.GetProperty("tolerance").GetSingle();
        var outside = fixture.GetProperty("outside").GetSingle();
        var inside = fixture.GetProperty("inside").GetSingle();
        var iterations = fixture.GetProperty("iterations").GetInt32();
        var neighbors = (NearestNeighbors)fixture.GetProperty("neighbors").GetInt32();
        var mode = (TilePolicy)fixture.GetProperty("mode").GetInt32();
        var minLevel = fixture.GetProperty("minLevel").GetInt32();
        switch (fixture.GetProperty("operation").GetString())
        {
            case "prune": Prune.Apply(tree, tolerance); break;
            case "pruneTiles": Prune.PruneTiles(tree, tolerance); break;
            case "pruneInactive": Prune.PruneInactive(tree); break;
            case "pruneInactiveWithValue": Prune.PruneInactiveWithValue(tree, outside); break;
            case "pruneLevelSet": Prune.PruneLevelSet(tree); break;
            case "pruneLevelSetWithValues": Prune.PruneLevelSet(tree, outside, inside); break;
            case "flood": SignedFloodFill.Apply(tree, minLevel); break;
            case "floodWithValues": SignedFloodFill.WithValues(tree, outside, inside, minLevel); break;
            case "dilate": Morphology.DilateActiveValues(tree, iterations, neighbors, mode); break;
            case "erode": Morphology.ErodeActiveValues(tree, iterations, neighbors, mode); break;
            default: throw new InvalidOperationException(name);
        }
        Assert.Equal(fixture.GetProperty("resultBackground").GetSingle(), tree.Background);
        Assert.Equal(fixture.GetProperty("activeCount").GetUInt64(), tree.ActiveVoxelCount);
        Assert.Equal(fixture.GetProperty("leafCount").GetInt64(), tree.LeafCount);
        foreach (var sample in fixture.GetProperty("samples").EnumerateArray())
        {
            var coordinate = new Coord(sample[0].GetInt32(), sample[1].GetInt32(), sample[2].GetInt32());
            var active = tree.ProbeValue(coordinate, out var value);
            var expectedValue = sample[3].GetSingle();
            var expectedActive = sample[4].GetInt32() != 0;
            var expectedDepth = sample[5].GetInt32();
            Assert.True(value.Equals(expectedValue), $"{name} at {coordinate}: expected value {expectedValue}, got {value}.");
            Assert.True(active == expectedActive, $"{name} at {coordinate}: activity mismatch.");
            Assert.True(tree.GetValueDepth(coordinate) == expectedDepth, $"{name} at {coordinate}: expected depth {expectedDepth}, got {tree.GetValueDepth(coordinate)}.");
        }
    }

    private static Coord Coordinate(JsonElement values) => new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());
}
