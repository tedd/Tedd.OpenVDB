using System.Globalization;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public class VelocityFieldNativeTests
{
    private static readonly JsonDocument Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-velocity-fields.json")));
    public static IEnumerable<object[]> Cases()
    {
        int index = 0; foreach (var test in Fixture.RootElement.GetProperty("cases").EnumerateArray()) yield return [index++, test.GetProperty("kind").GetString()!, test.GetProperty("precision").GetString()!];
    }
    [Fact]
    public void FixturePinsAllNativeFieldAndInterpolationProfiles()
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.RootElement.GetProperty("upstreamCommit").GetString());
        Assert.Equal(64, Fixture.RootElement.GetProperty("cases").GetArrayLength());
        Assert.Equal(16, Fixture.RootElement.GetProperty("cases").EnumerateArray().Count(x => x.GetProperty("kind").GetString() == "enright"));
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void PinnedNativeVelocitySamplesAndRungeKuttaStagesMatch(int index, string kind, string precision)
    {
        var test = Fixture.RootElement.GetProperty("cases")[index];
        if (kind == "enright")
        {
            IVelocityField field = precision == "float" ? new EnrightField<float>() : new EnrightField<double>();
            foreach (var sample in test.GetProperty("samples").EnumerateArray())
            {
                double time = Number(sample.GetProperty("time")); var position = Vector(sample.GetProperty("world")); var c = Vector(sample.GetProperty("coord"));
                Near(sample.GetProperty("value"), field.Sample(position, time), precision, "Enright world");
                Near(sample.GetProperty("coordValue"), field.Sample(new Coord((int)c.X, (int)c.Y, (int)c.Z), time), precision, "Enright coordinate");
            }
        }
        else if (precision == "float") CheckGrid<Vec3f>(test, precision); else CheckGrid<Vec3d>(test, precision);
    }
    private static void CheckGrid<V>(JsonElement test, string precision) where V : unmanaged
    {
        var grid = VelocityFieldTestSystems.Grid<V>(test.GetProperty("map").GetInt32());
        var order = (SamplingOrder)test.GetProperty("order").GetInt32(); bool staggered = test.GetProperty("staggered").GetBoolean();
        var sampler = new VelocitySampler<V>(grid, order, staggered); var discrete = new DiscreteField<V>(grid, order, staggered); var integrator = new VelocityIntegrator<V>(grid, order, staggered);
        foreach (var sample in test.GetProperty("samples").EnumerateArray())
        {
            Vec3d world = Vector(sample.GetProperty("world")), c = Vector(sample.GetProperty("coord")); Vec3f worldF = (Vec3f)world;
            Assert.Equal(sample.GetProperty("active").GetBoolean(), sampler.Sample(world, out V value));
            Assert.Equal(sample.GetProperty("activeFloat").GetBoolean(), sampler.Sample(worldF, out V valueF));
            Near(sample.GetProperty("sample"), VelocityFieldTestSystems.Double(value), precision, "sample"); Near(sample.GetProperty("sampleFloat"), VelocityFieldTestSystems.Double(valueF), precision, "float location sample");
            Near(sample.GetProperty("discrete"), VelocityFieldTestSystems.Double(discrete.Sample(world, 1.25)), precision, "discrete");
            Near(sample.GetProperty("discreteCoord"), VelocityFieldTestSystems.Double(discrete.Sample(new Coord((int)c.X, (int)c.Y, (int)c.Z), 1.25)), precision, "discrete coordinate");
            foreach (var run in sample.GetProperty("runs").EnumerateArray())
            {
                double dt = run.GetProperty("dt").GetDouble(); int rk = run.GetProperty("order").GetInt32();
                Near(run.GetProperty("double"), integrator.RungeKutta(dt, world, rk), precision, $"RK{rk} double location {world}");
                Near(run.GetProperty("float"), integrator.RungeKutta(dt, worldF, rk).AsVec3d(), "float", $"RK{rk} float location {worldF}");
            }
        }
    }
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();
    private static Vec3d Vector(JsonElement value) => new(Number(value[0]), Number(value[1]), Number(value[2]));
    private static void Near(JsonElement expected, Vec3d actual, string precision, string context)
    {
        double tolerance = precision == "float" ? 2e-6 : 5e-13;
        for (int axis = 0; axis < 3; ++axis)
        {
            double e = Number(expected[axis]), a = actual[axis];
            if (double.IsNaN(e)) Assert.True(double.IsNaN(a), context);
            else if (double.IsInfinity(e)) Assert.Equal(e, a);
            else Assert.True(System.Math.Abs(e - a) <= tolerance * System.Math.Max(1, System.Math.Abs(e)), $"{context} [{axis}]: expected {e:R}, actual {a:R}");
        }
    }
}

internal static class VelocityFieldTestSystems
{
    internal static Grid<V> Grid<V>(int map) where V : unmanaged
    {
        var grid = new Grid<V>(Value<V>(new(-0.125, 0.25, 0.375))) { Transform = Transform(map) };
        for (int x = -5; x <= 5; ++x) for (int y = -5; y <= 5; ++y) for (int z = -5; z <= 5; ++z)
        {
            V value = Value<V>(new(0.03125 * x * x + 0.0625 * y - 0.0234375 * z + 0.2, -0.015625 * y * y + 0.125 * z + 0.015625 * x * z - 0.1, 0.046875 * z * z - 0.0625 * x + 0.03125 * x * y + 0.3));
            grid.Tree.SetValue(new(x, y, z), value); if ((x + y + z) % 5 == 0) grid.Tree.SetValueOff(new(x, y, z));
        }
        return grid;
    }
    internal static V Value<V>(Vec3d value) where V : unmanaged => typeof(V) == typeof(Vec3f) ? (V)(object)(Vec3f)value : (V)(object)value;
    internal static Vec3d Double<V>(V value) where V : unmanaged => typeof(V) == typeof(Vec3f) ? ((Vec3f)(object)value).AsVec3d() : (Vec3d)(object)value;
    internal static Transform Transform(int map)
    {
        var transform = new Transform();
        if (map == 1) { transform.PreScale(0.375); transform.PostTranslate(new(1.25, -2.5, 0.75)); }
        if (map == 2) { transform.PreScale(new Vec3d(0.7, 1.125, 1.375)); transform.PostRotate(0.21, Axis.Z); transform.PostShear(0.13, Axis.X, Axis.Y); transform.PostTranslate(new(-0.25, 0.75, 1.125)); }
        if (map == 3) { transform = Tedd.OpenVDB.Math.Transform.CreateFrustumTransform(new BBoxd(new Vec3d(-8, -6, -4), new Vec3d(8, 6, 12)), 0.65, 9, 0.75); transform.PostTranslate(new(0.5, -0.25, 1.25)); }
        return transform;
    }
}
