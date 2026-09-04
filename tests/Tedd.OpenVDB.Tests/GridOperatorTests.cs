using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public class GridOperatorTests
{
    [Fact]
    public void MaskIntersectsActivityInIndexSpaceAndDoesNotAlterSource()
    {
        var grid = new Grid<double>(2) { Name = "source", GridClass = GridClass.LevelSet, Transform = Transform.CreateLinearTransform(.5) };
        grid.Tree.AddTile(1, Coord.Zero, 4, true); var mask = new MaskGrid(); mask.SetValueOn(new(0, 0, 0)); mask.SetValueOn(new(7, 7, 7)); mask.SetValueOn(new(8, 0, 0)); mask.Transform = Transform.CreateLinearTransform(100);
        var output = GridOperators.Gradient(grid, mask, new() { Threaded = false });
        Assert.Equal(2UL, output.ActiveVoxelCount); Assert.True(output.Tree.IsValueOn(Coord.Zero)); Assert.True(output.Tree.IsValueOn(new(7, 7, 7))); Assert.False(output.Tree.IsValueOn(new(8, 0, 0)));
        Assert.Equal(512UL, grid.ActiveVoxelCount); Assert.Equal(0, grid.Tree.LeafCount); Assert.Equal(1UL, grid.Tree.ActiveTileCount);
        Assert.Equal(string.Empty, output.Name); Assert.Equal(GridClass.Unknown, output.GridClass); Assert.Equal("covariant", output.GetMetadata<string>("vector_type"));
        Assert.NotSame(grid.Transform, output.Transform); grid.Transform.PostScale(4); Assert.Equal(new Vec3d(.5), output.Transform.VoxelSize);
    }
    [Fact]
    public void DensificationBudgetAndCancellationLeaveSourceUnchanged()
    {
        var grid = new Grid<double>(); grid.Tree.AddTile(2, Coord.Zero, 1, true);
        Assert.Throws<InvalidOperationException>(() => GridOperators.Gradient(grid, options: new() { MaximumNewLeaves = 1, Threaded = false }));
        Assert.Equal(0, grid.Tree.LeafCount); Assert.Equal(1UL, grid.Tree.ActiveTileCount);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => GridOperators.Laplacian(grid, options: new() { CancellationToken = cancellation.Token }));
        Assert.Equal(0, grid.Tree.LeafCount);
    }
    [Theory]
    [InlineData("covariant", "covariant normalize")]
    [InlineData("  CoVaRiAnT  ", "covariant normalize")]
    [InlineData("contravariant absolute", "contravariant absolute")]
    [InlineData("invariant", "invariant")]
    [InlineData("unknown", "invariant")]
    public void NormalizePreservesNativeVectorMetadata(string input, string expected)
    {
        var grid = new Grid<Vec3d>(new Vec3d(3, 4, 0)); grid.SetMetadata("vector_type", input); grid.Tree.AddTile(3, Coord.Zero, new Vec3d(6, 8, 0), true);
        var result = GridOperators.Normalize(grid, options: new() { Threaded = false });
        Assert.Equal(expected, result.GetMetadata<string>("vector_type")); Assert.InRange((result.Background - new Vec3d(.6, .8, 0)).Length, 0, 1e-15);
        Assert.Equal(0, result.Tree.LeafCount); Assert.Equal(1UL, result.Tree.ActiveTileCount); Assert.InRange((result[Coord.Zero] - new Vec3d(.6, .8, 0)).Length, 0, 1e-15);
        Assert.Equal(new Vec3d(6, 8, 0), grid[Coord.Zero]);
    }
    [Fact]
    public void NormalizeAndMagnitudeHandleZeroNaNAndInfinityLikeNativeVectors()
    {
        var grid = new Grid<Vec3d>(); grid[Coord.Zero] = Vec3d.Zero; grid[new(1, 0, 0)] = new(double.NaN, 1, 2); grid[new(2, 0, 0)] = new(double.PositiveInfinity, 1, 2);
        var normalized = GridOperators.Normalize(grid, options: new() { Threaded = false }); var magnitude = GridOperators.Magnitude(grid, options: new() { Threaded = false });
        Assert.Equal(Vec3d.Zero, normalized[Coord.Zero]); Assert.Equal(Vec3d.Zero, normalized[new(1, 0, 0)]);
        Assert.True(double.IsNaN(normalized[new(2, 0, 0)].X)); Assert.Equal(0, normalized[new(2, 0, 0)].Y);
        Assert.Equal(0, magnitude[Coord.Zero]); Assert.True(double.IsNaN(magnitude[new(1, 0, 0)])); Assert.Equal(double.PositiveInfinity, magnitude[new(2, 0, 0)]);
        Assert.Equal(3UL, normalized.ActiveVoxelCount);
    }
    [Fact]
    public void ClosestPointPreservesNativeTileOriginEvaluation()
    {
        var grid = new Grid<double>(2); grid.Tree.AddTile(1, Coord.Zero, 2, true); grid.Transform.PostTranslate(new(4, 5, 6));
        var result = GridOperators.ClosestPoint(grid, options: new() { Threaded = false });
        Assert.Equal(0, result.Tree.LeafCount); Assert.Equal(new Vec3d(4, 5, 6), result[Coord.Zero]); Assert.Equal(result[Coord.Zero], result[new(7, 7, 7)]);
        Assert.Equal("contravariant absolute", result.GetMetadata<string>("vector_type"));
    }
    [Fact]
    public void StaggeredDivergenceUsesForwardDifferences()
    {
        var grid = new Grid<Vec3d>(); for (int x = -1; x <= 1; ++x) for (int y = -1; y <= 1; ++y) for (int z = -1; z <= 1; ++z) grid[new(x, y, z)] = new(x * x, y * y, z * z);
        var centered = GridOperators.Divergence(grid, options: new() { Threaded = false }); grid.GridClass = GridClass.Staggered;
        var staggered = GridOperators.Divergence(grid, options: new() { Threaded = false }); Assert.Equal(0, centered[Coord.Zero]); Assert.Equal(3, staggered[Coord.Zero]);
    }
    [Fact]
    public void NullInputAndCoordinateOverflowFailWithoutChangingInput()
    {
        Assert.Throws<ArgumentNullException>(() => GridOperators.Gradient((Grid<float>)null!)); Assert.Throws<ArgumentNullException>(() => GridOperators.Divergence((Grid<Vec3d>)null!));
        var grid = new Grid<double>(); Coord boundary = new(int.MaxValue, 0, 0); grid[boundary] = 4;
        Assert.Throws<OverflowException>(() => GridOperators.Gradient(grid, options: new() { Threaded = false })); Assert.Equal(4, grid[boundary]); Assert.Equal(1UL, grid.ActiveVoxelCount);
    }
}
