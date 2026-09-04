using System.Globalization;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public class VolumeAdvectionNativeTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() => { using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-volume-advection.json"))); return document.RootElement.Clone(); });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((test, index) => new object[] { index, test.GetProperty("type").GetString()!, test.GetProperty("velocity").GetString()!, test.GetProperty("scheme").GetInt32(), test.GetProperty("limiter").GetInt32() });
    [Fact] public void FixtureProvenanceAndCoverageArePinned() { Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.GetProperty("upstreamCommit").GetString()); Assert.Equal(538, Fixture.Value.GetProperty("cases").GetArrayLength()); }
    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeTopologyStoredValuesAndCorrectionsMatch(int index, string type, string velocity, int scheme, int limiter)
    {
        var test = Fixture.Value.GetProperty("cases")[index]; Assert.Equal(scheme, test.GetProperty("scheme").GetInt32()); Assert.Equal(limiter, test.GetProperty("limiter").GetInt32());
        switch (type) { case "float": Run<float>(test, velocity); break; case "double": Run<double>(test, velocity); break; case "half": Run<Half>(test, velocity); break; case "int": Run<int>(test, velocity); break; case "vec3f": Run<Vec3f>(test, velocity); break; case "vec3d": Run<Vec3d>(test, velocity); break; case "vec3i": Run<Vec3i>(test, velocity); break; case "vec2d": Run<Vec2d>(test, velocity); break; case "vec4d": Run<Vec4d>(test, velocity); break; default: throw new NotSupportedException(); }
    }
    private static void Run<T>(JsonElement test, string velocity) where T : unmanaged { if (velocity == "float") Run<T, Vec3f>(test); else Run<T, Vec3d>(test); }
    private static void Run<T, V>(JsonElement test) where T : unmanaged where V : unmanaged
    {
        int map = test.GetProperty("map").GetInt32(); var input = VolumeAdvectionTestSystems.Shape<T>(test.GetProperty("shape").GetInt32(), map); var field = VolumeAdvectionTestSystems.Velocity<V>(test.GetProperty("kind").GetInt32());
        var solver = new VolumeAdvection<V>(field, test.GetProperty("staggered").GetBoolean()) { Scheme = (VolumeAdvectionScheme)test.GetProperty("scheme").GetInt32(), Limiter = (VolumeAdvectionLimiter)test.GetProperty("limiter").GetInt32(), SubSteps = test.GetProperty("substeps").GetInt32(), GrainSize = 0 };
        double time = test.GetProperty("dt").GetDouble(); var order = (SamplingOrder)test.GetProperty("order").GetInt32(); int mask = test.GetProperty("mask").GetInt32();
        Assert.Equal(test.GetProperty("maximum").GetDouble(), solver.MaxVelocity); Assert.Equal(test.GetProperty("distance").GetInt32(), solver.GetMaxDistance(input, time / solver.SubSteps));
        var output = mask == 0 ? solver.Advect(input, time, order) : solver.Advect(input, VolumeAdvectionTestSystems.Mask(mask, map), time, order);
        Assert.Equal(test.GetProperty("activeCount").GetUInt64(), output.ActiveVoxelCount); Assert.Equal(test.GetProperty("leafCount").GetInt64(), output.Tree.LeafCount); Assert.Equal(test.GetProperty("rootCount").GetInt32(), output.Tree.RootEntryCount);
        foreach (var expected in test.GetProperty("activeValues").EnumerateArray())
        {
            Coord coordinate = new(expected[0].GetInt32(), expected[1].GetInt32(), expected[2].GetInt32()); Assert.True(output.Tree.IsValueOn(coordinate), coordinate.ToString()); Assert.Equal(expected[3].GetInt32() != 0, output.Tree.GetValueDepth(coordinate) == 3); Near(expected[4], output.Tree.GetValue(coordinate), coordinate);
        }
        int i = 0; foreach (var position in Fixture.Value.GetProperty("coordinates").EnumerateArray())
        {
            Coord coordinate = new(position[0].GetInt32(), position[1].GetInt32(), position[2].GetInt32()); var expected = test.GetProperty("probes")[i++]; Near(expected[0], output.Tree.GetValue(coordinate), coordinate); Assert.Equal(expected[1].GetInt32() != 0, output.Tree.IsValueOn(coordinate)); Assert.Equal(expected[2].GetInt32(), output.Tree.GetValueDepth(coordinate));
        }
    }
    private static void Near<T>(JsonElement expected, T actual, Coord coordinate) where T : unmanaged
    {
        var components = VolumeAdvectionTestSystems.Components(actual); Assert.Equal(expected.GetArrayLength(), components.Length);
        for (int i = 0; i < components.Length; ++i)
        {
            double e = expected[i].ValueKind == JsonValueKind.String ? double.Parse(expected[i].GetString()!, CultureInfo.InvariantCulture) : expected[i].GetDouble(), a = components[i];
            if (double.IsNaN(e)) Assert.True(double.IsNaN(a));
            else if (typeof(T) == typeof(int) || typeof(T) == typeof(Vec3i) || typeof(T) == typeof(Half)) Assert.Equal(e, a);
            else Assert.True(System.Math.Abs(e - a) <= (typeof(T) == typeof(float) || typeof(T) == typeof(Vec3f) ? 3e-6 : 3e-13) * System.Math.Max(1, System.Math.Abs(e)), $"{coordinate}[{i}]: expected {e:R}, actual {a:R}");
        }
    }
}
