using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public sealed class ParticlesTests
{
    [Fact]
    public void SingleSphereMatchesIndependentEuclideanBandAndActivity()
    {
        var grid = new Grid<float>(2) { Transform = Transform.CreateLinearTransform() }; var raster = new ParticlesToLevelSet<float>(grid); raster.RasterizeSpheres(new ParticleArray([new(Vec3d.Zero, 5)]));
        for (int x = -8; x <= 8; ++x) for (int y = -8; y <= 8; ++y) for (int z = -8; z <= 8; ++z)
        {
            Coord c = new(x, y, z); double distance = System.Math.Sqrt((double)x * x + y * y + z * z); float expected = (float)System.Math.Clamp(distance - 5, -2, 2);
            Assert.InRange(System.MathF.Abs(expected - grid.Tree.GetValue(c)), 0, 1e-6f); Assert.Equal(distance > 3 && distance < 7, grid.Tree.IsValueOn(c));
        }
        Assert.Equal(GridClass.Unknown, grid.GridClass);
    }
    [Fact]
    public void LargeMaskUsesSparseTilesAndStrictSphereBoundary()
    {
        var grid = new Grid<bool> { Transform = Transform.CreateLinearTransform() }; var raster = new ParticlesToLevelSet<bool>(grid); raster.RasterizeSpheres(new ParticleArray([new(Vec3d.Zero, 20)]));
        Assert.True(grid.Tree.ActiveTileCount > 0);
        for (int x = -21; x <= 21; ++x) for (int y = -21; y <= 21; ++y) for (int z = -21; z <= 21; ++z) { Coord c = new(x, y, z); bool inside = x * x + y * y + z * z < 400; Assert.Equal(inside, grid.Tree.IsValueOn(c)); Assert.Equal(inside, grid.Tree.GetValue(c)); }
    }
    [Fact]
    public void AttributeTiesKeepFirstSphereAndLastMaskParticle()
    {
        var points = new ParticleArray<int>([new(Vec3d.Zero, 3), new(Vec3d.Zero, 3)], [17, 29]);
        var sdf = new Grid<float>(3) { Transform = Transform.CreateLinearTransform() }; var sf = new ParticlesToLevelSet<float, int>(sdf); sf.RasterizeSpheres(points); Assert.True(sdf.IsEmpty); Assert.Null(sf.AttributeGrid); sf.Finalize();
        Assert.Equal(17, sf.AttributeGrid!.Tree.GetValue(new(3, 0, 0))); Assert.Equal(0, sf.AttributeGrid.Tree.GetValue(new(30, 0, 0)));
        var mask = new Grid<bool> { Transform = Transform.CreateLinearTransform() }; var mf = new ParticlesToLevelSet<bool, int>(mask); mf.RasterizeSpheres(points); mf.Finalize();
        Assert.Equal(29, mf.AttributeGrid!.Tree.GetValue(Coord.Zero)); Assert.True(mask.Tree.HasSameTopology(mf.AttributeGrid.Tree));
    }
    [Fact]
    public void UnmanagedBlockAttributesTransferWithoutNumericInterpretation()
    {
        Block attribute = new(65530, 0xabcdef12, -19); var grid = new Grid<double>(3) { Transform = Transform.CreateLinearTransform() }; var raster = new ParticlesToLevelSet<double, Block>(grid);
        raster.RasterizeSpheres(new ParticleArray<Block>([new(Vec3d.Zero, 3)], [attribute])); raster.Finalize(true); Assert.Equal(attribute, raster.AttributeGrid!.Tree.GetValue(new(3, 0, 0))); Assert.Equal(default, raster.AttributeGrid.Tree.GetValue(new(100)));
    }
    [Fact]
    public void AttributeFinalizeReplacesEmptyTreeButRetainsNonemptyTree()
    {
        var points = new ParticleArray<ushort>([new(Vec3d.Zero, 3)], [71]);
        foreach (bool populated in new[] { false, true })
        {
            var grid = new Grid<float>(3) { Transform = Transform.CreateLinearTransform(), Name = "output" }; if (populated) grid.Tree.SetValueOn(new(32), 1);
            var old = grid.Tree; var accessor = grid.GetAccessor(); var raster = new ParticlesToLevelSet<float, ushort>(grid); raster.RasterizeSpheres(points); Assert.Same(old, grid.Tree); raster.Finalize();
            if (populated) { Assert.Same(old, grid.Tree); Assert.Equal(grid.Tree.GetValue(new(3, 0, 0)), accessor.GetValue(new(3, 0, 0))); Assert.False(raster.AttributeGrid!.Tree.IsValueOn(new(32))); }
            else { Assert.NotSame(old, grid.Tree); Assert.Equal(3, accessor.GetValue(new(3, 0, 0))); }
            Assert.Equal("output", grid.Name);
        }
    }
    [Fact]
    public void RadiusFilteringUsesVoxelUnitsAndCountsResetEachCall()
    {
        var grid = new Grid<float>(1.5f) { Transform = Transform.CreateLinearTransform(.5) }; var raster = new ParticlesToLevelSet<float>(grid);
        Assert.Equal(1.5, raster.MinimumRadius); Assert.Equal(100, raster.MaximumRadius); Assert.Equal(3, raster.HalfWidth);
        raster.RasterizeSpheres(new ParticleArray([new(Vec3d.Zero, .5), new(Vec3d.Zero, 51), new(Vec3d.Zero, 1)])); Assert.Equal(1ul, raster.MinimumRadiusCount); Assert.Equal(1ul, raster.MaximumRadiusCount); Assert.True(raster.IgnoredParticles);
        raster.RasterizeSpheres(new ParticleArray([new(Vec3d.Zero, 1)])); Assert.False(raster.IgnoredParticles);
        raster.MinimumRadius = -1; Assert.Equal(0, raster.MinimumRadius); raster.MinimumRadius = 4; raster.MaximumRadius = 2; Assert.Equal(4, raster.MaximumRadius); raster.MinimumRadius = 9; Assert.Equal(4, raster.MaximumRadius);
    }
    [Fact]
    public void InvalidPositionsAreSkippedButFixedRadiusCountersUseFullInputCount()
    {
        var grid = new Grid<float>(3); var raster = new ParticlesToLevelSet<float>(grid); var points = new ParticleArray([new(new(double.NaN, 0, 0), .1), new(Vec3d.Zero, .1)]);
        raster.RasterizeSpheres(points); Assert.Equal(1ul, raster.MinimumRadiusCount); raster.RasterizeSpheres(points, .1); Assert.Equal(2ul, raster.MinimumRadiusCount); Assert.True(grid.IsEmpty);
    }
    [Fact]
    public void FixedRadiusRequiresOnlyPositions()
    {
        var grid = new Grid<bool>(); var raster = new ParticlesToLevelSet<bool>(grid); raster.RasterizeSpheres(new PointArray([Vec3d.Zero]), 2); Assert.True(grid.Tree.IsValueOn(Coord.Zero));
    }
    [Fact]
    public void ZeroVelocityTrailMatchesSphereAndTrailsExtendOppositeVelocity()
    {
        var sphere = new Grid<float>(3); var trail = new Grid<float>(3); var particles = new ParticleArray([new(Vec3d.Zero, 3)]); new ParticlesToLevelSet<float>(sphere).RasterizeSpheres(particles); new ParticlesToLevelSet<float>(trail).RasterizeTrails(particles); LevelSetMorphTests.AssertEqual(sphere.Tree, trail.Tree);
        var moving = new Grid<bool>(); new ParticlesToLevelSet<bool>(moving).RasterizeTrails(new ParticleArray([new(Vec3d.Zero, 3, new(6, 0, 0))])); Assert.True(moving.Tree.IsValueOn(new(-5, 0, 0))); Assert.False(moving.Tree.IsValueOn(new(5, 0, 0)));
    }
    [Fact]
    public void ZeroMinimumRadiusTrailTerminatesWithExplicitProgressFailure()
    {
        var raster = new ParticlesToLevelSet<float>(ParticlesConformanceTests.Grid<float>(0)) { MinimumRadius = 0, MaximumSphereInstances = 1000 };
        Assert.Throws<ArithmeticException>(() => raster.RasterizeTrails(ParticlesConformanceTests.Particles(0), .7));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidTrailSpacingIsRejected(double delta) => Assert.Throws<ArgumentOutOfRangeException>(() => new ParticlesToLevelSet<float>(new(3)).RasterizeTrails(new ParticleArray([]), delta));
    [Fact]
    public void CancellationBudgetsAndInvalidRadiusAreBounded()
    {
        var grid = new Grid<float>(3); var raster = new ParticlesToLevelSet<float>(grid) { MaximumSphereInstances = 0 }; var points = new ParticleArray([new(Vec3d.Zero, 3)]);
        Assert.Throws<OperationCanceledException>(() => raster.RasterizeSpheres(points, new CancellationToken(true))); Assert.True(grid.IsEmpty);
        Assert.Throws<InvalidOperationException>(() => raster.RasterizeSpheres(points)); Assert.True(grid.IsEmpty);
        raster.MaximumSphereInstances = 1; raster.MaximumVoxelEvaluations = 0; Assert.Throws<InvalidOperationException>(() => raster.RasterizeSpheres(points)); Assert.True(grid.IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() => raster.RasterizeSpheres(new ParticleArray([new(Vec3d.Zero, double.NaN)])));
    }
    [Fact]
    public void DestinationAndAttributeTransformsFollowNativeCaptureRules()
    {
        var grid = new Grid<float>(3) { Transform = Transform.CreateLinearTransform() }; var original = grid.Transform; var raster = new ParticlesToLevelSet<float, int>(grid); grid.Transform = Transform.CreateLinearTransform(2);
        raster.RasterizeSpheres(new ParticleArray<int>([new(Vec3d.Zero, 3)], [4])); raster.Finalize(); Assert.Equal(1, raster.VoxelSize); Assert.Equal(1, raster.AttributeGrid!.Transform.VoxelSize.X); Assert.Equal(2, grid.Transform.VoxelSize.X); Assert.NotSame(original, raster.AttributeGrid.Transform);
    }
    [Fact]
    public void UniformTransformAndSupportedValueTypesAreValidated()
    {
        Assert.Throws<NotSupportedException>(() => new ParticlesToLevelSet<int>(new())); Assert.Throws<ArgumentException>(() => new ParticlesToLevelSet<float>(new(3) { Transform = new(Mat4d.CreateScale(new Vec3d(1, 2, 3))) }));
        Assert.Throws<ArgumentException>(() => new ParticleArray<int>(new Particle[2], new int[1]));
    }
    [Fact]
    public void ConvenienceRasterizationMatchesExplicitPruning()
    {
        var particles = new ParticleArray([new(Vec3d.Zero, 3)]); var a = new Grid<float>(3); var b = new Grid<float>(3); ParticleConversion.ParticlesToSdf(particles, a); var raster = new ParticlesToLevelSet<float>(b); raster.RasterizeSpheres(particles); raster.Finalize(true); LevelSetMorphTests.AssertEqual(a.Tree, b.Tree);
        var maskA = new Grid<bool>(); var maskB = new Grid<bool>(); ParticleConversion.ParticlesToMask(particles, maskA); var maskRaster = new ParticlesToLevelSet<bool>(maskB); maskRaster.RasterizeSpheres(particles); maskRaster.Finalize(true); LevelSetMorphTests.AssertEqual(maskA.Tree, maskB.Tree);
    }
    private readonly record struct Block(ushort Id, uint Color, int Flags);
}
