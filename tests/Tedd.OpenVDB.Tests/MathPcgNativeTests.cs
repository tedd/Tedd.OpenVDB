using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Pcg = Tedd.OpenVDB.Math.Pcg;
namespace Tedd.OpenVDB.Tests;

public class MathPcgNativeTests
{
    private static readonly JsonDocument Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-pcg.json")));
    public static IEnumerable<object[]> Cases()
    {
        int index = 0;
        foreach (var test in Fixture.RootElement.GetProperty("cases").EnumerateArray())
            yield return [index++, test.GetProperty("kind").GetString()!, test.GetProperty("precision").GetString()!];
    }
    [Fact]
    public void NativeFixtureHasPinnedProvenance()
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.RootElement.GetProperty("upstreamCommit").GetString());
        Assert.Equal(588, Fixture.RootElement.GetProperty("cases").GetArrayLength());
    }
    private static double Number(JsonElement v) => v.ValueKind == JsonValueKind.String ? double.Parse(v.GetString()!, CultureInfo.InvariantCulture) : v.GetDouble();
    private static void Near(double expected, double actual, double tolerance, string label)
    {
        if (double.IsNaN(expected)) { Assert.True(double.IsNaN(actual), label); return; }
        if (double.IsInfinity(expected)) { Assert.Equal(expected, actual); return; }
        Assert.True(System.Math.Abs(expected - actual) <= tolerance * System.Math.Max(1, System.Math.Abs(expected)), $"{label}: native {expected:R}, managed {actual:R}");
    }
    private static void CheckVector<T>(JsonElement expected, Pcg.Vector<T> actual, double tolerance, string label) where T : unmanaged, IFloatingPointIeee754<T>
    {
        Assert.Equal(expected.GetArrayLength(), actual.Size);
        for (int i = 0; i < actual.Size; ++i) Near(Number(expected[i]), double.CreateTruncating(actual[i]), tolerance, label + "[" + i + "]");
    }
    private static void CheckMatrix<T>(JsonElement expected, Pcg.SparseStencilMatrix<T> actual, double tolerance) where T : unmanaged, IFloatingPointIeee754<T>
    {
        Assert.Equal(expected.GetArrayLength(), actual.Size);
        for (int row = 0; row < actual.Size; ++row)
        {
            var entries = actual.GetConstRow(row); Assert.Equal(expected[row].GetArrayLength(), entries.Count);
            for (int i = 0; i < entries.Count; ++i)
            {
                Assert.Equal(expected[row][i][0].GetInt32(), entries[i].Column);
                Near(Number(expected[row][i][1]), double.CreateTruncating(entries[i].Value), tolerance, "factor row " + row + " column " + entries[i].Column);
            }
        }
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public void ManagedPcgMatchesNative(int index, string kind, string precision)
    {
        JsonElement test = Fixture.RootElement.GetProperty("cases")[index];
        if (precision == "float") Check<float>(test, kind, 3e-6); else Check<double>(test, kind, 5e-13);
    }
    private static void Check<T>(JsonElement test, string kind, double tolerance) where T : unmanaged, IFloatingPointIeee754<T>
    {
        T C(double value) => T.CreateTruncating(value);
        if (kind == "vector")
        {
            int size = test.GetProperty("size").GetInt32(), profile = test.GetProperty("profile").GetInt32();
            var a = new Pcg.Vector<T>(size); var b = new Pcg.Vector<T>(size);
            for (int i = 0; i < size; ++i)
            {
                a[i] = C((i * 17 + 3) % 101 - 50) / C(7); b[i] = C((i * 29 + 1) % 97 - 48) / C(11);
                if (profile == 1) { a[i] = i % 4 == 0 ? C(1e12) : i % 4 == 2 ? C(-1e12) : C(i % 7 - 3); b[i] = T.One; }
                if (profile == 2) { a[i] = C(i % 11 - 5) * C(1e-12); b[i] = C(i % 7 - 3) * C(1e-12); }
                if (profile == 3) { a[i] = i % 2 != 0 ? -T.Zero : T.Zero; b[i] = -T.One; }
                if (profile == 4) { a[i] = i % 3 == 0 ? T.NaN : i % 3 == 1 ? T.PositiveInfinity : T.NegativeInfinity; b[i] = T.One; }
            }
            // These reductions preserve the exact native order, so require exact representable values.
            Assert.Equal(Number(test.GetProperty("dot")), double.CreateTruncating(a.Dot(b)));
            Assert.Equal(Number(test.GetProperty("infinity")), double.CreateTruncating(a.InfinityNorm()));
            Assert.Equal(Number(test.GetProperty("l2")), double.CreateTruncating(a.L2Norm()));
            Assert.Equal(Number(test.GetProperty("finite")) != 0, a.IsFinite());
            a.Scale(1.00000000000001); Assert.Equal(Number(test.GetProperty("scaledDot")), double.CreateTruncating(a.Dot(b))); return;
        }
        if (kind == "interruption")
        {
            var a = PcgTestSystems.Build<T>(2); var b = new Pcg.Vector<T>(a.Size, T.One); var x = new Pcg.Vector<T>(a.Size, C(.7));
            using var cancellation = new CancellationTokenSource();
            var interrupting = new InterruptingPreconditioner<T>(new Pcg.JacobiPreconditioner<T>(a), cancellation);
            if (test.GetProperty("mode").GetInt32() == 0) cancellation.Cancel();
            Assert.Equal(1, test.GetProperty("interrupted").GetInt32());
            Assert.Throws<OperationCanceledException>(() => Pcg.ConjugateGradient.Solve(a, b, x, interrupting, new Pcg.State(false, 50, 0, 0), cancellation.Token));
            Assert.Equal(test.GetProperty("calls").GetInt32() - 1, interrupting.ApplyCalls);
            CheckVector(test.GetProperty("solution"), x, 0, "interrupted solution"); return;
        }
        if (kind == "row")
        {
            var a = new Pcg.SparseStencilMatrix<T>(7, 7); var row = a.GetRowEditor(3);
            row.SetValue(5, C(11)); row.SetValue(1, C(-2)); row.SetValue(3, T.Zero); row.SetValue(0, C(4)); row.SetValue(5, C(12)); row.Scale(1.00000000000001);
            CheckMatrix(test.GetProperty("matrix"), a, 0);
            var input = new Pcg.Vector<double>(7); var output = new Pcg.Vector<double>(7);
            var single = new Pcg.Vector<float>(7); var singleOutput = new Pcg.Vector<float>(7);
            for (int i = 0; i < 7; ++i) { input[i] = (i - 3) / 7.0; single[i] = (float)input[i]; }
            a.VectorMultiply(input, output); a.VectorMultiply(single, singleOutput);
            CheckVector(test.GetProperty("productDouble"), output, 0, "mixed double product"); CheckVector(test.GetProperty("productFloat"), singleOutput, 0, "mixed float product"); return;
        }
        if (kind == "invalidFactor")
        {
            int profile = test.GetProperty("profile").GetInt32(); var matrix = new Pcg.SparseStencilMatrix<T>(3, 7);
            matrix[0, 0] = profile == 4 ? T.NaN : C(profile == 0 ? 0 : profile == 1 ? .000009999 : profile == 2 ? .00001 : 1);
            matrix[1, 1] = T.One; matrix[2, 2] = profile == 3 ? -T.One : T.One;
            var ic = new Pcg.IncompleteCholeskyPreconditioner<T>(matrix);
            Assert.Equal(Number(test.GetProperty("valid")) != 0, ic.IsValid);
            CheckMatrix(test.GetProperty("lower"), ic.LowerMatrix, 0); CheckMatrix(test.GetProperty("upper"), ic.UpperMatrix, 0); return;
        }
        var system = PcgTestSystems.Build<T>(test.GetProperty("profile").GetInt32());
        if (kind == "factor")
        {
            var ic = new Pcg.IncompleteCholeskyPreconditioner<T>(system); Assert.Equal(Number(test.GetProperty("valid")) != 0, ic.IsValid);
            CheckMatrix(test.GetProperty("lower"), ic.LowerMatrix, 0); CheckMatrix(test.GetProperty("upper"), ic.UpperMatrix, 0);
            var residual = new Pcg.Vector<T>(system.Size); var result = new Pcg.Vector<T>(system.Size);
            for (int i = 0; i < system.Size; ++i) residual[i] = C(i % 17 - 8) / C(13);
            ic.Apply(residual, result); CheckVector(test.GetProperty("choleskyApplied"), result, 0, "IC apply");
            new Pcg.JacobiPreconditioner<T>(system).Apply(residual, result); CheckVector(test.GetProperty("jacobiApplied"), result, 0, "Jacobi apply"); return;
        }
        int initial = test.GetProperty("initial").GetInt32(); var rhs = new Pcg.Vector<T>(system.Size); var solution = new Pcg.Vector<T>(system.Size);
        for (int i = 0; i < system.Size; ++i) { rhs[i] = initial == 2 ? T.Zero : C((i * 13 + 2) % 29 - 14) / C(7); solution[i] = initial == 0 ? T.Zero : C(i % 5 - 2) / C(11); }
        Pcg.Preconditioner<T> preconditioner = test.GetProperty("mode").GetInt32() == 0 ? new Pcg.JacobiPreconditioner<T>(system) : new Pcg.IncompleteCholeskyPreconditioner<T>(system);
        var limit = new Pcg.State(false, test.GetProperty("maximum").GetInt32(), Number(test.GetProperty("limitRelative")), Number(test.GetProperty("limitAbsolute")));
        var state = Pcg.ConjugateGradient.Solve(system, rhs, solution, preconditioner, limit);
        Assert.Equal(Number(test.GetProperty("success")) != 0, state.Success); Assert.Equal(test.GetProperty("iterations").GetInt32(), state.Iterations);
        Near(Number(test.GetProperty("absolute")), state.AbsoluteError, tolerance, "absolute error"); Near(Number(test.GetProperty("relative")), state.RelativeError, tolerance, "relative error");
        CheckVector(test.GetProperty("solution"), solution, tolerance, "solution");
    }
    private sealed class InterruptingPreconditioner<T>(Pcg.Preconditioner<T> inner, CancellationTokenSource cancellation) : Pcg.Preconditioner<T>(inner.Size)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        public int ApplyCalls { get; private set; }
        public override void Apply(Pcg.Vector<T> residual, Pcg.Vector<T> result)
        {
            ++ApplyCalls; inner.Apply(residual, result); cancellation.Cancel();
        }
    }
}
