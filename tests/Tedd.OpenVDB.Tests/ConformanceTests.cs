using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

/// <summary>Expected states emitted by the pinned, compiled upstream C++ implementation.</summary>
public sealed class ConformanceTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-int32.json"))));

    public static IEnumerable<object[]> Cases() => Fixture.Value.RootElement.GetProperty("cases")
        .EnumerateArray().Select(item => new object[] { item.GetProperty("name").GetString()! });

    [Theory]
    [MemberData(nameof(Cases))]
    public void OperationTraceMatchesPinnedNativeOpenVdb(string name)
    {
        JsonElement scenario = Fixture.Value.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == name);
        int background = scenario.GetProperty("background").GetInt32();
        var tree = new VdbTree<int>(background);
        var other = new VdbTree<int>(background + 100);
        int ordinal = 0;
        foreach (JsonElement step in scenario.GetProperty("steps").EnumerateArray())
        {
            string context = $"{name}, step {ordinal++} ({step.GetProperty("op").GetString()})";
            Apply(ref tree, ref other, step);
            Verify(tree, other, step, context);
        }
    }

    [Fact]
    public void FixtureIdentifiesTheExactUpstreamRevision()
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("upstreamCommit").GetString());
        Assert.Equal("13.0.1", Fixture.Value.RootElement.GetProperty("upstreamVersion").GetString());
        Assert.Equal("Tree4<int32_t,5,4,3>", Fixture.Value.RootElement.GetProperty("tree").GetString());
    }

    private static Coord Coordinate(JsonElement element) => new(element[0].GetInt32(), element[1].GetInt32(), element[2].GetInt32());

    private static void Apply(ref VdbTree<int> tree, ref VdbTree<int> other, JsonElement step, string? operation = null)
    {
        operation ??= step.GetProperty("op").GetString()!;
        if (operation.StartsWith("other-", StringComparison.Ordinal))
        {
            Apply(ref other, ref tree, step, operation[6..]);
            return;
        }
        Coord at = Coordinate(step.GetProperty("at"));
        int value = step.GetProperty("value").GetInt32();
        bool active = step.GetProperty("active").GetBoolean();
        switch (operation)
        {
            case "on": tree.SetValueOn(at, value); break;
            case "off": tree.SetValueOff(at, value); break;
            case "only": tree.SetValueOnly(at, value); break;
            case "onExisting": tree.SetValueOn(at); break;
            case "offExisting": tree.SetValueOff(at); break;
            case "active": tree.SetActiveState(at, active); break;
            case "tile": tree.AddTile(step.GetProperty("level").GetInt32(), at, value, active); break;
            case "fill": tree.Fill(new CoordBBox(at, Coordinate(step.GetProperty("max"))), value, active); break;
            case "union": tree.TopologyUnion(other, active); break;
            case "intersection": tree.TopologyIntersection(other); break;
            case "difference": tree.TopologyDifference(other); break;
            case "prune": tree.Prune(); break;
            case "clear": tree.Clear(); break;
            case "copy":
                var original = tree;
                tree = original.DeepCopy();
                // Mutating and clearing the source must not modify the copy's values or topology.
                original.SetValueOn(new Coord(23, 31, -57), int.MinValue);
                original.Clear();
                break;
            case "noop": break;
            default: throw new InvalidOperationException("Unknown fixture operation");
        }
    }

    private static void Verify(VdbTree<int> tree, VdbTree<int> other, JsonElement expected, string context)
    {
        if (expected.TryGetProperty("sameTopology", out JsonElement sameTopology))
            Assert.True(tree.HasSameTopology(other) == sameTopology.GetBoolean(), $"{context}: structural topology comparison differs");
        ulong count = expected.GetProperty("activeCount").GetUInt64();
        long leafCount = expected.GetProperty("leafCount").GetInt64();
        Assert.True(count == tree.ActiveVoxelCount, $"{context}: active count {tree.ActiveVoxelCount}, expected {count}");
        Assert.True(leafCount == tree.LeafCount, $"{context}: leaf count {tree.LeafCount}, expected {leafCount}");
        JsonElement bounds = expected.GetProperty("bounds");
        bool hasBounds = tree.TryGetActiveVoxelBoundingBox(out CoordBBox actualBounds);
        Assert.True(hasBounds == (bounds.ValueKind != JsonValueKind.Null), $"{context}: active bounding-box presence differs");
        if (hasBounds)
        {
            var expectedBounds = new CoordBBox(Coordinate(bounds[0]), Coordinate(bounds[1]));
            Assert.True(expectedBounds == actualBounds, $"{context}: bounds {actualBounds}, expected {expectedBounds}");
        }
        foreach (JsonElement sample in expected.GetProperty("samples").EnumerateArray())
        {
            Coord at = Coordinate(sample.GetProperty("at"));
            int value = sample.GetProperty("value").GetInt32();
            bool active = sample.GetProperty("active").GetBoolean();
            int depth = sample.GetProperty("depth").GetInt32();
            Assert.True(tree.GetValue(at) == value, $"{context}, {at}: value {tree.GetValue(at)}, expected {value}");
            Assert.True(tree.IsValueOn(at) == active, $"{context}, {at}: active state differs");
            Assert.True(tree.GetValueDepth(at) == depth, $"{context}, {at}: depth {tree.GetValueDepth(at)}, expected {depth}");
        }
    }
}
