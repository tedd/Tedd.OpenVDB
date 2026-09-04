using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathFiniteDifferenceNativeTests
{
    private static readonly JsonDocument Fixture = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-finite-difference.json")));
    public static IEnumerable<object[]> Cases()
    {
        int index = 0; foreach (JsonElement test in Fixture.RootElement.GetProperty("cases").EnumerateArray())
            yield return new object[] { index++, test.GetProperty("kind").GetString()! };
    }
    [Fact]
    public void FixtureHasPinnedProvenance()
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.RootElement.GetProperty("upstreamCommit").GetString());
        Assert.True(Fixture.RootElement.GetProperty("cases").GetArrayLength() >= 100);
    }
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();
    private static double[] Values(JsonElement test, string name) => test.GetProperty(name).EnumerateArray().Select(Number).ToArray();
    private static void Near(double expected, double actual, double tolerance, string label)
    {
        if (double.IsNaN(expected)) { Assert.True(double.IsNaN(actual), label); return; }
        if (double.IsInfinity(expected)) { Assert.Equal(expected, actual); return; }
        Assert.True(System.Math.Abs(actual - expected) <= tolerance * System.Math.Max(1, System.Math.Abs(expected)), $"{label}: native {expected:R}, managed {actual:R}");
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void ManagedFiniteDifferencesAndLayoutsMatchNative(int index, string kind)
    {
        JsonElement test = Fixture.RootElement.GetProperty("cases")[index];
        if (kind == "difference")
        {
            if (test.GetProperty("precision").GetString() == "float") CheckDifference<float>(test, 2e-7); else CheckDifference<double>(test, 2e-12);
        }
        else
        {
            var grid = new Grid<double>(0); double[] input = Values(test, "input"); int n = 0;
            for (int x = -3; x <= 3; ++x) for (int y = -3; y <= 3; ++y) for (int z = -3; z <= 3; ++z) grid.Tree.SetValueOn(new(x, y, z), input[n++]);
            Stencil<double> stencil = (int)Number(test.GetProperty("shape")) switch
            {
                0 => new SevenPointStencil<double>(grid),
                1 => new ThirteenPointStencil<double>(grid),
                2 => new NineteenPointStencil<double>(grid),
                3 => new SecondOrderDenseStencil<double>(grid),
                4 => new FourthOrderDenseStencil<double>(grid),
                5 => new SixthOrderDenseStencil<double>(grid),
                6 => new BoxStencil<double>(grid),
                7 => new DenseStencil<double>(grid, 1),
                8 => new DenseStencil<double>(grid, 2),
                9 => new DenseStencil<double>(grid, 3),
                _ => throw new InvalidOperationException("Unknown stencil shape")
            };
            stencil.MoveTo(Coord.Zero);
            Assert.Equal((int)Number(test.GetProperty("size")), stencil.Count);
            Assert.Equal(Values(test, "values"), stencil.Values.ToArray()); Assert.Equal(Number(test.GetProperty("center")), stencil.CenterValue);
            Near(Number(test.GetProperty("mean")), stencil.Mean, 2e-13, "mean");
            Assert.Equal(Number(test.GetProperty("median")), stencil.Median); Assert.Equal(Number(test.GetProperty("min")), stencil.Min);
            Assert.Equal(Number(test.GetProperty("max")), stencil.Max);
        }
    }
    private static void CheckDifference<T>(JsonElement test, double tolerance) where T : unmanaged, IFloatingPointIeee754<T>
    {
        T[] values = Values(test, "values").Select(T.CreateTruncating).ToArray(); double[] first = Values(test, "first"), second = Values(test, "second");
        for (int scheme = 0; scheme < 14; ++scheme) Near(first[scheme], double.CreateTruncating(FiniteDifference.First<T>((DScheme)scheme, values)), tolerance, "first " + scheme);
        for (int scheme = 0; scheme < 3; ++scheme) Near(second[scheme], double.CreateTruncating(FiniteDifference.Second<T>((DDScheme)scheme, values)), tolerance, "second " + scheme);
        Near(Number(test.GetProperty("weno")), double.CreateTruncating(FiniteDifference.Weno5(values[0], values[1], values[2], values[3], values[4])), tolerance, "WENO5");
        Near(Number(test.GetProperty("godunovOut")), double.CreateTruncating(FiniteDifference.GodunovNormSquared(true, values[0], values[1], values[2], values[3], values[4], values[5])), tolerance, "Godunov outside");
        Near(Number(test.GetProperty("godunovIn")), double.CreateTruncating(FiniteDifference.GodunovNormSquared(false, values[0], values[1], values[2], values[3], values[4], values[5])), tolerance, "Godunov inside");
    }
}
