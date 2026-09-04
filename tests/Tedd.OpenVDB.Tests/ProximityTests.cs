// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class ProximityTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-proximity.json"))));
    public static IEnumerable<object[]> Cases() { for (int i = 0; i < Fixture.Value.RootElement.GetProperty("cases").GetArrayLength(); i++) yield return [i]; }
    [Theory]
    [MemberData(nameof(Cases))]
    public void NativeClosestPointsAndBarycentricsMatch(int index)
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        var c = Fixture.Value.RootElement.GetProperty("cases")[index];
        Vec3d a = Read(c.GetProperty("a")), b = Read(c.GetProperty("b")), third = Read(c.GetProperty("c")), p = Read(c.GetProperty("p"));
        Vec3d actual = Proximity.ClosestPointOnTriangleToPoint(a, b, third, p, out var barycentric); Equal(Read(c.GetProperty("triangle")), actual); Equal(Read(c.GetProperty("barycentric")), barycentric);
        Equal(Read(c.GetProperty("segment")), Proximity.ClosestPointOnSegmentToPoint(a, b, p, out double t)); Near(Number(c.GetProperty("t")), t);
    }
    [Theory]
    [InlineData(-1, -1, 0, 0, 0, 0, 1, 0, 0)]
    [InlineData(2, 0, 0, 1, 0, 0, 0, 1, 0)]
    [InlineData(0, 2, 0, 0, 1, 0, 0, 0, 1)]
    [InlineData(0.25, 0.25, 4, 0.25, 0.25, 0, 0.5, 0.25, 0.25)]
    [InlineData(1, 1, 3, 0.5, 0.5, 0, 0, 0.5, 0.5)]
    [InlineData(0.25, -1, 0, 0.25, 0, 0, 0.75, 0.25, 0)]
    [InlineData(-1, 0.25, 0, 0, 0.25, 0, 0.75, 0, 0.25)]
    public void AnalyticTriangleVoronoiRegions(double px, double py, double pz, double x, double y, double z, double u, double v, double w)
    {
        Assert.Equal(new Vec3d(x, y, z), Proximity.ClosestPointOnTriangleToPoint(Vec3d.Zero, Vec3d.UnitX, Vec3d.UnitY, new(px, py, pz), out var barycentric)); Assert.Equal(new Vec3d(u, v, w), barycentric);
    }
    [Fact]
    public void SegmentDegeneracyAndInteriorProjectionAreWellDefined()
    {
        var a = new Vec3d(2, 3, 4); Assert.Equal(a, Proximity.ClosestPointOnSegmentToPoint(a, a, new(10), out double t)); Assert.Equal(0, t);
        Assert.Equal(new Vec3d(2, 3, 6), Proximity.ClosestPointOnSegmentToPoint(a, new(2, 3, 8), new(7, 8, 6), out t)); Assert.Equal(0.5, t);
    }
    [Fact]
    public void NativeRelativeEqualityCanCollapseTinyEdgesAtLargeCoordinates()
    {
        var a = new Vec3d(1e12); var b = a + new Vec3d(100, 0, 0); var c = a + new Vec3d(0, 100, 0);
        Assert.Equal(a, Proximity.ClosestPointOnTriangleToPoint(a, b, c, a + new Vec3d(20, 20, 20), out var barycentric)); Assert.Equal(Vec3d.UnitX, barycentric);
    }
    [Fact]
    public void RigidTranslationPreservesDistanceAndBarycentrics()
    {
        Vec3d a = new(-2, 1, 0), b = new(4, 1, 2), c = new(0, 5, 1), p = new(0.5, 1.5, 3), delta = new(8, -16, 4);
        var first = Proximity.ClosestPointOnTriangleToPoint(a, b, c, p, out var uvw); var translated = Proximity.ClosestPointOnTriangleToPoint(a + delta, b + delta, c + delta, p + delta, out var shifted);
        Equal(first + delta, translated); Equal(uvw, shifted); Near(1, uvw.Sum);
    }
    private static Vec3d Read(JsonElement e) => new(Number(e[0]), Number(e[1]), Number(e[2]));
    private static double Number(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : double.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    private static void Equal(Vec3d expected, Vec3d actual) { for (int i = 0; i < 3; i++) Near(expected[i], actual[i]); }
    private static void Near(double expected, double actual) { if (!double.IsFinite(expected)) Assert.Equal(expected, actual); else Assert.True(System.Math.Abs(expected - actual) <= 2e-13 * System.Math.Max(1, System.Math.Abs(expected)), $"Native={expected:R}; managed={actual:R}"); }
}
