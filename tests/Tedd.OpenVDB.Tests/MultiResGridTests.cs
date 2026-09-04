// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class MultiResGridConformanceTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() =>
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-multires-grid.json.gz"));
        using var gzip = new GZipStream(input, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    });
    public static IEnumerable<object[]> Cases()
    {
        int i = 0; foreach (var c in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray()) yield return [i++, $"{c.GetProperty("type")}/{c.GetProperty("kind")}/scene={c.GetProperty("scene")}/injection={c.GetProperty("injection")}"];
    }
    [Theory, MemberData(nameof(Cases))]
    public void NativeTopologyTransfersSamplingAndFractionalGridsMatch(int index, string label)
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        Assert.NotEmpty(label); var c = Fixture.Value.RootElement.GetProperty("cases")[index];
        if (c.GetProperty("type").GetString() == "float") Run<float>(c); else Run<double>(c);
    }
    private static void Run<T>(JsonElement c) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (c.GetProperty("kind").GetString() == "contract") { Contract<T>(c); return; }
        var input = CreateSource<T>(c.GetProperty("scene").GetInt32()); var multi = new MultiResGrid<T>(3, input, c.GetProperty("injection").GetInt32() != 0);
        Assert.Equal(3L, multi.Metadata.MetaValue<long>("MultiResGrid_Levels")); Assert.Equal("retained", multi.Metadata.MetaValue<string>("custom"));
        if (c.GetProperty("kind").GetString() == "fraction")
        {
            Snapshot(c.GetProperty("grid"), multi.CreateGrid(c.GetProperty("level").GetSingle(), (SamplingOrder)c.GetProperty("order").GetInt32())); return;
        }
        int action = c.GetProperty("action").GetInt32();
        if (action == 1) { multi.GetTree(0).SetValueOn(Coord.Zero, T.CreateTruncating(15)); multi.GetTree(1).SetValueOnly(new(29), T.CreateTruncating(42)); multi.RestrictActiveVoxels(1); }
        if (action == 2) { multi.GetTree(1).SetValueOn(Coord.Zero, T.CreateTruncating(-5)); multi.GetTree(0).SetValueOnly(new(29), T.CreateTruncating(42)); multi.ProlongateActiveVoxels(0); }
        for (int i = 0; i < 3; i++) Snapshot(c.GetProperty("grids")[i], multi.GetGrid(i));
        for (int order = 0; order < 3; order++) foreach (var sample in c.GetProperty("samples")[order].EnumerateArray())
            Equal<T>(Number(sample[4]), multi.SampleValue(new Vec3d(Number(sample[1]), Number(sample[2]), Number(sample[3])), Number(sample[0]), (SamplingOrder)order));
        foreach (var transfer in c.GetProperty("transfers").EnumerateArray())
        {
            Coord p = Coordinate(transfer[0]); Equal<T>(Number(transfer[1]), multi.RestrictVoxel(p, 1)); Equal<T>(Number(transfer[2]), multi.RestrictVoxel(p, 1, true)); Equal<T>(Number(transfer[3]), multi.ProlongateVoxel(p, 0));
        }
    }
    private static void Contract<T>(JsonElement c) where T : unmanaged, IFloatingPointIeee754<T>
    {
        int id = c.GetProperty("scene").GetInt32(); var input = CreateSource<T>(0); string name = string.Empty; bool shared = false, inputEmpty = false;
        var error = Record.Exception(() =>
        {
            if (id == 2) input.SetMetadata("MultiResGrid_Levels", "conflict");
            if (id == 3) input.SetMetadata("MultiResGrid_Level", "conflict");
            if (id == 4) input.SetMetadata("MultiResGrid_Level", 0L);
            if (id == 5) { input.RemoveMetadata("name"); input.SetMetadata("name", 123); }
            if (id >= 6) { input.Tree.SetValueOn(Coord.Zero, T.CreateTruncating(7)); var tree = input.Tree; var m = new MultiResGrid<T>(2, input, stealTree: id == 6); shared = ReferenceEquals(tree, m.FinestTree); inputEmpty = input.IsEmpty; name = m.GetGrid(0).Name; }
            else { var m = new MultiResGrid<T>(id < 2 ? id : 2, input); name = id == 4 ? m.CreateGrid(0).Name : m.GetGrid(0).Name; }
        });
        string expected = c.GetProperty("error").GetString()!;
        if (expected == "TypeError") Assert.IsType<InvalidCastException>(error);
        else if (expected == "ValueError") Assert.IsType<ArgumentOutOfRangeException>(error);
        else Assert.Null(error);
        Assert.Equal(c.GetProperty("name").GetString(), name); Assert.Equal(c.GetProperty("shared").GetInt32() != 0, shared); Assert.Equal(c.GetProperty("inputEmpty").GetInt32() != 0, inputEmpty);
    }
    private static void Snapshot<T>(JsonElement e, Grid<T> grid) where T : unmanaged, IFloatingPointIeee754<T>
    {
        Assert.Equal(e.GetProperty("name").GetString(), grid.Name); Assert.Equal(e.GetProperty("class").GetInt32(), (int)grid.GridClass);
        Equal<T>(Number(e.GetProperty("background")), grid.Background); Assert.Equal(e.GetProperty("activeCount").GetUInt64(), grid.ActiveVoxelCount);
        Assert.Equal(e.GetProperty("leaves").GetInt64(), grid.Tree.LeafCount); Assert.Equal(e.GetProperty("tiles").GetUInt64(), grid.Tree.ActiveTileCount);
        Vec3d origin = grid.Transform.IndexToWorld(Vec3d.Zero), size = grid.Transform.VoxelSize;
        for (int i = 0; i < 3; i++) { Assert.Equal(Number(e.GetProperty("origin")[i]), origin[i], 12); Assert.Equal(Number(e.GetProperty("voxelSize")[i]), size[i], 12); }
        var actual = grid.Tree.EnumerateValues().ToDictionary(v => v.Coordinate); Assert.Equal(e.GetProperty("values").GetArrayLength(), actual.Count);
        foreach (var expected in e.GetProperty("values").EnumerateArray())
        {
            Coord p = Coordinate(expected[0]); Assert.True(actual.TryGetValue(p, out var value), $"Missing active value {p}");
            Equal<T>(Number(expected[1]), value.Value); Assert.Equal(expected[2].GetInt32(), 3 - value.Level);
        }
        foreach (var probe in e.GetProperty("probes").EnumerateArray())
        {
            Coord p = Coordinate(probe[0]); Equal<T>(Number(probe[1]), grid.Tree.GetValue(p)); Assert.Equal(probe[2].GetInt32() != 0, grid.Tree.IsValueOn(p)); Assert.Equal(probe[3].GetInt32(), grid.Tree.GetValueDepth(p));
        }
    }
    internal static Grid<T> CreateSource<T>(int scene) where T : unmanaged, IFloatingPointIeee754<T>
    {
        T N(double value) => T.CreateTruncating(value);
        var g = new Grid<T>(N(scene == 5 ? 1.5 : scene == 4 ? 9 : 0)) { Name = "detail", Transform = Transform.CreateLinearTransform(0.5) }; g.SetMetadata("custom", "retained"); g.Transform.PostTranslate(new(1.25, -2.5, 4));
        var t = g.Tree;
        if (scene == 1) { t.SetValueOn(Coord.Zero, N(64)); t.SetValueOn(new(-3, 1, 5), N(-7)); t.SetValueOnly(new(9, -8, 0), N(21)); }
        if (scene == 2) for (int x = -4; x <= 4; x++) for (int y = -4; y <= 4; y++) for (int z = -4; z <= 4; z++) t.SetValueOn(new(x, y, z), N(0.125 * x * x + 0.0625 * y * z - 0.25 * z + 0.5));
        if (scene == 3) { t.AddTile(1, Coord.Zero, N(2), true); t.AddTile(1, new(-16, 8, 0), N(-1), true); t.SetValueOn(new(17, 2, -5), N(8)); }
        if (scene == 4) { t.TouchLeaf(new(-8)); t.SetValueOnly(new(1, 2, 3), N(-11)); t.AddTile(1, new(16), N(27), false); t.SetValueOn(new(4, 3, 2), N(5)); }
        if (scene == 5) { g.GridClass = GridClass.LevelSet; for (int x = -9; x <= 9; x++) for (int y = -9; y <= 9; y++) for (int z = -9; z <= 9; z++) { double d = 0.5 * System.Math.Sqrt(x * x + y * y + z * z) - 2.7; if (System.Math.Abs(d) < 1.5) t.SetValueOn(new(x, y, z), N(d)); else if (d < 0) t.SetValueOff(new(x, y, z), N(-1.5)); } }
        if (scene == 6) for (int x = -5; x <= 5; x++) for (int y = -3; y <= 3; y++) for (int z = -2; z <= 2; z++) { double d = ((x + 5) * 73 + (y + 3) * 19 + (z + 2) * 11) % 29 - 14; if ((x - y + z) % 3 != 0) t.SetValueOn(new(x, y, z), N(d / 7)); }
        if (scene == 7) { t.SetValueOn(new(-1), N(-0.0)); t.SetValueOn(new(2, 0, 0), N(1e12)); t.SetValueOn(new(-2, 0, 0), N(-1e12)); t.SetValueOn(new(0, 2, 0), N(1.234567890123)); t.SetValueOn(new(0, -2, 0), N(-0.23456789)); }
        return g;
    }
    private static void Equal<T>(double expected, T actual) where T : unmanaged, IFloatingPointIeee754<T>
    {
        double value = double.CreateTruncating(actual); if (double.IsNaN(expected)) { Assert.True(double.IsNaN(value)); return; }
        if (double.IsInfinity(expected)) { Assert.Equal(expected, value); return; }
        double tolerance = typeof(T) == typeof(float) ? 2e-6 : 2e-13;
        Assert.True(System.Math.Abs(expected - value) <= tolerance * System.Math.Max(1, System.Math.Abs(expected)), $"Expected {expected:R}, actual {value:R}.");
    }
    private static Coord Coordinate(JsonElement p) => new(p[0].GetInt32(), p[1].GetInt32(), p[2].GetInt32());
    private static double Number(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : double.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
}

public class MultiResGridTests
{
    [Fact]
    public void ImpulseHasExactTensorProductRestrictionWeights()
    {
        var grid = new Grid<double>(); grid[Coord.Zero] = 64; var m = new MultiResGrid<double>(3, grid);
        Assert.Equal(8, m.GetTree(1).GetValue(Coord.Zero)); Assert.Equal(1UL, m.GetTree(1).ActiveVoxelCount);
        Assert.Equal(1, m.GetTree(2).GetValue(Coord.Zero)); Assert.Equal(64, grid[Coord.Zero]);
        Assert.Equal(8, m.ProlongateVoxel(Coord.Zero, 0)); Assert.Equal(4, m.ProlongateVoxel(new(1, 0, 0), 0)); Assert.Equal(2, m.ProlongateVoxel(new(1, 1, 0), 0)); Assert.Equal(1, m.ProlongateVoxel(new(1, 1, 1), 0));
    }
    [Fact]
    public void OddImpulseActivatesEightCoarseNeighborsWithCornerWeights()
    {
        var grid = new Grid<float>(); grid[new(-1, -1, -1)] = 64; var m = new MultiResGrid<float>(2, grid);
        Assert.Equal(8UL, m.CoarsestTree.ActiveVoxelCount);
        foreach (var (c, v) in m.CoarsestTree.EnumerateActiveVoxels()) { Assert.Equal(1, v); Assert.InRange(c.X, -1, 0); Assert.InRange(c.Y, -1, 0); Assert.InRange(c.Z, -1, 0); }
        var injected = new MultiResGrid<float>(2, grid, true); Assert.True(injected.CoarsestTree.IsEmpty);
    }
    [Fact]
    public void CopiesAndStealsRetainTheNativeOwnershipContracts()
    {
        var grid = MultiResGridConformanceTests.CreateSource<double>(3); var originalTree = grid.Tree; var copy = new MultiResGrid<double>(2, grid);
        Assert.NotSame(originalTree, copy.FinestTree); Assert.Equal(2UL, grid.Tree.ActiveTileCount); Assert.Equal(0UL, copy.FinestTree.ActiveTileCount);
        var stolen = new MultiResGrid<double>(2, grid, stealTree: true); Assert.Same(originalTree, stolen.FinestTree); Assert.True(grid.IsEmpty); Assert.Equal("detail", grid.Name); Assert.Equal(0, grid.Background);
        Assert.NotSame(grid.Transform, stolen.Transform); Assert.Equal(0UL, originalTree.ActiveTileCount);
    }
    [Fact]
    public void IntegerGridSharesTreeWhileFractionalFactoryCopiesIt()
    {
        var m = new MultiResGrid<double>(3, 0); m.FinestTree.SetValueOn(Coord.Zero, 12); var a = m.GetGrid(0); var b = m.CreateGrid(0);
        Assert.Same(m.FinestTree, a.Tree); Assert.NotSame(m.FinestTree, b.Tree); b[Coord.Zero] = 99; Assert.Equal(12, a[Coord.Zero]);
        a[Coord.Zero] = 7; Assert.Equal(7, m.FinestTree[Coord.Zero]); Assert.Equal(0L, a.GetMetadata<long>("MultiResGrid_Level")); Assert.Equal(0f, b.GetMetadata<float>("MultiResGrid_Level"));
        Assert.Equal(3, m.GetGrids().Count);
    }
    [Fact]
    public void GridMetadataAndTransformsAreIndependentCopies()
    {
        var grid = MultiResGridConformanceTests.CreateSource<double>(1); var m = new MultiResGrid<double>(3, grid); var coarse = m.GetGrid(2);
        Assert.Equal("detail_level_2", coarse.Name); Assert.Equal(new Vec3d(2), coarse.Transform.VoxelSize); Assert.Equal(grid.Transform.IndexToWorld(Vec3d.Zero), coarse.IndexToWorld(Vec3d.Zero));
        m.Name = "renamed"; m.GridClass = GridClass.FogVolume; Assert.Equal("detail_level_2", coarse.Name); Assert.Equal(GridClass.Unknown, coarse.GridClass);
        Assert.Equal("renamed_level_1", m.GetGrid(1).Name); Assert.Equal(GridClass.FogVolume, m.GetGrid(1).GridClass); m.ClearGridClass(); Assert.Equal(GridClass.Unknown, m.GridClass);
    }
    [Fact]
    public void ActiveTransfersPreserveInactiveValuesAndActiveTiles()
    {
        var m = new MultiResGrid<double>(3, 3); m.FinestTree.SetValueOn(Coord.Zero, 64); m.GetTree(1).SetValueOn(Coord.Zero, 1);
        m.GetTree(1).SetValueOnly(new(1), 77); m.GetTree(1).AddTile(1, new(16), 88, true); m.RestrictActiveVoxels(1);
        Assert.Equal(77, m.GetTree(1).GetValue(new(1))); Assert.Equal(88, m.GetTree(1).GetValue(new(16))); Assert.Equal(1UL, m.GetTree(1).ActiveTileCount);
    }
    [Fact]
    public void CoordinateConversionsRespectFractionalLevels()
    {
        Assert.Equal(new Vec3d(-2, 4, 8), MultiResGrid<double>.Xyz(new Coord(-1, 2, 4), 2, 1));
        Assert.Equal(new Vec3d(-0.5, 1, 2), MultiResGrid<double>.Xyz(new Vec3d(-1, 2, 4), 0, 1));
        var value = MultiResGrid<double>.Xyz(new Vec3d(2), 0.25, 0.75); Assert.Equal(System.Math.Sqrt(2), value.X, 14);
    }
    [Theory, InlineData(0), InlineData(1), InlineData(32), InlineData(-1)]
    public void InvalidLevelCountsAreRejected(int levels) => Assert.Throws<ArgumentOutOfRangeException>(() => new MultiResGrid<double>(levels, 0));
    [Fact]
    public void InvalidSampleAndTransferLevelsAreRejected()
    {
        var m = new MultiResGrid<float>(2, 0); Assert.Throws<ArgumentOutOfRangeException>(() => m.GetTree(-1)); Assert.Throws<ArgumentOutOfRangeException>(() => m.GetGrid(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.RestrictVoxel(Coord.Zero, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => m.ProlongateVoxel(Coord.Zero, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.CreateGrid(float.NaN)); Assert.Throws<ArgumentOutOfRangeException>(() => m.SampleValue(Coord.Zero, 2.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.SampleValue(Coord.Zero, 0.0, (SamplingOrder)9)); Assert.Throws<OverflowException>(() => m.RestrictVoxel(Coord.MaxValue, 1));
    }
    [Fact]
    public void TileExpansionAndFractionalGrowthHonorBudgets()
    {
        var grid = new Grid<float>(); grid.Tree.AddTile(2, Coord.Zero, 1, true);
        Assert.Throws<InvalidOperationException>(() => new MultiResGrid<float>(2, grid, options: new() { MaximumLeaves = 1 })); Assert.Equal(1UL, grid.Tree.ActiveTileCount);
        Assert.Throws<InvalidOperationException>(() => new MultiResGrid<float>(2, grid, options: new() { MaximumActiveVoxels = 1 }));
        var m = new MultiResGrid<float>(2, 0, options: new() { MaximumActiveVoxels = 1 }); m.CoarsestTree.SetValueOn(Coord.Zero, 1);
        Assert.Throws<InvalidOperationException>(() => m.CreateGrid(0.5f)); Assert.Equal(1UL, m.CoarsestTree.ActiveVoxelCount);
    }
    [Fact]
    public void CanceledConstructionPreservesInputAndPreCanceledTransfersDoNotMutate()
    {
        var cts = new CancellationTokenSource(); var grid = new Grid<double>(); grid[Coord.Zero] = 64; cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new MultiResGrid<double>(2, grid, stealTree: true, options: new() { CancellationToken = cts.Token })); Assert.Equal(64, grid[Coord.Zero]);
        var tokenSource = new CancellationTokenSource(); var m = new MultiResGrid<double>(2, 0, options: new() { CancellationToken = tokenSource.Token }); m.GetTree(1).SetValueOn(Coord.Zero, 5); tokenSource.Cancel();
        Assert.Throws<OperationCanceledException>(() => m.RestrictActiveVoxels(1)); Assert.Equal(5, m.GetTree(1)[Coord.Zero]);
    }
    [Fact]
    public void FractionalTopologyIgnoresManuallyInsertedActiveCoarseTiles()
    {
        var m = new MultiResGrid<double>(2, 0); m.CoarsestTree.AddTile(1, Coord.Zero, 12, true); Assert.True(m.CreateGrid(0.5f).IsEmpty);
    }
}