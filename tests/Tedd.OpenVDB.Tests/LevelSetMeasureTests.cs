using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;

namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetMeasureTests
{
    [Theory, InlineData(-2, 0), InlineData(-1.5, 0), InlineData(0, 0.6666666666666666), InlineData(1.5, 0), InlineData(2, 0)]
    public void DiracDeltaHasNativeCompactSupport(double position, double expected) => Assert.Equal(expected, new DiracDelta(1.5).Evaluate(position), 14);

    [Fact]
    public void DiracDeltaPropagatesNan() => Assert.True(double.IsNaN(new DiracDelta(1.5).Evaluate(double.NaN)));

    [Fact]
    public void SphereIntegralsRecoverAnalyticGeometryWithinDiscretizationError()
    {
        const double radius = 10;
        var grid = LevelSetSphere.Create(radius, new Vec3d(1.25, -2.5, 0.75), 0.5);
        var measure = new LevelSetMeasure<double>(grid);
        Assert.InRange(measure.Area() / (4 * System.Math.PI * radius * radius), 0.99, 1.01);
        Assert.InRange(measure.Volume() / (4.0 / 3 * System.Math.PI * radius * radius * radius), 0.99, 1.01);
        Assert.InRange(measure.AverageMeanCurvature() * radius, 0.99, 1.01);
        Assert.InRange(measure.TotalGaussianCurvature() / (4 * System.Math.PI), 0.99, 1.01);
        Assert.Equal(2, measure.EulerCharacteristic); Assert.Equal(0, measure.Genus);
        Assert.Equal(measure.Area(false) * 0.25, measure.Area());
        Assert.Equal(measure.Volume(false) * 0.125, measure.Volume());
        Assert.Equal(measure.TotalMeanCurvature(false) * 0.5, measure.TotalMeanCurvature());
    }

    [Fact]
    public void CachedMeasurementsRequireExplicitReinitialization()
    {
        var grid = LevelSetSphere.Create(4.0, Vec3d.Zero, 1.0);
        var measure = new LevelSetMeasure<double>(grid);
        double area = measure.Area();
        foreach (var leaf in grid.Tree.EnumerateLeaves()) leaf.Fill(3, false);
        Assert.Equal(area, measure.Area());
        measure.Init(grid);
        Assert.Equal(0, measure.Area()); Assert.Equal(0, measure.Volume());
        Assert.True(double.IsNaN(measure.AverageMeanCurvature()));
    }

    [Fact]
    public void NativeTileOnlyNonemptyLevelSetHasZeroLeafIntegrals()
    {
        var grid = new Grid<double>(3) { GridClass = GridClass.LevelSet };
        grid.Tree.AddTile(3, new(0), -3, false);
        var measure = new LevelSetMeasure<double>(grid);
        Assert.Equal(0, measure.Area()); Assert.Equal(0, measure.TotalGaussianCurvature());
        Assert.Equal(0, measure.EulerCharacteristic); Assert.Equal(1, measure.Genus);
    }

    [Fact]
    public void InvalidGridClassNonuniformScaleEmptyAndHalfInputsAreRejected()
    {
        var empty = new Grid<float>(3) { GridClass = GridClass.LevelSet };
        Assert.Throws<ArgumentException>(() => new LevelSetMeasure<float>(empty));
        empty.Tree.SetValueOn(new(0), 0); empty.GridClass = GridClass.FogVolume;
        Assert.Throws<ArgumentException>(() => new LevelSetMeasure<float>(empty));
        empty.GridClass = GridClass.LevelSet; empty.Transform = new Transform(Mat4d.CreateScale(new Vec3d(1, 2, 1)));
        Assert.Throws<ArgumentException>(() => new LevelSetMeasure<float>(empty));
        Assert.Throws<NotSupportedException>(() => new LevelSetMeasure<Half>(new Grid<Half>()));
    }
}
