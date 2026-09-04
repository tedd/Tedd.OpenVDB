using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class MathOperatorNativeTests
{
    private static readonly JsonDocument Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-operators.json")));
    public static IEnumerable<object[]> Cases()
    {
        int index = 0; foreach (var test in Fixture.RootElement.GetProperty("cases").EnumerateArray()) yield return [index++, test.GetProperty("kind").GetString()!, test.GetProperty("precision").GetString()!];
    }
    [Fact]
    public void FixtureHasPinnedProvenance()
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.RootElement.GetProperty("upstreamCommit").GetString());
        Assert.True(Fixture.RootElement.GetProperty("cases").GetArrayLength() >= 144);
    }
    private static double N(JsonElement v) => v.ValueKind == JsonValueKind.String ? double.Parse(v.GetString()!, CultureInfo.InvariantCulture) : v.GetDouble();
    private static Vec3d V(JsonElement v) => new(N(v[0]), N(v[1]), N(v[2]));
    private static void Near(double e, double a, double tolerance, string label)
    {
        if (double.IsNaN(e)) { Assert.True(double.IsNaN(a), label); return; }
        if (double.IsInfinity(e)) { Assert.Equal(e, a); return; }
        Assert.True(System.Math.Abs(a - e) <= tolerance * System.Math.Max(1, System.Math.Abs(e)), label + ": native " + e.ToString("R") + " managed " + a.ToString("R"));
    }
    private static void Near(JsonElement e, Vec3d a, double tolerance, string label) { for (int i = 0; i < 3; ++i) Near(N(e[i]), a[i], tolerance, label + "[" + i + "]"); }
    private static Transform Map(int kind)
    {
        var matrix = new Mat4d(1.3, .2, -.1, 0, -.3, .8, .25, 0, .1, -.15, 1.7, 0, 3, -2, 4, 1);
        return kind == 0 ? new Transform(matrix) : new Transform(new NonlinearFrustumMap(new BBoxd(new Vec3d(-4, -8, -3), new Vec3d(8, 10, 12)), .6, 5, matrix));
    }
    private static Grid<T> Input<T>(JsonElement test, out Coord center) where T : unmanaged, IFloatingPointIeee754<T>
    {
        center = Coord.Round(V(test.GetProperty("center"))); var grid = new Grid<T>(); int i = 0; var input = test.GetProperty("input");
        for (int x = -3; x <= 3; ++x) for (int y = -3; y <= 3; ++y) for (int z = -3; z <= 3; ++z) grid[center + new Coord(x, y, z)] = T.CreateTruncating(N(input[i++]));
        return grid;
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void ManagedOperatorsMatchNative(int index, string kind, string precision)
    {
        var test = Fixture.RootElement.GetProperty("cases")[index];
        if (kind == "vector") { if (precision == "float") CheckVector<Vec3f, float>(test, 3e-6); else CheckVector<Vec3d, double>(test, 3e-12); return; }
        if (precision == "float") CheckScalar<float>(test, kind, 3e-6); else CheckScalar<double>(test, kind, 3e-12);
    }
    private static void CheckScalar<T>(JsonElement test, string kind, double tolerance) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var grid = Input<T>(test, out Coord center); var velocity = new Vec3d(-2, 0, 3);
        void Scalar(string field, T result) => Near(N(test.GetProperty(field)), double.CreateTruncating(result), tolerance, field);
        void Vector(string field, Vec3d result) => Near(test.GetProperty(field), result, tolerance, field);
        if (kind == "convenience")
        {
            double dx = N(test.GetProperty("dx")); var grad = new GradStencil<T>(grid, dx); var weno = new WenoStencil<T>(grid, dx); var curve = new CurvatureStencil<T>(grid, dx);
            grad.MoveTo(center); weno.MoveTo(center); curve.MoveTo(center);
            Vector("gradient", grad.Gradient()); Vector("biased", grad.Gradient(velocity)); Vector("cpt", grad.ClosestPoint());
            Scalar("norm", grad.NormSquaredGradient()); Scalar("laplacian", grad.Laplacian()); Assert.Equal(N(test.GetProperty("crossing")) != 0, grad.ZeroCrossing());
            Vector("wenoGradient", weno.Gradient()); Vector("wenoBiased", weno.Gradient(velocity)); Scalar("wenoNorm", weno.NormSquaredGradient());
            Scalar("wenoIsoNorm", weno.NormSquaredGradient(T.CreateTruncating(4))); Scalar("wenoLaplacian", weno.Laplacian()); Assert.Equal(N(test.GetProperty("wenoCrossing")) != 0, weno.ZeroCrossing());
            Scalar("mean", curve.MeanCurvature()); Scalar("gaussian", curve.GaussianCurvature()); Scalar("meanNorm", curve.MeanCurvatureNormGradient()); Scalar("gaussianNorm", curve.GaussianCurvatureNormGradient());
            var c = curve.Curvatures(); Scalar("mean", c.Mean); Scalar("gaussian", c.Gaussian); c = curve.CurvaturesNormGradient(); Scalar("meanNorm", c.Mean); Scalar("gaussianNorm", c.Gaussian);
            var p = curve.PrincipalCurvatures(); Scalar("minimum", p.Minimum); Scalar("maximum", p.Maximum);
            Vector("gradient", curve.Gradient()); Scalar("laplacian", curve.Laplacian()); return;
        }
        var map = Map((int)N(test.GetProperty("map"))); var a = grid.GetAccessor(); var stencil = new DenseStencil<T>(grid, 3); stencil.MoveTo(center);
        var gradients = test.GetProperty("gradients");
        for (int i = 0; i < 14; ++i)
        {
            var scheme = (DScheme)i; Near(gradients[i][0], DifferentialOperators.IndexGradient(a, center, scheme), tolerance, "index gradient " + i);
            Near(gradients[i][1], DifferentialOperators.Gradient(map, a, center, scheme), tolerance, "world gradient " + i);
            Near(gradients[i][0], DifferentialOperators.IndexGradient(stencil, scheme), tolerance, "stencil index gradient " + i);
            Near(gradients[i][1], DifferentialOperators.Gradient(map, stencil, scheme), tolerance, "stencil world gradient " + i);
        }
        var biases = test.GetProperty("bias");
        for (int i = 0; i < 5; ++i)
        {
            var scheme = (BiasedGradientScheme)i;
            Near(biases[i][0], DifferentialOperators.IndexBiasedGradient(a, center, velocity, scheme), tolerance, "index bias " + i);
            Near(biases[i][1], DifferentialOperators.BiasedGradient(map, a, center, velocity, scheme), tolerance, "world bias " + i);
            Near(N(biases[i][2]), double.CreateTruncating(DifferentialOperators.IndexNormSquaredGradient(a, center, scheme)), tolerance, "index norm " + i);
            Near(N(biases[i][3]), double.CreateTruncating(DifferentialOperators.NormSquaredGradient(map, a, center, scheme)), tolerance, "world norm " + i);
            Near(biases[i][0], DifferentialOperators.IndexBiasedGradient(stencil, velocity, scheme), tolerance, "stencil index bias " + i);
            Near(biases[i][1], DifferentialOperators.BiasedGradient(map, stencil, velocity, scheme), tolerance, "stencil world bias " + i);
            Near(N(biases[i][2]), double.CreateTruncating(DifferentialOperators.IndexNormSquaredGradient(stencil, scheme)), tolerance, "stencil index norm " + i);
            Near(N(biases[i][3]), double.CreateTruncating(DifferentialOperators.NormSquaredGradient(map, stencil, scheme)), tolerance, "stencil world norm " + i);
        }
        var seconds = test.GetProperty("seconds");
        for (int i = 0; i < 3; ++i)
        {
            var second = (DDScheme)i; var first = (DScheme)(i + 1);
            Near(N(seconds[i][0]), double.CreateTruncating(DifferentialOperators.IndexLaplacian(a, center, second)), tolerance, "index laplacian " + i);
            Near(N(seconds[i][1]), double.CreateTruncating(DifferentialOperators.Laplacian(map, a, center, second)), tolerance, "world laplacian " + i);
            Near(N(seconds[i][2]), double.CreateTruncating(DifferentialOperators.MeanCurvature(map, a, center, second, first)), tolerance, "world curvature " + i);
            Near(N(seconds[i][3]), double.CreateTruncating(DifferentialOperators.MeanCurvatureNormGradient(map, a, center, second, first)), tolerance, "world curvature norm " + i);
            DifferentialOperators.IndexMeanCurvatureParts(a, center, out T alpha, out T beta, second, first);
            Near(N(seconds[i][4]), double.CreateTruncating(alpha), tolerance, "alpha " + i); Near(N(seconds[i][5]), double.CreateTruncating(beta), tolerance, "beta " + i);
            Near(N(seconds[i][0]), double.CreateTruncating(DifferentialOperators.IndexLaplacian(stencil, second)), tolerance, "stencil index laplacian " + i);
            // Mixed high-order accessors and stencils have different native grouping.
            Near(N(seconds[i][1]), double.CreateTruncating(DifferentialOperators.Laplacian(map, stencil, second)), tolerance * 4, "stencil world laplacian " + i);
            Near(N(seconds[i][2]), double.CreateTruncating(DifferentialOperators.MeanCurvature(map, stencil, second, first)), tolerance * 4, "stencil world curvature " + i);
            Near(N(seconds[i][3]), double.CreateTruncating(DifferentialOperators.MeanCurvatureNormGradient(map, stencil, second, first)), tolerance * 4, "stencil world curvature norm " + i);
        }
        Vector("cptIndex", DifferentialOperators.ClosestPoint(map, a, center, false)); Vector("cptWorld", DifferentialOperators.ClosestPoint(map, a, center, true));
        Vector("cptIndex", DifferentialOperators.ClosestPoint(map, stencil, false)); Vector("cptWorld", DifferentialOperators.ClosestPoint(map, stencil, true));
    }
    private static void CheckVector<VT, T>(JsonElement test, double tolerance) where VT : unmanaged where T : unmanaged, IFloatingPointIeee754<T>
    {
        Coord center = Coord.Round(V(test.GetProperty("center"))); var map = Map((int)N(test.GetProperty("map"))); var grid = new Grid<VT>(); int index = 0;
        var input = test.GetProperty("input");
        for (int x = -3; x <= 3; ++x) for (int y = -3; y <= 3; ++y) for (int z = -3; z <= 3; ++z)
        {
            Vec3d value = V(input[index++]); grid[center + new Coord(x, y, z)] = typeof(VT) == typeof(Vec3f) ? (VT)(object)new Vec3f((float)value.X, (float)value.Y, (float)value.Z) : (VT)(object)value;
        }
        var a = grid.GetAccessor(); var expected = test.GetProperty("derivatives");
        for (int i = 0; i < expected.GetArrayLength(); ++i)
        {
            var scheme = (DScheme)i; double indexDiv, worldDiv; Vec3d indexCurl, worldCurl;
            if (typeof(VT) == typeof(Vec3f))
            {
                var f = (ValueAccessor<Vec3f>)(object)a; indexDiv = DifferentialOperators.IndexDivergence(f, center, scheme); worldDiv = DifferentialOperators.Divergence(map, f, center, scheme);
                var c = DifferentialOperators.IndexCurl(f, center, scheme); indexCurl = new(c.X, c.Y, c.Z); c = DifferentialOperators.Curl(map, f, center, scheme); worldCurl = new(c.X, c.Y, c.Z);
            }
            else
            {
                var d = (ValueAccessor<Vec3d>)(object)a; indexDiv = DifferentialOperators.IndexDivergence(d, center, scheme); worldDiv = DifferentialOperators.Divergence(map, d, center, scheme);
                indexCurl = DifferentialOperators.IndexCurl(d, center, scheme); worldCurl = DifferentialOperators.Curl(map, d, center, scheme);
            }
            Near(N(expected[i][0]), indexDiv, tolerance, "vector index divergence " + i); Near(N(expected[i][1]), worldDiv, tolerance, "vector world divergence " + i);
            Near(expected[i][2], indexCurl, tolerance, "vector index curl " + i); Near(expected[i][3], worldCurl, tolerance, "vector world curl " + i);
        }
    }
}
