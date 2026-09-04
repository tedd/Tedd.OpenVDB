// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class MeshEdgeTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-mesh-edge.json"))));
    public static IEnumerable<object[]> NativeCases()
    {
        int i = 0; foreach (var c in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray()) yield return [i++, $"scene={c.GetProperty("scene")}/mode={c.GetProperty("mode")}"];
    }
    [Theory, MemberData(nameof(NativeCases))]
    public void NativeLatticeEdgesAndCellIntersectionsMatch(int index, string label)
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        var c = Fixture.Value.RootElement.GetProperty("cases")[index];
        Vec3f[] points = c.GetProperty("points").EnumerateArray().Select(p => new Vec3f(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())).ToArray();
        MeshQuad[] faces = c.GetProperty("faces").EnumerateArray().Select(f => new MeshQuad(f[0].GetInt32(), f[1].GetInt32(), f[2].GetInt32(), f[3].GetInt32())).ToArray();
        var mesh = new MeshToVoxelEdgeData(); mesh.Convert(points, faces); var accessor = mesh.GetAccessor(); var tree = accessor.Tree;
        Assert.True(c.GetProperty("active").GetUInt64() == tree.ActiveVoxelCount, $"{label}: active native={c.GetProperty("active")}, managed={tree.ActiveVoxelCount}");
        Assert.Equal(c.GetProperty("leaves").GetArrayLength(), tree.LeafCount);
        foreach (var l in c.GetProperty("leaves").EnumerateArray())
        {
            var leaf = tree.ProbeLeaf(ReadCoord(l.GetProperty("origin"))); Assert.NotNull(leaf);
            for (int offset = 0; offset < 512; offset++)
            {
                var expected = l.GetProperty("values")[offset]; Assert.Equal(expected[0].GetInt32() != 0, leaf.IsValueOn(offset));
                var value = leaf.GetValue(offset); var d = expected[1];
                Assert.Equal(d[0].GetSingle(), value.XDistance); Assert.Equal(d[1].GetSingle(), value.YDistance); Assert.Equal(d[2].GetSingle(), value.ZDistance);
                Assert.Equal(d[3].GetInt32(), value.XPrimitive); Assert.Equal(d[4].GetInt32(), value.YPrimitive); Assert.Equal(d[5].GetInt32(), value.ZPrimitive);
            }
        }
        foreach (var cell in c.GetProperty("cells").EnumerateArray())
        {
            List<Vec3d> intersections = [new(999)]; List<int> primitives = [1234];
            MeshToVoxelEdgeData.GetEdgeData(accessor, ReadCoord(cell.GetProperty("coordinate")), intersections, primitives);
            Assert.Equal(cell.GetProperty("points").EnumerateArray().Select(p => new Vec3d(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble())), intersections);
            Assert.Equal(cell.GetProperty("primitives").EnumerateArray().Select(p => p.GetInt32()), primitives);
        }
    }
    [Fact]
    public void UnitEdgesIncludeStartAndExcludeEnd()
    {
        Vec3f[] points = [new(0, -2, -2), new(0, 2, -2), new(0, 0, 2)]; MeshQuad[] faces = [new(0, 1, 2, -1)];
        var mesh = new MeshToVoxelEdgeData(); mesh.Convert(points, faces); var accessor = mesh.GetAccessor();
        Assert.True(accessor.ProbeValue(Coord.Zero, out var zero)); Assert.Equal(0, zero.XPrimitive); Assert.Equal(0, zero.XDistance);
        Assert.False(accessor.IsValueOn(new(-1, 0, 0))); Assert.Equal(-1, accessor.GetValue(new(-1, 0, 0)).XPrimitive);
        Assert.Equal(-1, zero.YPrimitive); Assert.Equal(-1, zero.ZPrimitive);
    }
    [Fact]
    public void ClosestIntersectionWinsAndEqualDistancesKeepFirstPrimitive()
    {
        Vec3f[] points = [new(0.75f, -2, -2), new(0.75f, 2, -2), new(0.75f, 0, 2), new(0.25f, -2, -2), new(0.25f, 2, -2), new(0.25f, 0, 2)];
        var mesh = new MeshToVoxelEdgeData(); mesh.Convert(points, new MeshQuad[] { new(0, 1, 2, -1), new(3, 4, 5, -1), new(3, 4, 5, -1) });
        var value = mesh.GetAccessor().GetValue(Coord.Zero); Assert.Equal(0.25f, value.XDistance); Assert.Equal(1, value.XPrimitive);
    }
    [Fact]
    public void EdgeEqualityUsesPrimitiveIndicesOnly()
    {
        var a = new MeshToVoxelEdgeData.EdgeData(1); var b = new MeshToVoxelEdgeData.EdgeData(0.5f);
        Assert.Equal(a, b); Assert.Equal(a.GetHashCode(), b.GetHashCode()); Assert.NotEqual(a, b with { YPrimitive = 2 });
    }
    [Fact]
    public void RepeatedConversionClearsOldDataAndInvalidatesCachedAccessor()
    {
        var mesh = SmallPlane(); var accessor = mesh.GetAccessor(); Assert.True(accessor.IsValueOn(Coord.Zero));
        var copy = mesh.CopyTree(); mesh.Convert(ReadOnlyMemory<Vec3f>.Empty, ReadOnlyMemory<MeshQuad>.Empty);
        Assert.False(accessor.IsValueOn(Coord.Zero)); Assert.Equal(new MeshToVoxelEdgeData.EdgeData(), accessor.GetValue(Coord.Zero)); Assert.True(copy.IsValueOn(Coord.Zero));
    }
    [Theory, InlineData(0), InlineData(1), InlineData(2), InlineData(3)]
    public void FailedConversionPreservesPreviousResult(int failure)
    {
        var mesh = SmallPlane(); var accessor = mesh.GetAccessor(); var before = accessor.GetValue(Coord.Zero);
        Vec3f[] points = [new(0.25f, -2, -2), new(0.25f, 2, -2), new(0.25f, 0, 2)]; MeshQuad[] faces = [new(0, 1, 2, -1)];
        var options = failure switch
        {
            0 => new MeshToVolumeOptions { MaximumLeaves = 0 },
            1 => new MeshToVolumeOptions { MaximumVoxelEvaluations = 0 },
            2 => new MeshToVolumeOptions { CancellationToken = new(true) },
            _ => new MeshToVolumeOptions()
        };
        if (failure == 3) points[0] = new(float.NaN, 0, 0);
        Assert.ThrowsAny<Exception>(() => mesh.Convert(points, faces, options));
        Assert.True(accessor.IsValueOn(Coord.Zero)); Assert.Equal(before.XDistance, accessor.GetValue(Coord.Zero).XDistance);
    }
    [Fact]
    public void CellEdgeOrderAndAppendSemanticsAreStable()
    {
        var tree = new VdbTree<MeshToVoxelEdgeData.EdgeData>(new());
        var d = new MeshToVoxelEdgeData.EdgeData() { XDistance = 0.25f, YDistance = 0.5f, ZDistance = 0.75f, XPrimitive = 1, YPrimitive = 2, ZPrimitive = 3 };
        foreach (Coord c in new CoordBBox(Coord.Zero, new(1))) tree.SetValueOn(c, d);
        List<Vec3d> points = [new(99)]; List<int> primitives = [99];
        MeshToVoxelEdgeData.GetEdgeData(new(tree), Coord.Zero, points, primitives);
        Assert.Equal(new[] { 99, 1, 2, 3, 2, 3, 2, 1, 2, 1, 1, 3, 3 }, primitives);
        Assert.Equal(13, points.Count); Assert.Equal(new Vec3d(0.25, 0, 0), points[1]); Assert.Equal(new Vec3d(1, 1, 0.75), points[^1]);
        MeshToVoxelEdgeData.GetEdgeData(new(tree), new(100), points, primitives); Assert.Equal(13, points.Count);
    }
    private static MeshToVoxelEdgeData SmallPlane()
    {
        var mesh = new MeshToVoxelEdgeData(); mesh.Convert(new Vec3f[] { new(0.5f, -2, -2), new(0.5f, 2, -2), new(0.5f, 0, 2) }, new MeshQuad[] { new(0, 1, 2, -1) }); return mesh;
    }
    private static Coord ReadCoord(JsonElement c) => new(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt32());
}
