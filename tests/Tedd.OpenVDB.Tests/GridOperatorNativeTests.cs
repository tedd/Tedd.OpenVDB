using System.Globalization;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public class GridOperatorNativeTests
{
    private static readonly JsonDocument Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-grid-operators.json")));
    public static IEnumerable<object[]> Cases()
    {
        int i = 0; foreach (var test in Fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            yield return [i, false]; yield return [i++, true];
        }
    }
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();
    private static Coord CoordOf(JsonElement v) => new((int)Number(v[0]), (int)Number(v[1]), (int)Number(v[2]));
    private static Transform Map(int kind)
    {
        var m = new Mat4d(1.3, .2, -.1, 0, -.3, .8, .25, 0, .1, -.15, 1.7, 0, 3, -2, 4, 1);
        return kind == 0 ? new Transform(m) : new Transform(new NonlinearFrustumMap(new BBoxd(new Vec3d(-4, -8, -3), new Vec3d(8, 10, 12)), .6, 5, m));
    }
    [Fact]
    public void FixtureHasPinnedProvenance()
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.RootElement.GetProperty("upstreamCommit").GetString());
        Assert.Equal(320, Fixture.RootElement.GetProperty("cases").GetArrayLength());
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void DerivedGridValuesTopologyMetadataAndMasksMatchNative(int index, bool threaded)
    {
        var expected = Fixture.RootElement.GetProperty("cases")[index]; int scenario = (int)Number(expected.GetProperty("scenario")), map = (int)Number(expected.GetProperty("map")), op = (int)Number(expected.GetProperty("operation"));
        bool isFloat = Number(expected.GetProperty("precision")) == 4;
        MaskGrid? mask = null; if (Number(expected.GetProperty("masked")) != 0) { mask = new(); mask.SetValueOn(Coord.Zero); mask.SetValueOn(new(7, 0, 0)); mask.SetValueOn(new(-1, 0, 0)); mask.Transform = Transform.CreateLinearTransform(43); }
        var options = new GridOperatorOptions { Threaded = threaded }; IGrid result;
        if (isFloat)
        {
            var scalar = new Grid<float>(3); var vector = new Grid<Vec3f>(new Vec3f(3, -4, 0));
            Initialize(scalar, vector, scenario, (double x) => (float)x, (Vec3d v) => new Vec3f((float)v.X, (float)v.Y, (float)v.Z));
            scalar.Transform = Map(map); vector.Transform = Map(map);
            result = op switch
            {
                0 => GridOperators.Gradient(scalar, mask, options),
                1 => GridOperators.Laplacian(scalar, mask, options),
                2 => GridOperators.MeanCurvature(scalar, mask, options),
                3 => GridOperators.ClosestPoint(scalar, true, mask, options),
                4 => GridOperators.Divergence(vector, mask, options),
                5 => GridOperators.Curl(vector, mask, options),
                6 => GridOperators.Magnitude(vector, mask, options),
                7 => GridOperators.Normalize(vector, mask, options),
                _ => throw new InvalidOperationException()
            };
        }
        else
        {
            var scalar = new Grid<double>(3); var vector = new Grid<Vec3d>(new Vec3d(3, -4, 0));
            Initialize(scalar, vector, scenario, (double x) => x, (Vec3d v) => v);
            scalar.Transform = Map(map); vector.Transform = Map(map);
            result = op switch
            {
                0 => GridOperators.Gradient(scalar, mask, options),
                1 => GridOperators.Laplacian(scalar, mask, options),
                2 => GridOperators.MeanCurvature(scalar, mask, options),
                3 => GridOperators.ClosestPoint(scalar, true, mask, options),
                4 => GridOperators.Divergence(vector, mask, options),
                5 => GridOperators.Curl(vector, mask, options),
                6 => GridOperators.Magnitude(vector, mask, options),
                7 => GridOperators.Normalize(vector, mask, options),
                _ => throw new InvalidOperationException()
            };
        }
        switch (result) { case Grid<float> g: Check(g, expected, 4e-6); break; case Grid<double> g: Check(g, expected, 4e-12); break; case Grid<Vec3f> g: Check(g, expected, 4e-6); break; case Grid<Vec3d> g: Check(g, expected, 4e-12); break; default: throw new InvalidOperationException(); }
    }
    private static void Initialize<S, V>(Grid<S> scalar, Grid<V> vector, int scenario, Func<double, S> scalarValue, Func<Vec3d, V> vectorValue) where S : unmanaged where V : unmanaged
    {
        if (scenario < 3)
        {
            if (scenario == 0) for (int x = -1; x <= 1; ++x) for (int y = -1; y <= 1; ++y) for (int z = -1; z <= 1; ++z)
            { Coord c = new(x, y, z); scalar[c] = scalarValue(x * x + 2 * y * y + 3 * z * z + x * y + 2); vector[c] = vectorValue(new(x * x - y, y * y + z, z * z + x)); }
            else
            {
                scalar.Tree.AddTile(1, Coord.Zero, scalarValue(2), true); vector.Tree.AddTile(1, Coord.Zero, vectorValue(new(2, 1, -3)), true);
                scalar.Tree.SetValueOff(new(-1, 0, 0), scalarValue(19)); vector.Tree.SetValueOff(new(-1, 0, 0), vectorValue(new(19, 2, -4)));
            }
        }
        if (scenario == 4) { scalar.Tree.AddTile(3, new(4096), scalarValue(5), false); vector.Tree.AddTile(3, new(4096), vectorValue(new(5, 0, 0)), false); }
        scalar.Name = "input"; scalar.GridClass = GridClass.LevelSet; vector.Name = "vector input"; vector.SetMetadata("vector_type", "covariant");
        if (scenario == 2) vector.GridClass = GridClass.Staggered;
    }
    private static void Value<T>(JsonElement expected, T actual, double tolerance, string label) where T : unmanaged
    {
        switch (actual)
        {
            case Vec3f v: for (int i = 0; i < 3; ++i) Near(Number(expected[i]), v[i], tolerance, label); break;
            case Vec3d v: for (int i = 0; i < 3; ++i) Near(Number(expected[i]), v[i], tolerance, label); break;
            default: Near(Number(expected), Convert.ToDouble(actual, CultureInfo.InvariantCulture), tolerance, label); break;
        }
    }
    private static void Near(double expected, double actual, double tolerance, string label)
    {
        if (double.IsNaN(expected)) { Assert.True(double.IsNaN(actual), label); return; }
        if (double.IsInfinity(expected)) { Assert.Equal(expected, actual); return; }
        Assert.True(System.Math.Abs(actual - expected) <= tolerance * System.Math.Max(1, System.Math.Abs(expected)), label + ": native " + expected.ToString("R") + " managed " + actual.ToString("R"));
    }
    private static void Check<T>(Grid<T> actual, JsonElement expected, double tolerance) where T : unmanaged
    {
        Value(expected.GetProperty("background"), actual.Background, tolerance, "background");
        Assert.Equal((ulong)Number(expected.GetProperty("activeCount")), actual.ActiveVoxelCount);
        Assert.Equal((long)Number(expected.GetProperty("leafCount")), actual.Tree.LeafCount);
        Assert.Equal((GridClass)(int)Number(expected.GetProperty("class")), actual.GridClass); Assert.Equal(expected.GetProperty("name").GetString(), actual.Name);
        string vectorType = actual.TryGetMetadata<string>("vector_type", out var v) ? v : "invariant"; Assert.Equal(expected.GetProperty("vectorType").GetString(), vectorType);
        var values = actual.Tree.EnumerateValues().ToDictionary(x => (x.Coordinate, x.Level)); var expectedValues = expected.GetProperty("values");
        Assert.Equal(expectedValues.GetArrayLength(), values.Count);
        foreach (var entry in expectedValues.EnumerateArray()) { var key = (CoordOf(entry[0]), (int)Number(entry[1])); Assert.True(values.TryGetValue(key, out var found), "Missing active entry " + key); Value(entry[2], found.Value, tolerance, "active " + key); }
        foreach (var probe in expected.GetProperty("probes").EnumerateArray())
        {
            Coord c = CoordOf(probe[0]); Value(probe[1], actual.Tree.GetValue(c), tolerance, "probe " + c); Assert.Equal(Number(probe[2]) != 0, actual.Tree.IsValueOn(c)); Assert.Equal((int)Number(probe[3]), actual.Tree.GetValueDepth(c));
        }
    }
}
