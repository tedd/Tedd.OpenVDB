using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathOperatorTests
{
    private static Grid<double> Field(Func<Vec3d, double> value, Coord center, int radius = 3, Transform? transform = null)
    {
        var grid = new Grid<double> { Transform = transform ?? new() };
        for (int x = -radius; x <= radius; ++x) for (int y = -radius; y <= radius; ++y) for (int z = -radius; z <= radius; ++z)
        { Coord c = center + new Coord(x, y, z); grid[c] = value(grid.IndexToWorld(c)); }
        return grid;
    }
    private static void Near(double expected, double actual, double epsilon = 1e-10) => Assert.InRange(actual, expected - epsilon, expected + epsilon);
    private static void Near(Vec3d expected, Vec3d actual, double epsilon = 1e-10) { for (int i = 0; i < 3; ++i) Near(expected[i], actual[i], epsilon); }
    [Theory]
    [InlineData(.25)]
    [InlineData(1)]
    [InlineData(2.5)]
    [InlineData(-.5)]
    public void UniformConvenienceStencilsDifferentiatePolynomial(double dx)
    {
        Coord center = new(2, -3, 1); var grid = Field(v => 2 * v.X - 3 * v.Y + 5 * v.Z + 7, center);
        var grad = new GradStencil<double>(grid, dx); var weno = new WenoStencil<double>(grid, dx); grad.MoveTo(center); weno.MoveTo(center);
        Near(new Vec3d(2, -3, 5) / dx, grad.Gradient()); Near(new Vec3d(2, -3, 5) / dx, weno.Gradient());
        Near(new Vec3d(2, -3, 5) / dx, grad.Gradient(new(0, 0, 0))); Near(new Vec3d(2, -3, 5) / dx, weno.Gradient(new(0, 0, 0)));
        Near(38 / (dx * dx), grad.NormSquaredGradient()); Near(38 / (dx * dx), weno.NormSquaredGradient());
        Near(0, grad.Laplacian()); Near(0, weno.Laplacian());
    }
    [Theory]
    [InlineData(.5)]
    [InlineData(1)]
    [InlineData(2)]
    public void CurvatureOfQuadraticSphereHasKnownPrincipalValues(double dx)
    {
        Coord center = new(3, 4, 0); var grid = Field(v => v.LengthSquared - 25, center, 1); var curve = new CurvatureStencil<double>(grid, dx); curve.MoveTo(center);
        Near(new Vec3d(6, 8, 0) / dx, curve.Gradient()); Near(6 / (dx * dx), curve.Laplacian());
        Near(1 / (5 * dx), curve.MeanCurvature()); Near(1 / (25 * dx * dx), curve.GaussianCurvature());
        Near(2 / (dx * dx), curve.MeanCurvatureNormGradient()); Near(2 / (5 * dx * dx * dx), curve.GaussianCurvatureNormGradient());
        var pair = curve.PrincipalCurvatures(); Near(1 / (5 * dx), pair.Minimum, 1e-8); Near(1 / (5 * dx), pair.Maximum, 1e-8);
        Assert.Equal(new Coord(-1, -1, 0), curve.OffsetAt(7)); Assert.Equal(new Coord(1, -1, 0), curve.OffsetAt(8)); Assert.Equal(new Coord(0, 1, 1), curve.OffsetAt(18));
    }
    [Fact]
    public void ConvenienceStencilsSnapshotValuesAndVoxelScale()
    {
        var grid = Field(v => v.X, Coord.Zero); grid.Transform = Transform.CreateLinearTransform(2);
        var gradient = new GradStencil<double>(grid); var weno = new WenoStencil<double>(grid); var curve = new CurvatureStencil<double>(grid);
        Assert.Throws<InvalidOperationException>(() => gradient.Gradient()); Assert.Throws<InvalidOperationException>(() => weno.NormSquaredGradient()); Assert.Throws<InvalidOperationException>(() => curve.Curvatures());
        gradient.MoveTo(Coord.Zero); grid.Transform.PostScale(3); Near(.5, gradient.Gradient().X);
        grid[new Coord(1, 0, 0)] = 9; Near(.5, gradient.Gradient().X); gradient.MoveTo(Coord.Zero); Near(2.5, gradient.Gradient().X);
        double[] snapshot = gradient.Values.ToArray(); Assert.Throws<OverflowException>(() => gradient.MoveTo(new Coord(int.MaxValue, 0, 0))); Assert.Equal(snapshot, gradient.Values.ToArray());
    }
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void ZeroCrossingPreservesNativeCenterZeroConvention(int neighbor, bool crossing)
    {
        var grid = new Grid<double>(); grid[new Coord(1, 0, 0)] = neighbor;
        var grad = new GradStencil<double>(grid); var weno = new WenoStencil<double>(grid); grad.MoveTo(Coord.Zero); weno.MoveTo(Coord.Zero);
        Assert.Equal(crossing, grad.ZeroCrossing()); Assert.Equal(crossing, weno.ZeroCrossing());
    }
    [Fact]
    public void BiasedOperatorAndConvenienceStencilSelectDifferentZeroVelocityBranches()
    {
        var grid = Field(v => v.X * v.X, Coord.Zero); var grad = new GradStencil<double>(grid); grad.MoveTo(Coord.Zero);
        Near(1, grad.Gradient(Vec3d.Zero).X); Near(-1, DifferentialOperators.IndexBiasedGradient(grid.GetAccessor(), Coord.Zero, Vec3d.Zero).X);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AffineWorldOperatorsDifferentiateWorldQuadratic(int order)
    {
        var transform = new Transform(new Mat4d(1.2, .3, .1, 0, -.2, .7, .4, 0, .3, -.1, 2, 0, 3, -2, 5, 1)); Coord center = new(1, -2, 3);
        var grid = Field(v => v.LengthSquared, center, 3, transform); Vec3d point = grid.IndexToWorld(center); var accessor = grid.GetAccessor();
        Near(point * 2, DifferentialOperators.Gradient(transform, accessor, center, (DScheme)(order + 1)), 2e-10);
        Near(6, DifferentialOperators.Laplacian(transform, accessor, center, (DDScheme)order), 2e-10);
        Near(1 / point.Length, DifferentialOperators.MeanCurvature(transform, accessor, center, (DDScheme)order, (DScheme)(order + 1)), 2e-10);
        Near(2, DifferentialOperators.MeanCurvatureNormGradient(transform, accessor, center, (DDScheme)order, (DScheme)(order + 1)), 2e-10);
    }
    [Fact]
    public void IndexClosestPointPreservesNativeInversePointMapTranslation()
    {
        var transform = new Transform(Mat4d.CreateTranslation(new(4, 5, 6))); var grid = Field(v => v.X - 4, new(2, 0, 0), 1, transform);
        var a = grid.GetAccessor(); Coord c = new(2, 0, 0);
        Near(new Vec3d(4, 5, 6), DifferentialOperators.ClosestPoint(transform, a, c, true));
        Near(new Vec3d(4, 5, 6), DifferentialOperators.ClosestPoint(transform, a, c, false));
        var convenience = new GradStencil<double>(grid); convenience.MoveTo(c); Near(Vec3d.Zero, convenience.ClosestPoint());
    }
    [Fact]
    public void ZeroGradientAndNaNFollowNativeCurvatureRules()
    {
        var grid = new Grid<double>(3); var stencil = new CurvatureStencil<double>(grid); stencil.MoveTo(Coord.Zero);
        Assert.Equal((0d, 0d), stencil.Curvatures()); Assert.Equal((0d, 0d), stencil.PrincipalCurvatures());
        Assert.False(DifferentialOperators.IndexMeanCurvatureParts(grid.GetAccessor(), Coord.Zero, out double alpha, out double beta)); Assert.Equal(0, alpha); Assert.Equal(0, beta);
        grid[new Coord(1, 0, 0)] = double.NaN; stencil.MoveTo(Coord.Zero); Assert.True(double.IsNaN(stencil.MeanCurvature())); Assert.True(double.IsNaN(DifferentialOperators.MeanCurvature(grid.Transform, grid.GetAccessor(), Coord.Zero)));
    }
    [Fact]
    public void InvalidSchemesAndIncompleteStencilNeighborhoodsFailExplicitly()
    {
        var grid = new Grid<double>(); var a = grid.GetAccessor(); var stencil = new SevenPointStencil<double>(grid); stencil.MoveTo(Coord.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => DifferentialOperators.IndexGradient(a, Coord.Zero, DScheme.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => DifferentialOperators.IndexLaplacian(a, Coord.Zero, DDScheme.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => DifferentialOperators.IndexBiasedGradient(a, Coord.Zero, Vec3d.Zero, BiasedGradientScheme.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => DifferentialOperators.Laplacian(grid.Transform, stencil));
        Assert.Throws<OverflowException>(() => DifferentialOperators.IndexGradient(a, new(int.MinValue, 0, 0)));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void VectorDerivativesIncludingComponentwiseWenoDifferentiateLinearField(int scheme)
    {
        var grid = new Grid<Vec3d>();
        for (int x = -3; x <= 3; ++x) for (int y = -3; y <= 3; ++y) for (int z = -3; z <= 3; ++z) grid[new(x, y, z)] = new(2 * x + 3 * y, 4 * y + 5 * z, 6 * z + 7 * x);
        double factor = scheme == 0 ? 2 : 1; var accessor = grid.GetAccessor();
        Near(12 * factor, DifferentialOperators.IndexDivergence(accessor, Coord.Zero, (DScheme)scheme));
        Near(new Vec3d(-5, -7, -3) * factor, DifferentialOperators.IndexCurl(accessor, Coord.Zero, (DScheme)scheme));
    }
}
