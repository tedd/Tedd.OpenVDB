// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class StatisticsTests
{
    [Fact]
    public void EmptyStatisticsHaveSourceDefaults()
    {
        var s = new Stats(); Assert.Equal(0UL, s.Count); Assert.Equal(double.MaxValue, s.Minimum); Assert.Equal(-double.MaxValue, s.Maximum); Assert.Equal(0, s.Mean); Assert.Equal(0, s.Variance);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(19)]
    [InlineData(1000)]
    public void IncrementalAndWeightedStatisticsMatchPopulation(int count)
    {
        var scalar = new Stats(); var weighted = new Stats(); for (int i = 0; i < count; i++) { scalar.Add(3); scalar.Add(7); }
        weighted.Add(3, (ulong)count); weighted.Add(7, (ulong)count);
        Assert.Equal((ulong)count * 2, scalar.Count); Assert.Equal(3, scalar.Minimum); Assert.Equal(7, scalar.Maximum); Assert.Equal(5, scalar.Mean, 12); Assert.Equal(4, scalar.Variance, 12); Assert.Equal(scalar.Mean, weighted.Mean, 12); Assert.Equal(scalar.Variance, weighted.Variance, 12);
    }
    [Fact]
    public void PartitionMergeAndSelfMergePreserveVariance()
    {
        var a = new Stats(); var b = new Stats(); for (int i = 0; i < 100; i++) (i < 50 ? a : b).Add(1e9 + i); a.Add(b);
        Assert.Equal(1e9 + 49.5, a.Mean, 8); Assert.Equal(833.25, a.Variance, 8); a.Add(a); Assert.Equal(200UL, a.Count); Assert.Equal(833.25, a.Variance, 8);
    }
    [Fact]
    public void OverflowDoesNotPartiallyModifyPopulation()
    {
        var s = new Stats(); s.Add(3, ulong.MaxValue); Assert.Throws<OverflowException>(() => s.Add(7)); Assert.Equal(ulong.MaxValue, s.Count); Assert.Equal(3, s.Mean); Assert.Equal(3, s.Maximum);
        var h = new Histogram(0, 10); h.Add(1, ulong.MaxValue); Assert.Throws<OverflowException>(() => h.Add(1)); Assert.Equal(ulong.MaxValue, h.Count);
    }
    [Fact]
    public void ExtremaAndNumericMinMaxRetainNativeNaNOperandOrdering()
    {
        var s = new Extrema(); s.Add(2); s.Add(double.NaN); Assert.True(double.IsNaN(s.Minimum)); s.Add(3); Assert.Equal(3, s.Minimum);
        var m = new MinMax<double>(); m.Add(double.NaN); Assert.Equal(double.MaxValue, m.Minimum); m.Add(2); m.Add(7); Assert.Equal(2, m.Minimum); Assert.Equal(7, m.Maximum);
    }
    [Fact]
    public void HistogramUpperBoundAndEpsilonUseLastBin()
    {
        var h = new Histogram(0, 1, 10); Assert.True(h.Add(0)); Assert.True(h.Add(0.1)); Assert.True(h.Add(1)); Assert.True(h.Add(1 + 0.5e-10)); Assert.False(h.Add(1 + 2e-10)); Assert.False(h.Add(double.NaN)); Assert.False(h.Add(double.PositiveInfinity));
        Assert.Equal(4UL, h.Count); Assert.Equal(1UL, h[0]); Assert.Equal(1UL, h[1]); Assert.Equal(2UL, h[9]); Assert.Equal(0.9, h.BinMinimum(9), 12); Assert.Equal(1, h.BinMaximum(9), 12);
    }
    [Fact]
    public void HistogramCanUseConstantStatsRangeAndMergeMatchingBins()
    {
        var s = new Stats(); s.Add(3); var h = new Histogram(s, 4); h.Add(3, 7); Assert.Equal(7UL, h[0]); var other = new Histogram(s, 4); other.Add(3, 2); Assert.True(h.Add(other)); Assert.Equal(9UL, h.Count); Assert.False(h.Add(new Histogram(0, 1, 4)));
    }
    [Fact]
    public void InvalidHistogramConfigurationAndIndicesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new Histogram(1, 1)); Assert.Throws<ArgumentException>(() => new Histogram(double.NaN, 1)); Assert.Throws<ArgumentOutOfRangeException>(() => new Histogram(0, 1, 0)); Assert.Throws<ArgumentException>(() => new Histogram(new Stats())); Assert.Throws<ArgumentOutOfRangeException>(() => new Histogram(0, 1).BinMinimum(-1));
    }
    [Fact]
    public void LargeTilesAreWeightedWithoutMaterializingVoxels()
    {
        var tree = new VdbTree<double>(); tree.AddTile(3, new(0, 0, 0), 2, true); tree.AddTile(3, new(4096, 0, 0), 6, true); var stats = Statistics.Compute(tree);
        Assert.Equal(2UL << 36, stats.Count); Assert.Equal(4, stats.Mean); Assert.Equal(4, stats.Variance); Assert.Equal(0L, tree.LeafCount);
        var histogram = Statistics.Histogram(tree, 0, 8, 4); Assert.Equal(1UL << 36, histogram[1]); Assert.Equal(1UL << 36, histogram[3]);
    }
    [Fact]
    public void VectorStatisticsUseMagnitudeAndActivityIsIndependent()
    {
        var tree = new VdbTree<Vec3d>(); tree.SetValueOn(new(0, 0, 0), new(3, 4, 0)); tree.SetValueOff(new(1, 0, 0), new(9, 12, 0)); var s = Statistics.Compute(tree); Assert.Equal(1UL, s.Count); Assert.Equal(5, s.Mean);
    }
}

public class NativeStatisticsConformanceTests
{
    private static readonly Lazy<System.Text.Json.JsonDocument> Fixture = new(() => System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-statistics.json"))));
    public static IEnumerable<object[]> Cases() { for (int seed = 1; seed <= 25; seed++) for (int mode = 0; mode < 4; mode++) yield return [seed, mode]; }
    [Theory]
    [MemberData(nameof(Cases))]
    public void WeightedMomentsMatchNative(int seed, int mode)
    {
        var root = Fixture.Value.RootElement; Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", root.GetProperty("commit").GetString()); var expected = root.GetProperty("cases")[(seed - 1) * 4 + mode]; var a = new Stats(); var b = new Stats(); int index = 0;
        foreach (var input in expected.GetProperty("inputs").EnumerateArray()) { var target = mode == 2 && index++ >= 10 ? b : a; if (mode == 0) target.Add(input[0].GetDouble()); else target.Add(input[0].GetDouble(), input[1].GetUInt64()); }
        if (mode == 2) a.Add(b); if (mode == 3) a.Add(a); Assert.Equal(expected.GetProperty("count").GetUInt64(), a.Count); Assert.Equal(expected.GetProperty("min").GetDouble(), a.Minimum); Assert.Equal(expected.GetProperty("max").GetDouble(), a.Maximum);
        Check(expected.GetProperty("mean").GetDouble(), a.Mean); Check(expected.GetProperty("variance").GetDouble(), a.Variance);
        static void Check(double wanted, double actual) => Assert.InRange(System.Math.Abs(wanted - actual), 0, 2e-12 * System.Math.Max(1, System.Math.Abs(wanted)));
    }
    [Theory]
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
    public void HistogramBinsMatchNative(int bins)
    {
        var expected = Fixture.Value.RootElement.GetProperty("histograms")[bins - 1]; var h = new Histogram(-1, 1, bins); foreach (double value in new[] { -2.0, -1.0, -0.75, -0.25, 0.0, 0.25, 0.75, 1.0, 1.0 + 5e-11, 1.0 + 2e-10 }) h.Add(value, 3);
        Assert.Equal(expected.GetProperty("count").GetUInt64(), h.Count); Assert.Equal(expected.GetProperty("values").EnumerateArray().Select(v => v.GetUInt64()), h.Bins.ToArray());
    }
}
