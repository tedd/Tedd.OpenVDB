using System.Numerics;
using Pcg = Tedd.OpenVDB.Math.Pcg;
namespace Tedd.OpenVDB.Tests;

public class MathPcgSolverTests
{
    [Fact]
    public void DefaultTerminationUsesSpacingAtOne()
    {
        var f = Pcg.ConjugateGradient.TerminationDefaults<float>(); var d = Pcg.ConjugateGradient.TerminationDefaults<double>();
        Assert.False(f.Success); Assert.Equal(50, f.Iterations); Assert.Equal(1e-6, f.RelativeError);
        Assert.Equal(1.1920928955078125e-5, f.AbsoluteError); Assert.Equal(2.220446049250313e-14, d.AbsoluteError);
        Assert.Throws<NotSupportedException>(() => Pcg.ConjugateGradient.TerminationDefaults<Half>());
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void KnownNativeFiveByFiveSystemSolvesAndHasSmallTrueResidual(bool single, bool cholesky)
    {
        if (single) Known<float>(cholesky); else Known<double>(cholesky);
    }
    private static void Known<T>(bool cholesky) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var a = PcgTestSystems.Build<T>(0); var b = new Pcg.Vector<T>(5, T.One); var x = new Pcg.Vector<T>(5, T.Zero);
        var aBefore = a.DeepCopy(); var bBefore = b.DeepCopy();
        Pcg.Preconditioner<T> preconditioner = cholesky ? new Pcg.IncompleteCholeskyPreconditioner<T>(a) : new Pcg.JacobiPreconditioner<T>(a);
        var state = Pcg.ConjugateGradient.Solve(a, b, x, preconditioner);
        Assert.True(state.Success); Assert.InRange(state.Iterations, 1, 20);
        double[] expected = [1.0 / 96, 3.0 / 32, 1.0 / 8, 7.0 / 96, 3.0 / 32];
        for (int i = 0; i < 5; ++i) Assert.InRange(System.Math.Abs(double.CreateTruncating(x[i]) - expected[i]), 0, typeof(T) == typeof(float) ? 1e-6 : 1e-13);
        var ax = new Pcg.Vector<T>(5); a.VectorMultiply(x, ax);
        for (int i = 0; i < 5; ++i) Assert.InRange(double.CreateTruncating(T.Abs(ax[i] - T.One)), 0, typeof(T) == typeof(float) ? 2e-6 : 2e-13);
        Assert.True(a.IsApproxEqual(aBefore, T.Zero)); Assert.True(b.IsApproxEqual(bBefore, T.Zero));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CholeskyFactorSnapshotsAndRepeatedApplicationsAreIndependent(bool single)
    {
        if (single) Factors<float>(); else Factors<double>();
    }
    private static void Factors<T>() where T : unmanaged, IFloatingPointIeee754<T>
    {
        var matrix = PcgTestSystems.Build<T>(3); var preconditioner = new Pcg.IncompleteCholeskyPreconditioner<T>(matrix);
        var input = new Pcg.Vector<T>(matrix.Size, T.One); var output = new Pcg.Vector<T>(matrix.Size); preconditioner.Apply(input, output); var expected = output.DeepCopy();
        matrix[0, 0] = T.CreateChecked(1000); var snapshot = preconditioner.LowerMatrix; snapshot[0, 0] = T.CreateChecked(2000);
        var second = preconditioner.UpperMatrix; second.GetRowEditor(0).Clear();
        preconditioner.Apply(input, output); Assert.True(expected.IsApproxEqual(output, T.Zero));
        Assert.NotEqual(snapshot[0, 0], preconditioner.LowerMatrix[0, 0]); Assert.True(preconditioner.UpperMatrix.GetConstRow(0).Count > 0);
        var lower = preconditioner.LowerMatrix; var upper = preconditioner.UpperMatrix;
        for (int row = 0; row < lower.Size; ++row) foreach (var entry in lower.GetConstRow(row)) { Assert.True(entry.Column <= row); Assert.Equal(entry.Value, upper[entry.Column, row]); }
    }
    [Fact]
    public void JacobiSnapshotAndInPlaceApplicationAreSupported()
    {
        var a = new Pcg.SparseStencilMatrix<double>(3, 1); a[0, 0] = 2; a[1, 1] = 4; a[2, 2] = 8;
        var jacobi = new Pcg.JacobiPreconditioner<double>(a); Assert.True(jacobi.IsFinite()); Assert.True(jacobi.IsValid);
        a[0, 0] = 100; var vector = new Pcg.Vector<double>(3, 8); jacobi.Apply(vector, vector); Assert.Equal(new[] { 4.0, 2, 1 }, vector.AsSpan().ToArray());
        a[1, 1] = 0; var zeroDiagonal = new Pcg.JacobiPreconditioner<double>(a); Assert.False(zeroDiagonal.IsFinite()); Assert.True(zeroDiagonal.IsValid);
        zeroDiagonal.Apply(new Pcg.Vector<double>(3, 1), vector); Assert.True(double.IsPositiveInfinity(vector[1]));
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(.000009)]
    public void InvalidCholeskyCannotBeAppliedAndLeavesOutputUnchanged(double pivot)
    {
        var a = new Pcg.SparseStencilMatrix<double>(3, 3); a[0, 0] = 1; a[1, 1] = pivot; a[2, 2] = 2;
        var ic = new Pcg.IncompleteCholeskyPreconditioner<double>(a); Assert.False(ic.IsValid);
        var result = new Pcg.Vector<double>(3, 71);
        Assert.Throws<ArithmeticException>(() => ic.Apply(new Pcg.Vector<double>(3, 1), result)); Assert.Equal(new[] { 71.0, 71, 71 }, result.AsSpan().ToArray());
        Assert.Equal(1, ic.LowerMatrix[0, 0]); Assert.Equal(pivot, ic.LowerMatrix[1, 1]);
    }
    [Fact]
    public void CholeskyRejectsUnsupportedTriangularCapacityAndAliasing()
    {
        var matrix = new Pcg.SparseStencilMatrix<double>(5, 5);
        for (int row = 0; row < 5; ++row) for (int column = 0; column < 5; ++column) matrix[row, column] = row == column ? 10 : -1;
        Assert.Throws<ArgumentException>(() => new Pcg.IncompleteCholeskyPreconditioner<double>(matrix));
        var a = PcgTestSystems.Build<double>(0); var ic = new Pcg.IncompleteCholeskyPreconditioner<double>(a); var x = new Pcg.Vector<double>(5, 4);
        Assert.Throws<ArgumentException>(() => ic.Apply(x, x)); Assert.Equal(4, x[0]);
        Assert.Throws<ArithmeticException>(() => ic.Apply(new Pcg.Vector<double>(4), x));
    }
    [Fact]
    public void EmptySolverAndInitiallyConvergedStateFollowNativeEarlyReturnRules()
    {
        var canceled = new CancellationToken(true); var empty = new Pcg.SparseStencilMatrix<double>(0, 7);
        var state = Pcg.ConjugateGradient.Solve(empty, new Pcg.Vector<double>(1), new Pcg.Vector<double>(2), new Pcg.JacobiPreconditioner<double>(empty), cancellationToken: canceled);
        Assert.Equal(default, state);
        var a = new Pcg.SparseStencilMatrix<double>(1, 1); a[0, 0] = 2;
        state = Pcg.ConjugateGradient.Solve(a, new Pcg.Vector<double>(1, 6), new Pcg.Vector<double>(1, 3), new Pcg.JacobiPreconditioner<double>(a), cancellationToken: canceled);
        Assert.True(state.Success); Assert.Equal(0, state.Iterations); Assert.Equal(0, state.AbsoluteError); Assert.Equal(0, state.RelativeError);
    }
    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    public void InitialAbsoluteToleranceDoesNotTriggerEarlyConvergence(int iterations)
    {
        var a = new Pcg.SparseStencilMatrix<double>(1, 1); a[0, 0] = 2; var x = new Pcg.Vector<double>(1, 0);
        var state = Pcg.ConjugateGradient.Solve(a, new Pcg.Vector<double>(1, 2), x, new Pcg.JacobiPreconditioner<double>(a), new Pcg.State(true, iterations, 0, 100));
        Assert.False(state.Success); Assert.Equal(0, state.Iterations); Assert.Equal(2, state.AbsoluteError); Assert.Equal(1, state.RelativeError); Assert.Equal(0, x[0]);
    }
    [Fact]
    public void InitialResidualErrorIsAnInfinityNormAndZeroRhsUsesDenominatorOne()
    {
        var a = new Pcg.SparseStencilMatrix<double>(2, 1); a[0, 0] = 1; a[1, 1] = 1;
        var x = new Pcg.Vector<double>(new double[] { 3, -4 }); var b = new Pcg.Vector<double>(2, 0);
        var state = Pcg.ConjugateGradient.Solve(a, b, x, new Pcg.JacobiPreconditioner<double>(a), new Pcg.State(false, 0, 0, 0));
        Assert.Equal(4, state.AbsoluteError); Assert.Equal(4, state.RelativeError);
        state = Pcg.ConjugateGradient.Solve(a, b, x, new Pcg.JacobiPreconditioner<double>(a)); Assert.True(state.Success); Assert.Equal(1, state.Iterations); Assert.Equal(0, x[0]); Assert.Equal(0, x[1]);
    }
    [Fact]
    public void SolverRejectsDimensionAndAliasErrorsBeforeMutation()
    {
        var a = PcgTestSystems.Build<double>(0); var x = new Pcg.Vector<double>(5, 17); var p = new Pcg.JacobiPreconditioner<double>(a);
        Assert.Throws<ArithmeticException>(() => Pcg.ConjugateGradient.Solve(a, new Pcg.Vector<double>(4), x, p));
        Assert.Throws<ArithmeticException>(() => Pcg.ConjugateGradient.Solve(a, new Pcg.Vector<double>(5), new Pcg.Vector<double>(4), p));
        Assert.Throws<ArithmeticException>(() => Pcg.ConjugateGradient.Solve(a, new Pcg.Vector<double>(5), x, new Pcg.JacobiPreconditioner<double>(new(2, 1))));
        Assert.Throws<ArgumentException>(() => Pcg.ConjugateGradient.Solve(a, x, x, p)); Assert.Equal(17, x[0]);
    }
    [Fact]
    public void CancellationBeforeIterationPreservesInitialGuess()
    {
        var a = PcgTestSystems.Build<double>(2); var x = new Pcg.Vector<double>(a.Size, .7); var p = new Pcg.JacobiPreconditioner<double>(a);
        Assert.Throws<OperationCanceledException>(() => Pcg.ConjugateGradient.Solve(a, new Pcg.Vector<double>(a.Size, 1), x, p, cancellationToken: new(true)));
        Assert.All(x.AsSpan().ToArray(), value => Assert.Equal(.7, value));
    }
    [Fact]
    public void CancellationAfterPreconditioningRetainsCompletedIteration()
    {
        var a = PcgTestSystems.Build<double>(2); var b = new Pcg.Vector<double>(a.Size, 1); var expected = new Pcg.Vector<double>(a.Size, 0); var x = expected.DeepCopy();
        var inner = new Pcg.JacobiPreconditioner<double>(a);
        var one = Pcg.ConjugateGradient.Solve(a, b, expected, inner, new Pcg.State(false, 1, 0, 0)); Assert.Equal(1, one.Iterations);
        using var cancellation = new CancellationTokenSource(); var preconditioner = new CancelingPreconditioner(inner, cancellation);
        Assert.Throws<OperationCanceledException>(() => Pcg.ConjugateGradient.Solve(a, b, x, preconditioner, new Pcg.State(false, 20, 0, 0), cancellation.Token));
        Assert.True(expected.IsApproxEqual(x, 0)); Assert.Equal(1, preconditioner.Calls);
    }
    private sealed class CancelingPreconditioner(Pcg.Preconditioner<double> inner, CancellationTokenSource source) : Pcg.Preconditioner<double>(inner.Size)
    {
        public int Calls { get; private set; }
        public override void Apply(Pcg.Vector<double> residual, Pcg.Vector<double> result) { ++Calls; inner.Apply(residual, result); source.Cancel(); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeededTridiagonalSystemsRecoverKnownSolutions(bool single)
    {
        if (single) RandomSystems<float>(); else RandomSystems<double>();
    }
    private static void RandomSystems<T>() where T : unmanaged, IFloatingPointIeee754<T>
    {
        var random = new Random(125913);
        for (int trial = 0; trial < 16; ++trial)
        {
            int size = random.Next(2, 40); var a = new Pcg.SparseStencilMatrix<T>(size, 3); var exact = new Pcg.Vector<T>(size);
            for (int i = 0; i < size; ++i)
            {
                a[i, i] = T.CreateChecked(4 + random.NextDouble()); exact[i] = T.CreateChecked(random.NextDouble() * 2 - 1);
                if (i > 0) a[i, i - 1] = a[i - 1, i] = T.CreateChecked(-random.NextDouble());
            }
            var rhs = new Pcg.Vector<T>(size); a.VectorMultiply(exact, rhs);
            foreach (bool cholesky in new[] { false, true })
            {
                var x = new Pcg.Vector<T>(size, T.Zero);
                Pcg.Preconditioner<T> preconditioner = cholesky ? new Pcg.IncompleteCholeskyPreconditioner<T>(a) : new Pcg.JacobiPreconditioner<T>(a);
                var limit = Pcg.ConjugateGradient.TerminationDefaults<T>(); if (typeof(T) == typeof(double)) limit.RelativeError = 1e-12; var state = Pcg.ConjugateGradient.Solve(a, rhs, x, preconditioner, limit); Assert.True(state.Success);
                for (int i = 0; i < size; ++i) Assert.InRange(double.CreateTruncating(T.Abs(x[i] - exact[i])), 0, typeof(T) == typeof(float) ? 3e-6 : 3e-12);
            }
        }
    }
}
