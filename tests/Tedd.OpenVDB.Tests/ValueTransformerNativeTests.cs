// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class ValueTransformerNativeTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-value-transformer.json.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    });
    public static IEnumerable<object[]> Cases()
    {
        int index = 0;
        foreach (var c in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
            yield return [index++, c.GetProperty("kind").GetString()! + "/" + (c.TryGetProperty("type", out var t) ? t.GetString() : "float")];
    }
    [Theory, MemberData(nameof(Cases))]
    public void NativeModifiersCallbacksReductionsAndOutputTreesMatch(int index, string label)
    {
        Assert.NotEmpty(label); Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        var c = Fixture.Value.RootElement.GetProperty("cases")[index];
        switch (c.GetProperty("kind").GetString())
        {
            case "foreach": ForEach(c); return;
            case "accumulate": Accumulate(c); return;
            case "transform": Transform(c); return;
        }
        switch (c.GetProperty("type").GetString())
        {
            case "bool": Modifier(c, e => e.GetInt32() != 0); break;
            case "int": Modifier(c, e => e.GetInt32()); break;
            case "long": Modifier(c, e => e.GetInt64()); break;
            case "float": Modifier(c, e => e.GetSingle()); break;
            case "double": Modifier(c, e => e.GetDouble()); break;
            case "half": Modifier(c, e => (Half)e.GetDouble()); break;
            case "vec2i": Modifier(c, e => new Vec2i(e[0].GetInt32(), e[1].GetInt32())); break;
            case "vec2f": Modifier(c, e => new Vec2f(e[0].GetSingle(), e[1].GetSingle())); break;
            case "vec2d": Modifier(c, e => new Vec2d(e[0].GetDouble(), e[1].GetDouble())); break;
            case "vec3i": Modifier(c, e => new Vec3i(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32())); break;
            case "vec3f": Modifier(c, e => new Vec3f(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle())); break;
            case "vec3d": Modifier(c, e => new Vec3d(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble())); break;
            case "vec4i": Modifier(c, e => new Vec4i(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32())); break;
            case "vec4f": Modifier(c, e => new Vec4f(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle())); break;
            case "vec4d": Modifier(c, e => new Vec4d(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble(), e[3].GetDouble())); break;
            default: throw new InvalidOperationException(label);
        }
    }
    private static void Modifier<T>(JsonElement c, Func<JsonElement, T> read) where T : unmanaged
    {
        var tree = new VdbTree<T>(read(c.GetProperty("background"))); T initial = read(c.GetProperty("initial")); Coord coordinate = new(-1, 3, 128);
        int scene = c.GetProperty("scene").GetInt32();
        if (scene == 1) tree.SetValueOff(coordinate, initial);
        if (scene == 2) tree.SetValueOn(coordinate, initial);
        if (scene >= 3) tree.AddTile(scene - 2, coordinate, initial, scene != 4);
        var accessor = tree.GetAccessor(); int operation = c.GetProperty("operation").GetInt32(); bool useAccessor = c.GetProperty("accessor").GetInt32() != 0;
        foreach (var step in c.GetProperty("steps").EnumerateArray())
        {
            T value = read(step.GetProperty("candidate"));
            if (useAccessor) switch (operation)
                {
                    case 0: ValueTransformer.SetValueOnMin(accessor, coordinate, value); break;
                    case 1: ValueTransformer.SetValueOnMax(accessor, coordinate, value); break;
                    case 2: ValueTransformer.SetValueOnSum(accessor, coordinate, value); break;
                    case 3: ValueTransformer.SetValueOnMult(accessor, coordinate, value); break;
                }
            else switch (operation)
                {
                    case 0: ValueTransformer.SetValueOnMin(tree, coordinate, value); break;
                    case 1: ValueTransformer.SetValueOnMax(tree, coordinate, value); break;
                    case 2: ValueTransformer.SetValueOnSum(tree, coordinate, value); break;
                    case 3: ValueTransformer.SetValueOnMult(tree, coordinate, value); break;
                }
            var state = step.GetProperty("state");
            Assert.Equal(state.GetProperty("active").GetUInt64(), tree.ActiveVoxelCount);
            Assert.Equal(state.GetProperty("leaves").GetInt64(), tree.LeafCount);
            Assert.Equal(state.GetProperty("depth").GetInt32(), tree.GetValueDepth(coordinate));
            Assert.Equal(state.GetProperty("on").GetInt32() != 0, tree.IsValueOn(coordinate));
            Assert.Equal(read(state.GetProperty("value")), tree.GetValue(coordinate));
            var near = state.GetProperty("near"); Coord other = coordinate.OffsetBy(1, 1, 1);
            Assert.Equal(read(near[0]), tree.GetValue(other)); Assert.Equal(near[1].GetInt32() != 0, tree.IsValueOn(other)); Assert.Equal(near[2].GetInt32(), tree.GetValueDepth(other));
        }
    }
    private static VdbTree<float> Input()
    {
        var tree = new VdbTree<float>();
        for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++) for (int z = -2; z <= 2; z++)
            if ((x + y + z) % 3 != 0) tree.SetValueOn(new(x, y, z), x + 2 * y - 3 * z);
        tree.AddTile(1, new(8, 0, 0), 7, true); tree.SetValueOff(new(24, 0, 0), 2); tree.AddTile(3, new(4096), -2, false); return tree;
    }
    private static ValueTransformOptions Options(JsonElement c) => new()
    {
        Threaded = c.GetProperty("threaded").GetInt32() != 0,
        ShareOperation = !c.TryGetProperty("shared", out var shared) || shared.GetInt32() != 0,
        GrainSize = int.MaxValue
    };
    private static void ForEach(JsonElement c)
    {
        var tree = Input();
        long count = ValueTransformer.ForEach(tree.EnumerateValueHandles((ValueIterationMode)c.GetProperty("mode").GetInt32()), 11L,
            static (TreeValue<float> value, ref long n) => { n++; value.SetValue(value.Value + .5f); }, Options(c));
        Assert.Equal(c.GetProperty("count").GetInt64(), count); Groups(c.GetProperty("groups"), tree, v => v);
    }
    private struct Sum { public double Value; public ulong Count; public ulong Joins; }
    private static void Accumulate(JsonElement c)
    {
        var result = ValueTransformer.Accumulate(Input().EnumerateValues((ValueIterationMode)c.GetProperty("mode").GetInt32()), new Sum { Value = 11, Count = 13 },
            static (VoxelValue<float> item, ref Sum sum) => { sum.Value += (double)item.Value * item.VoxelCount; sum.Count++; },
            static (ref Sum sum, Sum other) => { sum.Value += other.Value; sum.Count += other.Count; sum.Joins++; }, Options(c));
        Assert.Equal(c.GetProperty("sum").GetDouble(), result.Value); Assert.Equal(c.GetProperty("count").GetUInt64(), result.Count); Assert.Equal(c.GetProperty("joins").GetUInt64(), result.Joins);
    }
    private static void Transform(JsonElement c)
    {
        var input = Input(); var output = new VdbTree<double>(9); output.SetValueOn(new(-20), 17);
        long count = ValueTransformer.TransformValues(input, output, 11L,
            static (VoxelValue<float> value, ValueAccessor<double> target, ref long n) =>
            {
                n++; target.Tree.Fill(new(value.Bounds.Min.OffsetBy(2, 0, 0), value.Bounds.Max.OffsetBy(2, 0, 0)), (double)value.Value * 2 + 1, true);
            }, options: Options(c), merge: (MergePolicy)c.GetProperty("merge").GetInt32());
        Assert.Equal(c.GetProperty("count").GetInt64(), count); Assert.Equal(c.GetProperty("active").GetUInt64(), output.ActiveVoxelCount);
        Groups(c.GetProperty("groups"), output, v => v);
        foreach (var p in c.GetProperty("probes").EnumerateArray())
        {
            Coord coordinate = new(p[0].GetInt32(), p[1].GetInt32(), p[2].GetInt32());
            Assert.Equal(p[3].GetDouble(), output.GetValue(coordinate)); Assert.Equal(p[4].GetInt32() != 0, output.IsValueOn(coordinate)); Assert.Equal(p[5].GetInt32(), output.GetValueDepth(coordinate));
        }
    }
    private static void Groups<T>(JsonElement expected, VdbTree<T> tree, Func<T, double> number) where T : unmanaged
    {
        var groups = tree.EnumerateValues(ValueIterationMode.All).GroupBy(v => (Depth: 3 - v.Level, Active: v.IsActive, Value: number(v.Value)))
            .Select(g => (g.Key.Depth, g.Key.Active, g.Key.Value, Count: g.LongCount())).OrderBy(g => g.Depth).ThenBy(g => g.Active).ThenBy(g => g.Value).ToArray();
        Assert.Equal(expected.GetArrayLength(), groups.Length);
        for (int i = 0; i < groups.Length; i++)
        {
            var e = expected[i]; var a = groups[i]; Assert.Equal(e[0].GetInt32(), a.Depth); Assert.Equal(e[1].GetInt32() != 0, a.Active);
            Assert.Equal(e[2].GetDouble(), a.Value); Assert.Equal(e[3].GetInt64(), a.Count);
        }
    }
}
