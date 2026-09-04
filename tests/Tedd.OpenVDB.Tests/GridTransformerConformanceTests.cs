using System.Globalization;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class GridTransformerConformanceTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() =>
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-grid-transformer.json")));
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", document.RootElement.GetProperty("upstreamCommit").GetString());
        return document.RootElement.Clone();
    });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((c, index) => new object[] { index, c.GetProperty("type").GetString()!, c.GetProperty("profile").GetInt32(), c.GetProperty("kernel").GetInt32(), c.GetProperty("shape").GetInt32(), c.GetProperty("operation").GetInt32(), c.GetProperty("staggered").GetInt32() != 0 });
    [Theory, MemberData(nameof(Cases))]
    public void SamplingAndTopologyMatchNative(int index, string type, int profile, int kernel, int shape, int operation, bool staggered)
    {
        if (type == "float") Run<float>(index, profile, kernel, shape, operation, staggered);
        else if (type == "double") Run<double>(index, profile, kernel, shape, operation, staggered);
        else Run<Vec3f>(index, profile, kernel, shape, operation, staggered);
    }
    private static void Run<T>(int index, int profile, int kernel, int shape, int operation, bool staggered) where T : unmanaged
    {
        var item = Fixture.Value.GetProperty("cases")[index];
        Grid<T> input = Shape<T>(shape), output = Output<T>(); var tree = output.Tree; var oldAccessor = output.GetAccessor();
        GridTransformer transformer = CreateTransformer(profile); transformer.Threaded = false; transformer.TransformTiles = item.GetProperty("transformTiles").GetInt32() != 0;
        var expectedMatrix = item.GetProperty("matrix");
        for (int row = 0; row < 4; ++row) for (int col = 0; col < 4; ++col) Assert.True(System.Math.Abs(transformer.Transform[row, col] - expectedMatrix[row * 4 + col].GetDouble()) < 1e-12, $"matrix[{row},{col}]");
        var order = (SamplingOrder)kernel;
        if (operation == 0) transformer.TransformGrid(input, output, order, staggered);
        else if (operation == 1) new GridResampler { Threaded = false, TransformTiles = transformer.TransformTiles }.TransformGrid(new MatrixGridTransform(Matrix(profile)), input, output, order, staggered);
        else
        {
            input.Transform = profile is 12 or 14 or 15 ? Frustum() : new Transform(Matrix(profile));
            output.Transform = profile is 13 or 14 ? Frustum() : profile == 15 ? Frustum(.75) : new Transform();
            if (operation == 2) Resampling.DoResampleToMatch(input, output, order, staggered);
            else Resampling.ResampleToMatch(input, output, order, staggered);
        }
        Assert.Equal("retained", output.Name); Assert.Equal(GridClass.FogVolume, output.GridClass);
        AssertValue(item.GetProperty("background"), output.Background, "background");
        Assert.Equal(item.GetProperty("active").GetUInt64(), output.ActiveVoxelCount);
        Assert.Equal(item.GetProperty("leaves").GetInt64(), output.Tree.LeafCount);
        Assert.Equal(item.GetProperty("roots").GetInt32(), output.Tree.RootEntryCount);
        Assert.Equal(item.GetProperty("tiles").GetUInt64(), output.Tree.ActiveTileCount);
        var accessor = output.GetAccessor(); var samples = item.GetProperty("samples"); int n = 0;
        foreach (var coordinate in Fixture.Value.GetProperty("coordinates").EnumerateArray())
        {
            Coord point = new(coordinate[0].GetInt32(), coordinate[1].GetInt32(), coordinate[2].GetInt32()); var sample = samples[n++];
            AssertValue(sample[0], accessor.GetValue(point), point.ToString());
            Assert.Equal(sample[1].GetInt32() != 0, accessor.IsValueOn(point)); Assert.Equal(sample[2].GetInt32(), output.Tree.GetValueDepth(point));
            if (ReferenceEquals(tree, output.Tree)) Assert.Equal(accessor.GetValue(point), oldAccessor.GetValue(point));
        }
    }
    private static void AssertValue<T>(JsonElement expected, T actual, string location) where T : unmanaged
    {
        if (typeof(T) == typeof(Vec3f)) { var vector = (Vec3f)(object)actual; Compare(expected[0], vector.X, 3e-6); Compare(expected[1], vector.Y, 3e-6); Compare(expected[2], vector.Z, 3e-6); }
        else Compare(expected, Convert.ToDouble(actual, CultureInfo.InvariantCulture), typeof(T) == typeof(float) ? 3e-6 : 3e-12);
        void Compare(JsonElement value, double observed, double epsilon)
        {
            double wanted = value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();
            Assert.True(wanted == observed || double.IsNaN(wanted) && double.IsNaN(observed) || System.Math.Abs(wanted - observed) <= epsilon * System.Math.Max(1, System.Math.Abs(wanted)), $"{location}: expected {wanted:R}, got {observed:R}");
        }
    }
    internal static T Value<T>(double value) where T : unmanaged => typeof(T) == typeof(Vec3f) ? (T)(object)new Vec3f((float)value, (float)(value * .5 - 1), (float)-value) : typeof(T) == typeof(float) ? (T)(object)(float)value : (T)(object)value;
    internal static Grid<T> Output<T>() where T : unmanaged
    {
        var output = new Grid<T>(Value<T>(7)) { Name = "retained", GridClass = GridClass.FogVolume };
        output.Tree.SetValueOn(new(0), Value<T>(80)); output.Tree.SetValueOn(new(30, 2, -1), Value<T>(81)); output.Tree.SetValueOff(new(-20, 1, 0), Value<T>(-7)); return output;
    }
    internal static Grid<T> Shape<T>(int shape = 0) where T : unmanaged
    {
        var grid = new Grid<T>(Value<T>(2.5));
        if (shape == 0)
        {
            for (int x = -2; x <= 3; ++x) for (int y = -1; y <= 2; ++y) for (int z = 0; z <= 4; ++z)
            {
                Coord p = new(x, y, z); T value = Value<T>((x * x + 2 * y - 3 * z) * .125 + .75);
                if ((x + 2 * y + z) % 4 != 0) grid.Tree.SetValueOn(p, value); else grid.Tree.SetValueOff(p, value);
            }
            grid.Tree.SetValueOff(new(-4, 0, 1), Value<T>(-2.5)); grid.Tree.SetValueOff(new(7), Value<T>(9)); grid.Tree.SetValueOn(new(8, 0, 0), Value<T>(1.5));
        }
        else if (shape == 1)
        {
            grid.Tree.AddTile(1, new(0), Value<T>(5), true); grid.Tree.AddTile(1, new(8, 0, 0), Value<T>(-3), false); grid.Tree.AddTile(1, new(0, 8, 0), Value<T>(2.5), true); grid.Tree.AddTile(1, new(-8, 0, 0), Value<T>(6), true); grid.Tree.SetValueOn(new(1), Value<T>(.125));
        }
        else if (shape == 2) { grid.Tree.SetValueOff(new(1, 2, 3), Value<T>(-4)); grid.Tree.AddTile(1, new(8, 0, 0), Value<T>(2.5), false); }
        else if (shape == 3) return typeof(T) == typeof(float) ? (Grid<T>)(object)LevelSetSphere.Create<float>(2.5f, new(.25, .5, -.25), 1, 2) : (Grid<T>)(object)LevelSetSphere.Create<double>(2.5, new(.25, .5, -.25), 1, 2);
        if (shape == 4) grid.Tree.AddTile(2, Coord.Zero, Value<T>(5), true);
        if (shape == 5) { grid.Tree.AddTile(3, Coord.Zero, Value<T>(5), true); grid.Tree.AddTile(3, new(4096, 0, 0), Value<T>(2.5), false); }
        return grid;
    }
    internal static Transform Frustum(double taper = .5) => Transform.CreateFrustumTransform(new BBoxd(new Vec3d(-4, -4, 0), new Vec3d(12, 12, 16)), taper, 12);
    internal static GridTransformer CreateTransformer(int profile) => profile is 8 or 9 ? new(new(1, -.5, .25), new(.2, .4, .75), new(.1, .2, -.15), new(.375, -.25, .625), profile == 8 ? "tsr" : "rst", profile == 8 ? "zyx" : "xyz") : new(Matrix(profile));
    internal static Mat4d Matrix(int profile) => profile switch
    {
        1 => Mat4d.CreateTranslation(new(1, -2, 3)),
        2 => Mat4d.CreateTranslation(new(.375, -.25, .625)),
        3 => Mat4d.CreateScale(new Vec3d(.5, .75, 1.25)),
        4 => Mat4d.CreateScale(new Vec3d(.25, .125, .375)),
        5 => Mat4d.CreateScale(new Vec3d(-.25, .5, 1)),
        6 => Mat4d.CreateRotation(.2, Axis.X) * Mat4d.CreateRotation(-.3, Axis.Y) * Mat4d.CreateRotation(.4, Axis.Z),
        7 => new(1, .25, 0, 0, 0, 1, -.125, 0, .0625, 0, 1, 0, .25, -.5, .75, 1),
        16 => Mat4d.CreateScale(1.0 / 512),
        17 => Mat4d.CreateScale(1.0 / 16),
        10 => Mat4d.CreateRotation(System.Math.PI / 2, Axis.Y),
        11 => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, .002, .5, .25, -.25, 1),
        _ => Mat4d.Identity
    };
}
