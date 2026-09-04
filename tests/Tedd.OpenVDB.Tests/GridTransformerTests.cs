using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public sealed class GridTransformerTests
{
    [Theory]
    [InlineData(SamplingOrder.Point)]
    [InlineData(SamplingOrder.Box)]
    [InlineData(SamplingOrder.Quadratic)]
    public void IntegerTranslationReproducesAffineFieldAndPreservesMetadata(SamplingOrder order)
    {
        var input = new Grid<double>(11); var output = new Grid<double>(7) { Name = "destination", GridClass = GridClass.FogVolume, Transform = Transform.CreateLinearTransform(4) };
        output.SetMetadata("sentinel", 123); var transform = output.Transform;
        for (int x = -4; x <= 4; ++x) for (int y = -4; y <= 4; ++y) for (int z = -4; z <= 4; ++z) input.Tree.SetValueOn(new(x, y, z), Field(x, y, z));
        new GridTransformer(Mat4d.CreateTranslation(new(2, -3, 1))) { Threaded = false }.TransformGrid(input, output, order);
        for (int x = -2; x <= 2; ++x) for (int y = -2; y <= 2; ++y) for (int z = -2; z <= 2; ++z) { Coord p = new(x + 2, y - 3, z + 1); Assert.True(output.Tree.IsValueOn(p)); Assert.Equal(Field(x, y, z), output.Tree.GetValue(p)); }
        Assert.Same(transform, output.Transform); Assert.Equal("destination", output.Name); Assert.Equal(GridClass.FogVolume, output.GridClass); Assert.Equal(123, output.Metadata["sentinel"].GetValue<int>()); Assert.Equal(11, output.Background);
    }
    [Theory]
    [InlineData(SamplingOrder.Box)]
    [InlineData(SamplingOrder.Quadratic)]
    public void FractionalTranslationReproducesAffineField(SamplingOrder order)
    {
        var input = new Grid<double>(); var output = new Grid<double>(); Vec3d shift = new(.375, -.25, .625);
        for (int x = -4; x <= 4; ++x) for (int y = -4; y <= 4; ++y) for (int z = -4; z <= 4; ++z) input.Tree.SetValueOn(new(x, y, z), Field(x, y, z));
        new GridTransformer(Mat4d.CreateTranslation(shift)) { Threaded = false }.TransformGrid(input, output, order);
        for (int x = -2; x <= 2; ++x) for (int y = -2; y <= 2; ++y) for (int z = -2; z <= 2; ++z) Assert.Equal(Field(x - shift.X, y - shift.Y, z - shift.Z), output.Tree.GetValue(new(x, y, z)), 12);
    }
    [Theory]
    [InlineData(2, SamplingOrder.Point)]
    [InlineData(2, SamplingOrder.Box)]
    [InlineData(4, SamplingOrder.Box)]
    [InlineData(7, SamplingOrder.Quadratic)]
    public void AliasedTreesMatchAnExplicitSnapshot(int profile, SamplingOrder order)
    {
        var input = GridTransformerConformanceTests.Shape<float>(); var separateSource = input.DeepCopy(); var expected = input.DeepCopy(); var alias = new Grid<float>(input.Tree);
        var transformer = GridTransformerConformanceTests.CreateTransformer(profile); transformer.Threaded = false;
        transformer.TransformGrid(separateSource, expected, order); transformer.TransformGrid(input, alias, order); EqualTrees(expected.Tree, alias.Tree);
        var self = separateSource.DeepCopy(); transformer.TransformGrid(self, self, order); EqualTrees(expected.Tree, self.Tree);
    }
    [Theory]
    [InlineData(2, SamplingOrder.Point)]
    [InlineData(4, SamplingOrder.Box)]
    [InlineData(7, SamplingOrder.Quadratic)]
    public void ParallelSamplingMatchesSerialExactly(int profile, SamplingOrder order)
    {
        var input = GridTransformerConformanceTests.Shape<float>(1); var serial = GridTransformerConformanceTests.Output<float>(); var parallel = serial.DeepCopy();
        var transformer = GridTransformerConformanceTests.CreateTransformer(profile); transformer.Threaded = false; transformer.TransformGrid(input, serial, order); transformer.Threaded = true; transformer.TransformGrid(input, parallel, order); EqualTrees(serial.Tree, parallel.Tree);
    }
    [Theory]
    [InlineData(SamplingOrder.Box)]
    [InlineData(SamplingOrder.Quadratic)]
    public void FinalOutputClipDoesNotClipIntermediateMipCoordinates(SamplingOrder order)
    {
        var input = new Grid<float>(3);
        for (int x = 8; x <= 15; ++x) for (int y = 8; y <= 15; ++y) for (int z = 8; z <= 15; ++z) input.Tree.SetValueOn(new(x, y, z), (float)Field(x, y, z));
        var full = new Grid<float>(3); var clipped = new Grid<float>(3); CoordBBox clip = new(new(2), new(3));
        var transformer = new GridTransformer(Mat4d.CreateScale(.25)) { Threaded = false }; transformer.TransformGrid(input, full, order); transformer.OutputClip = clip; transformer.TransformGrid(input, clipped, order);
        foreach (var value in clipped.Tree.EnumerateValues(ValueIterationMode.Active)) Assert.True(clip.Contains(value.Coordinate));
        for (int x = 2; x <= 3; ++x) for (int y = 2; y <= 3; ++y) for (int z = 2; z <= 3; ++z) { Coord p = new(x, y, z); Assert.Equal(full.Tree.GetValue(p), clipped.Tree.GetValue(p)); Assert.Equal(full.Tree.IsValueOn(p), clipped.Tree.IsValueOn(p)); }
        Assert.True(clipped.ActiveVoxelCount > 0);
    }
    [Fact]
    public void SampleBudgetRejectsRootTileBeforeMutatingDestination()
    {
        var source = new Grid<float>(3); source.Tree.AddTile(3, Coord.Zero, 5, true); var output = GridTransformerConformanceTests.Output<float>(); var before = output.DeepCopy();
        Assert.Throws<InvalidOperationException>(() => new GridResampler { MaximumSamples = 1_000_000 }.TransformGrid(new MatrixGridTransform(Mat4d.Identity), source, output, SamplingOrder.Point)); EqualTrees(before.Tree, output.Tree);
    }
    [Fact]
    public void OutputClipMakesRootTileSamplingBounded()
    {
        var input = new Grid<ushort>(); input.Tree.AddTile(3, Coord.Zero, 65000, true); var output = new Grid<ushort>();
        new GridResampler { OutputClip = new(new(-1), new(1)), MaximumSamples = 27, Threaded = false }.TransformGrid(new MatrixGridTransform(Mat4d.Identity), input, output, SamplingOrder.Point);
        Assert.Equal(8ul, output.ActiveVoxelCount);
        for (int x = -1; x <= 1; ++x) for (int y = -1; y <= 1; ++y) for (int z = -1; z <= 1; ++z) { Coord p = new(x, y, z); bool active = x >= 0 && y >= 0 && z >= 0; Assert.Equal(active, output.Tree.IsValueOn(p)); Assert.Equal(active ? (ushort)65000 : (ushort)0, output.Tree.GetValue(p)); }
    }
    [Fact]
    public void EmptyOutputClipDoesNotCreateLeaves()
    {
        var input = GridTransformerConformanceTests.Shape<float>(); var output = new Grid<float>();
        new GridResampler { OutputClip = CoordBBox.Empty, MaximumSamples = 0 }.TransformGrid(new MatrixGridTransform(Mat4d.Identity), input, output);
        Assert.Equal(input.Background, output.Background); Assert.Equal(0, output.Tree.LeafCount);
    }
    [Fact]
    public void PreCanceledOperationLeavesDestinationUntouched()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var source = GridTransformerConformanceTests.Shape<float>(); var output = GridTransformerConformanceTests.Output<float>(); var oldTree = output.Tree; var before = output.DeepCopy();
        Assert.Throws<OperationCanceledException>(() => new GridTransformer(Mat4d.Identity).TransformGrid(source, output, cancellationToken: cancellation.Token)); Assert.Same(oldTree, output.Tree); EqualTrees(before.Tree, output.Tree);
        Assert.Throws<OperationCanceledException>(() => Resampling.ResampleToMatch(source, output, cancellationToken: cancellation.Token)); EqualTrees(before.Tree, output.Tree);
    }
    [Fact]
    public void CancellationDuringSamplingLeavesCommittedTreeUsable()
    {
        using var cancellation = new CancellationTokenSource(); var source = GridTransformerConformanceTests.Shape<float>(); var output = new Grid<float>(7); var before = output.GetAccessor(); var transform = new CancelingMap(cancellation, 160);
        Assert.Throws<OperationCanceledException>(() => new GridResampler { Threaded = false }.TransformGrid(transform, source, output, cancellationToken: cancellation.Token));
        Assert.Equal(source.Background, output.Background); output.Tree.SetValueOn(new(1), 42); Assert.Equal(42, before.GetValue(new(1))); GC.Collect(2, GCCollectionMode.Forced, true, true); Assert.Equal(42, output.Tree.GetValue(new(1)));
    }
    [Fact]
    public void NonlinearMappingDoesNotEvaluateUnusedOriginOrAxes()
    {
        var input = new Grid<float>(); input.Tree.SetValueOn(new(24), 1); var output = new Grid<float>(); var transform = new RestrictedMap();
        new GridResampler { Threaded = false }.TransformGrid(transform, input, output, SamplingOrder.Point); Assert.Equal(1, output.Tree.GetValue(new(24))); Assert.True(transform.InverseCalls > 0);
    }
    [Fact]
    public void InactiveSamplesCannotEraseExistingActiveValues()
    {
        var input = new Grid<float>(2); input.Tree.SetValueOff(Coord.Zero, 13); var output = new Grid<float>(7); output.Tree.SetValueOn(Coord.Zero, 80);
        new GridTransformer(Mat4d.Identity) { Threaded = false }.TransformGrid(input, output, SamplingOrder.Point); Assert.True(output.Tree.IsValueOn(Coord.Zero)); Assert.Equal(80, output.Tree.GetValue(Coord.Zero)); Assert.Equal(2, output.Background);
    }
    [Fact]
    public void SuccessiveHalvingWithIdentityRemainderReplacesTree()
    {
        var input = GridTransformerConformanceTests.Shape<float>(); var output = GridTransformerConformanceTests.Output<float>(); var retained = new Grid<float>(output.Tree); var oldAccessor = output.GetAccessor();
        new GridTransformer(Mat4d.CreateScale(.25)) { Threaded = false }.TransformGrid(input, output);
        Assert.NotSame(retained.Tree, output.Tree); Assert.Equal(81, oldAccessor.GetValue(new(30, 2, -1))); Assert.Equal(input.Background, retained.Background); Assert.False(output.Tree.IsValueOn(new(30, 2, -1)));
    }
    [Theory]
    [InlineData(SamplingOrder.Point)]
    [InlineData(SamplingOrder.Box)]
    public void ConsistentIdentityCopyRetainsTilesAndDetachesOldAliases(SamplingOrder order)
    {
        var input = new Grid<ushort>(3); input.Tree.AddTile(3, Coord.Zero, 500, true); var output = new Grid<ushort>(7); var old = output.Tree; var oldAccessor = output.GetAccessor(); output.Tree.SetValueOn(new(-1), 42);
        Resampling.ResampleToMatch(input, output, order, maximumSamples: 0); Assert.NotSame(input.Tree, output.Tree); Assert.NotSame(old, output.Tree); Assert.Equal(1ul, output.Tree.ActiveTileCount); Assert.Equal(68719476736ul, output.ActiveVoxelCount); Assert.Equal((ushort)42, oldAccessor.GetValue(new(-1)));
        input.Tree.Clear(); Assert.Equal((ushort)500, output.Tree.GetValue(Coord.Zero));
    }
    [Fact]
    public void QuadraticIdentityStillExecutesResampling()
    {
        var input = new Grid<float>(); input.Tree.AddTile(3, Coord.Zero, 1, true); var output = new Grid<float>();
        Assert.Throws<InvalidOperationException>(() => Resampling.ResampleToMatch(input, output, SamplingOrder.Quadratic, maximumSamples: 0)); Assert.Equal(0, output.Tree.RootEntryCount);
    }
    [Theory]
    [InlineData(SamplingOrder.Point)]
    [InlineData(SamplingOrder.Box)]
    [InlineData(SamplingOrder.Quadratic)]
    public void IdenticalTransformLevelSetsCopyWithoutRebuilding(SamplingOrder order)
    {
        var input = GridTransformerConformanceTests.Shape<float>(3); input.Transform = GridTransformerConformanceTests.Frustum(); var output = new Grid<float>(9) { Transform = input.Transform.Copy(), GridClass = GridClass.FogVolume };
        Resampling.ResampleToMatch(input, output, order, maximumSamples: 0); EqualTrees(input.Tree, output.Tree); Assert.NotSame(input.Tree, output.Tree); Assert.Equal(GridClass.FogVolume, output.GridClass);
    }
    [Fact]
    public void DifferingTransformLevelSetRebuildsAndRetainsClassification()
    {
        var input = GridTransformerConformanceTests.Shape<float>(3); var output = new Grid<float>(9) { Transform = Transform.CreateLinearTransform(2) }; var before = output.Tree;
        Resampling.ResampleToMatch(input, output); Assert.NotSame(before, output.Tree); Assert.Equal(4, output.Background); Assert.Equal(GridClass.Unknown, output.GridClass); Assert.True(output.ActiveVoxelCount > 0);
    }
    [Fact]
    public void BooleanPointSamplingPreservesFalseActivePayloads()
    {
        var input = new Grid<bool>(); input.Tree.SetValueOn(Coord.Zero, false); input.Tree.SetValueOff(new(1, 0, 0), true); var output = new Grid<bool>();
        new GridTransformer(Mat4d.CreateTranslation(new(2, 0, 0))) { Threaded = false }.TransformGrid(input, output, SamplingOrder.Point);
        Assert.True(output.Tree.IsValueOn(new(2, 0, 0))); Assert.False(output.Tree.GetValue(new(2, 0, 0))); Assert.False(output.Tree.IsValueOn(new(3, 0, 0))); Assert.True(output.Tree.GetValue(new(3, 0, 0)));
    }
    [Theory]
    [InlineData("ts", "zyx")]
    [InlineData("ttr", "zyx")]
    [InlineData("tsr", "zzx")]
    [InlineData("tsr", "xy")]
    public void InvalidTransformOrdersAreRejected(string transformOrder, string rotationOrder) => Assert.Throws<ArgumentException>(() => new GridTransformer(Vec3d.Zero, Vec3d.One, Vec3d.Zero, Vec3d.Zero, transformOrder, rotationOrder));
    [Fact]
    public void InvalidNumericTransformsAndUnsupportedSamplingAreRejectedBeforeMutation()
    {
        Assert.Throws<ArgumentException>(() => new GridTransformer(Vec3d.Zero, Vec3d.Zero, Vec3d.Zero, Vec3d.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridTransformer(Mat4d.CreateScale(System.Math.Pow(2, -31))));
        Assert.Throws<ArgumentException>(() => new GridTransformer(Mat4d.CreateScale(double.NaN)));
        var input = new Grid<float>(); var output = new Grid<float>(7);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridTransformer(Mat4d.Identity).TransformGrid(input, output, (SamplingOrder)99));
        Assert.Throws<NotSupportedException>(() => new GridTransformer(Mat4d.Identity).TransformGrid(input, output, staggered: true)); Assert.Equal(7, output.Background);
    }
    private static double Field(double x, double y, double z) => 2 * x - 3 * y + .5 * z + 7;
    internal static void EqualTrees<T>(VdbTree<T> expected, VdbTree<T> actual) where T : unmanaged
    {
        Assert.Equal(expected.Background, actual.Background); Assert.Equal(expected.RootEntryCount, actual.RootEntryCount); Assert.Equal(expected.LeafCount, actual.LeafCount); Assert.Equal(expected.ActiveTileCount, actual.ActiveTileCount); Assert.Equal(expected.ActiveVoxelCount, actual.ActiveVoxelCount);
        var expectedLeaves = expected.EnumerateLeaves().ToArray(); var actualLeaves = actual.EnumerateLeaves().ToArray();
        for (int n = 0; n < expectedLeaves.Length; ++n) { Assert.Equal(expectedLeaves[n].Origin, actualLeaves[n].Origin); Assert.True(expectedLeaves[n].Values.SequenceEqual(actualLeaves[n].Values)); Assert.Equal(expectedLeaves[n].ActiveOffsets.ToArray(), actualLeaves[n].ActiveOffsets.ToArray()); }
    }
    private sealed class CancelingMap(CancellationTokenSource source, int cancelAt) : IGridTransform
    {
        private int _calls; public bool IsAffine => false; public Vec3d Transform(Vec3d coordinate) => coordinate;
        public Vec3d InverseTransform(Vec3d coordinate) { if (++_calls == cancelAt) source.Cancel(); return coordinate; }
    }
    private sealed class RestrictedMap : IGridTransform
    {
        public int InverseCalls; public bool IsAffine => false; public Vec3d Transform(Vec3d coordinate) => coordinate;
        public Vec3d InverseTransform(Vec3d coordinate) { if (coordinate.X < 10 || coordinate.Y < 10 || coordinate.Z < 10) throw new InvalidOperationException("Unused origin evaluated"); ++InverseCalls; return coordinate; }
    }
}
