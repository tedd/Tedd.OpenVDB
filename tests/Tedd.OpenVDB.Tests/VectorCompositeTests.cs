using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class VectorCompositeTests
{
    private static readonly Lazy<Dictionary<string, JsonElement>> Fixtures = new(() =>
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-vector-composite.json")));
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", document.RootElement.GetProperty("upstreamCommit").GetString());
        return document.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(static entry => entry.GetProperty("name").GetString()!, static entry => entry.Clone());
    });
    public static IEnumerable<object[]> NativeCases() => Fixtures.Value.Keys.Select(static name => new object[] { name });

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void VectorArithmeticAndMagnitudeOrderingMatchNative(string name)
    {
        var entry = Fixtures.Value[name];
        switch (entry.GetProperty("type").GetString())
        {
            case "Vec2d": Check(entry, static a => new Vec2d(a[0].GetDouble(), a[1].GetDouble()), new(1), Composite.CompSum, Composite.CompMul, Composite.CompDiv, Composite.CompMin, Composite.CompMax); break;
            case "Vec3d": Check(entry, static a => new Vec3d(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble()), new(1), Composite.CompSum, Composite.CompMul, Composite.CompDiv, Composite.CompMin, Composite.CompMax); break;
            case "Vec3f": Check(entry, static a => new Vec3f(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle()), new(1), Composite.CompSum, Composite.CompMul, Composite.CompDiv, Composite.CompMin, Composite.CompMax); break;
            case "Vec3i": Check(entry, static a => new Vec3i(a[0].GetInt32(), a[1].GetInt32(), a[2].GetInt32()), new(1), Composite.CompSum, Composite.CompMul, Composite.CompDiv, Composite.CompMin, Composite.CompMax); break;
            case "Vec4d": Check(entry, static a => new Vec4d(a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble(), a[3].GetDouble()), new(1), Composite.CompSum, Composite.CompMul, Composite.CompDiv, Composite.CompMin, Composite.CompMax); break;
            default: throw new InvalidOperationException(name);
        }
    }

    private static void Check<T>(JsonElement fixture, Func<JsonElement, T> read, T one,
        Action<VdbTree<T>, VdbTree<T>> sum, Action<VdbTree<T>, VdbTree<T>> mul,
        Action<VdbTree<T>, VdbTree<T>> div, Action<VdbTree<T>, VdbTree<T>> min, Action<VdbTree<T>, VdbTree<T>> max) where T : unmanaged
    {
        var operations = new[] { ("sum", sum), ("product", mul), ("quotient", div), ("minimum", min), ("maximum", max) };
        foreach (var (name, operation) in operations)
        {
            var a = new VdbTree<T>(one); var b = new VdbTree<T>(one);
            a.SetValueOff(new Coord(-1), read(fixture.GetProperty("a")));
            b.SetValueOn(new Coord(-1), read(fixture.GetProperty(name == "quotient" ? "divisor" : "b")));
            operation(a, b);
            Assert.Equal(read(fixture.GetProperty(name)), a.GetValue(new Coord(-1)));
            Assert.True(a.IsValueOn(new Coord(-1))); Assert.True(b.IsEmpty);
        }
    }

    [Fact]
    public void VectorOrderingUsesMagnitudeBeforeLexicographicTieAndDoesNotMixComponents()
    {
        var a = new VdbTree<Vec3d>(new(1)); var b = new VdbTree<Vec3d>(new(1));
        a.SetValueOn(new Coord(0), new(-100, 0, 0)); b.SetValueOn(new Coord(0), new(1, 1, 1));
        a.SetValueOn(new Coord(1), new(3, 4, 0)); b.SetValueOn(new Coord(1), new(0, 3, 4));
        Composite.CompMin(a, b);
        Assert.Equal(new Vec3d(1, 1, 1), a.GetValue(new Coord(0)));
        Assert.Equal(new Vec3d(0, 3, 4), a.GetValue(new Coord(1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnorderedMagnitudeFallsBackToNativeComponentComparison(bool maximum)
    {
        var a = new VdbTree<Vec3d>(new(1)); var b = new VdbTree<Vec3d>(new(1));
        var left = new Vec3d(double.NaN, 1, 2); var right = new Vec3d(9, 8, 7);
        a.SetValueOn(new Coord(0), left); b.SetValueOn(new Coord(0), right);
        a.SetValueOn(new Coord(1), new Vec3d(-0d)); b.SetValueOn(new Coord(1), new Vec3d(0d));
        if (maximum) Composite.CompMax(a, b); else Composite.CompMin(a, b);
        Assert.Equal(right, a.GetValue(new Coord(0)));
        Assert.Equal(BitConverter.DoubleToInt64Bits(0d), BitConverter.DoubleToInt64Bits(a.GetValue(new Coord(1)).X));
    }
}
