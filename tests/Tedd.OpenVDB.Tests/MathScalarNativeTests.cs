using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathScalarNativeTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() => { using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-scalar-math.json"))); return document.RootElement.Clone(); });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((sample, index) => new object[] { index, sample.GetProperty("kind").GetString()!, sample.GetProperty("type").GetString()! });
    [Fact] public void NativeRevisionAndCoverageArePinned() { Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.GetProperty("upstreamCommit").GetString()); Assert.Equal(1652, Fixture.Value.GetProperty("cases").GetArrayLength()); }
    [Theory]
    [MemberData(nameof(Cases))]
    public void ScalarKernelsMatchNativeEvaluationAndRounding(int index, string kind, string type)
    {
        var test = Fixture.Value.GetProperty("cases")[index]; Assert.Equal(kind, test.GetProperty("kind").GetString());
        if (type == "float") Run<float>(test); else if (type == "double") Run<double>(test); else Run<Half>(test);
    }
    private static void Run<T>(JsonElement test) where T : unmanaged, IFloatingPointIeee754<T>
    {
        switch (test.GetProperty("kind").GetString()) { case "core": Core<T>(test); break; case "floating": Floating<T>(test); break; case "minmax": MinMax<T>(test); break; default: throw new NotSupportedException(); }
    }
    private static void Core<T>(JsonElement test) where T : unmanaged, IFloatingPointIeee754<T>
    {
        T x = Read<T>(test.GetProperty("x")), y = Read<T>(test.GetProperty("y")), clamped = x; bool changed = VdbMath.ClampTest01(ref clamped);
        int[] flags = [changed ? 1 : 0, VdbMath.Sign(x), VdbMath.SignChange(x, y) ? 1 : 0, VdbMath.ZeroCrossing(x, y) ? 1 : 0, VdbMath.IsZero(x) ? 1 : 0, VdbMath.IsNegative(x) ? 1 : 0, VdbMath.IsApproxLarger(x, y, T.CreateTruncating(0.125)) ? 1 : 0]; Assert.Equal(test.GetProperty("flags").EnumerateArray().Select(v => v.GetInt32()), flags);
        T power = typeof(T) == typeof(float) ? T.CreateTruncating(VdbMath.Pow(float.CreateTruncating(T.Abs(x)), 0.75f)) : typeof(T) == typeof(double) ? T.CreateTruncating(VdbMath.Pow(double.CreateTruncating(T.Abs(x)), 0.75)) : T.CreateTruncating(VdbMath.Pow(Half.CreateTruncating(T.Abs(x)), (Half)0.75f));
        T[] values = [VdbMath.Clamp(x, T.CreateTruncating(-0.5), T.CreateTruncating(1.25)), VdbMath.Clamp01(x), clamped, VdbMath.SmoothUnitStep(x), VdbMath.SmoothUnitStep(x, -T.One, T.CreateTruncating(2)), VdbMath.Pow2(x), VdbMath.Pow3(x), VdbMath.Pow4(x), VdbMath.Pow(x, 5), VdbMath.Pow(x, -3), VdbMath.Inv(x), VdbMath.Chop(x, T.CreateTruncating(0.125)), VdbMath.Min(x, y), VdbMath.Max(x, y), power, VdbMath.Tolerance<T>(), VdbMath.Delta<T>(), VdbMath.Negative(x), VdbMath.Abs(x)];
        for (int i = 0; i < values.Length; ++i) Equivalent(Read<T>(test.GetProperty("values")[i]), values[i], i == 14, $"core[{i}] x={x}, y={y}");
    }
    private static void Floating<T>(JsonElement test) where T : unmanaged, IFloatingPointIeee754<T>
    {
        double x = Number(test.GetProperty("x")), y = Number(test.GetProperty("y")); double[] values;
        if (typeof(T) == typeof(float))
        {
            float a = (float)x, b = (float)y; values = [VdbMath.RoundUp(a), VdbMath.RoundDown(a), VdbMath.Round(a), VdbMath.RoundUp(a, b), VdbMath.RoundDown(a, b), VdbMath.Mod(a, b), VdbMath.EuclideanRemainder(a), VdbMath.IntegerPart(a), VdbMath.FractionalPart(a), VdbMath.Truncate(a, 0), VdbMath.Truncate(a, 1), VdbMath.Truncate(a, 2), VdbMath.Truncate(a, 5), VdbMath.Truncate(a, 19), VdbMath.Truncate(a, 20), VdbMath.Truncate(a, 63), VdbMath.Truncate(a, 64), VdbMath.Truncate(a, 65), VdbMath.Sqrt(a), VdbMath.Cbrt(a), VdbMath.Exp(a), VdbMath.Sin(a), VdbMath.Cos(a)];
        }
        else values = [VdbMath.RoundUp(x), VdbMath.RoundDown(x), VdbMath.Round(x), VdbMath.RoundUp(x, y), VdbMath.RoundDown(x, y), VdbMath.Mod(x, y), VdbMath.EuclideanRemainder(x), VdbMath.IntegerPart(x), VdbMath.FractionalPart(x), VdbMath.Truncate(x, 0), VdbMath.Truncate(x, 1), VdbMath.Truncate(x, 2), VdbMath.Truncate(x, 5), VdbMath.Truncate(x, 19), VdbMath.Truncate(x, 20), VdbMath.Truncate(x, 63), VdbMath.Truncate(x, 64), VdbMath.Truncate(x, 65), VdbMath.Sqrt(x), VdbMath.Cbrt(x), VdbMath.Exp(x), VdbMath.Sin(x), VdbMath.Cos(x)];
        for (int i = 0; i < values.Length; ++i) Equivalent(Read<T>(test.GetProperty("values")[i]), T.CreateTruncating(values[i]), i >= 18, $"floating[{i}] x={x:R}, y={y:R}");
        var ulps = test.GetProperty("ulps"); double near = Number(ulps[0]); int[] tolerances = [-1, 0, 1, 2, 8];
        for (int i = 0; i < tolerances.Length; ++i) Assert.Equal(ulps[i + 1].GetInt32() != 0, typeof(T) == typeof(float) ? VdbMath.IsUlpsEqual((float)x, (float)near, tolerances[i]) : VdbMath.IsUlpsEqual(x, near, tolerances[i]));
    }
    private static void MinMax<T>(JsonElement test) where T : unmanaged, IFloatingPointIeee754<T>
    {
        T[] values = test.GetProperty("inputs").EnumerateArray().Select(Read<T>).ToArray(); Equivalent(Read<T>(test.GetProperty("values")[0]), VdbMath.Min<T>(values), false, "Min"); Equivalent(Read<T>(test.GetProperty("values")[1]), VdbMath.Max<T>(values), false, "Max");
        T minimum = values.Length switch { 2 => VdbMath.Min(values[0], values[1]), 3 => VdbMath.Min(values[0], values[1], values[2]), 4 => VdbMath.Min(values[0], values[1], values[2], values[3]), 5 => VdbMath.Min(values[0], values[1], values[2], values[3], values[4]), 6 => VdbMath.Min(values[0], values[1], values[2], values[3], values[4], values[5]), 7 => VdbMath.Min(values[0], values[1], values[2], values[3], values[4], values[5], values[6]), _ => VdbMath.Min(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]) };
        T maximum = values.Length switch { 2 => VdbMath.Max(values[0], values[1]), 3 => VdbMath.Max(values[0], values[1], values[2]), 4 => VdbMath.Max(values[0], values[1], values[2], values[3]), 5 => VdbMath.Max(values[0], values[1], values[2], values[3], values[4]), 6 => VdbMath.Max(values[0], values[1], values[2], values[3], values[4], values[5]), 7 => VdbMath.Max(values[0], values[1], values[2], values[3], values[4], values[5], values[6]), _ => VdbMath.Max(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7]) };
        Equivalent(Read<T>(test.GetProperty("values")[0]), minimum, false, "Min overload"); Equivalent(Read<T>(test.GetProperty("values")[1]), maximum, false, "Max overload");
        T third = Read<T>(test.GetProperty("third")); Assert.Equal(test.GetProperty("indices")[0].GetInt32(), VdbMath.MinIndex(values[0], values[1], third)); Assert.Equal(test.GetProperty("indices")[1].GetInt32(), VdbMath.MaxIndex(values[0], values[1], third));
    }
    private static void Equivalent<T>(T expected, T actual, bool transcendental, string context) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (T.IsNaN(expected)) { Assert.True(T.IsNaN(actual), context); return; }
        if (expected == T.Zero && actual == T.Zero) { Assert.True(T.IsNegative(expected) == T.IsNegative(actual), context + " signed zero"); return; }
        if (expected == actual) return;
        double e = double.CreateTruncating(expected), a = double.CreateTruncating(actual), tolerance = typeof(T) == typeof(Half) ? 0 : typeof(T) == typeof(float) ? 3e-7 : 3e-15;
        Assert.True(transcendental && double.IsFinite(e) && double.IsFinite(a) && System.Math.Abs(e - a) <= tolerance * System.Math.Max(1, System.Math.Abs(e)), $"{context}: {a:R} != {e:R}");
    }
    private static T Read<T>(JsonElement value) where T : unmanaged, IFloatingPointIeee754<T> => T.CreateTruncating(Number(value));
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();
}
