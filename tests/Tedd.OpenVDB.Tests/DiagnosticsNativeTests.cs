using System.Globalization;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public class DiagnosticsNativeTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() => { using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-diagnostics.json"))); return document.RootElement.Clone(); });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((test, index) => new object[] { index, test.GetProperty("kind").GetString()!, test.GetProperty("type").GetString()! });
    [Fact] public void NativeRevisionAndBaseCoverageArePinned() { Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.GetProperty("upstreamCommit").GetString()); Assert.Equal(927, Fixture.Value.GetProperty("cases").GetArrayLength()); }
    [Theory]
    [MemberData(nameof(Cases))]
    public void NativePredicatesCountsMasksAndInactiveValuesMatch(int index, string kind, string type)
    {
        var test = Fixture.Value.GetProperty("cases")[index]; Assert.Equal(kind, test.GetProperty("kind").GetString());
        switch (type)
        {
            case "float": Run<float>(test); break;
            case "double": Run<double>(test); break;
            case "half": Run<Half>(test); break;
            case "int": Run<int>(test); break;
            case "long": Run<long>(test); break;
            case "uint": Run<uint>(test); break;
            case "ulong": Run<ulong>(test); break;
            case "bool": Run<bool>(test); break;
            case "vec2f": Run<Vec2f>(test); break;
            case "vec2d": Run<Vec2d>(test); break;
            case "vec2i": Run<Vec2i>(test); break;
            case "vec3f": Run<Vec3f>(test); break;
            case "vec3d": Run<Vec3d>(test); break;
            case "vec3i": Run<Vec3i>(test); break;
            case "vec4f": Run<Vec4f>(test); break;
            case "vec4d": Run<Vec4d>(test); break;
            case "vec4i": Run<Vec4i>(test); break;
            default: throw new NotSupportedException(type);
        }
    }
    private static void Run<T>(JsonElement test) where T : unmanaged
    {
        switch (test.GetProperty("kind").GetString()) { case "value": Values<T>(test); break; case "mask": Masks<T>(test); break; case "unique": Unique<T>(test); break; case "gradient": if (typeof(T) == typeof(float)) Gradient<float>(test); else Gradient<double>(test); break; case "divergence": Divergence<T>(test); break; case "volume": Volume<T>(test); break; default: throw new NotSupportedException(); }
    }
    private static void Values<T>(JsonElement test) where T : unmanaged
    {
        T min = Read<T>(test.GetProperty("minimum")), max = Read<T>(test.GetProperty("maximum"));
        DiagnosticCheck<T>[] checks = [new CheckNan<T>(), new CheckInf<T>(), new CheckFinite<T>(), new CheckMagnitude<T>(Read<T>(test.GetProperty("magnitude")), Read<T>(test.GetProperty("tolerance"))), new CheckRange<T>(min, max), new CheckRange<T>(min, max, false, true), new CheckRange<T>(min, max, true, false), new CheckRange<T>(min, max, false, false), new CheckMin<T>(min), new CheckMax<T>(max)];
        for (int i = 0; i < checks.Length; ++i) Assert.Equal(test.GetProperty("descriptions")[i].GetString(), checks[i].Description);
        foreach (var sample in test.GetProperty("samples").EnumerateArray())
        {
            T value = Read<T>(sample[0]); for (int i = 0; i < checks.Length; ++i) Assert.Equal(sample[1][i].GetInt32() != 0, checks[i].Fails(value));
        }
    }
    private static void Masks<T>(JsonElement test) where T : unmanaged
    {
        var grid = Shape<T>(test.GetProperty("badBackground").GetInt32() != 0); var diagnose = new Diagnose<T>(grid); var mode = (Tree.ValueIterationMode)test.GetProperty("mode").GetInt32(); int flags = test.GetProperty("flags").GetInt32();
        DiagnosticCheck<T>[] checks = [new CheckNan<T>(mode), new CheckInf<T>(mode), new CheckFinite<T>(mode), new CheckRange<T>(Value<T>(-1), Value<T>(1), mode: mode), new CheckMagnitude<T>(Value<T>(3), Value<T>(0), mode)];
        for (int i = 0; i < checks.Length; ++i)
        {
            var expected = test.GetProperty("steps")[i]; Assert.Equal(expected.GetProperty("message").GetString(), diagnose.Check(checks[i], (flags & 8) != 0, (flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0));
            Assert.Equal(expected.GetProperty("failures").GetUInt64(), diagnose.FailureCount); Assert.Equal(expected.GetProperty("valueCount").GetUInt64(), diagnose.ValueCount);
            int p = 0; foreach (var point in Fixture.Value.GetProperty("coordinates").EnumerateArray())
            {
                Coord coordinate = new(point[0].GetInt32(), point[1].GetInt32(), point[2].GetInt32()); var probe = expected.GetProperty("maskProbes")[p++]; Assert.Equal(probe[0].GetInt32() != 0, diagnose.Mask.Tree.GetValue(coordinate)); Assert.Equal(probe[1].GetInt32() != 0, diagnose.Mask.Tree.IsValueOn(coordinate));
            }
        }
        Assert.Equal(test.GetProperty("maskMap").GetString(), diagnose.Mask.Transform.MapType);
    }
    private static Grid<T> Shape<T>(bool badBackground) where T : unmanaged
    {
        var grid = new Grid<T>(Value<T>(badBackground ? double.NaN : 3)) { Transform = Transform.CreateLinearTransform(0.75) }; grid.Transform.PostTranslate(new(1, -2, 0.5));
        grid.Tree.SetValue(new(-1, 0, 0), Value<T>(0.5)); grid.Tree.SetValue(Coord.Zero, Value<T>(double.NaN, 1, 2, 3)); grid.Tree.SetValue(new(1, 0, 0), Value<T>(double.PositiveInfinity, 1, 2, 3)); grid.Tree.SetValueOff(new(2, 0, 0), Value<T>(double.NegativeInfinity, 2, 3, 4)); grid.Tree.SetValueOff(new(3, 0, 0), Value<T>(7)); grid.Tree.AddTile(1, new(16, 0, 0), Value<T>(double.NaN), true); grid.Tree.AddTile(1, new(24, 0, 0), Value<T>(-2), false); grid.Tree.AddTile(2, new(128, 0, 0), Value<T>(double.PositiveInfinity), true); grid.Tree.AddTile(3, new(4096, 0, 0), Value<T>(-9), true); return grid;
    }
    private static void Unique<T>(JsonElement test) where T : unmanaged
    {
        int shape = test.GetProperty("shape").GetInt32(), limit = test.GetProperty("limit").GetInt32(); var grid = new Grid<T>(Value<T>(3));
        if (shape == 1) grid.Tree.AddTile(3, new(4096, 0, 0), Value<T>(-4), false);
        if (shape >= 2) { grid.Tree.AddTile(3, Coord.Zero, Value<T>(3), true); if (shape == 3) grid.Tree.AddTile(3, new(-4096, 0, 0), Value<T>(3), true); grid.Tree.SetValueOff(new(4, 0, 0), Value<T>(3)); grid.Tree.SetValueOff(Coord.Zero, Value<T>(-2)); grid.Tree.SetValueOff(new(1, 0, 0), Value<T>(1)); grid.Tree.SetValue(new(2, 0, 0), Value<T>(99)); grid.Tree.AddTile(1, new(32, 0, 0), Value<T>(8), false); }
        if (shape == 3) { grid.Tree.SetValueOff(new(-8, 0, 0), Value<T>(-7)); grid.Tree.SetValueOff(new(16, 0, 0), Value<T>(-9)); }
        bool expectedSuccess = test.GetProperty("success").GetInt32() != 0; Assert.Equal(expectedSuccess, Diagnostics.UniqueInactiveValues(grid, out T[] values, limit));
        if (expectedSuccess) Assert.Equal(test.GetProperty("values").EnumerateArray().Select(Read<T>).ToArray(), values); else Assert.True(values.Length > limit);
    }
    private static void Gradient<T>(JsonElement test) where T : unmanaged, System.Numerics.IFloatingPointIeee754<T>
    {
        int shape = test.GetProperty("shape").GetInt32(), kind = test.GetProperty("check").GetInt32(); double dx = test.GetProperty("dx").GetDouble(); var grid = new Grid<T>(T.CreateTruncating(3)) { Transform = Transform.CreateLinearTransform(dx) };
        for (int x = -4; x <= 4; ++x) for (int y = -4; y <= 4; ++y) for (int z = -4; z <= 4; ++z)
        {
            double value = shape == 1 ? 0.075 * x * x + 0.025 * y * y * y - 0.125 * z - 0.3 : shape == 2 ? System.Math.Abs((double)x) - 0.2 + 0.125 * y - 0.5 : 0.75 * x + 0.125 * y - 0.25 * z - 0.3;
            if (shape == 3 && x == 0 && y == 0 && z == 0) value = double.NaN; if (shape == 3 && x == 1 && y == 0 && z == 0) value = double.PositiveInfinity; grid.Tree.SetValue(new(x, y, z), T.CreateTruncating(value));
        }
        T min = T.CreateTruncating(shape == 2 ? -2 : 0.7), max = T.CreateTruncating(shape == 2 ? -0.5 : 1.2);
        DiagnosticCheck<T> check = kind < 5 ? new CheckNormGrad<T>(grid, min, max, (BiasedGradientScheme)kind) : new CheckEikonal<T>(grid, min, max, kind == 5 ? EikonalStencilKind.Weno : EikonalStencilKind.Gradient);
        Assert.Equal(test.GetProperty("description").GetString(), check.Description);
        foreach (var raw in test.GetProperty("raw").EnumerateArray()) Assert.Equal(raw[1].GetInt32() != 0, check.Fails(T.CreateTruncating(Number(raw[0]))));
        foreach (var voxel in test.GetProperty("voxels").EnumerateArray()) { Coord p = new(voxel[0].GetInt32(), voxel[1].GetInt32(), voxel[2].GetInt32()); Assert.Equal(voxel[3].GetInt32() != 0, check.FailsVoxel(p, grid.Tree.GetValue(p))); }
        var diagnose = new Diagnose<T>(grid); var expected = test.GetProperty("steps")[0]; Assert.Equal(expected.GetProperty("message").GetString(), diagnose.Check(check, true)); CheckMask(expected, diagnose.Mask, diagnose.FailureCount, diagnose.ValueCount);
    }
    private static void Divergence<T>(JsonElement test) where T : unmanaged
    {
        double dx = test.GetProperty("dx").GetDouble(); var scheme = (DScheme)test.GetProperty("scheme").GetInt32(); var grid = new Grid<T>(Value<T>(0)) { Transform = Transform.CreateLinearTransform(dx) };
        for (int x = -5; x <= 5; ++x) for (int y = -5; y <= 5; ++y) for (int z = -5; z <= 5; ++z) grid.Tree.SetValue(new(x, y, z), Value<T>(0.25 * x + 0.025 * y * y - 0.015 * z * z * z, 0.125 * y * y + 0.075 * x * z, -0.25 * z + 0.035 * z * z * z + 0.125 * x * y));
        var check = new CheckDivergence<T>(grid, -0.2, 0.4, scheme); var accessor = grid.GetAccessor();
        foreach (var value in test.GetProperty("values").EnumerateArray())
        {
            Coord p = new(value[0].GetInt32(), value[1].GetInt32(), value[2].GetInt32()); double actual = typeof(T) == typeof(Vec3f) ? (float)(1.0 / dx) * DifferentialOperators.IndexDivergence((Tree.ValueAccessor<Vec3f>)(object)accessor, p, scheme) : (1.0 / dx) * DifferentialOperators.IndexDivergence((Tree.ValueAccessor<Vec3d>)(object)accessor, p, scheme); double expected = Number(value[3]);
            Assert.True(System.Math.Abs(expected - actual) <= (typeof(T) == typeof(Vec3f) ? 3e-6 : 3e-13) * System.Math.Max(1, System.Math.Abs(expected)), $"{scheme} at {p}: {actual:R} != {expected:R}"); Assert.Equal(value[4].GetInt32() != 0, check.FailsVoxel(p, grid.Tree.GetValue(p)));
        }
    }
    private static void Volume<T>(JsonElement test) where T : unmanaged
    {
        bool fog = test.GetProperty("fog").GetInt32() != 0; int shape = test.GetProperty("shape").GetInt32(), number = test.GetProperty("number").GetInt32(); double background = shape == 3 ? (fog ? 0.1 : 2) : (fog ? 0 : 3);
        var grid = new Grid<T>(Value<T>(background)) { GridClass = fog ? GridClass.FogVolume : GridClass.LevelSet };
        if (shape == 1) grid.GridClass = GridClass.Unknown; if (shape == 2) grid.Transform.PostScale(new Vec3d(1, 2, 1)); if (shape == 4) grid.Tree.AddTile(1, new(16, 0, 0), Value<T>(fog ? 1.25 : 2), true); if (shape == 5) grid.Tree.SetValue(Coord.Zero, Value<T>(double.NaN)); if (shape == 6) grid.Tree.SetValue(Coord.Zero, Value<T>(fog ? 1.25 : 4)); if (shape == 7) grid.Tree.SetValueOff(Coord.Zero, Value<T>(fog ? 0.2 : 1));
        if (shape == 8) for (int x = -4; x <= 4; ++x) for (int y = -1; y <= 1; ++y) for (int z = -1; z <= 1; ++z) grid.Tree.SetValue(new(x, y, z), Value<T>(fog ? 0.5 : System.Math.Max(-3.0, System.Math.Min(3.0, 0.1 * x)))); if (shape == 9) grid.Tree.SetValueOff(Coord.Zero, Value<T>(double.PositiveInfinity));
        if (fog) { var check = new CheckFogVolume<T>(grid); Assert.Equal(test.GetProperty("message").GetString(), check.Check(number, true)); CheckMask(test, check.Mask, check.FailureCount, check.ValueCount); }
        else { var check = new CheckLevelSet<T>(grid); Assert.Equal(test.GetProperty("message").GetString(), check.Check(number, true)); CheckMask(test, check.Mask, check.FailureCount, check.ValueCount); }
    }
    private static void CheckMask(JsonElement expected, Grid<bool> mask, ulong failures, ulong values)
    {
        Assert.Equal(expected.GetProperty("failures").GetUInt64(), failures); Assert.Equal(expected.GetProperty("valueCount").GetUInt64(), values); int i = 0;
        foreach (var coordinate in Fixture.Value.GetProperty("coordinates").EnumerateArray()) { Coord p = new(coordinate[0].GetInt32(), coordinate[1].GetInt32(), coordinate[2].GetInt32()); var probe = expected.GetProperty("maskProbes")[i++]; Assert.Equal(probe[0].GetInt32() != 0, mask.Tree.GetValue(p)); Assert.Equal(probe[1].GetInt32() != 0, mask.Tree.IsValueOn(p)); }
    }

    internal static T Value<T>(double a, double? b = null, double? c = null, double? d = null) where T : unmanaged
    {
        double second = b ?? a, third = c ?? a, fourth = d ?? a;
        object value = typeof(T) == typeof(float) ? (object)(float)a : typeof(T) == typeof(double) ? a : typeof(T) == typeof(Half) ? (Half)(float)a : typeof(T) == typeof(int) ? (int)a : typeof(T) == typeof(long) ? (long)a : typeof(T) == typeof(uint) ? (uint)a : typeof(T) == typeof(ulong) ? (ulong)a : typeof(T) == typeof(bool) ? a != 0 : typeof(T) == typeof(Vec2f) ? new Vec2f((float)a, (float)second) : typeof(T) == typeof(Vec2d) ? new Vec2d(a, second) : typeof(T) == typeof(Vec2i) ? new Vec2i((int)a, (int)second) : typeof(T) == typeof(Vec3f) ? new Vec3f((float)a, (float)second, (float)third) : typeof(T) == typeof(Vec3d) ? new Vec3d(a, second, third) : typeof(T) == typeof(Vec3i) ? new Vec3i((int)a, (int)second, (int)third) : typeof(T) == typeof(Vec4f) ? new Vec4f((float)a, (float)second, (float)third, (float)fourth) : typeof(T) == typeof(Vec4d) ? new Vec4d(a, second, third, fourth) : typeof(T) == typeof(Vec4i) ? new Vec4i((int)a, (int)second, (int)third, (int)fourth) : throw new NotSupportedException(); return (T)value;
    }
    private static T Read<T>(JsonElement value) where T : unmanaged
    {
        JsonElement scalar = value.ValueKind == JsonValueKind.Array ? value[0] : value;
        if (typeof(T) == typeof(long)) return (T)(object)scalar.GetInt64(); if (typeof(T) == typeof(ulong)) return (T)(object)scalar.GetUInt64();
        double Component(int index) => Number(value.ValueKind != JsonValueKind.Array ? value : value[index < value.GetArrayLength() ? index : 0]);
        return Value<T>(Component(0), Component(1), Component(2), Component(3));
    }
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();
}
