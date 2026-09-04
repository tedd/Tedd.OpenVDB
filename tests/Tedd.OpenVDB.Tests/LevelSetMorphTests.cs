using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetMorphTests
{
    [Fact]
    public void DefaultsAndConfigurationRetainGridReferences()
    {
        var source = Source(); var target = Target(source); var morph = new LevelSetMorphing<float>(source, target);
        Assert.Same(source, morph.Grid); Assert.Same(target, morph.Target); Assert.Null(morph.AlphaMask); Assert.Equal(.75f, morph.VoxelSize);
        Assert.Equal(BiasedGradientScheme.HJWeno5, morph.SpatialScheme); Assert.Equal(TemporalIntegrationScheme.TvdRungeKutta2, morph.TemporalScheme);
        Assert.Equal(BiasedGradientScheme.HJWeno5, morph.TrackerSpatialScheme); Assert.Equal(TemporalIntegrationScheme.TvdRungeKutta1, morph.TrackerTemporalScheme);
        Assert.Equal(3, morph.NormalizationCount); Assert.Equal(0f, morph.MinMask); Assert.Equal(1f, morph.MaxMask); Assert.False(morph.IsMaskInverted);
        morph.SetAlphaMask(target); Assert.Same(target, morph.AlphaMask); morph.SetAlphaMask(null); Assert.Null(morph.AlphaMask);
        morph.SetTarget(source); Assert.Same(source, morph.Target); Assert.Throws<ArgumentNullException>(() => morph.SetTarget(null!));
        morph.SetMaskRange(-2, 3); Assert.Equal(-2, morph.MinMask); Assert.Equal(3, morph.MaxMask);
        Assert.Throws<ArgumentException>(() => morph.SetMaskRange(1, 1)); Assert.Throws<ArgumentException>(() => morph.SetMaskRange(float.NaN, 1));
    }
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ZeroOrNonAdvancingSpeedsLeaveSourceExactlyUntouched(int targetShape)
    {
        var source = Source(); var before = source.DeepCopy(); var target = targetShape == 7 ? source : LevelSetMorphConformanceTests.Target(source, targetShape);
        Assert.Equal(0ul, new LevelSetMorphing<float>(source, target).Advect(0, 1)); AssertEqual(before.Tree, source.Tree);
    }
    [Theory]
    [InlineData(1, 0)]
    [InlineData(.25f, .25f)]
    public void ReverseAndEqualTimesDoNotMutate(float begin, float end)
    {
        var source = Source(); var before = source.DeepCopy(); Assert.Equal(0ul, new LevelSetMorphing<float>(source, Target(source)).Advect(begin, end)); AssertEqual(before.Tree, source.Tree);
    }
    [Fact]
    public void NaNMaskGatesOutAllSpeedsAndInversionRestoresThem()
    {
        var source = Source(); var original = source.DeepCopy(); var target = Target(source);
        var morph = new LevelSetMorphing<float>(source, target) { GrainSize = 0 }; morph.SetAlphaMask(new(float.NaN) { Transform = source.Transform.Copy() });
        Assert.Equal(0ul, morph.Advect(0, .1f)); AssertEqual(original.Tree, source.Tree);
        morph.IsMaskInverted = true; Assert.True(morph.Advect(0, .1f) > 0); Assert.NotEqual(original.Tree.GetValue(new(3, 0, 0)), source.Tree.GetValue(new(3, 0, 0)));
    }
    [Fact]
    public void DoubleMaskRangeKeepsDoublePrecision()
    {
        var source = LevelSetMorphConformanceTests.Source<double>(); var morph = new LevelSetMorphing<double>(source, source);
        const double min = 1.00000000001, max = 1.00000000002; morph.SetMaskRange(min, max); Assert.Equal(min, morph.MinMask); Assert.Equal(max, morph.MaxMask);
    }
    [Fact]
    public void CancellationAndBufferBudgetFailBeforePublishingChanges()
    {
        var source = Source(); var before = source.DeepCopy(); var morph = new LevelSetMorphing<float>(source, Target(source)) { MaximumBufferedLeaves = 0 };
        Assert.Throws<OperationCanceledException>(() => morph.Advect(0, 1, new CancellationToken(true))); AssertEqual(before.Tree, source.Tree);
        Assert.Throws<InvalidOperationException>(() => morph.Advect(0, 1)); AssertEqual(before.Tree, source.Tree);
        morph.MaximumBufferedLeaves = int.MaxValue; Assert.True(morph.Advect(0, .1f) > 0);
    }
    [Fact]
    public void SerialAndParallelBufferedStagesProduceIdenticalValues()
    {
        var serial = Source(); var parallel = serial.DeepCopy(); var target = Target(serial);
        var a = new LevelSetMorphing<float>(serial, target) { GrainSize = 0, TemporalScheme = TemporalIntegrationScheme.TvdRungeKutta3 };
        var b = new LevelSetMorphing<float>(parallel, target) { GrainSize = 1, TemporalScheme = TemporalIntegrationScheme.TvdRungeKutta3 };
        a.SetAlphaMask(LevelSetMorphConformanceTests.Mask(serial, 4)); b.SetAlphaMask(a.AlphaMask);
        Assert.Equal(a.Advect(0, .75f), b.Advect(0, .75f)); AssertEqual(serial.Tree, parallel.Tree);
    }
    [Fact]
    public void TargetAndMaskAliasesAreSampledBeforeEachBufferedStage()
    {
        var source = Source(); var original = source.DeepCopy(); var morph = new LevelSetMorphing<float>(source, source) { GrainSize = 0 }; morph.SetAlphaMask(source); morph.SetMaskRange(-1, 1);
        Assert.Equal(0ul, morph.Advect(0, 1)); AssertEqual(original.Tree, source.Tree);
        morph.SetTarget(Target(source)); Assert.True(morph.Advect(0, .25f) > 0);
    }
    [Fact]
    public void TrackerObservesReplacedSourceTreeAndOldAccessorsRemainUsable()
    {
        var source = Source(); var accessor = source.GetAccessor(); var oldTree = source.Tree; var target = Target(source); var morph = new LevelSetMorphing<float>(source, target) { GrainSize = 0 };
        float oldValue = accessor.GetValue(new(3, 0, 0)); source.SetTree(source.Tree.DeepCopy()); Assert.True(morph.Advect(0, .25f) > 0);
        Assert.Equal(oldValue, accessor.GetValue(new(3, 0, 0))); Assert.Equal(oldValue, oldTree.GetValue(new(3, 0, 0))); Assert.NotEqual(oldValue, source.Tree.GetValue(new(3, 0, 0)));
    }
    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(10)]
    public void NoActiveLeafVoxelsProduceNoIterations(int scene)
    {
        var source = LevelSetMorphConformanceTests.Source<float>(scene); var before = source.DeepCopy(); Assert.Equal(0ul, new LevelSetMorphing<float>(source, Target(source)).Advect(0, 1)); AssertEqual(before.Tree, source.Tree);
    }
    [Fact]
    public void ActiveTilesOutsideLeafBandRemainSparseAndUnmodified()
    {
        var source = LevelSetMorphConformanceTests.Source<float>(8); var morph = new LevelSetMorphing<float>(source, Target(source)) { GrainSize = 0 };
        Assert.True(morph.Advect(0, .25f) > 0); Assert.Equal(-1f, source.Tree.GetValue(new(64))); Assert.True(source.Tree.IsValueOn(new(64))); Assert.Null(source.Tree.ProbeLeaf(new(64))); Assert.Equal(1ul, source.Tree.ActiveTileCount);
    }
    [Fact]
    public void OnlySourceValuesAndTopologyChange()
    {
        var source = Source(); var target = Target(source); var targetCopy = target.DeepCopy(); var mask = LevelSetMorphConformanceTests.Mask(source, 4); var maskCopy = mask.DeepCopy();
        var transform = source.Transform; var tree = source.Tree; var accessor = source.GetAccessor(); var morph = new LevelSetMorphing<float>(source, target); morph.SetAlphaMask(mask);
        Assert.True(morph.Advect(0, .25f) > 0); Assert.Same(transform, source.Transform); Assert.Same(tree, source.Tree); Assert.Equal("source", source.Name); Assert.Equal(GridClass.LevelSet, source.GridClass);
        Assert.Equal(source.Tree.GetValue(new(3, 0, 0)), accessor.GetValue(new(3, 0, 0))); AssertEqual(targetCopy.Tree, target.Tree); AssertEqual(maskCopy.Tree, mask.Tree);
    }
    [Theory]
    [InlineData(float.NaN, 1)]
    [InlineData(0, float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, 0)]
    public void NonFiniteTimeIsRejected(float start, float end)
    {
        var source = Source(); Assert.Throws<ArgumentOutOfRangeException>(() => new LevelSetMorphing<float>(source, Target(source)).Advect(start, end));
    }
    [Theory]
    [InlineData(BiasedGradientScheme.Second)]
    [InlineData(BiasedGradientScheme.Third)]
    [InlineData(BiasedGradientScheme.Weno5)]
    [InlineData(BiasedGradientScheme.Unknown)]
    public void UnsupportedMorphingSchemesAreRejected(BiasedGradientScheme scheme)
    {
        var source = Source(); var morph = new LevelSetMorphing<float>(source, source) { SpatialScheme = scheme }; Assert.Throws<ArgumentOutOfRangeException>(() => morph.Advect(0, 0));
    }
    [Fact]
    public void TemporalSchemeValidationAndScalarSupportAreExplicit()
    {
        var source = Source(); var morph = new LevelSetMorphing<float>(source, source) { TemporalScheme = TemporalIntegrationScheme.Unknown }; Assert.Throws<ArgumentOutOfRangeException>(() => morph.Advect(0, 0));
        Assert.Throws<NotSupportedException>(() => new LevelSetMorphing<Half>(new((Half)3) { GridClass = GridClass.LevelSet }, new((Half)3)));
    }
    private static Grid<float> Source() => LevelSetMorphConformanceTests.Source<float>();
    private static Grid<float> Target(Grid<float> source) => LevelSetMorphConformanceTests.Target(source);
    internal static void AssertEqual<T>(VdbTree<T> expected, VdbTree<T> actual) where T : unmanaged
    {
        Assert.Equal(expected.Background, actual.Background); Assert.Equal(expected.ActiveVoxelCount, actual.ActiveVoxelCount); Assert.Equal(expected.ActiveTileCount, actual.ActiveTileCount); Assert.Equal(expected.RootEntryCount, actual.RootEntryCount); Assert.Equal(expected.LeafCount, actual.LeafCount);
        foreach (var a in expected.EnumerateLeaves()) { var b = actual.ProbeLeaf(a.Origin); Assert.NotNull(b); Assert.Equal(a.Values.ToArray(), b.Values.ToArray()); for (int i = 0; i < 512; ++i) Assert.Equal(a.IsValueOn(i), b.IsValueOn(i)); }
        foreach (var value in expected.EnumerateValues()) { Assert.Equal(value.Value, actual.GetValue(value.Coordinate)); Assert.Equal(value.IsActive, actual.IsValueOn(value.Coordinate)); }
    }
}
