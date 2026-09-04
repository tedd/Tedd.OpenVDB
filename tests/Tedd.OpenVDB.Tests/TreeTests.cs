using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public class TreeTests
{
    public static IEnumerable<object[]> Coordinates()
    {
        foreach (int n in new[] { int.MinValue, -4097, -4096, -129, -128, -9, -8, -1, 0, 7, 8, 127, 128, 4095, 4096, int.MaxValue })
            yield return new object[] { n };
    }

    [Theory, MemberData(nameof(Coordinates))]
    public void PointRoundtripAcrossEveryBoundary(int n)
    {
        var tree = new VdbTree<int>(-12);
        Coord c = new(n, n, n);
        Assert.Equal(-1, tree.GetValueDepth(c));
        Assert.Equal(-12, tree.GetValue(c));
        Assert.False(tree.IsValueOn(c));
        tree.SetValueOnly(c, 42);
        Assert.False(tree.ProbeValue(c, out int value));
        Assert.Equal(42, value);
        Assert.Equal(3, tree.GetValueDepth(c));
        tree.SetValueOn(c);
        Assert.Equal(1UL, tree.ActiveVoxelCount);
        Assert.Equal(1, tree.LeafCount);
        Assert.True(tree.TryGetActiveVoxelBoundingBox(out var box));
        Assert.Equal(new CoordBBox(c, c), box);
        tree.SetValueOff(c);
        Assert.Equal(42, tree.GetValue(c));
        Assert.Equal(0UL, tree.ActiveVoxelCount);
        tree.SetValueOff(c, -12);
        tree.Prune();
        Assert.Equal(0, tree.RootEntryCount);
    }

    [Theory]
    [InlineData(0, 1UL, 3)]
    [InlineData(1, 512UL, 2)]
    [InlineData(2, 2097152UL, 1)]
    [InlineData(3, 68719476736UL, 0)]
    public void TilesRepresentCorrectVolumes(int level, ulong volume, int depth)
    {
        var tree = new VdbTree<int>(-1);
        Coord c = new(-1, -1, -1);
        tree.AddTile(level, c, 7, true);
        Assert.Equal(volume, tree.ActiveVoxelCount);
        Assert.Equal(depth, tree.GetValueDepth(c));
        Assert.Equal(level == 0 ? 1 : 0, tree.LeafCount);
        tree.SetValueOn(c, 8);
        Assert.Equal(volume, tree.ActiveVoxelCount);
        Assert.Equal(8, tree.GetValue(c));
        Assert.Equal(1, tree.LeafCount);
        if (level > 0) Assert.Equal(7, tree.GetValue(new(-2, -1, -1)));
        tree.SetValueOff(c);
        Assert.Equal(volume - 1, tree.ActiveVoxelCount);
        Assert.Equal(8, tree.GetValue(c));
    }

    [Fact]
    public void NoOpWritesMatchUpstreamRootAllocation()
    {
        var tree = new VdbTree<int>(5);
        Coord c = default;
        tree.SetValueOff(c, 5); tree.SetValueOff(c); tree.SetActiveState(c, false);
        Assert.Equal(-1, tree.GetValueDepth(c));
        Assert.Equal(0, tree.RootEntryCount);
        tree.SetValueOnly(c, 5);
        Assert.Equal(1, tree.GetValueDepth(c));
        Assert.Equal(0, tree.LeafCount);
        tree.AddTile(3, c, 9, true);
        tree.SetValueOn(c, 9); tree.SetValueOnly(c, 9); tree.SetActiveState(c, true);
        Assert.Equal(0, tree.GetValueDepth(c));
        Assert.Equal(0, tree.LeafCount);
    }

    [Fact]
    public void InactiveTileSubdividesWithoutActivatingSiblings()
    {
        var tree = new VdbTree<int>();
        tree.AddTile(3, default, 19, false);
        tree.SetValueOn(new(20, 40, 60), 99);
        Assert.Equal(1UL, tree.ActiveVoxelCount);
        Assert.Equal(19, tree.GetValue(new(20, 40, 59)));
        Assert.Equal(19, tree.GetValue(new(128, 128, 128)));
        Assert.False(tree.IsValueOn(new(20, 40, 59)));
    }

    [Fact]
    public void PruneCollapsesUniformNodesAndKeepsNonBackgroundInactiveTiles()
    {
        var tree = new VdbTree<int>();
        tree.AddTile(3, default, 9, true);
        tree.SetValueOn(new(1, 2, 3), 10);
        tree.SetValueOn(new(1, 2, 3), 9);
        tree.Prune();
        Assert.Equal(0, tree.LeafCount);
        Assert.Equal(0, tree.GetValueDepth(new(1, 2, 3)));
        tree.SetActiveState(new CoordBBox(new(0, 0, 0), new(4095, 4095, 4095)), false);
        tree.Prune();
        Assert.Equal(1, tree.RootEntryCount);
        Assert.Equal(9, tree.GetValue(default));
    }

    [Theory]
    [InlineData(-9, 8)]
    [InlineData(120, 136)]
    [InlineData(4090, 4101)]
    [InlineData(int.MaxValue - 3, int.MaxValue)]
    [InlineData(int.MinValue, int.MinValue + 3)]
    public void InclusiveFillCrossesBoundaries(int start, int end)
    {
        var box = new CoordBBox(new(start, start, start), new(end, end, end));
        var tree = new VdbTree<int>(-1);
        tree.Fill(box, 3);
        Assert.Equal(box.Volume, tree.ActiveVoxelCount);
        Assert.True(tree.TryGetActiveVoxelBoundingBox(out var actual));
        Assert.Equal(box, actual);
        foreach (var c in box) { Assert.True(tree.IsValueOn(c)); Assert.Equal(3, tree.GetValue(c)); }
        tree.Fill(box, 4, false);
        Assert.Equal(0UL, tree.ActiveVoxelCount);
        Assert.Equal(4, tree.GetValue(box.Min));
    }

    [Fact]
    public void WholeRootFillUsesOneTileWithoutVoxelAllocation()
    {
        var tree = new VdbTree<byte>();
        tree.Fill(new(new(-4096, -4096, -4096), new(-1, -1, -1)), 2);
        Assert.Equal(1, tree.RootEntryCount);
        Assert.Equal(0, tree.LeafCount);
        Assert.Single(tree.EnumerateValues());
        Assert.Equal(68719476736UL, tree.ActiveVoxelCount);
    }

    [Fact]
    public void CopyAndClearDoNotAlias()
    {
        var tree = new VdbTree<int>(-1);
        tree.AddTile(2, default, 8, false);
        tree.SetValueOn(new(7, 8, 9), 10);
        var copy = tree.DeepCopy();
        copy.SetValueOnly(new(7, 8, 9), 20);
        Assert.Equal(10, tree.GetValue(new(7, 8, 9)));
        tree.Clear();
        Assert.Equal(-1, tree.GetValue(default));
        Assert.Equal(8, copy.GetValue(default));
        Assert.Equal(1UL, copy.ActiveVoxelCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AccessorInvalidatesAfterStructuralReplacement(int operation)
    {
        var tree = new VdbTree<int>();
        var accessor = tree.GetAccessor();
        Coord c = new(1, 2, 3);
        accessor.SetValueOn(c, 7);
        Assert.Equal(7, accessor.GetValue(c));
        switch (operation)
        {
            case 0: tree.Clear(); break;
            case 1: tree.AddTile(3, c, 0, false); break;
            case 2: tree.Fill(new(new(0, 0, 0), new(7, 7, 7)), 0, false); break;
            case 3: tree.SetValueOff(c, 0); tree.Prune(); break;
            case 4: tree.Clip(CoordBBox.Empty); break;
        }
        Assert.Equal(0, accessor.GetValue(c));
        accessor.SetValueOn(c, 11);
        Assert.Equal(11, tree.GetValue(c));
        Assert.True(tree.IsValueOn(c));
    }

    [Fact]
    public void DenseSpansRespectZContiguousOrderAndCheckBeforeMutation()
    {
        var tree = new VdbTree<int>();
        var box = new CoordBBox(new(-2, -3, -4), new(2, 3, 4));
        var expected = Enumerable.Range(0, (int)box.Volume).ToArray();
        tree.CopyFromDense(box, expected);
        int[] actual = new int[expected.Length];
        tree.CopyToDense(box, actual);
        Assert.Equal(expected, actual);
        Assert.Throws<ArgumentException>(() => tree.CopyFromDense(box, new int[expected.Length - 1]));
        tree.CopyToDense(box, actual);
        Assert.Equal(expected, actual);
        Assert.Throws<ArgumentException>(() => tree.CopyToDense(box, new int[expected.Length + 1]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(491)]
    public void RandomizedMutationsMatchIndependentDenseModel(int seed)
    {
        var tree = new VdbTree<int>(-10);
        var model = new Dictionary<Coord, (int Value, bool Active)>();
        var random = new Random(seed);
        for (int i = 0; i < 2500; i++)
        {
            Coord c = new(random.Next(-15, 16), random.Next(-15, 16), random.Next(-15, 16));
            int value = random.Next(-2, 3);
            var old = model.GetValueOrDefault(c, (Value: -10, Active: false));
            switch (random.Next(5))
            {
                case 0: tree.SetValueOn(c, value); model[c] = (value, true); break;
                case 1: tree.SetValueOff(c, value); model[c] = (value, false); break;
                case 2: tree.SetValueOnly(c, value); model[c] = (value, old.Active); break;
                case 3: tree.SetActiveState(c, true); model[c] = (old.Value, true); break;
                default: tree.SetActiveState(c, false); model[c] = (old.Value, false); break;
            }
            if (i % 137 == 0) tree.Prune();
        }
        foreach (var (c, expected) in model)
        {
            Assert.Equal(expected.Value, tree.GetValue(c));
            Assert.Equal(expected.Active, tree.IsValueOn(c));
        }
        Assert.Equal((ulong)model.Count(p => p.Value.Active), tree.ActiveVoxelCount);
        var expectedBox = CoordBBox.Empty;
        foreach (var item in model.Where(p => p.Value.Active)) expectedBox = expectedBox.Expand(item.Key);
        Assert.True(tree.TryGetActiveVoxelBoundingBox(out var actualBox));
        Assert.Equal(expectedBox, actualBox);
    }

    [Fact]
    public void ClipPreservesInactiveValuesInsideAndResetsOutside()
    {
        var tree = new VdbTree<int>(-1);
        tree.AddTile(2, default, 8, true);
        tree.SetValueOff(new(3, 3, 3), 99);
        var box = new CoordBBox(new(2, 2, 2), new(5, 5, 5));
        tree.Clip(box);
        Assert.Equal(box.Volume - 1, tree.ActiveVoxelCount);
        Assert.Equal(99, tree.GetValue(new(3, 3, 3)));
        Assert.False(tree.IsValueOn(new(3, 3, 3)));
        Assert.Equal(-1, tree.GetValue(new(1, 2, 3)));
    }
}
