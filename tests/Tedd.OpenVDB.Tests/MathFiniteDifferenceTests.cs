using System.Numerics;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathFiniteDifferenceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public void EveryFirstDerivativeSchemeDifferentiatesLinearFields(int schemeValue)
    {
        var scheme = (DScheme)schemeValue;
        double[] values = Enumerable.Range(-3, 7).Select(x => 2.5 * x + 7).ToArray();
        double expected = scheme == DScheme.CenteredSecondTwice ? 5 : 2.5;
        Assert.InRange(System.Math.Abs(FiniteDifference.First<double>(scheme, values) - expected), 0, 1e-11);
        Assert.InRange(System.Math.Abs(FiniteDifference.First<float>(scheme, values.Select(x => (float)x).ToArray()) - expected), 0, 2e-5);
        Assert.Equal(scheme, FiniteDifference.ParseScheme("  " + FiniteDifference.SchemeToString(scheme).ToUpperInvariant() + "  "));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SecondDerivativeSchemesDifferentiateQuadraticFields(int schemeValue)
    {
        double[] values = Enumerable.Range(-3, 7).Select(x => 3d * x * x + 2 * x - 5).ToArray();
        Assert.InRange(System.Math.Abs(FiniteDifference.Second<double>((DDScheme)schemeValue, values) - 6), 0, 1e-12);
    }
    [Fact]
    public void HighOrderCentralDifferencesReproduceTheirPolynomialOrders()
    {
        double[] fourth = Enumerable.Range(-3, 7).Select(x => System.Math.Pow(x + 2, 4)).ToArray();
        double[] sixth = Enumerable.Range(-3, 7).Select(x => System.Math.Pow(x + 2, 6)).ToArray();
        Assert.InRange(System.Math.Abs(FiniteDifference.First<double>(DScheme.CenteredFourth, fourth) - 32), 0, 1e-12);
        Assert.InRange(System.Math.Abs(FiniteDifference.First<double>(DScheme.CenteredSixth, sixth) - 192), 0, 1e-11);
        Assert.InRange(System.Math.Abs(FiniteDifference.Second<double>(DDScheme.CenteredFourth, fourth) - 48), 0, 1e-12);
        Assert.InRange(System.Math.Abs(FiniteDifference.Second<double>(DDScheme.CenteredSixth, sixth) - 480), 0, 1e-11);
    }
    [Fact]
    public void AccessorAndDenseStencilMixedDifferencesMatchPolynomialGradientAndHessian()
    {
        var grid = new Grid<double>(0);
        for (int x = -3; x <= 3; ++x) for (int y = -3; y <= 3; ++y) for (int z = -3; z <= 3; ++z)
            grid.Tree.SetValueOn(new(x, y, z), 4 + 2d * x - 3 * y + 5 * z + 2 * x * x + 3 * y * y + 4 * z * z + 7 * x * y + 11 * x * z + 13 * y * z);
        var accessor = grid.GetAccessor(); var stencil = new SixthOrderDenseStencil<double>(grid); stencil.MoveTo(Coord.Zero);
        for (int order = 0; order < 3; ++order)
        {
            DDScheme scheme = (DDScheme)order;
            Assert.InRange(System.Math.Abs(FiniteDifference.Second(accessor, Coord.Zero, Axis.X, scheme) - 4), 0, 1e-12);
            Assert.InRange(System.Math.Abs(stencil.SecondDerivative(Axis.Y, scheme) - 6), 0, 1e-12);
            Assert.InRange(System.Math.Abs(FiniteDifference.MixedSecond(accessor, Coord.Zero, Axis.X, Axis.Y, scheme) - 7), 0, 1e-12);
            Assert.InRange(System.Math.Abs(stencil.MixedSecondDerivative(Axis.X, Axis.Z, scheme) - 11), 0, 1e-12);
            Assert.InRange(System.Math.Abs(stencil.MixedSecondDerivative(Axis.Y, Axis.Z, scheme) - 13), 0, 1e-12);
        }
        Assert.Equal(2, FiniteDifference.First(accessor, Coord.Zero, Axis.X));
        Assert.Equal(-3, stencil.FirstDerivative(Axis.Y)); Assert.Equal(5, stencil.FirstDerivative(Axis.Z));
    }
    [Fact]
    public void GodunovSelectionRetainsInsideOutsideSignsAndNativeNaNOrdering()
    {
        Assert.Equal(9 + 25 + 49, FiniteDifference.GodunovNormSquared(true, 2d, -3, 4, -5, 6, -7));
        Assert.Equal(0, FiniteDifference.GodunovNormSquared(false, 2d, -3, 4, -5, 6, -7));
        Assert.Equal(9 + 25 + 49, FiniteDifference.GodunovNormSquared(false, -2d, 3, -4, 5, -6, 7));
        Assert.True(double.IsNaN(FiniteDifference.GodunovNormSquared(true, double.NaN, 1, 2, 3, 4, 5)));
    }
    [Fact]
    public void InvalidSchemesAndCoordinateNeighborhoodsFailBeforeSampling()
    {
        Assert.Equal(DScheme.Unknown, FiniteDifference.ParseScheme("not-a-scheme"));
        Assert.Equal(string.Empty, FiniteDifference.SchemeToString((DScheme)99));
        Assert.Throws<ArgumentException>(() => FiniteDifference.First<double>(DScheme.CenteredSecond, new double[6]));
        Assert.Throws<ArgumentOutOfRangeException>(() => FiniteDifference.Second<double>(DDScheme.Unknown, new double[7]));
        var accessor = new Grid<double>(0).GetAccessor();
        Assert.Throws<OverflowException>(() => FiniteDifference.First(accessor, new Coord(int.MaxValue, 0, 0), Axis.X));
        Assert.Throws<OverflowException>(() => FiniteDifference.Second(accessor, new Coord(0, int.MinValue, 0), Axis.Y));
        Assert.Throws<ArgumentException>(() => FiniteDifference.MixedSecond(accessor, Coord.Zero, Axis.X, Axis.X));
        Assert.Equal(0, FiniteDifference.First(accessor, new Coord(int.MaxValue, 0, 0), Axis.X, DScheme.BackwardFirst));
    }
}
