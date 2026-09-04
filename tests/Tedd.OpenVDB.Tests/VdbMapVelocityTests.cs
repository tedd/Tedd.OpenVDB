// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbMapVelocityTests
{
    private static readonly JsonDocument Native = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "maps", "native-map-velocity.json")));
    public static IEnumerable<object[]> Cases() => Native.RootElement.GetProperty("cases").EnumerateArray().Select((entry, index) => new object[] { index, entry.GetProperty("precision").GetString()! });
    [Theory, MemberData(nameof(Cases))]
    public void PreviouslyRecordedNativeSpecializedMapVelocityBoundariesMatch(int index, string precision)
    {
        JsonElement entry = Native.RootElement.GetProperty("cases")[index];
        if (precision == "float") Check<Vec3f>(entry); else Check<Vec3d>(entry);
    }
    private static V Value<V>(Vec3d value) where V : unmanaged => typeof(V) == typeof(Vec3f) ? (V)(object)(Vec3f)value : (V)(object)value;
    private static Vec3d Double<V>(V value) where V : unmanaged => value is Vec3f single ? single.AsVec3d() : (Vec3d)(object)value;
    private static Vec3d Vector(JsonElement array) => new(array[0].GetDouble(), array[1].GetDouble(), array[2].GetDouble());
    private static void Check<V>(JsonElement expected) where V : unmanaged
    {
        var grid = new Grid<V>(Value<V>(new(-.125, .25, .375))) { Transform = Transform.CreateLinearTransform(.375) };
        grid.Transform.PostTranslate(new(1.25, -2.5, .75)); Assert.Equal("UniformScaleTranslateMap", grid.Transform.MapType);
        for (int x = -5; x <= 5; ++x) for (int y = -5; y <= 5; ++y) for (int z = -5; z <= 5; ++z)
        {
            V value = Value<V>(new(.03125 * x * x + .0625 * y - .0234375 * z + .2, -.015625 * y * y + .125 * z + .015625 * x * z - .1, .046875 * z * z - .0625 * x + .03125 * x * y + .3));
            grid.Tree.SetValue(new(x, y, z), value); if ((x + y + z) % 5 == 0) grid.Tree.SetValueOff(new(x, y, z));
        }
        var sampler = new VelocitySampler<V>(grid, (SamplingOrder)expected.GetProperty("order").GetInt32(), expected.GetProperty("staggered").GetBoolean());
        foreach (JsonElement sample in expected.GetProperty("samples").EnumerateArray())
        {
            Vec3d world = Vector(sample.GetProperty("world"));
            Assert.Equal(sample.GetProperty("active").GetBoolean(), sampler.Sample(world, out V actual));
            Assert.Equal(sample.GetProperty("activeFloat").GetBoolean(), sampler.Sample((Vec3f)world, out V actualFloat));
            CheckValue(sample.GetProperty("sample"), Double(actual), typeof(V) == typeof(Vec3f)); CheckValue(sample.GetProperty("sampleFloat"), Double(actualFloat), typeof(V) == typeof(Vec3f));
        }
    }
    private static void CheckValue(JsonElement expected, Vec3d actual, bool single)
    {
        double tolerance = single ? 2e-6 : 5e-13;
        for (int axis = 0; axis < 3; ++axis)
        {
            double value = expected[axis].GetDouble();
            Assert.True(System.Math.Abs(value - actual[axis]) <= tolerance * System.Math.Max(1, System.Math.Abs(value)), $"Expected {value:R}; got {actual[axis]:R}.");
        }
    }
}
