using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
using Pcg = Tedd.OpenVDB.Math.Pcg;
namespace Tedd.OpenVDB.Tests;

internal static class PoissonTestSystems
{
    public static (VdbTree<T> Source, VdbTree<bool> Domain) Input<T>(int profile) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var source = new VdbTree<T>(T.CreateChecked(1.25)); var domain = new VdbTree<bool>();
        void Set(Coord c) { domain[c] = true; source[c] = T.CreateTruncating((c.X * 3 - c.Y * 2 + c.Z) / 7.0); }
        if (profile == 0) { source.SetValueOff(new(-100, 2, 3), T.CreateChecked(11)); domain.SetValueOff(new(4096, 0, 0), false); return (source, domain); }
        if (profile == 1) { Set(new(1, -2, 3)); return (source, domain); }
        if (profile == 2)
        {
            foreach (Coord c in new Coord[] { new(-4097, -129, -9), new(-4096, -128, -8), new(-9, 0, 9), new(-8, 0, 8), new(-1, 0, 1), new(0, 0, 0), new(1, 0, -1), new(7, 0, 0), new(8, 0, 0), new(127, 0, 0), new(128, 0, 0), new(4095, 0, 0), new(4096, 0, 0) }) Set(c);
            return (source, domain);
        }
        if (profile == 4)
        {
            var box = new CoordBBox(new(-8), new(-1)); source.Fill(box, T.CreateChecked(.75)); domain.Fill(box, true); source.SetValueOff(new(2, 1, 1), T.CreateChecked(11)); return (source, domain);
        }
        if (profile == 7) { for (int x = -4; x <= 4; x += 2) for (int y = -2; y <= 2; y += 2) for (int z = -2; z <= 2; z += 2) Set(new(x, y, z)); return (source, domain); }
        int xmin = profile == 6 ? 0 : -2, xmax = profile == 6 ? 2 : profile == 3 ? 3 : 2;
        int ymin = profile == 6 ? -5 : profile == 3 ? -3 : -2, ymax = profile == 6 ? -1 : 2;
        int zmin = profile == 6 ? 0 : profile == 3 ? -1 : -2, zmax = profile == 6 ? 2 : profile == 3 ? 4 : 2;
        for (int x = xmin; x <= xmax; ++x) for (int y = ymin; y <= ymax; ++y) for (int z = zmin; z <= zmax; ++z)
        {
            Coord c = new(x, y, z); domain[c] = true;
            if (profile == 5) { if ((x + y + z) % 3 == 0) source.SetValueOff(c, T.CreateChecked(5.5)); else if ((x + y + z) % 3 == 1) source[c] = T.CreateChecked(-.25); }
            else if (profile == 6) source[c] = T.Zero; else Set(c);
        }
        if (profile == 5) domain.SetValueOff(Coord.Zero, false); return (source, domain);
    }
    public static PoissonBoundaryCondition Boundary(int mode) => (Coord c, Coord n, ref double source, ref double diagonal) =>
    {
        if (mode == 0) { diagonal -= 1; return; }
        if (mode == 1) { int axis = n.X != c.X ? 0 : n.Y != c.Y ? 1 : 2; double sign = n[axis] < c[axis] ? -1 : 1; source = source * 1.01 + (axis + 1) * .125 + sign * .01; diagonal -= 1 + (axis + 1) * .125; return; }
        if (mode == 2 && n.X == c.X && n.Z == c.Z) { if (n.Y < c.Y) source -= 1; else diagonal -= 1; }
    };
}

public class PoissonNativeTests
{
    private static readonly JsonDocument Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-poisson.json")));
    public static IEnumerable<object[]> Cases()
    {
        int index = 0; foreach (var test in Fixture.RootElement.GetProperty("cases").EnumerateArray()) yield return [index++, test.GetProperty("precision").GetString()!, test.GetProperty("profile").GetInt32()];
    }
    [Fact]
    public void FixtureHasPinnedProvenance()
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.RootElement.GetProperty("upstreamCommit").GetString()); Assert.Equal(162, Fixture.RootElement.GetProperty("cases").GetArrayLength());
    }
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? double.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetDouble();
    private static void Near(double expected, double actual, double tolerance, string label)
    {
        if (double.IsNaN(expected)) { Assert.True(double.IsNaN(actual), label); return; }
        if (double.IsInfinity(expected)) { Assert.Equal(expected, actual); return; }
        Assert.True(System.Math.Abs(expected - actual) <= tolerance * System.Math.Max(1, System.Math.Abs(expected)), $"{label}: native {expected:R} managed {actual:R}");
    }
    private static void Vector(JsonElement expected, Pcg.Vector<double> actual, double tolerance, string label)
    {
        Assert.Equal(expected.GetArrayLength(), actual.Size); for (int i = 0; i < actual.Size; ++i) Near(Number(expected[i]), actual[i], tolerance, label + "[" + i + "]");
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void ManagedPoissonMatchesNative(int index, string precision, int profile)
    {
        var test = Fixture.RootElement.GetProperty("cases")[index];
        if (test.GetProperty("kind").GetString() == "conversion")
        {
            var domain = new VdbTree<bool>(); for (int i = 0; i < 8; ++i) domain[new(0, 0, i)] = true;
            var indexTree = PoissonSolver.CreateIndexTree(domain); var values = new Pcg.Vector<double>(8);
            for (int i = 0; i < 8; ++i) values[i] = Number(test.GetProperty("input")[i]);
            Pcg.Vector<double> converted;
            if (test.GetProperty("target").GetString() == "half") converted = PoissonSolver.CreateVectorFromTree<double, Half>(PoissonSolver.CreateTreeFromVector(values, indexTree, (Half)0), indexTree);
            else converted = PoissonSolver.CreateVectorFromTree<double>(PoissonSolver.CreateTreeFromVector(values, indexTree, false), indexTree);
            for (int i = 0; i < 8; ++i)
            {
                double expected = Number(test.GetProperty("converted")[i]);
                if (double.IsNaN(expected)) Assert.True(double.IsNaN(converted[i])); else Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(converted[i]));
            }
            return;
        }
        if (precision == "float") Check<float>(test, profile); else Check<double>(test, profile);
    }
    private static void Check<T>(JsonElement test, int profile) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var (source, domain) = PoissonTestSystems.Input<T>(profile); bool staggered = test.GetProperty("staggered").GetInt32() != 0;
        var index = PoissonSolver.CreateIndexTree(domain); Assert.Equal(test.GetProperty("indexBackground").GetInt32(), index.Background);
        Assert.Equal(test.GetProperty("leafCount").GetInt64(), index.LeafCount); Assert.Equal(test.GetProperty("rootEntries").GetInt32(), index.RootEntryCount); Assert.Equal(test.GetProperty("activeCount").GetUInt64(), index.ActiveVoxelCount);
        int cursor = 0; var expectedIndices = test.GetProperty("index");
        foreach (var leaf in index.EnumerateLeaves()) foreach (int offset in leaf.ActiveOffsets)
        {
            var expected = expectedIndices[cursor++]; Assert.Equal(new Coord(expected[0].GetInt32(), expected[1].GetInt32(), expected[2].GetInt32()), leaf.OffsetToGlobalCoord(offset)); Assert.Equal(expected[3].GetInt32(), leaf.GetValue(offset));
        }
        Assert.Equal(expectedIndices.GetArrayLength(), cursor);
        var b = PoissonSolver.CreateVectorFromTree<double, T>(source, index); Vector(test.GetProperty("source"), b, 0, "source");
        var interior = new VdbTree<bool>(); interior.TopologyUnion(index); Morphology.ErodeActiveValues(interior, 1, NearestNeighbors.Face, TilePolicy.IgnoreTiles);
        var boundary = PoissonTestSystems.Boundary(test.GetProperty("mode").GetInt32());
        var matrix = PoissonSolver.CreateISLaplacianWithBoundaryConditions(index, interior, boundary, b, staggered);
        var expectedMatrix = test.GetProperty("matrix"); Assert.Equal(expectedMatrix.GetArrayLength(), matrix.Size);
        for (int row = 0; row < matrix.Size; ++row)
        {
            var actual = matrix.GetConstRow(row); Assert.Equal(expectedMatrix[row].GetArrayLength(), actual.Count);
            for (int i = 0; i < actual.Count; ++i) { Assert.Equal(expectedMatrix[row][i][0].GetInt32(), actual[i].Column); Near(Number(expectedMatrix[row][i][1]), actual[i].Value, 0, "matrix coefficient"); }
        }
        Vector(test.GetProperty("modifiedSource"), b, 0, "boundary source");
        var packed = PoissonSolver.CreateVectorFromTree<float, T>(source, index); var rebuilt = PoissonSolver.CreateTreeFromVector(packed, index, T.CreateChecked(-17));
        Vector(test.GetProperty("roundTripFloat"), PoissonSolver.CreateVectorFromTree<double, T>(rebuilt, index), 0, "float round trip"); Assert.Equal(Number(test.GetProperty("roundTripBackground")), double.CreateTruncating(rebuilt.Background));
        var state = new Pcg.State(false, test.GetProperty("maximum").GetInt32(), Number(test.GetProperty("limitRelative")), Number(test.GetProperty("limitAbsolute")));
        var options = test.GetProperty("jacobi").GetInt32() != 0 ? new PoissonSolverOptions { PreconditionerFactory = static a => new Pcg.JacobiPreconditioner<double>(a) } : new PoissonSolverOptions();
        var solution = PoissonSolver.SolveWithBoundaryConditions(source, domain, boundary, ref state, staggered, options);
        Assert.Equal(test.GetProperty("success").GetInt32() != 0, state.Success); Assert.Equal(test.GetProperty("iterations").GetInt32(), state.Iterations);
        Near(Number(test.GetProperty("relative")), state.RelativeError, 5e-13, "relative error"); Near(Number(test.GetProperty("absolute")), state.AbsoluteError, 5e-13, "absolute error");
        Assert.Equal(Number(test.GetProperty("outputBackground")), double.CreateTruncating(solution.Background));
        Vector(test.GetProperty("solution"), PoissonSolver.CreateVectorFromTree<double, T>(solution, index), typeof(T) == typeof(float) ? 2e-6 : 5e-13, "solution");
    }
}
