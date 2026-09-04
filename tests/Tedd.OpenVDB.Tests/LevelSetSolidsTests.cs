using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetSolidsTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(20)]
    public void NamedSolidsContainTheirCenterAndHaveFiniteNarrowBands(int faces)
    {
        Vec3f center = new(1, 0, 0);
        Grid<float> grid = faces switch
        {
            4 => LevelSetPlatonic.CreateTetrahedron<float>(4, center, .5f, 2),
            6 => LevelSetPlatonic.CreateCube<float>(4, center, .5f, 2),
            8 => LevelSetPlatonic.CreateOctahedron<float>(4, center, .5f, 2),
            12 => LevelSetPlatonic.CreateDodecahedron<float>(4, center, .5f, 2),
            _ => LevelSetPlatonic.CreateIcosahedron<float>(4, center, .5f, 2)
        };
        Assert.True(grid.Tree.GetValue(new(2, 0, 0)) < 0); Assert.Equal(1, grid.Background); Assert.Equal(GridClass.LevelSet, grid.GridClass); Assert.True(grid.ActiveVoxelCount > 0);
        foreach (var leaf in grid.Tree.EnumerateLeaves()) foreach (int offset in leaf.ActiveOffsets) { float value = leaf.GetValue(offset); Assert.True(float.IsFinite(value)); Assert.InRange(value, -1, 1); }
    }
    [Fact]
    public void CubeScaleIsItsWorldSpaceSideLength()
    {
        var grid = LevelSetPlatonic.CreateCube<double>(4, voxelSize: 1, halfWidth: 2);
        foreach (Coord face in new Coord[] { new(2, 0, 0), new(-2, 0, 0), new(0, 2, 0), new(0, -2, 0), new(0, 0, 2), new(0, 0, -2) }) Assert.InRange(System.Math.Abs(grid.Tree.GetValue(face)), 0, 1e-5);
        Assert.True(grid.Tree.GetValue(Coord.Zero) < 0); Assert.True(grid.Tree.GetValue(new(3, 0, 0)) > 0);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(16)]
    public void InvalidFaceCountsAreRejected(int faces) => Assert.Throws<ArgumentOutOfRangeException>(() => LevelSetPlatonic.Create<float>(faces));
    [Fact]
    public void PlatonicHalfMatchesNativeUnsupportedTypeContract() => Assert.Throws<NotSupportedException>(() => LevelSetPlatonic.Create<Half>(6));
    [Fact]
    public void PlatonicCancellationAndGeometricBudgetAreObserved()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => LevelSetPlatonic.Create<float>(6, options: new() { CancellationToken = cancellation.Token }));
        Assert.Throws<InvalidOperationException>(() => LevelSetPlatonic.Create<float>(6, options: new() { MaximumVoxelEvaluations = 0 }));
    }
    [Fact]
    public void TopologyConversionIgnoresValuesForUnmanagedBlockRecords()
    {
        var integers = new Grid<ushort>(99); var records = new Grid<Block>(new Block(42, 7)); var boolean = new Grid<bool>(true); CoordBBox box = new(new(-3, -2, -1), new(3, 2, 1));
        integers.Tree.Fill(box, 0, true); records.Tree.Fill(box, new(7, 91), true); boolean.Tree.Fill(box, false, true);
        var expected = TopologyToLevelSet.Create(integers, 2, 0);
        GridTransformerTests.EqualTrees(expected.Tree, TopologyToLevelSet.Create(records, 2, 0).Tree);
        GridTransformerTests.EqualTrees(expected.Tree, TopologyToLevelSet.Create(boolean, 2, 0).Tree);
        Assert.Equal(new Block(7, 91), records.Tree.GetValue(Coord.Zero)); Assert.False(boolean.Tree.GetValue(Coord.Zero)); Assert.True(boolean.Tree.IsValueOn(Coord.Zero));
    }
    [Fact]
    public void NativeNegativeCountsClampToWidthOneAndZeroOperations()
    {
        var input = LevelSetSolidsConformanceTests.Shape<int>(2); var clamped = TopologyToLevelSet.Create(input, 0, -2, -3, -4); var explicitCounts = TopologyToLevelSet.Create(input, 1, 0, 0, 0);
        GridTransformerTests.EqualTrees(explicitCounts.Tree, clamped.Tree);
    }
    [Fact]
    public void ClosingFillsTheInteriorOfASingleVoxelGap()
    {
        var input = LevelSetSolidsConformanceTests.Shape<int>(2);
        var gap = TopologyToLevelSet.Create(input, 1, 0); var closed = TopologyToLevelSet.Create(input, 1, 1);
        Assert.True(gap.Tree.GetValue(Coord.Zero) > 0); Assert.True(closed.Tree.GetValue(Coord.Zero) < 0);
    }
    [Fact]
    public void DilationMovesTheInterfaceByOneIndexVoxel()
    {
        var input = new Grid<int>(); input.Tree.Fill(new(new(-3), new(3)), 7, true);
        var original = TopologyToLevelSet.Create(input, 1, 0); var expanded = TopologyToLevelSet.Create(input, 1, 0, 1);
        Assert.True(original.Tree.GetValue(new(4, 0, 0)) > 0); Assert.True(expanded.Tree.GetValue(new(4, 0, 0)) < 0);
    }
    [Fact]
    public void EmptyTopologyRetainsOnlyRequestedBackgroundAndCopiedTransform()
    {
        var input = LevelSetSolidsConformanceTests.Shape<int>(4); input.Transform = Transform.CreateLinearTransform(.75);
        var output = TopologyToLevelSet.Create(input); Assert.True(output.IsEmpty); Assert.Equal(2.25f, output.Background); Assert.Equal(GridClass.LevelSet, output.GridClass); Assert.Empty(output.Name); Assert.NotSame(input.Transform, output.Transform);
        Assert.Equal(-1, input.Tree.GetValue(Coord.Zero)); Assert.True(input.Transform.IsEquivalentTo(output.Transform));
    }
    [Fact]
    public void SourceAliasesAndMetadataRemainIndependentAfterSmoothing()
    {
        var input = LevelSetSolidsConformanceTests.Shape<int>(2); input.SetMetadata("user", "input metadata"); var before = input.DeepCopy(); var accessor = input.GetAccessor(); var alias = new Grid<int>(input.Tree);
        var output = TopologyToLevelSet.Create(input, 2, 1, 0, 2); Assert.NotSame(input.Transform, output.Transform); Assert.False(output.Metadata.ContainsKey("user"));
        GridTransformerTests.EqualTrees(before.Tree, input.Tree); GridTransformerTests.EqualTrees(before.Tree, alias.Tree); Assert.Equal(before.Tree.GetValue(Coord.Zero), accessor.GetValue(Coord.Zero));
        input.Transform.PostTranslate(new Vec3d(10)); Assert.True(before.Transform.IsEquivalentTo(output.Transform));
    }
    [Fact]
    public void UniformScalingScalesDistanceButNotTopology()
    {
        var input = LevelSetSolidsConformanceTests.Shape<int>(1); var scaled = input.DeepCopy(); scaled.Transform = Transform.CreateLinearTransform(.75);
        var original = TopologyToLevelSet.Create(input, 2, 0); var output = TopologyToLevelSet.Create(scaled, 2, 0); Assert.True(original.Tree.HasSameTopology(output.Tree));
        foreach (var leaf in original.Tree.EnumerateLeaves()) for (int offset = 0; offset < 512; ++offset) Assert.InRange(System.Math.Abs(.75f * leaf.GetValue(offset) - output.Tree.GetValue(leaf.OffsetToGlobalCoord(offset))), 0, 1e-6);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonuniformAndFrustumVoxelsAreRejected(bool frustum)
    {
        var input = new Grid<int>(); input.Transform = frustum ? Transform.CreateFrustumTransform(new BBoxd(new Vec3d(-8), new Vec3d(8)), .75, 12) : new Transform(Mat4d.CreateScale(new Vec3d(1, 2, 1)));
        Assert.Throws<ArgumentException>(() => TopologyToLevelSet.Create(input));
    }
    [Fact]
    public void TopologyPrecancellationAndVoxelizationLimitLeaveInputUntouched()
    {
        var input = new Grid<int>(); input.Tree.AddTile(1, Coord.Zero, 1, true); var before = input.DeepCopy();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => TopologyToLevelSet.Create(input, cancellationToken: cancellation.Token));
        Assert.Throws<InvalidOperationException>(() => TopologyToLevelSet.Create(input, 5, 0, maximumNewLeaves: 0));
        GridTransformerTests.EqualTrees(before.Tree, input.Tree);
    }
    [Fact]
    public void OverflowingIterationCountsFailBeforeCopyingTopology()
    {
        var input = new Grid<int>(); Assert.Throws<OverflowException>(() => TopologyToLevelSet.Create(input, int.MaxValue)); Assert.Throws<OverflowException>(() => TopologyToLevelSet.Create(input, 1, int.MaxValue, 1)); Assert.True(input.IsEmpty);
    }
    private readonly record struct Block(ushort Id, int Flags);
}
