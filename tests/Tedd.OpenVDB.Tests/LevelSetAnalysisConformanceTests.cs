using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetAnalysisConformanceTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() =>
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-levelset-analysis.json")));
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", json.RootElement.GetProperty("upstreamCommit").GetString());
        return json.RootElement.Clone();
    });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select(entry => new object[] { entry.GetProperty("shape").GetInt32(), entry.GetProperty("operation").GetInt32() });
    public static IEnumerable<object[]> Measurements() => Fixture.Value.GetProperty("measurements").EnumerateArray().Select(entry => new object[] { entry.GetProperty("type").GetString()!, entry.GetProperty("voxelSize").GetDouble() });

    [Theory, MemberData(nameof(Cases))]
    public void MasksFogAndSegmentsMatchNativeValuesTopologyAndMetadata(int shape, int operation)
    {
        var expected = Fixture.Value.GetProperty("cases").EnumerateArray().Single(entry => entry.GetProperty("shape").GetInt32() == shape && entry.GetProperty("operation").GetInt32() == operation).GetProperty("outputs");
        var grid = Shape(shape);
        if (operation < 3) CheckOutputs(expected, new[] { LevelSetUtil.SdfInteriorMask(grid, operation == 0 ? 0 : operation == 1 ? 1 : 99) });
        else if (operation < 5) CheckOutputs(expected, new[] { LevelSetUtil.ExtractIsosurfaceMask(grid, operation == 3 ? 0 : 0.5f) });
        else if (operation < 8)
        {
            if (operation == 5) LevelSetUtil.SdfToFogVolume(grid); else LevelSetUtil.SdfToFogVolume(grid, operation == 6 ? 1.25f : 0);
            CheckOutputs(expected, new[] { grid });
        }
        else if (operation == 8) CheckOutputs(expected, Segmentation.ExtractActiveVoxelSegmentMasks(grid));
        else if (operation == 9) CheckOutputs(expected, Segmentation.SegmentActiveVoxels(grid));
        else CheckOutputs(expected, Segmentation.SegmentSdf(grid));
    }

    [Theory, MemberData(nameof(Measurements))]
    public void AreaVolumeAndCurvatureIntegralsMatchNative(string type, double voxelSize)
    {
        var expected = Fixture.Value.GetProperty("measurements").EnumerateArray().Single(entry => entry.GetProperty("type").GetString() == type && entry.GetProperty("voxelSize").GetDouble() == voxelSize).GetProperty("values");
        if (type == "float") CheckMeasurement<float>(expected, voxelSize); else CheckMeasurement<double>(expected, voxelSize);
    }

    private static void CheckMeasurement<T>(JsonElement expected, double voxelSize) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var grid = LevelSetSphere.Create<T>(T.CreateChecked(7.25), new(-1.25, 2.5, 0.75), T.CreateChecked(voxelSize), T.CreateChecked(3));
        var m = new LevelSetMeasure<T>(grid);
        double[] values = [m.Area(), m.Volume(), m.TotalMeanCurvature(), m.TotalGaussianCurvature(), m.AverageMeanCurvature(), m.AverageGaussianCurvature(), m.Area(false), m.Volume(false), m.TotalMeanCurvature(false), m.AverageMeanCurvature(false), m.AverageGaussianCurvature(false), m.EulerCharacteristic, m.Genus];
        for (int i = 0; i < values.Length; ++i)
        {
            double wanted = Number(expected[i]);
            double tolerance = System.Math.Max(1, System.Math.Abs(wanted)) * (typeof(T) == typeof(float) ? 1e-8 : 1e-10);
            Assert.True(System.Math.Abs(wanted - values[i]) <= tolerance, $"measure[{i}] expected {wanted:R}, actual {values[i]:R}");
        }
    }

    private static void CheckOutputs<T>(JsonElement expected, IReadOnlyList<Grid<T>> actual) where T : unmanaged
    {
        var remaining = expected.EnumerateArray().ToList();
        Assert.Equal(remaining.Count, actual.Count);
        foreach (var grid in actual)
        {
            int index = remaining.FindIndex(item => Difference(item, grid) is null);
            Assert.True(index >= 0, remaining.Count == 0 ? "Unexpected extra segment" : string.Join("; ", remaining.Select(item => Difference(item, grid))));
            remaining.RemoveAt(index);
        }
    }

    private static string? Difference<T>(JsonElement expected, Grid<T> grid) where T : unmanaged
    {
        var tree = grid.Tree;
        if (expected.GetProperty("active").GetUInt64() != tree.ActiveVoxelCount) return $"active {expected.GetProperty("active")} != {tree.ActiveVoxelCount}";
        if (expected.GetProperty("leaves").GetInt64() != tree.LeafCount) return $"leaves {expected.GetProperty("leaves")} != {tree.LeafCount}";
        if (expected.GetProperty("roots").GetInt32() != tree.RootEntryCount) return $"roots {expected.GetProperty("roots")} != {tree.RootEntryCount}";
        if (expected.GetProperty("tiles").GetUInt64() != tree.ActiveTileCount) return $"tiles {expected.GetProperty("tiles")} != {tree.ActiveTileCount}";
        if (!Equal(Number(expected.GetProperty("background")), Convert.ToDouble(tree.Background, CultureInfo.InvariantCulture))) return "background mismatch";
        if (expected.GetProperty("name").GetString() != grid.Name || expected.GetProperty("class").GetInt32() != (int)grid.GridClass || expected.GetProperty("custom").GetBoolean() != grid.Metadata.ContainsKey("custom")) return "metadata mismatch";
        var samples = expected.GetProperty("samples");
        int n = 0;
        foreach (var point in Fixture.Value.GetProperty("coordinates").EnumerateArray())
        {
            var c = new Coord(point[0].GetInt32(), point[1].GetInt32(), point[2].GetInt32());
            var sample = samples[n++];
            double actual = Convert.ToDouble(tree.GetValue(c), CultureInfo.InvariantCulture), wanted = Number(sample[0]);
            if (!Equal(wanted, actual)) return $"value at {c}: {wanted:R} != {actual:R}";
            if ((sample[1].GetInt32() != 0) != tree.IsValueOn(c)) return $"activity at {c}";
            if (sample[2].GetInt32() != tree.GetValueDepth(c)) return $"depth at {c}: {sample[2]} != {tree.GetValueDepth(c)}";
        }
        return null;
    }
    private static bool Equal(double a, double b) => a == b || double.IsNaN(a) && double.IsNaN(b);
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();

    internal static Grid<float> Shape(int id)
    {
        var grid = new Grid<float>(3); var tree = grid.Tree;
        if (id == 1) for (int x = -2; x <= 9; ++x) for (int y = -1; y <= 2; ++y) for (int z = -1; z <= 2; ++z) { var c = new Coord(x, y, z); tree.SetValueOff(c, x * 0.5f); if ((x + y + z) % 3 != 0) tree.SetValueOn(c); }
        if (id == 2) { tree.SetValueOff(new(0), -2); tree.SetValueOn(new(1, 0, 0), 2); tree.SetValueOff(new(4096, 0, 0), -1); }
        if (id == 3) { tree.AddTile(3, new(0), -3, false); tree.AddTile(3, new(8192, 0, 0), 3, false); }
        if (id == 4) { tree.AddTile(1, new(0), -3, true); tree.SetValueOn(new(8, 0, 0), 2); tree.AddTile(1, new(24, 0, 0), 1, true); }
        if (id == 5) { tree.AddTile(2, new(128, 0, 0), -3, false); tree.SetValueOn(new(0), -1); }
        if (id == 6) { foreach (Coord c in new Coord[] { new(-8, 0, 0), new(-7, 0, 0), new(-1, 0, 0), new(0), new(7, 7, 7), new(8, 7, 7), new(9, 8, 7), new(24, 0, 0) }) tree.SetValueOn(c, c.X + 1); tree.SetValueOff(new(40), -2); }
        if (id == 7) { tree.SetValueOn(new(0), 1); tree.SetValueOn(new(1, 0, 0), 2); }
        if (id == 8) grid = LevelSetSphere.Create(7.5f, new Vec3f(-1.25f, 2, 3.5f), 0.75f, 3);
        if (id is 9 or 10)
        {
            grid = LevelSetSphere.Create(4.0f, new Vec3f(-7, 0, 0), 1, 3);
            var rhs = LevelSetSphere.Create(4.0f, new Vec3f(8, 0, 0), 1, 3); Composite.CsgUnion(grid, rhs);
            if (id == 10) foreach (var value in grid.Tree.EnumerateValueHandles(ValueIterationMode.All).ToArray()) if (value.Value < -1.5f) { value.SetValue(-1.5f); value.SetActiveState(false); }
        }
        if (id == 11) { tree.SetValueOn(new(0), float.NaN); tree.SetValueOn(new(1, 0, 0), -1); tree.SetValueOff(new(2, 0, 0), float.NaN); }
        if (id == 12) tree.AddTile(1, new(0), -3, true);
        if (id == 13) { tree.AddTile(3, new(0), -3, false); tree.SetValueOn(new(4096, 0, 0), -1); }
        grid.Name = "analysis"; grid.SetMetadata("custom", "kept"); grid.GridClass = GridClass.LevelSet; return grid;
    }
}
