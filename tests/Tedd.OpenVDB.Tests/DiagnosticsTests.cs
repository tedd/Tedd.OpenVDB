using System.Globalization;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class DiagnosticsTests
{
    [Fact]
    public void ScalarAndVectorNonfiniteChecksInspectAllComponents()
    {
        Assert.True(new CheckNan<float>().Fails(float.NaN)); Assert.False(new CheckNan<float>().Fails(float.PositiveInfinity)); Assert.True(new CheckInf<double>().Fails(double.NegativeInfinity)); Assert.False(new CheckInf<double>().Fails(double.NaN));
        Assert.True(new CheckFinite<Vec4f>().Fails(new(0, 1, 2, float.NaN))); Assert.True(new CheckInf<Vec2d>().Fails(new(0, double.PositiveInfinity))); Assert.False(new CheckFinite<Vec3i>().Fails(new(int.MaxValue, int.MinValue, 0)));
        Assert.False(new CheckNan<long>().Fails(long.MaxValue)); Assert.False(new CheckFinite<ulong>().Fails(ulong.MaxValue)); Assert.False(new CheckFinite<bool>().Fails(true)); Assert.True(new CheckNan<Half>().Fails(Half.NaN));
        Assert.Throws<NotSupportedException>(() => new CheckNan<decimal>());
    }
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void RangeEndpointsAreIndependentAndNaNRemainsUnordered(bool lower, bool upper)
    {
        var check = new CheckRange<double>(-1, 2, lower, upper); Assert.Equal(!lower, check.Fails(-1)); Assert.Equal(!upper, check.Fails(2)); Assert.False(check.Fails(0)); Assert.True(check.Fails(-2)); Assert.True(check.Fails(3)); Assert.False(check.Fails(double.NaN));
        Assert.Equal($"outside the value range {(lower ? "[" : "]")}-1,2{(upper ? "]" : "[")}", check.Description);
        Assert.Throws<ArgumentException>(() => new CheckRange<double>(2, -1)); Assert.False(new CheckRange<double>(double.NaN, 2).Fails(-1000));
    }
    [Fact]
    public void VectorBoundsBroadcastAndComparisonUsesEveryComponent()
    {
        Assert.True(new CheckMin<Vec3f>(new(-1)).Fails(new(0, 0, -2))); Assert.True(new CheckMax<Vec4d>(new(1)).Fails(new(0, 0, 0, 2)));
        Assert.False(new CheckRange<Vec2f>(new(-1), new(1)).Fails(new(0.5f, -0.5f))); Assert.Throws<ArgumentException>(() => new CheckRange<Vec3d>(new(-1, 0, 1), new(2)));
        Assert.False(new CheckRange<Vec3f>(new(float.NaN), new(2)).Fails(new(-9))); Assert.True(new CheckRange<Vec3f>(new(float.NaN), new(2)).Fails(new(-9, 3, -9)));
    }
    [Fact]
    public void MagnitudeUsesAbsoluteInputsNativePrecisionAndDefaultInactiveIteration()
    {
        var check = new CheckMagnitude<float>(-3, -0.25f); Assert.False(check.Fails(-3.25f)); Assert.True(check.Fails(3.251f)); Assert.False(check.Fails(float.NaN)); Assert.Equal(ValueIterationMode.Inactive, check.IterationMode);
        Assert.Equal("not equal to +/-3 with a tolerance of 0.25", check.Description); Assert.Equal(1e-8f, new CheckMagnitude<float>(3).Tolerance); Assert.Equal(1e-15, new CheckMagnitude<double>(3).Tolerance); Assert.Equal((Half)0.00097656f, new CheckMagnitude<Half>((Half)3).Tolerance);
        Assert.False(new CheckMagnitude<Vec3d>(new Vec3d(3), new Vec3d(0)).Fails(new(-3, 3, -3))); Assert.True(new CheckMagnitude<Vec3d>(new Vec3d(3), new Vec3d(0)).Fails(new(-3, 0, -3)));
        Assert.True(new CheckMagnitude<uint>(5, 1).Fails(4)); Assert.False(new CheckMagnitude<uint>(5, 1).Fails(6));
    }
    [Fact]
    public void Int64AndUInt64BoundsPreserveBitsBeyondDoublePrecision()
    {
        const long min = 9007199254740993, max = 9007199254741001; var check = new CheckRange<long>(min, max); Assert.True(check.Fails(min - 1)); Assert.False(check.Fails(min)); Assert.True(check.Fails(max + 1));
        Assert.Equal("outside the value range [9007199254740993,9007199254741001]", check.Description);
        Assert.True(new CheckMin<ulong>(ulong.MaxValue).Fails(ulong.MaxValue - 1)); Assert.False(new CheckMax<ulong>(ulong.MaxValue - 1).Fails(ulong.MaxValue - 1));
        Assert.True(new CheckMagnitude<long>(min, 0).Fails(min + 1)); Assert.False(new CheckMagnitude<long>(min, 0).Fails(-min));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DiagnoseSeparatesFailedEntriesFromMaskedVoxelVolumeAndAccumulates(bool parallel)
    {
        var grid = new Grid<double>(-2) { Transform = new(Mat4d.CreateScale(0.5)) }; grid.Tree.SetValue(Coord.Zero, 3); grid.Tree.AddTile(3, new(4096, 0, 0), 4, true);
        var diagnose = new Diagnose<double>(grid); var check = new CheckRange<double>(0, 1);
        Assert.Equal("Background is outside the value range [0,1]\n1 tile is outside the value range [0,1]\n1 voxel is outside the value range [0,1]\n", diagnose.Check(check, true, parallel: parallel));
        Assert.Equal(3UL, diagnose.FailureCount); Assert.Equal((1UL << 36) + 1, diagnose.ValueCount); Assert.Equal(grid.Transform.Matrix, diagnose.Mask.Transform.Matrix);
        diagnose.Check(check, true, parallel: parallel); Assert.Equal(6UL, diagnose.FailureCount); Assert.Equal((1UL << 36) + 1, diagnose.ValueCount); Assert.Equal(3, grid.Tree.GetValue(Coord.Zero));
        Grid<bool> priorMask = diagnose.Mask; diagnose.Clear(); Assert.NotSame(priorMask, diagnose.Mask); Assert.True(diagnose.Mask.Transform.IsIdentity); Assert.Equal(0UL, diagnose.FailureCount); Assert.Equal(0UL, diagnose.ValueCount); Assert.Equal((1UL << 36) + 1, priorMask.ActiveVoxelCount);
    }
    [Fact]
    public void MaskUpdatesAndCheckFlagsAreIndependent()
    {
        var grid = new Grid<float>(float.NaN); grid.Tree.SetValue(Coord.Zero, float.NaN); var diagnose = new Diagnose<float>(grid);
        Assert.Equal("Background is NaN\n1 voxel is NaN\n", diagnose.Check(new CheckNan<float>(), checkTiles: false)); Assert.Equal(2UL, diagnose.FailureCount); Assert.Equal(0UL, diagnose.ValueCount);
        Assert.Equal("", diagnose.Check(new CheckNan<float>(), true, false, false, false)); Assert.Equal(2UL, diagnose.FailureCount);
        diagnose.Check(new CheckNan<float>(), true, checkTiles: false, checkBackground: false); Assert.Equal(3UL, diagnose.FailureCount); Assert.True(diagnose.Mask.Tree.IsValueOn(Coord.Zero));
        diagnose.Mask.Tree.SetValue(new(1, 0, 0), true); Assert.Equal(2UL, diagnose.ValueCount); Assert.Equal(3UL, diagnose.FailureCount);
    }
    [Fact]
    public void InactiveAndAllModesInspectStoredLeafValuesAndTiles()
    {
        var grid = new Grid<float>(); grid.Tree.SetValue(Coord.Zero, 2); grid.Tree.SetValueOff(new(1, 0, 0), 3); grid.Tree.AddTile(1, new(16, 0, 0), 4, false);
        var inactive = new Diagnose<float>(grid); inactive.Check(new CheckMax<float>(1, ValueIterationMode.Inactive), true, checkBackground: false, parallel: false); Assert.Equal(2UL, inactive.FailureCount); Assert.Equal(513UL, inactive.ValueCount); Assert.False(inactive.Mask.Tree.IsValueOn(Coord.Zero));
        var all = new Diagnose<float>(grid); all.Check(new CheckMax<float>(1, ValueIterationMode.All), true, checkBackground: false); Assert.Equal(3UL, all.FailureCount); Assert.Equal(514UL, all.ValueCount);
    }
    [Fact]
    public void CancellationAndPredicateExceptionsDoNotPublishPartialCountsOrMask()
    {
        var grid = new Grid<int>(); grid.Tree.SetValue(Coord.Zero, 2); var diagnose = new Diagnose<int>(grid); diagnose.Check(new CheckMax<int>(1), true);
        using var cancellation = new CancellationTokenSource(); var predicate = new CancelCheck(cancellation);
        Assert.ThrowsAny<OperationCanceledException>(() => diagnose.Check(predicate, true, checkTiles: false, checkBackground: false, parallel: false, cancellationToken: cancellation.Token)); Assert.Equal(1UL, diagnose.FailureCount); Assert.Equal(1UL, diagnose.ValueCount);
        Assert.Throws<OperationCanceledException>(() => diagnose.Check(predicate, cancellationToken: new CancellationToken(true)));
        Assert.Throws<InvalidOperationException>(() => diagnose.Check(new ThrowCheck(), true, checkTiles: false, checkBackground: false, parallel: false)); Assert.Equal(1UL, diagnose.FailureCount);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void NormGradSquaresBoundsAndMatchesAnalyticPlaneForEveryBias(int scheme)
    {
        var grid = Plane(); var check = new CheckNormGrad<double>(grid, 0.4, 0.6, (BiasedGradientScheme)scheme); Assert.False(check.FailsVoxel(Coord.Zero, 0)); Assert.True(check.FailsTile(new(Coord.Zero, new Coord(1)), 100)); Assert.True(check.Fails(1));
        Assert.True(new CheckNormGrad<double>(grid, -0.6, -0.4, (BiasedGradientScheme)scheme).FailsVoxel(Coord.Zero, 0)); Assert.Equal("outside the range of NormGrad [0.6,0.4]", new CheckNormGrad<double>(grid, -0.6, -0.4).Description);
    }
    [Theory]
    [InlineData(EikonalStencilKind.Weno)]
    [InlineData(EikonalStencilKind.Gradient)]
    public void EikonalOnlyChecksCrossingsAndUsesUnsquaredBounds(EikonalStencilKind kind)
    {
        var grid = Plane(); var check = new CheckEikonal<double>(grid, 0.4, 0.6, kind); Assert.True(check.FailsVoxel(Coord.Zero, 0)); Assert.False(check.FailsVoxel(new(2, 0, 0), 0.5)); Assert.True(check.FailsTile(new(Coord.Zero, new Coord(1)), 3));
        Assert.False(new CheckEikonal<double>(grid, 0.2, 0.3, kind).FailsVoxel(Coord.Zero, 0));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentialChecksCacheScaleAndAccessorAccordingToNativeCopyRules(bool eikonal)
    {
        var grid = Plane(); DiagnosticCheck<double> check = eikonal ? new CheckEikonal<double>(grid, 0.2, 0.3) : new CheckNormGrad<double>(grid, 0.4, 0.6);
        Assert.False(check.FailsVoxel(Coord.Zero, 0)); grid.Transform = new(Mat4d.CreateScale(1));
        Assert.False(check.FailsVoxel(Coord.Zero, 0)); Assert.Equal(eikonal, check.Copy().FailsVoxel(Coord.Zero, 0));
        var nonuniform = Plane(); nonuniform.Transform.PreScale(new Vec3d(1, 2, 1)); Assert.Throws<ArgumentException>(() => new CheckNormGrad<double>(nonuniform, 0, 1)); Assert.Throws<ArgumentException>(() => new CheckEikonal<double>(nonuniform, 0, 1));
        Assert.Throws<ArgumentException>(() => new CheckNormGrad<double>(grid, 2, 1)); Assert.Throws<NotSupportedException>(() => new CheckNormGrad<Half>(new Grid<Half>(), (Half)0, (Half)1));
    }
    public static IEnumerable<object[]> DivergenceSchemes() { for (int i = 0; i < 14; ++i) yield return [i]; }
    [Theory]
    [MemberData(nameof(DivergenceSchemes))]
    public void CorrectedDivergenceAdapterUsesNativeScalarDerivativeUnits(int scheme)
    {
        var grid = new Grid<Vec3f>(Vec3f.Zero) { Transform = new(Mat4d.CreateScale(0.5)) };
        for (int x = -4; x <= 4; ++x) for (int y = -4; y <= 4; ++y) for (int z = -4; z <= 4; ++z) grid.Tree.SetValue(new(x, y, z), new(0.5f * x, y, -0.25f * z));
        var check = new CheckDivergence<Vec3f>(grid, 2.4, 2.6, (DScheme)scheme); Assert.Equal(scheme == 0, check.FailsVoxel(Coord.Zero, Vec3f.Zero)); Assert.True(check.FailsTile(new(Coord.Zero, new Coord(1)), Vec3f.One)); Assert.False(check.FailsDivergence(2.5)); Assert.True(check.FailsDivergence(3));
    }
    [Fact]
    public void LevelSetAndFogFastChecksPreserveOrderingMessagesAndHalfClassification()
    {
        Assert.Equal("Value type is not floating point\n", new CheckLevelSet<int>(new Grid<int>()).Check(0)); Assert.Equal("Value type is not floating point", CheckFogVolume<Half>.CheckValueType());
        var level = new Grid<float>(3) { GridClass = GridClass.LevelSet }; var check = new CheckLevelSet<float>(level); Assert.Equal("", check.Check(5)); level.Tree.AddTile(1, Coord.Zero, 1, true); Assert.Equal("Has active tile values\n", check.Check(5));
        level.GridClass = GridClass.Unknown; Assert.Equal("Class type is not \"GRID_LEVEL_SET\"\n", check.Check(9)); Assert.Equal(0UL, check.FailureCount);
        var fog = new Grid<float>(); Assert.Equal("Class type is not \"GRID_LEVEL_SET\"", new CheckFogVolume<float>(fog).Check()); fog.GridClass = GridClass.FogVolume; Assert.Equal("", Diagnostics.CheckFogVolume(fog)); fog.Tree.SetValue(Coord.Zero, 2); Assert.Equal("1 voxel is outside the value range [0,1]\n", Diagnostics.CheckFogVolume(fog));
        var nan = new Grid<double>(double.NaN) { GridClass = GridClass.LevelSet }; Assert.Equal("", Diagnostics.CheckLevelSet(nan, 4)); Assert.Equal("Background is not finite\n", Diagnostics.CheckLevelSet(nan, 6));
    }
    [Fact]
    public void FogInactiveValuesAndLevelSetMagnitudeUseNativeTolerances()
    {
        var fog = new Grid<float> { GridClass = GridClass.FogVolume }; fog.Tree.SetValueOff(Coord.Zero, 1e-9f); Assert.Equal("", new CheckFogVolume<float>(fog).CheckInactiveValues()); fog.Tree.SetValueOff(Coord.Zero, 2e-8f); var check = new CheckFogVolume<float>(fog); Assert.Contains("1 voxel", check.CheckInactiveValues(true)); Assert.Equal(1UL, check.ValueCount);
        var level = new Grid<double>(3) { GridClass = GridClass.LevelSet }; level.Tree.SetValueOff(Coord.Zero, -3); Assert.Equal("", new CheckLevelSet<double>(level).CheckInactiveValues()); level.Tree.SetValueOff(Coord.Zero, -2.99); Assert.Contains("1 voxel", new CheckLevelSet<double>(level).CheckInactiveValues());
    }
    [Fact]
    public void UniqueInactiveValuesExcludeImplicitBackgroundAndActiveValues()
    {
        var grid = new Grid<long>(3); Assert.True(Diagnostics.UniqueInactiveValues(grid, out long[] empty, 0)); Assert.Empty(empty); grid.Tree.AddTile(3, new(4096, 0, 0), 7, false); Assert.True(Diagnostics.UniqueInactiveValues(grid, out long[] tile, 1)); Assert.Equal(new long[] { 7 }, tile);
        grid.Tree.SetValueOff(Coord.Zero, 9); grid.Tree.SetValue(new(1, 0, 0), 99); Assert.True(Diagnostics.UniqueInactiveValues(grid, out long[] all, 3)); Assert.Equal(new long[] { 3, 7, 9 }, all); Assert.False(Diagnostics.UniqueInactiveValues(grid, out long[] partial, 1)); Assert.True(partial.Length > 1); Assert.DoesNotContain(99, partial);
    }
    [Fact]
    public void UniqueVectorOrderIsLexicographicAndSignedZeroIsOneValue()
    {
        var vectors = new Grid<Vec3f>(new Vec3f(3)); vectors.Tree.SetValueOff(Coord.Zero, new Vec3f(-1, 100, 0)); vectors.Tree.SetValueOff(new(1, 0, 0), new Vec3f(0, -100, 0)); Assert.True(Diagnostics.UniqueInactiveValues(vectors, out Vec3f[] values, 3)); Assert.Equal(new Vec3f(-1, 100, 0), values[0]); Assert.Equal(new Vec3f(0, -100, 0), values[1]);
        var scalar = new Grid<double>(); scalar.Tree.TouchLeaf(Coord.Zero).SetValueOff(0, -0.0); Assert.True(Diagnostics.UniqueInactiveValues(scalar, out double[] zeros, 1)); Assert.Single(zeros);
    }
    [Fact]
    public void DiagnosticTextUsesInvariantNativeGeneralPrecision()
    {
        CultureInfo previous = CultureInfo.CurrentCulture; try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Assert.Equal("outside the value range [0.125,1.75]", new CheckRange<double>(0.125, 1.75).Description); Assert.Equal("larger than 12345678", new CheckMax<int>(12345678).Description); } finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void RepeatedNumericPredicateCallsAllocateNoManagedMemory()
    {
        var finite = new CheckFinite<Vec4d>(); var range = new CheckRange<Vec3f>(new Vec3f(-2), new Vec3f(2)); var magnitude = new CheckMagnitude<long>(9007199254740993, 0); int failures = 0;
        for (int i = 0; i < 2000; ++i) { failures += finite.Fails(new Vec4d(1, 2, 3, 4)) ? 1 : 0; failures += range.Fails(new Vec3f(3, 1, 0)) ? 1 : 0; failures += magnitude.Fails(9007199254740993) ? 1 : 0; }
        // The first optimized generic instantiation can be promoted after the initial warm-up on some runtimes.
        for (int i = 0; i < 10000; ++i) { failures += finite.Fails(new Vec4d(1, 2, 3, 4)) ? 1 : 0; failures += range.Fails(new Vec3f(3, 1, 0)) ? 1 : 0; failures += magnitude.Fails(9007199254740993) ? 1 : 0; }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; ++i) { failures += finite.Fails(new Vec4d(1, 2, 3, 4)) ? 1 : 0; failures += range.Fails(new Vec3f(3, 1, 0)) ? 1 : 0; failures += magnitude.Fails(9007199254740993) ? 1 : 0; }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before; Assert.Equal(22000, failures); Assert.Equal(0, allocated);
    }

    private static Grid<double> Plane() { var grid = new Grid<double>(3) { Transform = new(Mat4d.CreateScale(0.5)) }; for (int x = -5; x <= 5; ++x) for (int y = -4; y <= 4; ++y) for (int z = -4; z <= 4; ++z) grid.Tree.SetValue(new(x, y, z), 0.25 * x); return grid; }
    private sealed class CancelCheck(CancellationTokenSource source) : DiagnosticCheck<int> { public override string Description => "cancelled"; public override bool Fails(int value) { source.Cancel(); return true; } }
    private sealed class ThrowCheck : DiagnosticCheck<int> { public override string Description => "throw"; public override bool Fails(int value) => throw new InvalidOperationException(); }
}
