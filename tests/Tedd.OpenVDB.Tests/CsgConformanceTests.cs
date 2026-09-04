using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class CsgConformanceTests
{
    private static readonly Lazy<Dictionary<string, JsonElement>> Fixtures = new(() =>
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-csg.json")));
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", document.RootElement.GetProperty("upstreamCommit").GetString());
        return document.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(static entry => entry.GetProperty("name").GetString()!, static entry => entry.Clone());
    });

    public static IEnumerable<object[]> NativeCases() => Fixtures.Value.Keys.Select(static name => new object[] { name });

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void CsgValuesActivityTopologyAndBothInputPostStatesMatchNative(string name)
    {
        var fixture = Fixtures.Value[name];
        var a = Initialize(fixture.GetProperty("initialA"), fixture.GetProperty("backgroundA").GetSingle());
        var b = Initialize(fixture.GetProperty("initialB"), fixture.GetProperty("backgroundB").GetSingle());
        var prune = fixture.GetProperty("prune").GetBoolean();
        var cancelled = fixture.GetProperty("cancel").GetBoolean();
        // Accessors obtained before mutation must observe all moved/replaced nodes afterward.
        var accessorA = a.GetAccessor();
        var accessorB = b.GetAccessor();
        accessorA.GetValue(new Coord(1)); accessorB.GetValue(new Coord(1));
        switch (fixture.GetProperty("operation").GetString())
        {
            case "compSum": Composite.CompSum(a, b); break;
            case "compMul": Composite.CompMul(a, b); break;
            case "compMin": Composite.CompMin(a, b); break;
            case "compMax": Composite.CompMax(a, b); break;
            case "compDiv": Composite.CompDiv(a, b); break;
            case "compReplace": Composite.CompReplace(a, b); break;
            case "activeCopy": Composite.CompActiveLeafVoxels(b, a); break;
            case "activeAdd": Composite.CompActiveLeafVoxels(b, a, static (x, y) => x + y); break;
            case "unionCopy": a = Composite.CsgUnionCopy(a, b); accessorA = a.GetAccessor(); break;
            case "intersectionCopy": a = Composite.CsgIntersectionCopy(a, b); accessorA = a.GetAccessor(); break;
            case "differenceCopy": a = Composite.CsgDifferenceCopy(a, b); accessorA = a.GetAccessor(); break;
            case "union": Composite.CsgUnion(a, b, prune, cancelled); break;
            case "intersection": Composite.CsgIntersection(a, b, prune, cancelled); break;
            case "difference": Composite.CsgDifference(a, b, prune, cancelled); break;
            default: throw new InvalidOperationException(name);
        }
        Check(name + " A", fixture.GetProperty("resultA"), a, accessorA);
        Check(name + " B", fixture.GetProperty("resultB"), b, accessorB);
    }

    private static VdbTree<float> Initialize(JsonElement operations, float background)
    {
        var tree = new VdbTree<float>(background);
        foreach (var initial in operations.EnumerateArray())
        {
            var coordinate = Coordinate(initial.GetProperty("min"));
            var value = initial.GetProperty("value").GetSingle();
            var active = initial.GetProperty("active").GetBoolean();
            switch (initial.GetProperty("op").GetString())
            {
                case "sphere": tree = LevelSetSphere.Create(value, new Vec3f(coordinate.X, coordinate.Y, coordinate.Z), 1, background).Tree; break;
                case "fill": tree.Fill(new CoordBBox(coordinate, Coordinate(initial.GetProperty("max"))), value, active); break;
                case "tile": tree.AddTile(initial.GetProperty("level").GetInt32(), coordinate, value, active); break;
                case "touch": tree.TouchLeaf(coordinate); break;
                default: if (active) tree.SetValueOn(coordinate, value); else tree.SetValueOff(coordinate, value); break;
            }
        }
        return tree;
    }

    private static void Check(string name, JsonElement fixture, VdbTree<float> tree, ValueAccessor<float> accessor)
    {
        Assert.True(fixture.GetProperty("activeCount").GetUInt64() == tree.ActiveVoxelCount,
            $"{name}: expected active count {fixture.GetProperty("activeCount")}, actual {tree.ActiveVoxelCount}.");
        Assert.True(fixture.GetProperty("leafCount").GetInt64() == tree.LeafCount,
            $"{name}: expected leaf count {fixture.GetProperty("leafCount")}, actual {tree.LeafCount}.");
        foreach (var sample in fixture.GetProperty("samples").EnumerateArray())
        {
            var coordinate = new Coord(sample[0].GetInt32(), sample[1].GetInt32(), sample[2].GetInt32());
            var active = tree.ProbeValue(coordinate, out var value);
            var expectedValue = sample[3].ValueKind == JsonValueKind.String ? float.Parse(sample[3].GetString()!, System.Globalization.CultureInfo.InvariantCulture) : sample[3].GetSingle();
            var expectedActive = sample[4].GetInt32() != 0;
            var expectedDepth = sample[5].GetInt32();
            Assert.True(value.Equals(expectedValue), $"{name} at {coordinate}: expected value {expectedValue}, got {value}.");
            Assert.True(active == expectedActive, $"{name} at {coordinate}: activity mismatch.");
            Assert.True(tree.GetValueDepth(coordinate) == expectedDepth, $"{name} at {coordinate}: expected depth {expectedDepth}, got {tree.GetValueDepth(coordinate)}.");
            Assert.Equal(value, accessor.GetValue(coordinate));
            Assert.Equal(active, accessor.IsValueOn(coordinate));
        }
    }

    private static Coord Coordinate(JsonElement values) => new(values[0].GetInt32(), values[1].GetInt32(), values[2].GetInt32());
}
