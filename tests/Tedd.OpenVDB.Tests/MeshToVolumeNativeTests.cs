// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class MeshToVolumeNativeTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-mesh-to-volume.json"))));
    public static IEnumerable<object[]> Cases()
    {
        int i = 0; foreach (var c in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
            yield return [i++, $"{c.GetProperty("type").GetString()}/scene={c.GetProperty("scene")}/flags={c.GetProperty("flags")}/width={c.GetProperty("exterior")},{c.GetProperty("interior")}/interior={c.GetProperty("interiorMode")}"];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeMeshDistanceTopologyAndClosestPolygonsMatch(int index, string label)
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        var c = Fixture.Value.RootElement.GetProperty("cases")[index]; if (c.GetProperty("type").GetString() == "float") Run<float>(c, label); else Run<double>(c, label);
    }
    private static void Run<T>(JsonElement c, string label) where T : unmanaged, IFloatingPointIeee754<T>
    {
        Vec3d[] points = c.GetProperty("points").EnumerateArray().Select(ReadVector).ToArray();
        MeshQuad[] polygons = c.GetProperty("faces").EnumerateArray().Select(f => new MeshQuad(f[0].GetInt32(), f[1].GetInt32(), f[2].GetInt32(), f[3].GetInt32())).ToArray();
        var adapter = new QuadAndTriangleDataAdapter<Vec3d>(points, ReadOnlyMemory<MeshTriangle>.Empty, polygons);
        int transformMode = c.GetProperty("transformMode").GetInt32(); var transform = Transform.CreateLinearTransform(transformMode != 0 ? 0.75 : 1);
        if (transformMode != 0) { transform.PostRotate(0.21, Axis.Z); transform.PostTranslate(new(2, -1, 0.5)); }
        int interiorMode = c.GetProperty("interiorMode").GetInt32(); Func<Coord, bool>? interior = interiorMode == 0 ? null : p => (double)p.X * p.X + (double)p.Y * p.Y + (double)p.Z * p.Z < 9;
        var indices = new Grid<int>(-1);
        var result = MeshToVolume.Convert<T, QuadAndTriangleDataAdapter<Vec3d>>(adapter, transform, (float)Number(c.GetProperty("exterior")), (float)Number(c.GetProperty("interior")),
            (MeshToVolumeFlags)c.GetProperty("flags").GetInt32(), indices, interior, interiorMode == 2 ? InteriorTestStrategy.EveryTile : InteriorTestStrategy.EveryVoxel);
        Near(Number(c.GetProperty("background")), double.CreateTruncating(result.Background), label + "/background");
        Assert.Equal(c.GetProperty("class").GetInt32(), (int)result.GridClass); Assert.True(transform.IsEquivalentTo(result.Transform)); Assert.NotSame(transform, result.Transform);
        Assert.True(result.ActiveVoxelCount == c.GetProperty("active").GetUInt64(), $"{label} active: native={c.GetProperty("active")}, managed={result.ActiveVoxelCount}");
        Assert.Equal(c.GetProperty("indexActive").GetUInt64(), indices.ActiveVoxelCount);
        CompareLeaves(c.GetProperty("leaves"), result.Tree, label, false); CompareLeaves(c.GetProperty("indexLeaves"), indices.Tree, label, true);
        foreach (var p in c.GetProperty("probes").EnumerateArray())
        {
            Coord coordinate = new(p[0].GetInt32(), p[1].GetInt32(), p[2].GetInt32()); Near(Number(p[3]), double.CreateTruncating(result.Tree.GetValue(coordinate)), label + $"/probe{coordinate}");
            Assert.Equal(p[4].GetInt32() != 0, result.Tree.IsValueOn(coordinate)); Assert.Equal(p[5].GetInt32(), result.Tree.GetValueDepth(coordinate));
        }
    }
    private static void CompareLeaves<T>(JsonElement expected, VdbTree<T> tree, string label, bool integer) where T : unmanaged, INumber<T>
    {
        Assert.True(expected.GetArrayLength() == tree.LeafCount, $"{label} leaves: native={expected.GetArrayLength()}, managed={tree.LeafCount}");
        foreach (var l in expected.EnumerateArray())
        {
            Vec3d origin = ReadVector(l.GetProperty("origin")); var coordinate = new Coord((int)origin.X, (int)origin.Y, (int)origin.Z); var leaf = tree.ProbeLeaf(coordinate); Assert.NotNull(leaf);
            string active = l.GetProperty("active").GetString()!; var values = l.GetProperty("values");
            for (int offset = 0; offset < 512; offset++)
            {
                Assert.True((active[offset] == '1') == leaf.IsValueOn(offset), $"{label} activity at {leaf.OffsetToGlobalCoord(offset)}");
                double wanted = Number(values[offset]), actual = double.CreateTruncating(leaf.GetValue(offset));
                if (integer) Assert.Equal(wanted, actual); else Near(wanted, actual, label + $"/voxel{leaf.OffsetToGlobalCoord(offset)}");
            }
        }
    }
    private static Vec3d ReadVector(JsonElement value) => new(Number(value[0]), Number(value[1]), Number(value[2]));
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : double.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    private static void Near(double expected, double actual, string context)
    {
        if (!double.IsFinite(expected)) { Assert.Equal(expected, actual); return; }
        double epsilon = context.StartsWith("double/", StringComparison.Ordinal) ? 3e-12 : 3e-6;
        Assert.True(System.Math.Abs(expected - actual) <= epsilon * System.Math.Max(1, System.Math.Abs(expected)), $"{context}: native={expected:R}, managed={actual:R}");
    }
}
