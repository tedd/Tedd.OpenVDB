using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public sealed class PointScatterTests
{
    [Fact]
    public void DefaultEngineMatchesPublishedMt19937ReferenceSequence()
    {
        var r = new Mt19937(); uint[] expected = [3499211612, 581869302, 3890346734, 3586334585, 545404204, 4161255391, 3922919429, 949333985, 2715962298, 1323567403]; foreach (uint x in expected) Assert.Equal(x, r.NextUInt32());
    }
    [Theory]
    [InlineData(0)]
    [InlineData(623)]
    [InlineData(624)]
    [InlineData(625)]
    [InlineData(2000)]
    public void EngineClonesHaveIndependentStateAcrossTwists(int discard)
    {
        var a = new Mt19937(127); a.Discard((ulong)discard); var b = a.Clone(); for (int i = 0; i < 1300; ++i) Assert.Equal(a.NextUInt32(), b.NextUInt32()); a.Seed(2); Assert.NotEqual(a.NextUInt32(), b.NextUInt32());
    }
    [Fact]
    public void DistributionCopiesSnapshotsReseedsAndTemporaryRangesRemainIndependent()
    {
        var engine = new Mt19937(42); var raw = engine.Clone(); var real = new Random01(engine); var integer = new RandomUInt64(engine, 5, 99); real.Next(); integer.Next(); Assert.Equal(raw.NextUInt32(), engine.NextUInt32());
        var snapshot = real.EngineSnapshot; var copy = new Random01(snapshot); Assert.Equal(real.Next(), copy.Next()); real.SetSeed(123); Assert.Equal(new Random01(123).Next(), real.Next()); integer.SetSeed(123); Assert.Equal(new RandomUInt64(123, 5, 99).Next(), integer.Next()); integer.Next(100, 100); Assert.Equal(5ul, integer.Minimum); Assert.Equal(99ul, integer.Maximum); integer.SetRange(99, 5); Assert.Equal(5ul, integer.Minimum);
    }
    [Fact]
    public void ReusableCustomEngineIsCopiedAndReseededWithoutChangingItsType()
    {
        var engine = new ConstantEngine(uint.MaxValue); var random = new Random01(engine); Assert.Equal(1 - System.Math.ScaleB(1.0, -53), random.Next()); random.SetSeed(0); Assert.Equal(0, random.Next()); Assert.Equal(uint.MaxValue, engine.NextUInt32());
        var integer = new RandomUInt64(engine, 0, 8); Assert.Equal(8ul, integer.Next()); integer.SetSeed(uint.MaxValue - 1); Assert.Equal(8ul, integer.Next());
    }
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(.5, .5)]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(double.NegativeInfinity, 0)]
    public void SpreadClampsLikeNative(double input, double expected)
    { var s = new UniformPointScatter(_ => { }, 1ul, new Mt19937(), input); Assert.Equal(expected, s.Spread); }
    [Fact]
    public void ZeroSpreadReturnsVoxelOrTileCentersAndStillConsumesRandomDraws()
    {
        var g = new Grid<ushort>(); g.Tree.SetValueOn(new(-2, 3, 5), 7); g.Tree.AddTile(1, new(8), 99, true); var points = new List<Vec3d>(); var s = new DenseUniformPointScatter(points.Add, 1, new Mt19937(5), 0); s.Scatter(g); Assert.Equal(513, points.Count); Assert.Equal(new Vec3d(-2, 3, 5), points[0]); Assert.All(points.Skip(1), p => Assert.Equal(new Vec3d(11.5), p)); Assert.Single(g.Tree.EnumerateLeaves());
    }
    [Fact]
    public void UniformRootTileScatteringDoesNotAllocateVoxelNodes()
    {
        var g = new Grid<bool>(); g.Tree.AddTile(3, new(-4096), true, true); var points = new List<Vec3d>(); var s = new UniformPointScatter(points.Add, 1000ul, new Mt19937(17)); Assert.True(s.Scatter(g)); Assert.Equal(1ul << 36, s.VoxelCount); Assert.Empty(g.Tree.EnumerateLeaves()); Assert.Equal(1000, points.Count);
        Assert.All(points, p => { Assert.InRange(p.X, -4096.5, -.5); Assert.InRange(p.Y, -4096.5, -.5); Assert.InRange(p.Z, -4096.5, -.5); });
    }
    [Fact]
    public void UniformSelectionWeightsVirtualVoxelCountsNotStoredEntries()
    {
        var g = new Grid<float>(); g.Tree.SetValueOn(new(-1), 1); g.Tree.AddTile(1, Coord.Zero, 1, true); int voxel = 0, tile = 0; var s = new UniformPointScatter(p => { if (p.X < -.5) ++voxel; else ++tile; }, 51300ul, new Mt19937(31)); s.Scatter(g); Assert.InRange(voxel, 60, 140); Assert.Equal(51300, voxel + tile); Assert.Single(g.Tree.EnumerateLeaves());
    }
    [Fact]
    public void JitterHasExpectedMeanVarianceAndAxisIndependence()
    {
        var g = new Grid<float>(); g.Tree.SetValueOn(Coord.Zero, 1); double sx = 0, sy = 0, sz = 0, sxx = 0, sxy = 0; const int count = 60000;
        new UniformPointScatter(p => { sx += p.X; sy += p.Y; sz += p.Z; sxx += p.X * p.X; sxy += p.X * p.Y; }, (ulong)count, new Mt19937(28)).Scatter(g);
        Assert.InRange(sx / count, -.004, .004); Assert.InRange(sy / count, -.004, .004); Assert.InRange(sz / count, -.004, .004); Assert.InRange(sxx / count, .081, .086); Assert.InRange(sxy / count, -.002, .002);
    }
    [Fact]
    public void FractionalDenseSamplingIsBernoulliAndIntegerSamplingIsExact()
    {
        var g = new Grid<byte>(); g.Tree.Fill(new(new Coord(0), new Coord(19)), 1); var fractional = new DenseUniformPointScatter(_ => { }, .35f, new Mt19937(47)); Assert.True(fractional.Scatter(g)); Assert.InRange(fractional.PointCount, 2680ul, 2920ul); var exact = new DenseUniformPointScatter(_ => { }, 2, new Mt19937(47)); exact.Scatter(g); Assert.Equal(16000ul, exact.PointCount);
    }
    [Fact]
    public void FixedCountFirstCallBecomesDensityForTheSecondGrid()
    {
        var g = new Grid<float>(); g.Tree.SetValueOn(Coord.Zero, 1); var s = new UniformPointScatter(_ => { }, 5ul, new Mt19937()); s.Scatter(g); Assert.Equal(5, s.PointsPerVolume); g.Tree.SetValueOn(new(1), 1); s.Scatter(g); Assert.Equal(10ul, s.TargetPointCount); Assert.Equal(15ul, s.PointCount); Assert.Equal(2ul, s.VoxelCount);
    }
    [Fact]
    public void SmallPositiveDensityReturnsSuccessWithNoPoints()
    { var g = new Grid<float>(); g.Tree.SetValueOn(Coord.Zero, 1); var s = new UniformPointScatter(_ => throw new Exception(), .0001f, new Mt19937()); Assert.True(s.Scatter(g)); Assert.Equal(0ul, s.TargetPointCount); Assert.Equal(1ul, s.VoxelCount); }
    [Fact]
    public void EmptyInactiveAndDisabledSamplingFollowNativeCountingStages()
    {
        var g = new Grid<float>(); g.Tree.SetValueOff(Coord.Zero, 4); var uniform = new UniformPointScatter(_ => { }, 5ul, new Mt19937()); Assert.False(uniform.Scatter(g)); Assert.Equal(0ul, uniform.VoxelCount); Assert.Equal(5ul, uniform.TargetPointCount); g.Tree.SetValueOn(Coord.Zero, 4);
        var dense = new DenseUniformPointScatter(_ => { }, .0000001f, new Mt19937()); Assert.False(dense.Scatter(g)); Assert.Equal(0ul, dense.VoxelCount); var non = new NonUniformPointScatter(_ => { }, 0, new Mt19937()); Assert.False(non.Scatter(g)); Assert.Equal(0ul, non.VoxelCount);
    }
    [Fact]
    public void NonUniformValuesDetermineCountsIncludingNegativeZeroAndBooleanValues()
    {
        var g = new Grid<int>(); g.Tree.SetValueOn(Coord.Zero, 4); g.Tree.SetValueOn(new(1), 0); g.Tree.SetValueOn(new(2), -4); var points = new List<Vec3d>(); var s = new NonUniformPointScatter(points.Add, 2, new Mt19937(), 0); Assert.True(s.Scatter(g)); Assert.Equal(8ul, s.PointCount); Assert.All(points, p => Assert.Equal(Vec3d.Zero, p));
        var b = new Grid<bool>(); b.Tree.SetValueOn(Coord.Zero, true); b.Tree.SetValueOn(new(1), false); var booleans = new NonUniformPointScatter(_ => { }, 3, new Mt19937()); booleans.Scatter(b); Assert.Equal(3ul, booleans.PointCount);
    }
    [Fact]
    public void NonUniformSupportsHalfAndOpaqueBlocksWithExplicitScalarConversion()
    {
        var half = new Grid<Half>(); half.Tree.SetValueOn(Coord.Zero, (Half)2); var s = new NonUniformPointScatter(_ => { }, 2, new Mt19937()); s.Scatter(half); Assert.Equal(4ul, s.PointCount);
        var block = new Grid<Block>(); block.Tree.SetValueOn(Coord.Zero, new Block(3)); var custom = new NonUniformPointScatter(_ => { }, 2, new Mt19937()); custom.Scatter(block, x => x.Density); Assert.Equal(6ul, custom.PointCount);
    }
    [Fact]
    public void NonUniformUsesProductOfAxisLengthsWhereUniformUsesDeterminant()
    {
        var g = new Grid<float>(); g.Tree.SetValueOn(Coord.Zero, 1); g.Transform.PostShear(1, Axis.X, Axis.Y); var u = new UniformPointScatter(_ => { }, 10f, new Mt19937()); var n = new NonUniformPointScatter(_ => { }, 10f, new Mt19937()); u.Scatter(g); n.Scatter(g); Assert.Equal(10ul, u.PointCount); Assert.InRange(n.PointCount, 14ul, 15ul);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CancellationBeforeAndDuringCallbacksKeepsDeliveredCountAndReleasesInstance(int mode)
    {
        var g = PointScatterConformanceTests.CreateGrid(7); using var cts = new CancellationTokenSource(); int delivered = 0; Action<Vec3d> append = _ => { if (++delivered == 3) cts.Cancel(); }; BasePointScatter s = Create(mode, append);
        Assert.Throws<OperationCanceledException>(() => Scatter(s, g, new(true))); Assert.Equal(0ul, s.PointCount); Assert.Throws<OperationCanceledException>(() => Scatter(s, g, cts.Token)); Assert.Equal(3ul, s.PointCount); Assert.Equal(3, delivered); Assert.True(Scatter(s, g)); Assert.True(s.PointCount > 3);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void CallbackFailureDoesNotIncrementCountAndInstanceCanBeReused(int mode)
    { var g = PointScatterConformanceTests.CreateGrid(1); bool fail = true; BasePointScatter s = Create(mode, _ => { if (fail) throw new ArithmeticException(); }); Assert.Throws<ArithmeticException>(() => Scatter(s, g)); Assert.Equal(0ul, s.PointCount); fail = false; Assert.True(Scatter(s, g)); Assert.True(s.PointCount > 0); }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BudgetsStopAllScatterFamiliesWithoutExcessDelivery(int mode)
    { var g = PointScatterConformanceTests.CreateGrid(7); int count = 0; BasePointScatter s = Create(mode, _ => ++count); s.MaximumPoints = 2; Assert.Throws<InvalidOperationException>(() => Scatter(s, g)); Assert.InRange(count, 0, 2); Assert.Equal((ulong)count, s.PointCount); }
    [Fact]
    public void HugeUniformCountIsRejectedBeforeAllocation()
    { var g = PointScatterConformanceTests.CreateGrid(1); var s = new UniformPointScatter(_ => { }, ulong.MaxValue, new Mt19937()); Assert.Throws<ArgumentOutOfRangeException>(() => s.Scatter(g)); Assert.Equal(0ul, s.PointCount); }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SourceReplacementTopologyOrTransformMutationDuringAppendFailsExplicitly(int mutation)
    {
        var g = PointScatterConformanceTests.CreateGrid(1); var s = new UniformPointScatter(_ => { if (mutation == 0) g.SetTree(new VdbTree<float>()); if (mutation == 1) g.Tree.Clear(); if (mutation == 2) g.Transform.PostTranslate(Vec3d.One); if (mutation == 3) g.Transform = Transform.CreateLinearTransform(2); }, 5ul, new Mt19937()); Assert.Throws<InvalidOperationException>(() => s.Scatter(g)); Assert.Equal(1ul, s.PointCount);
    }
    [Fact]
    public void DensityCallbackCannotReplaceSourceBeforeAppending()
    { var g = PointScatterConformanceTests.CreateGrid(1); var s = new NonUniformPointScatter(_ => throw new Exception(), 1, new Mt19937()); Assert.Throws<InvalidOperationException>(() => s.Scatter(g, x => { g.SetTree(new VdbTree<float>()); return x; })); Assert.Equal(0ul, s.PointCount); }
    [Fact]
    public void RecursiveInvocationIsRejectedWithoutBreakingOuterInvocation()
    { var g = PointScatterConformanceTests.CreateGrid(1); UniformPointScatter? s = null; s = new(_ => Assert.Throws<InvalidOperationException>(() => s!.Scatter(g)), 5ul, new Mt19937()); Assert.True(s.Scatter(g)); Assert.Equal(5ul, s.PointCount); }
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void NonfiniteAndOverflowingCountsAreRejected(float density)
    { var g = PointScatterConformanceTests.CreateGrid(1); Assert.Throws<ArgumentOutOfRangeException>(() => new DenseUniformPointScatter(_ => { }, density, new Mt19937()).Scatter(g)); Assert.Throws<ArgumentOutOfRangeException>(() => new NonUniformPointScatter(_ => { }, density, new Mt19937()).Scatter(g)); Assert.Throws<ArgumentOutOfRangeException>(() => new DenseUniformPointScatter(_ => { }, float.MaxValue, new Mt19937()).Scatter(g)); }
    [Fact]
    public void NonfiniteStoredDensityIsRejectedEvenWithFiniteGlobalDensity()
    { var g = PointScatterConformanceTests.CreateGrid(1); g.Tree.SetValueOn(Coord.Zero, float.NaN); Assert.Throws<ArgumentOutOfRangeException>(() => new NonUniformPointScatter(_ => { }, 1, new Mt19937()).Scatter(g)); }
    private static BasePointScatter Create(int mode, Action<Vec3d> append) => mode switch { 0 => new UniformPointScatter(append, 5ul, new Mt19937(42)), 1 => new DenseUniformPointScatter(append, 4, new Mt19937(42)), _ => new NonUniformPointScatter(append, 4, new Mt19937(42)) };
    private static bool Scatter(BasePointScatter s, Grid<float> g, CancellationToken token = default) => s switch { UniformPointScatter u => u.Scatter(g, token), DenseUniformPointScatter d => d.Scatter(g, token), NonUniformPointScatter n => n.Scatter(g, token), _ => throw new InvalidOperationException() };
    private readonly record struct Block(ushort Density);
    private sealed class ConstantEngine(uint value) : IRandomEngine32 { private uint _value = value; public void Seed(uint seed) => _value = seed; public uint NextUInt32() => _value; public IRandomEngine32 Clone() => new ConstantEngine(_value); }
}
