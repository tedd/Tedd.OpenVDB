using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetTrackingConformanceTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() =>
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-levelset-tracking.json")));
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", json.RootElement.GetProperty("upstreamCommit").GetString());
        return json.RootElement.Clone();
    });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((entry, index) => new object[] { index, entry.GetProperty("type").GetString()!, entry.GetProperty("operation").GetString()!, entry.GetProperty("scheme").GetInt32(), entry.GetProperty("rk").GetInt32(), entry.GetProperty("mask").GetInt32() });
    [Theory, MemberData(nameof(Cases))]
    public void TrackerAndFilterMatchNative(int index, string type, string operation, int scheme, int rk, int mask)
    {
        var item = Fixture.Value.GetProperty("cases")[index];
        if (type == "float") Run<float>(item, operation, scheme, rk, mask); else Run<double>(item, operation, scheme, rk, mask);
    }
    private static void Run<T>(JsonElement item, string operation, int scheme, int rk, int maskMode) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var grid = Shape<T>(item.GetProperty("shape").GetInt32());
        var accessor = grid.GetAccessor(); accessor.GetValue(new(0));
        var filter = new LevelSetFilter<T>(grid) { GrainSize = 0, SpatialScheme = (BiasedGradientScheme)scheme, TemporalScheme = (TemporalIntegrationScheme)rk, NormalizationCount = item.GetProperty("count").GetInt32(), IsMaskInverted = maskMode == 2 };
        filter.SetMaskRange(0.125f, 0.875f);
        var mask = maskMode == 0 ? null : maskMode == 4 && operation == "offset" ? new Grid<float>((VdbTree<float>)(object)grid.Tree) { Transform = Transform.CreateLinearTransform(1.5) } : Mask(maskMode);
        switch (operation)
        {
            case "normalize": if (maskMode == 0) filter.Normalize(); else filter.Normalize(NormalizationMask()); break;
            case "track": filter.Track(); break;
            case "prune": filter.Trimming = (TrimMode)maskMode; filter.Prune(); break;
            case "dilate": filter.Dilate(maskMode); break;
            case "erode": filter.Erode(maskMode); break;
            case "resize": filter.Resize(maskMode); break;
            case "mean": filter.Mean(1, mask); break;
            case "gaussian": filter.Gaussian(1, mask); break;
            case "median": filter.Median(1, mask); break;
            case "laplacian": filter.Laplacian(mask); break;
            case "curvature": filter.MeanCurvature(mask); break;
            case "fillet": filter.Fillet(mask); break;
            case "offset": filter.Offset(T.CreateChecked(-0.65), mask); break;
            case "background": ChangeBackground.LevelSet(grid.Tree, T.CreateChecked(4)); break;
            case "asymmetric": ChangeBackground.AsymmetricLevelSet(grid.Tree, T.CreateChecked(4), T.CreateChecked(-5)); break;
            case "generalBackground": ChangeBackground.Apply(grid.Tree, T.CreateChecked(4)); break;
            default: throw new InvalidOperationException(operation);
        }
        var expected = item.GetProperty("result");
        Assert.Equal(expected.GetProperty("active").GetUInt64(), grid.ActiveVoxelCount);
        Assert.Equal(expected.GetProperty("leaves").GetInt64(), grid.Tree.LeafCount);
        Assert.Equal(expected.GetProperty("roots").GetInt32(), grid.Tree.RootEntryCount);
        Assert.Equal(expected.GetProperty("tiles").GetUInt64(), grid.Tree.ActiveTileCount);
        Assert.Equal(Number(expected.GetProperty("background")), double.CreateChecked(grid.Background));
        var samples = expected.GetProperty("samples"); int n = 0;
        foreach (var coordinate in Fixture.Value.GetProperty("coordinates").EnumerateArray())
        {
            var point = new Coord(coordinate[0].GetInt32(), coordinate[1].GetInt32(), coordinate[2].GetInt32()); var sample = samples[n++];
            double value = double.CreateChecked(accessor.GetValue(point)), wanted = Number(sample[0]);
            double tolerance = System.Math.Max(1, System.Math.Abs(wanted)) * (typeof(T) == typeof(float) ? 1e-6 : 1e-11);
            Assert.True(wanted == value || double.IsNaN(wanted) && double.IsNaN(value) || System.Math.Abs(wanted - value) <= tolerance, $"{point}: expected {wanted:R}, actual {value:R}");
            Assert.Equal(sample[1].GetInt32() != 0, accessor.IsValueOn(point));
            Assert.Equal(sample[2].GetInt32(), grid.Tree.GetValueDepth(point));
        }
    }
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();
    internal static Grid<T> Shape<T>(int id) where T : unmanaged, IFloatingPointIeee754<T>
    {
        T C(double value) => T.CreateChecked(value);
        var grid = LevelSetSphere.Create<T>(C(4.5), new(-0.25, 0.5, 0.25), C(0.75), C(3));
        if (id == 0) foreach (var value in grid.Tree.EnumerateValueHandles()) value.SetValueOnly(value.Value * C(1.125) + C((value.Coordinate.X + 2 * value.Coordinate.Y - value.Coordinate.Z) % 5) * C(0.03125));
        if (id == 1)
        {
            grid = new(C(2.25)) { Transform = Transform.CreateLinearTransform(0.75), GridClass = GridClass.LevelSet };
            for (int x = -5; x <= 5; ++x) for (int y = -5; y <= 5; ++y) for (int z = -5; z <= 5; ++z)
            {
                T value = C(System.Math.Max(System.Math.Max(System.Math.Abs(x), System.Math.Abs(y)), System.Math.Abs(z)) - 3) * C(0.75);
                if (T.Abs(value) < C(2.25)) grid.Tree.SetValueOn(new(x, y, z), value); else grid.Tree.SetValueOff(new(x, y, z), value);
            }
        }
        if (id == 2)
        {
            grid = new(C(3)) { GridClass = GridClass.LevelSet };
            for (int n = 0; n < 7; ++n) grid.Tree.SetValueOn(new(n, 0, 0), C(n - 3));
            grid.Tree.AddTile(1, new(16, 0, 0), C(-0.5), true); grid.Tree.AddTile(2, new(128, 0, 0), C(0.5), true);
            grid.Tree.AddTile(3, new(4096, 0, 0), C(-0.5), true); grid.Tree.AddTile(3, new(8192, 0, 0), C(-2), false);
            grid.Tree.SetValueOff(new(0, 1, 0), T.NaN);
        }
        return grid;
    }
    internal static Grid<float> Mask(int mode)
    {
        var mask = new Grid<float>(0.5f) { Transform = Transform.CreateLinearTransform(mode == 3 ? 1.5 : 0.75) };
        for (int x = -12; x <= 12; ++x) for (int y = -10; y <= 10; ++y) for (int z = -10; z <= 10; ++z) mask.Tree.SetValueOn(new(x, y, z), (x + 8) * 0.0625f);
        return mask;
    }
    internal static VdbTree<bool> NormalizationMask()
    {
        var mask = new VdbTree<bool>();
        for (int x = -10; x <= 10; ++x) for (int y = -10; y <= 10; ++y) for (int z = -10; z <= 10; ++z) if ((x + y + z) % 3 == 0) mask.SetValueOn(new(x, y, z), false);
        mask.AddTile(1, new(24, 0, 0), false, true); return mask;
    }
}
