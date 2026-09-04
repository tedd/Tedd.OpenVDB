// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class NativeRayTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-rays.json"))));
    public static IEnumerable<object[]> Cases()
    {
        int index = 0;
        foreach (var item in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
        {
            string label = item.GetProperty("kind").GetString()!;
            foreach (string key in new[] { "type", "scene", "level", "dilation", "iterations", "world" })
                if (item.TryGetProperty(key, out var value)) label += $"/{key}={value}";
            yield return [index++, label];
        }
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeRayIntervalsCrossingsAndNormalsMatch(int index, string label)
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        var item = Fixture.Value.RootElement.GetProperty("cases")[index];
        if (item.GetProperty("kind").GetString() == "volume") RunVolume(item, label);
        else if (item.GetProperty("type").GetString() == "float") RunLevel<float>(item, label);
        else RunLevel<double>(item, label);
    }
    private static void RunVolume(JsonElement item, string label)
    {
        var grid = MakeVolume(item.GetProperty("scene").GetInt32());
        var master = new VolumeRayIntersector<float>(grid, item.GetProperty("dilation").GetInt32(), item.GetProperty("level").GetInt32());
        CompareVector(item.GetProperty("bbox")[0], master.BoundingBox.Min, 0, label);
        CompareVector(item.GetProperty("bbox")[1], master.BoundingBox.Max, 0, label);
        bool world = item.GetProperty("world").GetInt32() != 0;
        foreach (var r in item.GetProperty("rays").EnumerateArray())
        {
            var ray = ReadRay(r); var a = new VolumeRayIntersector<float>(master); var b = new VolumeRayIntersector<float>(master);
            bool hit = world ? a.SetWorldRay(ray) : a.SetIndexRay(ray);
            Assert.Equal(r.GetProperty("clip").GetInt32() != 0, hit);
            Assert.Equal(hit, world ? b.SetWorldRay(ray) : b.SetIndexRay(ray));
            List<RayTimeSpan> spans = [new(-5, -2)]; a.Hits(spans); CompareSpans(r.GetProperty("hits"), spans, label + $"/ray={ray}");
            List<RayTimeSpan> marched = [];
            for (int n = 0; n < 1000; n++) { RayTimeSpan span = b.March(); if (!span.IsValid()) break; marched.Add(span); }
            CompareSpans(r.GetProperty("march"), marched, label);
            if (spans.Count == 0) continue;
            double mid = spans[0].Midpoint;
            CompareVector(r.GetProperty("indexPosition"), a.GetIndexPosition(mid), 2e-10, label);
            CompareVector(r.GetProperty("worldPosition"), a.GetWorldPosition(mid), 2e-10, label);
            Near(Number(r.GetProperty("worldTime")), a.GetWorldTime(mid), 2e-10, label);
        }
    }
    private static void RunLevel<T>(JsonElement item, string label) where T : unmanaged, IFloatingPointIeee754<T>
    {
        Grid<T> grid = MakeLevel<T>(item.GetProperty("scene").GetInt32());
        var intersector = new LevelSetRayIntersector<T>(grid, T.CreateTruncating(item.GetProperty("iso").GetDouble()), item.GetProperty("iterations").GetInt32(), item.GetProperty("level").GetInt32());
        bool world = item.GetProperty("world").GetInt32() != 0; double epsilon = typeof(T) == typeof(float) ? 3e-6 : 3e-12;
        foreach (var r in item.GetProperty("rays").EnumerateArray())
        {
            Rayd ray = ReadRay(r); Vec3d normal = default;
            bool hit = world ? intersector.IntersectsWorld(ray, out Vec3d position, out normal, out double time) : intersector.IntersectsIndex(ray, out position, out time);
            Assert.Equal(r.GetProperty("hit").GetInt32() != 0, hit);
            Assert.Equal(hit, world ? intersector.IntersectsWorld(ray) : intersector.IntersectsIndex(ray));
            if (!hit) { Assert.True(double.IsNaN(time)); continue; }
            Near(Number(r.GetProperty("time")), time, epsilon, label + $"/ray={ray}");
            CompareVector(r.GetProperty("position"), position, epsilon, label);
            if (world) CompareVector(r.GetProperty("normal"), normal, epsilon, label);
        }
    }
    internal static Grid<float> MakeVolume(int scene)
    {
        var grid = new Grid<float>();
        if (scene == 0)
        {
            foreach (var coordinate in new Coord[] { new(-17, -1, 0), new(0, 0, 0), new(7, 7, 7), new(24, 0, 0), new(127, 1, 1), new(128, 1, 1), new(4095, 0, 0), new(4096, 0, 0), new(-4097, 0, 0) }) grid.Tree.SetValueOn(coordinate, 1);
            grid.Tree.SetValueOff(new(16, 0, 0), 3); grid.Tree.AddTile(1, new(48, 0, 0), 2, true); grid.Tree.AddTile(2, new(256, 0, 0), 2, true); grid.Tree.AddTile(3, new(8192, 0, 0), 2, true);
        }
        else if (scene == 1)
        {
            grid.Tree.AddTile(3, new(-4096, 0, 0), 0, true); grid.Tree.AddTile(2, new(0, 0, 0), 0, true); grid.Tree.AddTile(1, new(128, 0, 0), 0, true); grid.Tree.SetValueOff(new(130, 0, 0), 7);
            grid.Tree.AddTile(1, new(160, 0, 0), 3, false); grid.Tree.TouchLeaf(new(184, 0, 0)); grid.Tree.SetValueOn(new(200, 0, 0), 0);
        }
        else for (int x = -18; x <= 19; x += 3) for (int y = -12; y <= 12; y += 4) for (int z = -9; z <= 9; z += 3) if ((x + 2 * y - z) % 7 != 0) grid.Tree.SetValueOn(new(x, y, z), 1);
        grid.Transform = Transform.CreateLinearTransform(0.75); grid.Transform.PostRotate(0.31, Axis.Z); grid.Transform.PostTranslate(new(2.25, -1.5, 0.75)); return grid;
    }
    internal static Grid<T> MakeLevel<T>(int scene) where T : unmanaged, IFloatingPointIeee754<T>
    {
        Grid<T> grid;
        if (scene < 2) grid = LevelSetSphere.Create(T.CreateTruncating(4.25), new(0.25, -0.375, 0.5), T.CreateTruncating(0.5), T.CreateTruncating(3));
        else
        {
            grid = new(T.CreateTruncating(3)) { GridClass = GridClass.LevelSet, Transform = Transform.CreateLinearTransform(0.5) };
            for (int x = -9; x <= 9; x++) for (int y = -9; y <= 9; y++) for (int z = -9; z <= 9; z++)
            {
                T value = T.CreateTruncating(x) * T.CreateTruncating(0.5) + T.CreateTruncating(y) * T.CreateTruncating(0.125) - T.CreateTruncating(z) * T.CreateTruncating(0.0625) - T.CreateTruncating(0.375);
                if (T.Abs(value) < T.CreateTruncating(3)) grid.Tree.SetValueOn(new(x, y, z), value); else grid.Tree.SetValueOff(new(x, y, z), value < T.Zero ? T.CreateTruncating(-3) : T.CreateTruncating(3));
            }
        }
        if (scene == 1) { grid.Transform.PostRotate(0.31, Axis.Z); grid.Transform.PostTranslate(new(2.25, -1.5, 0.75)); }
        return grid;
    }
    private static Rayd ReadRay(JsonElement r) => new(Vector(r.GetProperty("eye")), Vector(r.GetProperty("direction")), Number(r.GetProperty("range")[0]), Number(r.GetProperty("range")[1]));
    private static Vec3d Vector(JsonElement v) => new(Number(v[0]), Number(v[1]), Number(v[2]));
    private static double Number(JsonElement v) => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.Parse(v.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    private static void CompareSpans(JsonElement expected, List<RayTimeSpan> actual, string label)
    {
        Assert.True(expected.GetArrayLength() == actual.Count, $"{label}: native intervals={expected}, managed={string.Join(",", actual)}");
        for (int i = 0; i < actual.Count; i++) { Near(Number(expected[i][0]), actual[i].Start, 2e-10, label); Near(Number(expected[i][1]), actual[i].End, 2e-10, label); }
    }
    private static void CompareVector(JsonElement expected, Vec3d actual, double epsilon, string label) { for (int i = 0; i < 3; i++) Near(Number(expected[i]), actual[i], epsilon, label); }
    private static void Near(double expected, double actual, double epsilon, string label)
    { if (!double.IsFinite(expected)) Assert.Equal(expected, actual); else Assert.True(System.Math.Abs(expected - actual) <= epsilon * System.Math.Max(1, System.Math.Abs(expected)), $"{label}: native={expected:R}, managed={actual:R}"); }
}
