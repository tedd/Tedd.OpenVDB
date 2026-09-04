// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using System.Runtime.InteropServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class CountExtremaTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-count-extrema.json"))));
    public static IEnumerable<object[]> Cases()
    {
        int i = 0; foreach (var c in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray()) yield return [i++, $"{c.GetProperty("type")}/scene={c.GetProperty("scene")}"];
    }
    [Theory, MemberData(nameof(Cases))]
    public void NativeScalarBooleanAndVectorExtremaMatch(int index, string label)
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        var c = Fixture.Value.RootElement.GetProperty("cases")[index];
        switch (c.GetProperty("type").GetString())
        {
            case "bool": Run(c, e => e.GetInt32() != 0, Count.MinMax); break;
            case "int": Run(c, e => e.GetInt32(), tree => { var r = Count.MinMax(tree); return new ValueRange<int>(r.Minimum, r.Maximum); }); break;
            case "float": Run(c, e => (float)Number(e), tree => { var r = Count.MinMax(tree); return new ValueRange<float>(r.Minimum, r.Maximum); }); break;
            case "double": Run(c, Number, tree => { var r = Count.MinMax(tree); return new ValueRange<double>(r.Minimum, r.Maximum); }); break;
            case "vec2i": Run(c, e => new Vec2i(e[0].GetInt32(), e[1].GetInt32()), Count.MinMax); break;
            case "vec2f": Run(c, e => new Vec2f((float)Number(e[0]), (float)Number(e[1])), Count.MinMax); break;
            case "vec2d": Run(c, e => new Vec2d(Number(e[0]), Number(e[1])), Count.MinMax); break;
            case "vec3i": Run(c, e => new Vec3i(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32()), Count.MinMax); break;
            case "vec3f": Run(c, e => new Vec3f((float)Number(e[0]), (float)Number(e[1]), (float)Number(e[2])), Count.MinMax); break;
            case "vec3d": Run(c, e => new Vec3d(Number(e[0]), Number(e[1]), Number(e[2])), Count.MinMax); break;
            case "vec4i": Run(c, e => new Vec4i(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32()), Count.MinMax); break;
            case "vec4f": Run(c, e => new Vec4f((float)Number(e[0]), (float)Number(e[1]), (float)Number(e[2]), (float)Number(e[3])), Count.MinMax); break;
            case "vec4d": Run(c, e => new Vec4d(Number(e[0]), Number(e[1]), Number(e[2]), Number(e[3])), Count.MinMax); break;
            default: throw new InvalidOperationException(label);
        }
    }
    private static void Run<T>(JsonElement c, Func<JsonElement, T> parse, Func<VdbTree<T>, ValueRange<T>> calculate) where T : unmanaged
    {
        var tree = new VdbTree<T>(parse(c.GetProperty("background")));
        foreach (var operation in c.GetProperty("operations").EnumerateArray())
        {
            Coord coordinate = new(operation[1].GetInt32(), operation[2].GetInt32(), operation[3].GetInt32()); T value = parse(operation[4]);
            switch (operation[0].GetInt32())
            {
                case 0: tree.SetValueOn(coordinate, value); break;
                case 1: tree.SetValueOnly(coordinate, value); break;
                case 2: tree.AddTile(operation[5].GetInt32(), coordinate, value, operation[6].GetInt32() != 0); break;
            }
        }
        var result = calculate(tree); AssertValue(parse(c.GetProperty("minimum")), result.Minimum); AssertValue(parse(c.GetProperty("maximum")), result.Maximum);
    }
    private static void AssertValue<T>(T expected, T actual) where T : unmanaged
    {
        if (typeof(T) == typeof(Vec2f) || typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec4f))
        {
            var expectedComponents = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateReadOnlySpan(ref expected, 1));
            var actualComponents = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateReadOnlySpan(ref actual, 1));
            for (int i = 0; i < expectedComponents.Length; i++) Assert.Equal(expectedComponents[i], actualComponents[i]);
        }
        else if (typeof(T) == typeof(Vec2d) || typeof(T) == typeof(Vec3d) || typeof(T) == typeof(Vec4d))
        {
            var expectedComponents = MemoryMarshal.Cast<T, double>(MemoryMarshal.CreateReadOnlySpan(ref expected, 1));
            var actualComponents = MemoryMarshal.Cast<T, double>(MemoryMarshal.CreateReadOnlySpan(ref actual, 1));
            for (int i = 0; i < expectedComponents.Length; i++) Assert.Equal(expectedComponents[i], actualComponents[i]);
        }
        else Assert.Equal(expected, actual);
    }
    [Fact]
    public void CoordinatesUseNativeLexicographicOrder()
    {
        var tree = new VdbTree<Coord>(new Coord(5, 4, -6)); Assert.Equal(new ValueRange<Coord>(Coord.Zero, Coord.Zero), Count.MinMax(tree));
        var a = new Coord(5, 4, -7); var b = new Coord(5, 5, -6); tree.SetValueOn(Coord.Zero, a); tree.SetValueOn(new(-10), b);
        Assert.Equal(new ValueRange<Coord>(a, b), Count.MinMax(tree));
    }
    [Fact]
    public void VectorRangeRetainsWholeValuesInsteadOfComponentwiseExtrema()
    {
        var tree = new VdbTree<Vec3d>(); var a = new Vec3d(1, 9, -2); var b = new Vec3d(1, -3, 20); tree.SetValueOn(Coord.Zero, a); tree.SetValueOn(new(1), b);
        Assert.Equal(new ValueRange<Vec3d>(b, a), Count.MinMax(tree));
    }
    [Fact]
    public void FirstSignedZeroRetainsItsBitPattern()
    {
        var tree = new VdbTree<double>(); tree.SetValueOn(Coord.Zero, -0.0); tree.SetValueOn(new(1), 0.0); var result = Count.MinMax(tree);
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(result.Minimum)); Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(result.Maximum));
    }
    [Fact]
    public void ActiveFalseContributesToBooleanRange()
    {
        var tree = new VdbTree<bool>(true); tree.SetValueOn(Coord.Zero, false); tree.SetValueOn(new(1), true); tree.SetValueOff(new(8), false);
        Assert.Equal(new ValueRange<bool>(false, true), Count.MinMax(tree));
    }
    private static double Number(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : double.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
}
