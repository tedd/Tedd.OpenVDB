using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetRebuildTests
{
    [Fact]
    public void DefaultsMatchExplicitSymmetricWidths()
    {
        var input = LevelSetRebuildConformanceTests.Shape<float>(0);
        var implicitResult = LevelSetRebuild.Rebuild(input);
        var explicitResult = LevelSetRebuild.Rebuild(input, 0, 3, 3);
        GridTransformerTests.EqualTrees(implicitResult.Tree, explicitResult.Tree);
    }
    [Fact]
    public void TypedParametersNarrowThroughNativeFloatPrecision()
    {
        var input = LevelSetRebuildConformanceTests.Shape<double>(0);
        var typed = LevelSetRebuild.RebuildTyped(input, .5000000000001, 2.250000000001, 1.750000000001);
        var rounded = LevelSetRebuild.Rebuild(input, .5f, 2.25f, 1.75f);
        GridTransformerTests.EqualTrees(rounded.Tree, typed.Tree);
    }
    [Fact]
    public void ResultOwnsTransformAndLeavesInputAndAliasesUntouched()
    {
        var input = LevelSetRebuildConformanceTests.Shape<float>(0); var before = input.DeepCopy(); var alias = new Grid<float>(input.Tree); var accessor = alias.GetAccessor();
        Transform transform = LevelSetRebuildConformanceTests.CreateTransform(2); var original = transform.Copy();
        var rebuilt = LevelSetRebuild.Rebuild(input, transform: transform);
        Assert.NotSame(transform, rebuilt.Transform); Assert.True(original.IsEquivalentTo(rebuilt.Transform));
        Assert.NotSame(input.Tree, rebuilt.Tree); Assert.Equal(GridClass.LevelSet, rebuilt.GridClass); Assert.Empty(rebuilt.Name); Assert.False(rebuilt.Metadata.ContainsKey("custom"));
        transform.PostTranslate(new Vec3d(100)); Assert.True(original.IsEquivalentTo(rebuilt.Transform));
        GridTransformerTests.EqualTrees(before.Tree, input.Tree); GridTransformerTests.EqualTrees(before.Tree, alias.Tree); Assert.Equal(before.Tree.GetValue(Coord.Zero), accessor.GetValue(Coord.Zero));
    }
    [Fact]
    public void RebuiltNarrowBandPreservesInactiveNegativeInterior()
    {
        var input = LevelSetSphere.Create<float>(6, Vec3d.Zero, 1, 2);
        var rebuilt = LevelSetRebuild.Rebuild(input, halfWidth: 1.5f);
        Assert.Equal(1.5f, rebuilt.Background); Assert.Equal(-1.5f, rebuilt.Tree.GetValue(Coord.Zero)); Assert.False(rebuilt.Tree.IsValueOn(Coord.Zero));
        Assert.Equal(1.5f, rebuilt.Tree.GetValue(new(20, 0, 0)));
        foreach (var leaf in rebuilt.Tree.EnumerateLeaves()) foreach (int offset in leaf.ActiveOffsets)
        {
            Coord coordinate = leaf.OffsetToGlobalCoord(offset); double exact = coordinate.AsVec3d().Length - 6;
            Assert.True(System.Math.Abs(leaf.GetValue(offset) - exact) < .16, $"{coordinate}: {leaf.GetValue(offset)} versus {exact}");
        }
    }
    [Fact]
    public void EmptySurfaceReturnsClassifiedGridWithRequestedBackgroundAndTransform()
    {
        var input = new Grid<double>(4); var transform = Transform.CreateLinearTransform(.75);
        var rebuilt = LevelSetRebuild.Rebuild(input, 0, 2, 4, transform);
        Assert.True(rebuilt.IsEmpty); Assert.Equal(1.5, rebuilt.Background); Assert.Equal(GridClass.LevelSet, rebuilt.GridClass); Assert.True(transform.IsEquivalentTo(rebuilt.Transform));
    }
    [Fact]
    public void HalfRemainsUnsupportedByNativeRebuildContract() => Assert.Throws<NotSupportedException>(() => LevelSetRebuild.Rebuild(new Grid<Half>((Half)3)));
    [Fact]
    public void NaNIsovalueProducesEmptyNativeSurface()
    {
        var output = LevelSetRebuild.Rebuild(LevelSetRebuildConformanceTests.Shape<float>(0), float.NaN);
        Assert.True(output.IsEmpty); Assert.Equal(3, output.Background);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ResourceBudgetsFailWithoutChangingSource(int phase)
    {
        var input = LevelSetRebuildConformanceTests.Shape<float>(0); var before = input.DeepCopy();
        var options = phase == 1 ? new MeshToVolumeOptions { MaximumVoxelEvaluations = 0 } : phase == 2 ? new MeshToVolumeOptions { MaximumLeaves = 0 } : new();
        Assert.Throws<InvalidOperationException>(() => LevelSetRebuild.Rebuild(input, options: options, maximumCells: phase == 0 ? 0ul : ulong.MaxValue));
        GridTransformerTests.EqualTrees(before.Tree, input.Tree);
    }
    [Fact]
    public void PrecancellationIsObservedBeforeMeshing()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => LevelSetRebuild.Rebuild(LevelSetRebuildConformanceTests.Shape<float>(0), options: new() { CancellationToken = cancelled.Token }));
    }
    [Theory]
    [InlineData(SamplingOrder.Point)]
    [InlineData(SamplingOrder.Box)]
    [InlineData(SamplingOrder.Quadratic)]
    public void LevelSetResamplingRebuildsIndependentlyOfKernel(SamplingOrder order)
    {
        var input = LevelSetRebuildConformanceTests.Shape<float>(0); Transform transform = Transform.CreateLinearTransform(1.25);
        var output = new Grid<float>(9) { GridClass = GridClass.FogVolume, Name = "output", Transform = transform }; output.SetMetadata("user", "retained");
        var expected = LevelSetRebuild.Rebuild(input, halfWidth: 2, transform: transform);
        Resampling.ResampleToMatch(input, output, order);
        GridTransformerTests.EqualTrees(expected.Tree, output.Tree); Assert.Same(transform, output.Transform); Assert.Equal("output", output.Name); Assert.Equal(GridClass.FogVolume, output.GridClass); Assert.Equal("retained", output.Metadata["user"].GetValue<string>());
    }
    [Fact]
    public void TargetLevelSetBackgroundDeterminesRebuiltBand()
    {
        var input = LevelSetRebuildConformanceTests.Shape<double>(0); var output = new Grid<double>(3.125) { GridClass = GridClass.LevelSet, Transform = Transform.CreateLinearTransform(1.25) };
        var expected = LevelSetRebuild.Rebuild(input, halfWidth: 2.5f, transform: output.Transform);
        Resampling.ResampleToMatch(input, output); GridTransformerTests.EqualTrees(expected.Tree, output.Tree); Assert.Equal(3.125, output.Background);
    }
    [Fact]
    public void SharedTreeResamplingReplacesTargetWithoutInvalidatingSourceHandles()
    {
        var input = LevelSetRebuildConformanceTests.Shape<float>(0); var before = input.DeepCopy(); var accessor = input.GetAccessor();
        var output = new Grid<float>(input.Tree) { Transform = Transform.CreateLinearTransform(1.25) }; var oldTree = output.Tree;
        Resampling.ResampleToMatch(input, output); Assert.NotSame(oldTree, output.Tree); Assert.Same(oldTree, input.Tree);
        GridTransformerTests.EqualTrees(before.Tree, input.Tree); Assert.Equal(before.Tree.GetValue(Coord.Zero), accessor.GetValue(Coord.Zero));
    }
    [Fact]
    public void RebuiltOutputClipMatchesClippingCompleteResult()
    {
        var input = LevelSetRebuildConformanceTests.Shape<float>(0); var output = new Grid<float> { Transform = Transform.CreateLinearTransform(1.25) }; var expected = LevelSetRebuild.Rebuild(input, halfWidth: 2, transform: output.Transform);
        CoordBBox clip = new(new(-2), new(2)); expected.Tree.Clip(clip);
        Resampling.ResampleToMatch(input, output, outputClip: clip); GridTransformerTests.EqualTrees(expected.Tree, output.Tree);
    }
    [Fact]
    public void FailedRebuildPreservesOutputTreeMetadataAndExistingAccessor()
    {
        var input = LevelSetRebuildConformanceTests.Shape<float>(0); var output = new Grid<float>(7) { Name = "unchanged", Transform = Transform.CreateLinearTransform(1.25) }; output.Tree.SetValueOn(Coord.Zero, 42); var old = output.Tree; var accessor = output.GetAccessor();
        Assert.Throws<InvalidOperationException>(() => Resampling.ResampleToMatch(input, output, maximumSamples: 0));
        Assert.Same(old, output.Tree); Assert.Equal(42, accessor.GetValue(Coord.Zero)); Assert.Equal("unchanged", output.Name); Assert.Equal(7, output.Background);
    }
}
