using System.Runtime.InteropServices;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathExpandedVectorTests
{
    [Fact]
    public void VectorLayoutsContainOnlyExpectedPrimitiveComponents()
    {
        Assert.Equal(16, Marshal.SizeOf<Vec2d>());
        Assert.Equal(12, Marshal.SizeOf<Vec3f>());
        Assert.Equal(12, Marshal.SizeOf<Vec3i>());
        Assert.Equal(24, Marshal.SizeOf<Vec3d>());
        Assert.Equal(32, Marshal.SizeOf<Vec4d>());
        Assert.Equal(24, Marshal.OffsetOf<Vec4d>("<W>k__BackingField").ToInt32());
    }
    [Fact]
    public void Vec2ArithmeticProjectionAndPerpendicularMatchScalarReference()
    {
        Vec2d a = new(3, 4), b = new(-2, 5);
        Assert.Equal(new Vec2d(1, 9), a + b);
        Assert.Equal(new Vec2d(5, -1), a - b);
        Assert.Equal(new Vec2d(-6, 20), a * b);
        Assert.Equal(5, a.Length);
        Assert.Equal(14, a.Dot(b));
        Assert.Equal(23, a.Cross(b));
        Assert.Equal(new Vec2d(3, 0), a.Projection(new Vec2d(2, 0)));
        Assert.Equal(3, a.Component(new Vec2d(2, 0)));
        Vec2d perpendicular = a.Unit().ArbitraryPerpendicular();
        Assert.InRange(System.Math.Abs(a.Dot(perpendicular)), 0, 1e-14);
        Assert.InRange(System.Math.Abs(perpendicular.Length - 1), 0, 1e-14);
        Assert.Equal(new Vec2d(7, 6), 10 - a);
        Assert.Equal(new Vec2d(4, 3), 12 / a);
        Assert.Equal(new Vec2d(4, 3), a.Reversed());
    }
    [Fact]
    public void Vec4ArithmeticUsesAllFourCoordinates()
    {
        Vec4d a = new(1, 2, 3, 4), b = new(5, 6, 7, 8);
        Assert.Equal(70, a.Dot(b));
        Assert.Equal(30, a.LengthSquared);
        Assert.Equal(10, a.Sum);
        Assert.Equal(24, a.Product);
        Assert.Equal(new Vec4d(6, 8, 10, 12), a + b);
        Assert.Equal(new Vec4d(5, 12, 21, 32), a * b);
        Assert.Equal(new Vec4d(4, 3, 2, 1), a.Reversed());
        Assert.Equal(new Vec3d(1, 2, 3), a.Xyz);
        Assert.Equal(new Vec4d(1, 2, 3, 1), new Vec4d(new Vec3d(1, 2, 3)));
        Assert.Equal(new Vec4d(3, 4, 5, 6), Vec4d.Lerp(a, b, 0.5));
    }
    [Fact]
    public void FloatVectorCrossProductsPreserveFloatArithmetic()
    {
        Vec3f a = new(1, 2, 3), b = new(-3, 2, 1);
        Assert.Equal(new Vec3f(-4, -10, 8), a.Cross(b));
        Assert.Equal(4, a.Dot(b));
        Assert.Equal(new Vec3f(3, 5, 7), a + new Vec3f(2, 3, 4));
        Assert.Equal(new Vec3f(1, 3, 8), new Vec3f(8, 1, 3).Sorted());
        Assert.Equal(new Vec3d(1, 2, 3), (Vec3d)a);
        Assert.Equal(a, (Vec3f)a.AsVec3d());
        var normalized = new Vec3f(3, 0, 4).Unit(out float length);
        Assert.Equal(5, length);
        Assert.True(normalized.ApproxEquals(new Vec3f(0.6f, 0, 0.8f), 1e-7f));
    }
    [Fact]
    public void IntegerVectorsWrapArithmeticAndRetainExactCoordinateConversions()
    {
        Vec3i a = new(1, 2, 3), b = new(-3, 2, 1);
        Assert.Equal(new Vec3i(-4, -10, 8), a.Cross(b));
        Assert.Equal(4, a.Dot(b));
        Assert.Equal(new Vec3i(int.MinValue), new Vec3i(int.MaxValue) + Vec3i.One);
        Assert.Equal(new Vec3i(int.MinValue), -new Vec3i(int.MinValue));
        Assert.Equal(new Vec3i(-1, -2, 3), new Vec3i(-3, -7, 9) / 3);
        Assert.Equal(new Coord(1, 2, 3), (Coord)a);
        Assert.Equal(a, (Vec3i)new Coord(1, 2, 3));
        Assert.Equal(new Vec3d(1, 2, 3), a.AsVec3d());
        Assert.Equal(new Vec3i(-3, 2, 1), (Vec3i)new Vec3d(-3.9, 2.8, 1.1));
        Assert.Throws<DivideByZeroException>(() => a / 0);
        Assert.Throws<OverflowException>(() => new Vec3i(int.MinValue) / -1);
        Assert.Throws<OverflowException>(() => (Vec3i)new Vec3d(double.NaN));
        Assert.Throws<OverflowException>(() => (Vec3i)new Vec3d((double)int.MaxValue + 1));
    }
    [Fact]
    public void DegenerateNormalizationsMatchEachUpstreamSpecialization()
    {
        Assert.Throws<ArithmeticException>(() => Vec2d.Zero.Unit());
        Assert.Throws<ArithmeticException>(() => Vec3f.Zero.Unit());
        Assert.Throws<ArithmeticException>(() => Vec4d.Zero.Unit());
        Assert.False(Vec2d.Zero.TryNormalize(out var v2));
        Assert.False(Vec3f.Zero.TryNormalize(out var v3));
        Assert.False(Vec4d.Zero.TryNormalize(out var v4));
        Assert.Equal(Vec2d.Zero, v2);
        Assert.Equal(Vec3f.Zero, v3);
        Assert.Equal(Vec4d.Zero, v4);
        Assert.Equal(Vec2d.UnitX, Vec2d.Zero.UnitSafe());
        Assert.Equal(Vec3f.UnitX, Vec3f.Zero.UnitSafe());
        Assert.Equal(Vec4d.UnitX, Vec4d.Zero.UnitSafe());
        Assert.Equal(Vec3f.UnitX, new Vec3f(0, 1e-5f, 0).UnitSafe());
        Assert.Equal(Vec3d.UnitY, new Vec3d(0, 1e-5, 0).UnitSafe());
        Assert.Equal(Vec2d.Zero, new Vec2d(1, 2).Projection(Vec2d.Zero));
        Assert.Equal(0, new Vec2d(1, 2).Component(Vec2d.Zero));
    }
    [Fact]
    public void NaNEqualityAndSafeNormalizationUseNativeIeeeSemantics()
    {
        Assert.False(new Vec2d(double.NaN).Equals(new Vec2d(double.NaN)));
        Assert.False(new Vec3f(float.NaN).Equals(new Vec3f(float.NaN)));
        Assert.False(new Vec3d(double.NaN).Equals(new Vec3d(double.NaN)));
        Assert.False(new Vec4d(double.NaN).Equals(new Vec4d(double.NaN)));
        Assert.Equal(Vec2d.UnitX, new Vec2d(double.NaN).UnitSafe());
        Assert.Equal(Vec3f.UnitX, new Vec3f(float.NaN).UnitSafe());
        Assert.Equal(Vec3d.UnitX, new Vec3d(double.NaN).UnitSafe());
        Assert.Equal(Vec4d.UnitX, new Vec4d(double.NaN).UnitSafe());
        Assert.False(new Vec2d(double.NaN).IsFinite);
        Assert.False(new Vec3f(float.PositiveInfinity).IsFinite);
        Assert.False(new Vec4d(double.NegativeInfinity).IsFinite);
    }
    [Fact]
    public void ComponentMinMaxKeepFirstOperandForUnorderedAndEqualComparisons()
    {
        Assert.Equal(new Vec2d(3), Vec2d.Min(new(3), new(double.NaN)));
        Assert.Equal(new Vec3f(3), Vec3f.Max(new(3), new(float.NaN)));
        Assert.Equal(new Vec3d(3), Vec3d.Min(new(3), new(double.NaN)));
        Assert.Equal(new Vec4d(3), Vec4d.Max(new(3), new(double.NaN)));
        Assert.True(double.IsNaN(Vec2d.Min(new(double.NaN), new(3)).X));
        Assert.Equal(long.MinValue, BitConverter.DoubleToInt64Bits(Vec3d.Min(new(-0.0), new(0.0)).X));
        Assert.Equal(int.MinValue, BitConverter.SingleToInt32Bits(Vec3f.Max(new(-0.0f), new(0.0f)).X));
        Assert.Equal(new Vec2d(0).GetHashCode(), new Vec2d(-0.0).GetHashCode());
    }
    [Fact]
    public void SpanRoundtripsAndIndexerCapacityGuards()
    {
        double[] doubles = new double[6]; float[] floats = new float[4]; int[] integers = new int[4];
        new Vec2d(1, 2).CopyTo(doubles); Assert.Equal(new Vec2d(1, 2), Vec2d.FromSpan(doubles));
        new Vec3f(1, 2, 3).CopyTo(floats); Assert.Equal(new Vec3f(1, 2, 3), Vec3f.FromSpan(floats));
        new Vec3i(1, 2, 3).CopyTo(integers); Assert.Equal(new Vec3i(1, 2, 3), Vec3i.FromSpan(integers));
        new Vec4d(1, 2, 3, 4).CopyTo(doubles); Assert.Equal(new Vec4d(1, 2, 3, 4), Vec4d.FromSpan(doubles));
        Assert.Throws<ArgumentException>(() => Vec2d.One.CopyTo(new double[1]));
        Assert.Throws<ArgumentException>(() => Vec3f.FromSpan(new float[2]));
        Assert.Throws<ArgumentException>(() => Vec3i.One.CopyTo(new int[2]));
        Assert.Throws<ArgumentException>(() => Vec4d.FromSpan(new double[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Vec2d.One[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Vec3f.One[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Vec3i.One[3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => Vec4d.One[4]);
    }
    [Fact]
    public void RandomVectorNormalizationAndProjectionMatchIndependentIdentities()
    {
        Random random = new(92573);
        for (int i = 0; i < 1000; i++)
        {
            Vec2d a = new(random.NextDouble() * 20 - 10, random.NextDouble() * 20 - 10);
            Vec2d b = new(random.NextDouble() * 20 - 10, random.NextDouble() * 20 - 10);
            var projection = a.Projection(b);
            Assert.InRange(System.Math.Abs((a - projection).Dot(b)), 0, 1e-11);
            Assert.InRange(System.Math.Abs(a.Unit().Length - 1), 0, 1e-12);
            Vec4d four = new(a.X, a.Y, b.X, b.Y);
            Assert.InRange(System.Math.Abs(four.Unit().Length - 1), 0, 1e-12);
            Vec3f single = new((float)a.X, (float)a.Y, (float)b.X);
            Assert.InRange(System.MathF.Abs(single.Unit().Length - 1), 0, 2e-7f);
        }
    }
}
