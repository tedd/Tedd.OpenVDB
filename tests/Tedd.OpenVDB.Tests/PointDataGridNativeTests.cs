// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Points;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class PointDataGridNativeTests
{
    private const string Commit = "f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf";
    private static readonly Lazy<JsonElement> Fixture = new(() =>
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-point-data-grid.json.gz"));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        Assert.Equal(Commit, document.RootElement.GetProperty("upstreamCommit").GetString());
        Assert.Equal(12, document.RootElement.GetProperty("cases").GetArrayLength());
        return document.RootElement.Clone();
    });

    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray()
        .Select((item, index) => new object[] { index, item.GetProperty("scene").GetInt32(),
            item.GetProperty("transform").GetInt32(), item.GetProperty("codec").GetString()! });

    [Theory, MemberData(nameof(Cases))]
    public void TopologyOffsetsActivityOrderingAndDecodedPositionsMatchNative(
        int fixtureIndex, int scene, int transformProfile, string codec)
    {
        JsonElement expected = Fixture.Value.GetProperty("cases")[fixtureIndex];
        Transform transform = CreateTransform(transformProfile);
        var source = new PointArray(WorldPositions(scene, transform));
        PointDataGrid grid = codec switch
        {
            "null" => PointDataGrid.Create(source, transform),
            "fxpt16" => PointDataGrid.Create<FixedPoint16Codec<Vec3f, PositionRange>>(source, transform),
            _ => throw new InvalidDataException($"Unknown native codec '{codec}'.")
        };
        if (expected.GetProperty("mutated").GetInt32() != 0) MutateMask(grid);

        Assert.Equal(expected.GetProperty("treeType").GetString(), grid.NativeTreeType);
        Assert.Equal(typeof(PointDataIndex32), grid.ValueType);
        Assert.Equal(expected.GetProperty("points").GetUInt64(), grid.PointCount);
        Assert.Equal(expected.GetProperty("activeVoxels").GetUInt64(), grid.ActiveVoxelCount);
        Assert.Equal(expected.GetProperty("roots").GetInt32(), grid.Tree.RootEntryCount);
        Assert.Equal(expected.GetProperty("nodes").EnumerateArray().Select(value => value.GetInt64()), grid.Tree.NodeCount());
        Assert.Equal(grid.PointCount, checked(grid.OnPointCount + grid.OffPointCount));
        Assert.Equal(grid.Tree.RootEntryCount == 0, grid.IsEmpty);
        AssertVectorNear(ReadVec3d(expected.GetProperty("voxelSize")), grid.Transform.VoxelSize, 3e-13);
        AssertVectorNear(ReadVec3d(expected.GetProperty("zero")), grid.Transform.IndexToWorld(Vec3d.Zero), 3e-13);

        JsonElement expectedLeaves = expected.GetProperty("leaves");
        Assert.Equal(expectedLeaves.GetArrayLength(), grid.Tree.LeafCount);
        var allExpectedWorld = new List<Vec3d>();
        for (int leafIndex = 0; leafIndex < expectedLeaves.GetArrayLength(); ++leafIndex)
        {
            JsonElement expectedLeaf = expectedLeaves[leafIndex];
            PointDataLeafNode leaf = grid.Tree.Leaves[leafIndex];
            Assert.Equal(ReadCoord(expectedLeaf.GetProperty("origin")), leaf.Origin);
            Assert.Equal(expectedLeaf.GetProperty("points").GetInt32(), leaf.PointCount);
            Assert.Equal(expectedLeaf.GetProperty("onVoxels").GetInt32(), leaf.OnVoxelCount);
            Assert.Equal(expectedLeaf.GetProperty("onPoints").GetInt64(), leaf.OnPointCount);
            Assert.Equal(expectedLeaf.GetProperty("offPoints").GetInt64(), leaf.OffPointCount);
            Assert.Equal(codec, leaf.Descriptor.Type(0).Codec);
            leaf.ValidateOffsets();
            VerifyRuns(expectedLeaf.GetProperty("runs"), leaf);

            uint[] sourceOrder = expectedLeaf.GetProperty("source").EnumerateArray().Select(item => item.GetUInt32()).ToArray();
            Assert.Equal(leaf.PointCount, sourceOrder.Length);
            var positions = Assert.IsAssignableFrom<AttributeArray<Vec3f>>(leaf.GetConstAttributeArray("P"));
            JsonElement local = expectedLeaf.GetProperty("local");
            JsonElement world = expectedLeaf.GetProperty("world");
            for (int point = 0; point < leaf.PointCount; ++point)
            {
                Assert.Equal(ReadVec3f(local[point]), positions.Get(point));
                Vec3d expectedWorld = ReadVec3d(world[point]);
                AssertVectorNear(expectedWorld, grid.GetPosition(leaf, point), RelativeTolerance(expectedWorld));
                allExpectedWorld.Add(expectedWorld);
            }
            uint[] active = leaf.EnumerateIndices(PointDataIndexMode.Active).Select(index => sourceOrder[index]).ToArray();
            uint[] inactive = leaf.EnumerateIndices(PointDataIndexMode.Inactive).Select(index => sourceOrder[index]).ToArray();
            Assert.Equal(expectedLeaf.GetProperty("activeSource").EnumerateArray().Select(item => item.GetUInt32()), active);
            Assert.Equal(expectedLeaf.GetProperty("inactiveSource").EnumerateArray().Select(item => item.GetUInt32()), inactive);
        }
        Vec3d[] actualWorld = grid.EnumeratePositions().ToArray();
        Assert.Equal(allExpectedWorld.Count, actualWorld.Length);
        for (int point = 0; point < actualWorld.Length; ++point)
            AssertVectorNear(allExpectedWorld[point], actualWorld[point], RelativeTolerance(allExpectedWorld[point]));
    }

    private static void VerifyRuns(JsonElement expected, PointDataLeafNode leaf)
    {
        int offset = 0;
        foreach (JsonElement run in expected.EnumerateArray())
        {
            int count = run[0].GetInt32(); uint value = run[1].GetUInt32(); bool active = run[2].GetInt32() != 0;
            for (int index = 0; index < count; ++index, ++offset)
            {
                Assert.Equal(value, leaf.GetValue(offset));
                Assert.Equal(active, leaf.IsValueOn(offset));
            }
        }
        Assert.Equal(512, offset);
    }

    private static void MutateMask(PointDataGrid grid)
    {
        foreach (PointDataLeafNode leaf in grid.Tree.Leaves)
        {
            int occupied = -1, empty = -1;
            for (int offset = 0; offset < 512; ++offset)
            {
                if (!leaf.GetPointRange(offset).IsEmpty && occupied < 0) occupied = offset;
                if (leaf.GetPointRange(offset).IsEmpty && empty < 0) empty = offset;
            }
            if (occupied >= 0) leaf.SetActiveState(occupied, false);
            if (empty >= 0) leaf.SetActiveState(empty, true);
        }
    }

    private static Transform CreateTransform(int profile)
    {
        Transform transform = Transform.CreateLinearTransform(profile == 0 ? 1 : .75);
        if (profile == 1)
        {
            transform.PostTranslate(new Vec3d(.25, -.375, .125));
            transform.PostRotate(.2, Axis.Z);
        }
        return transform;
    }

    private static Vec3d[] WorldPositions(int scene, Transform transform) =>
        IndexPositions(scene).Select(transform.IndexToWorld).ToArray();

    private static IEnumerable<Vec3d> IndexPositions(int scene)
    {
        if (scene == 0) return [];
        if (scene == 1) return
        [
            new Vec3d(-.5), new Vec3d(.5), Vec3d.Zero, Vec3d.Zero,
            new Vec3d(7.499, -7.5, 1.25), new Vec3d(7.5, -8.5, 1.25),
            new Vec3d(-8.5, 7.5, -.5), new Vec3d(-8.499, 8.5, .5),
            new Vec3d(15.5), new Vec3d(-16.5),
            new Vec3d(.499999, -.499999, .25), new Vec3d(.500001, -.500001, -.25)
        ];
        if (scene == 2) return
        [
            new Vec3d(7.499, 0, 0), new Vec3d(7.5, 0, 0), new Vec3d(8.499, 0, 0), new Vec3d(8.5, 0, 0),
            new Vec3d(-7.5, 1, -1), new Vec3d(-8.5, 1, -1),
            new Vec3d(127.5, -127.5, 3), new Vec3d(128.5, -128.5, 3),
            new Vec3d(4095.5, 4, -4), new Vec3d(4096.5, 4, -4),
            new Vec3d(-4095.5, -4, 4), new Vec3d(-4096.5, -4, 4),
            new Vec3d(32767.25, -32768.25, 255.75), new Vec3d(-32768.25, 32767.25, -256.75)
        ];
        var values = new List<Vec3d>(46);
        for (int i = 0; i < 40; ++i)
            values.Add(new Vec3d(((i * 47) % 97 - 48) * 3.125 + i % 3 * .249,
                ((i * 29) % 83 - 41) * 2.375 - i % 5 * .125,
                ((i * 19) % 71 - 35) * 1.625 + i % 7 * .0625));
        values.AddRange([new Vec3d(-.5), new Vec3d(.5), Vec3d.Zero, Vec3d.Zero,
            new Vec3d(4095.5, -128.5, 7.5), new Vec3d(-4096.5, 128.5, -8.5)]);
        return values;
    }

    private static Coord ReadCoord(JsonElement value) => new(value[0].GetInt32(), value[1].GetInt32(), value[2].GetInt32());
    private static Vec3f ReadVec3f(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
    private static Vec3d ReadVec3d(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static double RelativeTolerance(Vec3d value) => 5e-12 * System.Math.Max(1, value.Length);
    private static void AssertVectorNear(Vec3d expected, Vec3d actual, double tolerance)
    {
        Assert.True((expected - actual).Length <= tolerance,
            $"expected {expected}, actual {actual}, distance {(expected - actual).Length}, tolerance {tolerance}");
    }
}
