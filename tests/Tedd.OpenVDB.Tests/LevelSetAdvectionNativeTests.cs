using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public class LevelSetAdvectionNativeTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() => { using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-levelset-advection.json"))); return document.RootElement.Clone(); });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((test, index) => new object[] { index, test.GetProperty("precision").GetString()!, test.GetProperty("fieldPrecision").GetString()!, test.GetProperty("kind").GetInt32(), test.GetProperty("scheme").GetInt32(), test.GetProperty("rk").GetInt32() });
    [Fact]
    public void NativeFixturePinsAllSchemesPrecisionsAndMapProfiles()
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.GetProperty("upstreamCommit").GetString());
        Assert.Equal(150, Fixture.Value.GetProperty("cases").GetArrayLength());
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeCflCountsEveryActiveValueAndStoredProbesMatch(int index, string precision, string fieldPrecision, int kind, int scheme, int rk)
    {
        var test = Fixture.Value.GetProperty("cases")[index]; if (precision == "float") Run<float>(test, fieldPrecision, kind, scheme, rk); else Run<double>(test, fieldPrecision, kind, scheme, rk);
    }
    private static void Run<T>(JsonElement test, string fieldPrecision, int kind, int scheme, int rk) where T : unmanaged, IFloatingPointIeee754<T>
    {
        int map = test.GetProperty("map").GetInt32(); bool empty = test.GetProperty("empty").GetBoolean(); double dx = map == 2 ? 1 : 0.75;
        Grid<T> grid = empty ? new Grid<T>(T.CreateTruncating(3 * dx)) { GridClass = GridClass.LevelSet, Transform = Transform.CreateLinearTransform(dx) } : LevelSetSphere.Create<T>(T.CreateTruncating(3.25), new(-0.25, 0.5, 0.25), T.CreateTruncating(dx), T.CreateTruncating(3));
        if (map == 1) grid.Transform.PostTranslate(new(0.25, 0.5, -0.375));
        if (map == 2) grid.Transform = new(Mat4d.CreateRotation(0.21, Axis.Y));
        IVelocityField field = kind == 2 ? fieldPrecision == "float" ? new EnrightField<float>() : new EnrightField<double>() : fieldPrecision == "float" ? CreateField<Vec3f>(grid.Transform, kind) : CreateField<Vec3d>(grid.Transform, kind);
        var solver = new LevelSetAdvection<T>(grid, field) { SpatialScheme = (BiasedGradientScheme)scheme, TemporalScheme = (TemporalIntegrationScheme)rk, TrackerSpatialScheme = BiasedGradientScheme.HJWeno5, TrackerTemporalScheme = TemporalIntegrationScheme.TvdRungeKutta1, NormalizationCount = 1, GrainSize = 0 };
        var accessor = grid.GetAccessor(); accessor.GetValue(Coord.Zero);
        Assert.Equal(test.GetProperty("steps").GetUInt64(), solver.Advect(T.CreateTruncating(test.GetProperty("time0").GetDouble()), T.CreateTruncating(test.GetProperty("time1").GetDouble())));
        Assert.Equal(test.GetProperty("activeCount").GetUInt64(), grid.ActiveVoxelCount); Assert.Equal(test.GetProperty("leafCount").GetInt64(), grid.Tree.LeafCount); Assert.Equal(test.GetProperty("rootCount").GetInt32(), grid.Tree.RootEntryCount); Assert.Equal(test.GetProperty("background").GetDouble(), double.CreateTruncating(grid.Background)); Assert.Equal(0UL, grid.Tree.ActiveTileCount);
        foreach (var expected in test.GetProperty("activeValues").EnumerateArray())
        {
            Coord coordinate = new(expected[0].GetInt32(), expected[1].GetInt32(), expected[2].GetInt32()); Assert.True(accessor.IsValueOn(coordinate), coordinate.ToString()); Near<T>(Number(expected[3]), double.CreateTruncating(accessor.GetValue(coordinate)), coordinate);
        }
        int i = 0; foreach (var expectedCoordinate in Fixture.Value.GetProperty("coordinates").EnumerateArray())
        {
            Coord coordinate = new(expectedCoordinate[0].GetInt32(), expectedCoordinate[1].GetInt32(), expectedCoordinate[2].GetInt32()); var expected = test.GetProperty("probes")[i++];
            Near<T>(Number(expected[0]), double.CreateTruncating(accessor.GetValue(coordinate)), coordinate); Assert.Equal(expected[1].GetInt32() != 0, accessor.IsValueOn(coordinate)); Assert.Equal(expected[2].GetInt32(), grid.Tree.GetValueDepth(coordinate));
        }
    }
    private static IVelocityField CreateField<V>(Transform transform, int kind) where V : unmanaged
    {
        Vec3d background = kind switch { 3 => Vec3d.Zero, 4 => new(1e-4), 5 => new(double.NaN), 6 => new(double.PositiveInfinity), _ => new(0.625, -0.375, 0.25) };
        var grid = new Grid<V>(VelocityFieldTestSystems.Value<V>(background)) { Transform = transform.Copy() };
        if (kind == 1)
        {
            grid.Transform = Transform.CreateLinearTransform(0.625); grid.Transform.PostTranslate(new(-0.5, 0.125, 0.25)); grid.Transform = new Transform(grid.Transform.Matrix);
            for (int x = -12; x <= 12; ++x) for (int y = -12; y <= 12; ++y) for (int z = -12; z <= 12; ++z)
                grid.Tree.SetValue(new(x, y, z), VelocityFieldTestSystems.Value<V>(new(0.015625 * x + 0.0078125 * y + 0.125, -0.0234375 * y + 0.015625 * z - 0.0625, 0.0078125 * x * z + 0.03125)));
        }
        return new DiscreteField<V>(grid);
    }
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();
    private static void Near<T>(double expected, double actual, Coord coordinate) where T : unmanaged
    {
        if (double.IsNaN(expected)) Assert.True(double.IsNaN(actual)); else Assert.True(System.Math.Abs(expected - actual) <= (typeof(T) == typeof(float) ? 2e-6 : 1e-11) * System.Math.Max(1, System.Math.Abs(expected)), $"{coordinate}: expected {expected:R}, actual {actual:R}");
    }
}
