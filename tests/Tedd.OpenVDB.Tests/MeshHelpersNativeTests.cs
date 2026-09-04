// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class MeshHelpersNativeTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-mesh-helpers.json"))));
    public static IEnumerable<object[]> Cases()
    {
        int index = 0; foreach (var c in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
            yield return [index++, $"{c.GetProperty("type").GetString()}/style={c.GetProperty("style")}/scene={c.GetProperty("scene")}/mode={c.GetProperty("mode")}/width={c.GetProperty("exterior")},{c.GetProperty("interior")}"];
    }
    [Theory, MemberData(nameof(Cases))]
    public void NativeHelpersAndBandFailuresMatch(int index, string label)
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        var c = Fixture.Value.RootElement.GetProperty("cases")[index]; if (c.GetProperty("type").GetString() == "float") Run<float>(c, label); else Run<double>(c, label);
    }
    private static void Run<T>(JsonElement c, string label) where T : unmanaged, IFloatingPointIeee754<T>
    {
        int style = c.GetProperty("style").GetInt32(), scene = c.GetProperty("scene").GetInt32(), mode = c.GetProperty("mode").GetInt32();
        float exterior = (float)Number(c.GetProperty("exterior")), interior = (float)Number(c.GetProperty("interior"));
        var transform = Transform.CreateLinearTransform(mode == 0 ? 1 : 0.75);
        if (mode == 2) { transform.PostRotate(0.31, Axis.Z); transform.PostTranslate(new(0.5, -1, 0.75)); }
        if (mode == 3) transform.PostScale(new Vec3d(1, 1.5, 0.5));
        var points = c.GetProperty("points").EnumerateArray().Select(p => new Vec3f(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())).ToArray();
        var triangles = c.GetProperty("triangles").EnumerateArray().Select(p => new MeshTriangle(p[0].GetInt32(), p[1].GetInt32(), p[2].GetInt32())).ToArray();
        var quads = c.GetProperty("quads").EnumerateArray().Select(p => new MeshQuad(p[0].GetInt32(), p[1].GetInt32(), p[2].GetInt32(), p[3].GetInt32())).ToArray();
        Grid<T> Convert() => style switch
        {
            0 => MeshToVolume.Convert<T>(new QuadAndTriangleDataAdapter<Vec3f>(points, triangles, quads), transform, exterior, interior),
            1 => MeshToVolume.CreateLevelSetBox<T>(new BBoxd(new Vec3d(-2, -1.5, -2.25), new Vec3d(2.75, 2.5, 1.75)), transform, exterior),
            2 => MeshToVolume.ToSignedDistanceField<T>(transform, points, triangles, quads, exterior, interior),
            3 => MeshToVolume.ToUnsignedDistanceField<T>(transform, points, triangles, quads, exterior),
            4 => MeshToVolume.ToLevelSet<T>(transform, points, triangles, quads, exterior),
            _ => Trace<T>(scene)
        };
        if (c.GetProperty("error").GetBoolean()) { Assert.Throws<ArgumentOutOfRangeException>(() => Convert()); return; }
        var grid = Convert(); Near(Number(c.GetProperty("background")), double.CreateTruncating(grid.Background), label);
        Assert.Equal(c.GetProperty("class").GetInt32(), (int)grid.GridClass);
        Assert.True(c.GetProperty("active").GetUInt64() == grid.ActiveVoxelCount, $"{label}: active native={c.GetProperty("active")}, managed={grid.ActiveVoxelCount}");
        Assert.Equal(c.GetProperty("leaves").GetArrayLength(), grid.Tree.LeafCount);
        foreach (var leafJson in c.GetProperty("leaves").EnumerateArray())
        {
            var p = leafJson.GetProperty("origin"); var leaf = grid.Tree.ProbeLeaf(new(p[0].GetInt32(), p[1].GetInt32(), p[2].GetInt32())); Assert.NotNull(leaf);
            string active = leafJson.GetProperty("active").GetString()!; var values = leafJson.GetProperty("values");
            for (int offset = 0; offset < 512; offset++)
            {
                Assert.True((active[offset] == '1') == leaf.IsValueOn(offset), $"{label}: activity {leaf.OffsetToGlobalCoord(offset)}");
                Near(Number(values[offset]), double.CreateTruncating(leaf.GetValue(offset)), label + $"/{leaf.OffsetToGlobalCoord(offset)}");
            }
        }
        foreach (var p in c.GetProperty("probes").EnumerateArray())
        {
            Coord coordinate = new(p[0].GetInt32(), p[1].GetInt32(), p[2].GetInt32());
            Near(Number(p[3]), double.CreateTruncating(grid.Tree.GetValue(coordinate)), label + $"/probe{coordinate}");
            Assert.Equal(p[4].GetInt32() != 0, grid.Tree.IsValueOn(coordinate)); Assert.Equal(p[5].GetInt32(), grid.Tree.GetValueDepth(coordinate));
        }
    }
    private static Grid<T> Trace<T>(int scene) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var grid = new Grid<T>(T.CreateTruncating(100));
        for (int x = 0; x < 16; x++) for (int y = 0; y < 8; y++) for (int z = 0; z < 8; z++)
        {
            Coord c = new(x - 8, y, z); bool barrier = x is 1 or 14 || y is 1 or 6 || z is 1 or 6;
            if (scene == 1 && x == 1 && y == 3 && z == 3) barrier = false;
            T value = T.CreateTruncating(barrier ? 0.5 : 100); if (scene == 2 && x == 5 && y == 3 && z == 3) value = T.CreateTruncating(-100);
            if (scene == 3 && x == 7) continue;
            grid.Tree.SetValueOff(c, value); if ((x * 64 + y * 8 + z) % 7 == 0) grid.Tree.SetActiveState(c, true);
        }
        grid.Tree.AddTile(1, new(32), T.CreateTruncating(4), true); MeshToVolume.TraceExteriorBoundaries(grid.Tree); return grid;
    }
    private static double Number(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : double.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    private static void Near(double expected, double actual, string label)
    {
        if (!double.IsFinite(expected)) { Assert.Equal(expected, actual); return; }
        double tolerance = label.StartsWith("double/", StringComparison.Ordinal) ? 3e-12 : 3e-6;
        Assert.True(System.Math.Abs(expected - actual) <= tolerance * System.Math.Max(1, System.Math.Abs(expected)), $"{label}: native={expected:R}, managed={actual:R}");
    }
}
