using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathScalarTests
{
    [Fact]
    public void ClampVariantsPreserveNativeNaNAndSignedZeroRules()
    {
        Assert.Equal(-2, VdbMath.Clamp(double.NaN, -2, 3)); Assert.Equal(0, VdbMath.Clamp01(double.NaN)); double nan = double.NaN; Assert.True(VdbMath.ClampTest01(ref nan)); Assert.Equal(1, nan);
        double zero = -0.0; Assert.False(VdbMath.ClampTest01(ref zero)); Assert.True(double.IsNegative(zero)); Assert.False(double.IsNegative(VdbMath.Clamp01(-0.0))); Assert.Equal(0, VdbMath.SmoothUnitStep(double.NaN));
        Assert.Throws<ArgumentException>(() => VdbMath.Clamp(0.0, 2, 1)); Assert.Throws<ArgumentException>(() => VdbMath.SmoothUnitStep(0.0, 1, 1)); Assert.Throws<ArgumentException>(() => VdbMath.SmoothUnitStep(0.0, double.NaN, 1));
    }
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(0.25, 0.15625)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.75, 0.84375)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void SmoothUnitStepMatchesCubicHermiteEndpointsAndInterior(double input, double expected)
    {
        Assert.Equal(expected, VdbMath.SmoothUnitStep(input)); Assert.Equal(expected, VdbMath.SmoothUnitStep(2 * input - 3, -3, -1));
    }
    [Fact]
    public void NativeMinMaxGroupingsAndNaNPriorityArePreserved()
    {
        double[] values = [1, 0, double.NaN, -3]; Assert.Equal(0, VdbMath.Min<double>(values)); Assert.Equal(0, VdbMath.Min(1.0, 0, double.NaN, -3)); Assert.Equal(1, VdbMath.Max(1.0, 0, double.NaN, 3));
        Assert.True(double.IsNaN(VdbMath.Min(double.NaN, 0))); Assert.True(double.IsNaN(VdbMath.Max(double.NaN, 0))); Assert.Equal(0, VdbMath.Min(0.0, double.NaN)); Assert.Equal(0, VdbMath.Max(0.0, double.NaN));
        Assert.True(double.IsNegative(VdbMath.Min(-0.0, 0.0))); Assert.False(double.IsNegative(VdbMath.Min(0.0, -0.0))); Assert.True(double.IsNegative(VdbMath.Max(-0.0, 0.0)));
    }
    [Fact]
    public void SpanExtremaSupportEmptyGuardsSingleValuesAndLongerOrderedReductions()
    {
        Assert.Throws<ArgumentException>(() => VdbMath.Min<double>([])); Assert.Throws<ArgumentException>(() => VdbMath.Max<double>([])); Assert.Equal(3, VdbMath.Min<int>([3]));
        Assert.Equal(-7, VdbMath.Min<int>([3, 4, 1, 8, 9, -2, 5, 6, -7])); Assert.Equal(9, VdbMath.Max<int>([3, 4, 1, 8, 9, -2, 5, 6, -7]));
    }
    [Fact]
    public void ExtremaIndicesChooseHighestTiedIndexAndPreserveUnorderedComparisons()
    {
        Assert.Equal(2, VdbMath.MinIndex(new Vec3i(2, 2, 2))); Assert.Equal(2, VdbMath.MaxIndex(new Vec3f(2, 2, 2))); Assert.Equal(1, VdbMath.MinIndex(new Vec3d(1, 1, 3))); Assert.Equal(1, VdbMath.MaxIndex(new Vec3d(3, 3, 1)));
        Assert.Equal(0, VdbMath.MinIndex(new Vec3d(double.NaN, -10, -20))); Assert.Equal(2, VdbMath.MinIndex(new Vec3d(1, double.NaN, -1))); Assert.Equal(0, VdbMath.MaxIndex(new Vec3d(double.NaN, 10, 20)));
    }
    [Theory]
    [InlineData(-2.5, -2)]
    [InlineData(-1.5, -1)]
    [InlineData(-0.5, 0)]
    [InlineData(0.5, 1)]
    [InlineData(1.5, 2)]
    [InlineData(2.5, 3)]
    public void NativeRoundTiesGoTowardPositiveInfinity(double value, double expected) { Assert.Equal(expected, VdbMath.Round(value)); Assert.Equal((float)expected, VdbMath.Round((float)value)); }
    [Fact]
    public void RoundingWithBasisUsesTruncatingRemainderEvenForNegativeValues()
    {
        Assert.Equal(-2, VdbMath.RoundUp(-5.5, 2)); Assert.Equal(-4, VdbMath.RoundDown(-5.5, 2)); Assert.Equal(-6, VdbMath.RoundUp(-5.5, -2)); Assert.Equal(-4, VdbMath.RoundDown(-5.5, -2));
        Assert.Equal(-2, VdbMath.RoundUp(-5, 2)); Assert.Equal(-4, VdbMath.RoundDown(-5, 2)); Assert.Equal(-1, VdbMath.Remainder(-5, 2)); Assert.Throws<DivideByZeroException>(() => VdbMath.RoundUp(1, 0)); Assert.True(double.IsNaN(VdbMath.RoundDown(1.0, 0)));
    }
    [Fact]
    public void IntegerFractionalAndEuclideanPartsHaveDistinctNegativeSemantics()
    {
        Assert.Equal(-2, VdbMath.IntegerPart(-2.75)); Assert.Equal(-0.75, VdbMath.FractionalPart(-2.75)); Assert.Equal(0.25, VdbMath.EuclideanRemainder(-2.75)); Assert.Equal(-3, VdbMath.Floor(-2.75)); Assert.Equal(-2, VdbMath.Ceil(-2.75));
        Assert.True(double.IsNegative(VdbMath.IntegerPart(-0.0))); Assert.True(double.IsNegative(VdbMath.FractionalPart(-0.0))); Assert.False(double.IsNegative(VdbMath.EuclideanRemainder(-0.0)));
    }
    [Fact]
    public void DecimalTruncateRetainsRoundingAndNativeUnsignedFactorOverflow()
    {
        Assert.Equal(1.13, VdbMath.Truncate(1.125, 2)); Assert.Equal(-1.12, VdbMath.Truncate(-1.125, 2)); Assert.Equal(-1, VdbMath.Truncate(-1.5, 0)); Assert.True(double.IsNaN(VdbMath.Truncate(1.0, 64))); Assert.True(float.IsNaN(VdbMath.Truncate(1f, 65))); Assert.Throws<ArgumentOutOfRangeException>(() => VdbMath.Truncate(1.0, uint.MaxValue));
    }
    [Fact]
    public void SignAndZeroCrossingPreserveLiteralNativeProducts()
    {
        Assert.Equal(0, VdbMath.Sign(double.NaN)); Assert.Equal(0, VdbMath.Sign(-0.0)); Assert.True(VdbMath.SignChange(-1.0, -0.0)); Assert.False(VdbMath.SignChange(1.0, double.NaN));
        Assert.True(VdbMath.ZeroCrossing(1e-300, 1e-300)); Assert.False(VdbMath.ZeroCrossing(0.0, double.PositiveInfinity)); Assert.True(VdbMath.ZeroCrossing((Half)0, (Half)1)); Assert.True(VdbMath.ZeroCrossing((short)-200, (short)200)); Assert.False(VdbMath.ZeroCrossing((short)200, (short)200));
        Assert.False(VdbMath.IsNegative(double.NaN)); Assert.False(VdbMath.IsNegative(true)); Assert.True(VdbMath.IsZero(-0.0)); Assert.False(VdbMath.Negative(true)); Assert.True(VdbMath.Negative(false));
    }
    [Fact]
    public void PowersAndInverseKeepIntegerAndIeeeContracts()
    {
        Assert.Equal(1, VdbMath.Pow(0.0, 0)); Assert.Equal(0.125, VdbMath.Pow(2.0, -3)); Assert.Equal(-8, VdbMath.Pow3(-2.0)); Assert.Equal(16, VdbMath.Pow4(-2.0)); Assert.Equal(0UL, VdbMath.Pow(2UL, -3)); Assert.Equal(1UL, VdbMath.Pow2(ulong.MaxValue));
        Assert.True(double.IsPositiveInfinity(VdbMath.Inv(0.0))); Assert.True(double.IsNegativeInfinity(VdbMath.Inv(-0.0))); Assert.True(double.IsNaN(VdbMath.Pow(-2.0, 0.5))); Assert.Throws<ArgumentOutOfRangeException>(() => VdbMath.Pow(1.0, int.MinValue)); Assert.Throws<DivideByZeroException>(() => VdbMath.Inv(0));
    }
    [Fact]
    public void NarrowIntegerApproximateComparisonUsesNativePromotion()
    {
        Assert.False(VdbMath.IsApproxLarger((short)-30000, (short)30000, (short)0)); Assert.True(VdbMath.IsApproxLarger((byte)200, (byte)10, (byte)0)); Assert.True(VdbMath.IsApproxLarger(1.0, 1.05, 0.1)); Assert.False(VdbMath.IsApproxLarger(double.NaN, 0, 0.1));
    }
    [Fact]
    public void ULPComparisonHandlesSignedZerosAdjacentRepresentationsAndNaNPayloads()
    {
        Assert.True(VdbMath.IsUlpsEqual(0.0, -0.0, 0)); Assert.False(VdbMath.IsUlpsEqual(1.0, double.BitIncrement(1.0), 0)); Assert.True(VdbMath.IsUlpsEqual(1.0, double.BitIncrement(1.0), 1)); Assert.True(VdbMath.IsUlpsEqual(-1f, float.BitIncrement(-1f), 1));
        double nan = BitConverter.Int64BitsToDouble(0x7ff8000000000001), adjacentNan = BitConverter.Int64BitsToDouble(0x7ff8000000000002); Assert.True(VdbMath.IsUlpsEqual(nan, nan, 0)); Assert.True(VdbMath.IsUlpsEqual(nan, adjacentNan, 1)); Assert.False(VdbMath.IsUlpsEqual(nan, adjacentNan, 0)); Assert.False(VdbMath.IsUlpsEqual(0.0, 0.0, -1));
        Assert.False(VdbMath.IsUlpsEqual(double.MaxValue, -double.MaxValue, long.MaxValue)); Assert.False(VdbMath.IsUlpsEqual(float.MaxValue, -float.MaxValue, int.MaxValue)); Assert.Equal(unchecked((long)0x8000000000000000), VdbMath.DoubleToInt64(-0.0)); Assert.Equal(unchecked((int)0x80000000), VdbMath.FloatToInt32(-0.0f));
    }
    [Fact]
    public void FloorCeilRejectUnrepresentableIntegralConversions()
    {
        Assert.Equal(int.MaxValue, VdbMath.Floor((double)int.MaxValue)); Assert.Equal(int.MinValue, VdbMath.Ceil((double)int.MinValue)); Assert.Throws<OverflowException>(() => VdbMath.Floor(double.NaN)); Assert.Throws<OverflowException>(() => VdbMath.Ceil(double.PositiveInfinity)); Assert.Throws<OverflowException>(() => VdbMath.Floor(2147483648f)); Assert.Throws<OverflowException>(() => VdbMath.Ceil(-2147483649.0));
    }
    [Fact]
    public void ConstantsAndChopPreserveNativeThresholds()
    {
        Assert.Equal(System.Math.PI, VdbMath.Pi<double>()); Assert.Equal(System.MathF.PI, VdbMath.Pi<float>()); Assert.Equal(0, VdbMath.Tolerance<int>()); Assert.Equal(1e-8f, VdbMath.Tolerance<float>()); Assert.Equal(1e-15, VdbMath.Tolerance<double>()); Assert.Equal((Half)0.00097656f, VdbMath.Tolerance<Half>()); Assert.Equal((Half)0.00390625f, VdbMath.Delta<Half>());
        Assert.Equal(0, VdbMath.Chop(-0.01, 0.1)); Assert.Equal(-0.1, VdbMath.Chop(-0.1, 0.1)); Assert.True(double.IsNaN(VdbMath.Chop(double.NaN, 1))); Assert.Throws<OverflowException>(() => VdbMath.Abs(long.MinValue)); Assert.True(VdbMath.Abs(true));
    }
    [Fact]
    public void SpanAndGenericScalarKernelsAllocateNoManagedMemory()
    {
        ReadOnlySpan<double> values = [1, 2, 3, 4, 5, 6, 7, 8]; double total = 0;
        for (int i = 0; i < 2000; ++i) total += VdbMath.Min(values) + VdbMath.Max(values) + VdbMath.SmoothUnitStep(0.5);
        long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 10000; ++i) total += VdbMath.Min(values) + VdbMath.Max(values) + VdbMath.SmoothUnitStep(0.5); long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(114000, total); Assert.Equal(0, allocated);
    }
}
