using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetFilterTests
{
    [Theory, InlineData("mean"), InlineData("gaussian"), InlineData("median"), InlineData("laplacian"), InlineData("curvature"), InlineData("fillet"), InlineData("offset")]
    public void ParallelAndSerialFiltersAgreeWithAlignedSelfMask(string operation)
    {
        var serial = LevelSetTrackingConformanceTests.Shape<float>(0); var parallel = serial.DeepCopy();
        var a = new LevelSetFilter<float>(serial) { GrainSize = 0, NormalizationCount = 1 };
        var b = new LevelSetFilter<float>(parallel) { GrainSize = 1, NormalizationCount = 1 };
        a.SetMaskRange(-2, 2); b.SetMaskRange(-2, 2);
        Apply(a, operation, serial); Apply(b, operation, parallel);
        Assert.True(serial.Tree.HasSameTopology(parallel.Tree));
        Assert.Equal(serial.Tree.EnumerateValues().ToArray(), parallel.Tree.EnumerateValues().ToArray());
    }
    [Theory, InlineData("mean"), InlineData("gaussian"), InlineData("median"), InlineData("laplacian"), InlineData("curvature"), InlineData("fillet"), InlineData("offset")]
    public void FiltersHonorPreCancellation(string operation)
    {
        var grid = LevelSetTrackingConformanceTests.Shape<float>(0); var original = grid.Tree.DeepCopy();
        Assert.Throws<OperationCanceledException>(() => Apply(new LevelSetFilter<float>(grid), operation, null, new CancellationToken(true)));
        Assert.Equal(original.EnumerateValues().ToArray(), grid.Tree.EnumerateValues().ToArray());
    }
    [Fact]
    public void OffsetTracksAcrossMoreThanOneVoxelAndKeepsSharedGridTreeIdentity()
    {
        var grid = LevelSetSphere.Create(6.0, Vec3d.Zero, 0.5); var tree = grid.Tree; var accessor = grid.GetAccessor();
        double original = accessor.GetValue(new(12, 0, 0));
        var filter = new LevelSetFilter<double>(grid) { NormalizationCount = 3, GrainSize = 0 };
        filter.Offset(-1);
        Assert.Same(tree, grid.Tree); Assert.Equal(3, filter.HalfWidth);
        Assert.InRange(accessor.GetValue(new(12, 0, 0)) - original, -1.05, -0.95);
        Assert.All(grid.Tree.EnumerateValues(), value => Assert.True(value.Value > -grid.Background && value.Value < grid.Background));
    }
    [Fact]
    public void TinyOffsetIsSkippedAtNativeCflThresholdAndInvalidOffsetsAreRejected()
    {
        var grid = LevelSetTrackingConformanceTests.Shape<float>(0); var original = grid.Tree.DeepCopy(); var filter = new LevelSetFilter<float>(grid);
        filter.Offset(0.0001f); Assert.Equal(original.EnumerateValues().ToArray(), grid.Tree.EnumerateValues().ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => filter.Offset(float.PositiveInfinity)); Assert.Throws<ArgumentOutOfRangeException>(() => filter.Offset(float.NaN));
    }
    [Fact]
    public void ZeroAlphaStillPerformsNativeTrackingAndMaskRangeRejectsInvalidValues()
    {
        var grid = LevelSetTrackingConformanceTests.Shape<float>(0); var before = grid.Tree.DeepCopy(); var filter = new LevelSetFilter<float>(grid) { GrainSize = 0 };
        filter.Laplacian(new Grid<float>(0));
        Assert.NotEqual(before.EnumerateValues().ToArray(), grid.Tree.EnumerateValues().ToArray());
        Assert.Throws<ArgumentException>(() => filter.SetMaskRange(1, 1));
    }
    [Fact]
    public void TransformedSharedTreeOffsetMaskUsesNativeSerialOrderEvenWhenParallelIsRequested()
    {
        var serial = LevelSetTrackingConformanceTests.Shape<float>(0); var parallel = serial.DeepCopy();
        var maskA = new Grid<float>(serial.Tree) { Transform = Transform.CreateLinearTransform(1.5) };
        var maskB = new Grid<float>(parallel.Tree) { Transform = Transform.CreateLinearTransform(1.5) };
        new LevelSetFilter<float>(serial) { GrainSize = 0, NormalizationCount = 1 }.Offset(-0.65f, maskA);
        new LevelSetFilter<float>(parallel) { GrainSize = 1, NormalizationCount = 1 }.Offset(-0.65f, maskB);
        Assert.Equal(serial.Tree.EnumerateValues().ToArray(), parallel.Tree.EnumerateValues().ToArray());
    }
    private static void Apply(LevelSetFilter<float> filter, string operation, Grid<float>? mask, CancellationToken token = default)
    {
        switch (operation)
        {
            case "mean": filter.Mean(1, mask, token); break;
            case "gaussian": filter.Gaussian(1, mask, token); break;
            case "median": filter.Median(1, mask, token); break;
            case "laplacian": filter.Laplacian(mask, token); break;
            case "curvature": filter.MeanCurvature(mask, token); break;
            case "fillet": filter.Fillet(mask, token); break;
            case "offset": filter.Offset(0.2f, mask, token); break;
        }
    }
}
