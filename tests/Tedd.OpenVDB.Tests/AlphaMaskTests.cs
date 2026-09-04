using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class AlphaMaskTests
{
    [Theory, InlineData(-1, 0), InlineData(0, 0), InlineData(0.25, 0.15625), InlineData(0.5, 0.5), InlineData(0.75, 0.84375), InlineData(1, 1), InlineData(2, 1)]
    public void SmoothWeightsMatchCubicStep(float value, float expected)
    {
        var target = new Grid<float>(); var mask = new Grid<float>(value);
        var sampler = new AlphaMask<float>(target, mask);
        Assert.Equal(expected > 0, sampler.Evaluate(new(0), out float alpha, out float beta));
        Assert.Equal(expected, alpha); Assert.Equal(1 - expected, beta);
        new AlphaMask<float>(target, mask, invert: true).Evaluate(new(0), out alpha, out beta);
        Assert.Equal(1 - expected, alpha); Assert.Equal(expected, beta);
    }
    [Fact]
    public void MaskSamplingAccountsForVoxelScaleAndInactiveStoredValues()
    {
        var target = new Grid<double>() { Transform = Transform.CreateLinearTransform(0.5) };
        var mask = new Grid<float>(); mask.Tree.SetValueOff(new(1, 0, 0), 1);
        new AlphaMask<double>(target, mask).Evaluate(new(1, 0, 0), out float alpha, out float beta);
        Assert.Equal(0.5f, alpha); Assert.Equal(0.5f, beta);
    }
    [Fact]
    public void NanMaskProducesZeroWeightBeforeInversionAndRangesAreValidated()
    {
        var target = new Grid<float>(); var mask = new Grid<float>(float.NaN);
        Assert.False(new AlphaMask<float>(target, mask).Evaluate(new(0), out float a, out float b));
        Assert.Equal(0, a); Assert.Equal(1, b);
        Assert.Throws<ArgumentException>(() => new AlphaMask<float>(target, mask, 1, 1));
        Assert.Throws<ArgumentException>(() => new AlphaMask<float>(target, mask, float.NaN, 1));
    }
}
