// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class ActiveValuesNativeTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-active-values.json.gz"));
        using var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    });
    private static readonly Lazy<VdbTree<int>[]> Trees = new(() => Enumerable.Range(0, 7).Select(CreateTree).ToArray());
    public static IEnumerable<object[]> Cases()
    {
        int i = 0; foreach (var c in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray()) yield return [i++, $"scene={c.GetProperty("scene")}, bounds={c.GetProperty("bounds").GetRawText()}"];
    }
    [Theory, MemberData(nameof(Cases))]
    public void NativeRegionalQueriesCountsAndTileOrderMatch(int index, string label)
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        var c = Fixture.Value.RootElement.GetProperty("cases")[index]; var tree = Trees.Value[c.GetProperty("scene").GetInt32()]; var bounds = Box(c.GetProperty("bounds")); var finder = new FindActiveValues<int>(tree);
        Assert.Equal(c.GetProperty("any").GetInt32() != 0, finder.AnyActiveValues(bounds));
        Assert.Equal(c.GetProperty("cachedAny").GetInt32() != 0, finder.AnyActiveValues(bounds, true));
        Assert.Equal(c.GetProperty("voxels").GetInt32() != 0, finder.AnyActiveVoxels(bounds));
        Assert.Equal(c.GetProperty("tiles").GetInt32() != 0, finder.AnyActiveTiles(bounds));
        Assert.Equal(c.GetProperty("none").GetInt32() != 0, finder.NoActiveValues(bounds));
        Assert.True(c.GetProperty("nativeCachedCount").GetUInt64() == finder.Count(bounds), $"{label}: cached native={c.GetProperty("nativeCachedCount")}, managed={finder.Count(bounds)}");
        Assert.Equal(c.GetProperty("count").GetUInt64(), finder.CountActiveValues(bounds));
        Assert.Equal(c.GetProperty("leafCount").GetUInt64(), finder.CountActiveLeafVoxels(bounds));
        Assert.Equal(c.GetProperty("count").GetUInt64(), Count.ActiveVoxels(tree, bounds));
        Assert.Equal(c.GetProperty("leafCount").GetUInt64(), Count.ActiveLeafVoxels(tree, bounds));
        var totals = c.GetProperty("totals");
        Assert.Equal(totals[0].GetUInt64(), Count.ActiveVoxels(tree)); Assert.Equal(totals[1].GetUInt64(), Count.ActiveLeafVoxels(tree));
        Assert.Equal(totals[2].GetUInt64(), Count.InactiveVoxels(tree)); Assert.Equal(totals[3].GetUInt64(), Count.InactiveLeafVoxels(tree)); Assert.Equal(totals[4].GetUInt64(), Count.ActiveTiles(tree));
        var extrema = Count.MinMax(tree); Assert.Equal(totals[5].GetInt32(), extrema.Minimum); Assert.Equal(totals[6].GetInt32(), extrema.Maximum);
        var tiles = finder.ActiveTiles(bounds); Assert.Equal(c.GetProperty("activeTiles").GetArrayLength(), tiles.Count); int tileIndex = 0;
        foreach (var t in c.GetProperty("activeTiles").EnumerateArray())
            Assert.Equal(new TileData<int>(Box(t.GetProperty("bounds")), t.GetProperty("value").GetInt32(), t.GetProperty("level").GetInt32(), t.GetProperty("active").GetInt32() != 0), tiles[tileIndex++]);
    }
    internal static VdbTree<int> CreateTree(int scene)
    {
        var tree = new VdbTree<int>(23);
        if (scene == 1) { tree.SetValueOn(new(0), 1); tree.SetValueOn(new(2), 2); tree.SetValueOn(new(7), 3); tree.SetValueOff(new(4), 40); }
        if (scene == 2) { tree.AddLeaf(new(new(0), 7, true)); tree.SetValueOff(new(3), -10); }
        if (scene == 3)
        {
            tree.AddTile(3, new(-4096, 0, 0), -5, true); tree.AddTile(3, new(4096, 0, 0), -2, false); tree.AddTile(2, new(0), 8, true); tree.AddTile(1, new(128, 0, 0), 9, true);
            tree.SetValueOn(new(256, 0, 0), 10); tree.SetValueOn(new(256, 2, 2), 11); tree.SetValueOn(new(8192, 7, 7), 12);
        }
        if (scene == 4) { tree.AddTile(3, new(0), 7, true); tree.SetValueOff(new(0), -10); }
        if (scene == 5) { tree.Fill(new(new(-11, -9, -7), new(15, 17, 19)), 4); tree.Fill(new(new(-2), new(3)), 0, false); tree.AddTile(1, new(32), -20, false); }
        if (scene == 6)
        {
            for (int i = 0; i < 256; i++) tree.SetValueOn(new((i * 47) % 313 - 150, (i * 13) % 101 - 50, (i * 31) % 191 - 90), i - 100);
            tree.AddTile(2, new(256), 51, true); tree.AddTile(1, new(-256), 52, true); tree.AddTile(3, new(-8192), 53, true);
        }
        return tree;
    }
    private static Coord ReadCoord(JsonElement c) => new(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt32());
    private static CoordBBox Box(JsonElement b) => new(ReadCoord(b[0]), ReadCoord(b[1]));
}

public class ActiveValuesTests
{
    [Fact]
    public void LeafAndTileActivityAreDistinctFromStoredValues()
    {
        var tree = new VdbTree<int>(9); tree.AddTile(1, new(0), 0, true); tree.SetValueOn(new(16), 0); tree.SetValueOff(new(24), 22);
        var finder = new FindActiveValues<int>(tree);
        Assert.True(finder.AnyActiveTiles(new(new(0), new(7)))); Assert.False(finder.AnyActiveVoxels(new(new(0), new(7))));
        Assert.True(finder.AnyActiveVoxels(new(new(16), new(23)))); Assert.False(finder.AnyActiveTiles(new(new(16), new(23))));
        Assert.True(finder.NoActiveValues(new(new(24), new(31)))); Assert.Equal(513UL, finder.CountActiveValues(new(new(0), new(31))));
    }
    [Fact]
    public void AccurateAndNativeCachedCountersExposeTheirDocumentedDifference()
    {
        var tree = ActiveValuesNativeTests.CreateTree(1); var finder = new FindActiveValues<int>(tree); var emptyCell = new CoordBBox(new(1), new(1));
        Assert.Equal(3UL, finder.Count(emptyCell)); Assert.Equal(0UL, finder.CountActiveValues(emptyCell)); Assert.Equal(0UL, Count.ActiveVoxels(tree, emptyCell));
        Assert.False(finder.AnyActiveValues(emptyCell));
    }
    [Fact]
    public void AccurateLargeTileCountDoesNotNarrowThroughInt32()
    {
        var finder = new FindActiveValues<int>(ActiveValuesNativeTests.CreateTree(4)); var region = new CoordBBox(new(0), new(4095));
        Assert.Equal((1UL << 36) - 1, finder.CountActiveValues(region)); Assert.Equal(ulong.MaxValue, finder.Count(region)); Assert.Equal(511UL, finder.CountActiveLeafVoxels(region));
    }
    [Fact]
    public void EmptyAndReversedBoxesNeverQueryTheirMidpoints()
    {
        var tree = new VdbTree<int>(); tree.SetValueOn(new(-1), 1); tree.SetValueOn(new(3), 1); var finder = new FindActiveValues<int>(tree);
        foreach (var region in new[] { CoordBBox.Empty, new CoordBBox(new(7), new(0)) })
        {
            Assert.False(finder.AnyActiveValues(region)); Assert.False(finder.AnyActiveValues(region, true)); Assert.True(finder.NoActiveValues(region));
            Assert.False(finder.AnyActiveVoxels(region)); Assert.False(finder.AnyActiveTiles(region)); Assert.Equal(0UL, finder.Count(region)); Assert.Empty(finder.ActiveTiles(region));
        }
    }
    [Fact]
    public void FullCoordinateDomainAndEndpointMidpointsAreSafe()
    {
        var tree = new VdbTree<int>(); tree.SetValueOn(Coord.MinValue, 1); tree.SetValueOn(Coord.MaxValue, 2); var finder = new FindActiveValues<int>(tree);
        Assert.Equal(2UL, finder.CountActiveValues(CoordBBox.Infinite));
        Assert.True(finder.AnyActiveValues(new(Coord.MaxValue, Coord.MaxValue), true)); Assert.True(finder.AnyActiveValues(new(Coord.MinValue, Coord.MinValue)));
        Assert.False(finder.AnyActiveValues(new(new(int.MaxValue - 20), new(int.MaxValue - 10))));
    }
    [Fact]
    public void StructuralChangesRequireUpdateAndRetargetingReleasesOldTopology()
    {
        var tree = new VdbTree<int>(); tree.SetValueOn(Coord.Zero, 1); var finder = new FindActiveValues<int>(tree);
        tree.AddTile(1, new(16), 4, true); Assert.Throws<InvalidOperationException>(() => finder.CountActiveValues(CoordBBox.Infinite));
        finder.Update(tree); Assert.Equal(513UL, finder.CountActiveValues(CoordBBox.Infinite));
        finder.Update(new VdbTree<int>(2)); Assert.True(finder.NoActiveValues(CoordBBox.Infinite));
    }
    [Fact]
    public void ActiveTileBoundsAreTrimmedAndReturnedValuesAreCopies()
    {
        var tree = new VdbTree<int>(); tree.AddTile(2, new(-128), 7, true); var finder = new FindActiveValues<int>(tree);
        var bounds = new CoordBBox(new(-100, -75, -50), new(-98, -73, -48)); var tiles = finder.ActiveTiles(bounds);
        Assert.Equal(new TileData<int>(bounds, 7, 2), Assert.Single(tiles));
        tiles[0] = new(CoordBBox.Empty, 0, 0, false); Assert.Equal(7, Assert.Single(finder.ActiveTiles(bounds)).Value);
    }
    [Fact]
    public void WarmCachedQueriesAllocateNothingAndSupportIndependentParallelReads()
    {
        var tree = ActiveValuesNativeTests.CreateTree(3); var finder = new FindActiveValues<int>(tree); var region = new CoordBBox(new(0), new(130));
        for (int i = 0; i < 100; i++) { finder.AnyActiveValues(region); finder.CountActiveValues(region); finder.AnyActiveTiles(region); finder.AnyActiveVoxels(region); }
        long before = GC.GetAllocatedBytesForCurrentThread(); ulong result = 0;
        for (int i = 0; i < 1000; i++) { result += finder.CountActiveValues(region); finder.AnyActiveValues(region); finder.AnyActiveTiles(region); finder.AnyActiveVoxels(region); }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.True(result > 0); Assert.Equal(0, allocated);
        ulong expected = finder.CountActiveValues(region); Parallel.For(0, 128, _ => Assert.Equal(expected, finder.CountActiveValues(region)));
    }
    [Theory, InlineData(1), InlineData(7), InlineData(17)]
    public void RandomSmallRegionsMatchDirectVoxelEnumeration(int seed)
    {
        var random = new Random(seed); var tree = new VdbTree<int>();
        tree.AddTile(1, new(0), 0, true);
        for (int i = 0; i < 500; i++) { Coord c = new(random.Next(-16, 17), random.Next(-16, 17), random.Next(-16, 17)); tree.SetValueOn(c, random.Next()); }
        var finder = new FindActiveValues<int>(tree);
        for (int trial = 0; trial < 100; trial++)
        {
            Coord min = new(random.Next(-20, 20), random.Next(-20, 20), random.Next(-20, 20));
            var region = CoordBBox.CreateCube(min, random.Next(1, 8)); ulong expected = 0; foreach (var c in region) if (tree.IsValueOn(c)) expected++;
            Assert.Equal(expected, finder.CountActiveValues(region)); Assert.Equal(expected != 0, finder.AnyActiveValues(region));
        }
    }
    [Fact]
    public void EmptyExtremaIgnoreNonzeroBackgroundAndActiveNanRetainsNativeOrdering()
    {
        var empty = Count.MinMax(new VdbTree<double>(42)); Assert.Equal(0, empty.Minimum); Assert.Equal(0, empty.Maximum);
        var tree = new VdbTree<double>(); tree.SetValueOn(Coord.Zero, double.NaN); tree.SetValueOn(new(1), 2);
        var nan = Count.MinMax(tree); Assert.True(double.IsNaN(nan.Minimum)); Assert.True(double.IsNaN(nan.Maximum));
    }
}
