// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/ConjGradient.h.
using System.Numerics;

namespace Tedd.OpenVDB.Math.Pcg;

/// <summary>Termination limits or resulting solver diagnostics. Success is ignored when used as a limit.</summary>
public record struct State(bool Success, int Iterations, double RelativeError, double AbsoluteError);

public static class ConjugateGradient
{
    public static State TerminationDefaults<T>() where T : unmanaged, IFloatingPointIeee754<T>
    {
        Scalar<T>.Validate();
        // C++ numeric_limits::epsilon is spacing at one, not the CLR subnormal Epsilon constant.
        double epsilon = double.CreateTruncating(T.BitIncrement(T.One) - T.One);
        return new(false, 50, 1e-6, epsilon * 100.0);
    }
    /// <summary>Solves A*x=b for a symmetric positive-definite matrix, updating the initial guess in place.</summary>
    /// <remarks>Cancellation is checked at iteration boundaries, as with native interrupters; an empty
    /// or initially converged system returns without checking it. Cancellation and preconditioner
    /// exceptions retain the last completed solution update. RHS and solution must be distinct.
    /// Error diagnostics use the infinity norm. The native L2 divergence guard and arithmetic order
    /// are retained. As upstream requires, input coefficients and vectors should be finite.</remarks>
    public static State Solve<T>(SparseStencilMatrix<T> matrix, Vector<T> rhs, Vector<T> solution,
        Preconditioner<T> preconditioner, State? termination = null, CancellationToken cancellationToken = default)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(matrix); ArgumentNullException.ThrowIfNull(rhs);
        ArgumentNullException.ThrowIfNull(solution); ArgumentNullException.ThrowIfNull(preconditioner);
        State result = default; int size = matrix.Size; if (size == 0) return result;
        if (rhs.Size != size || solution.Size != size) throw new ArithmeticException("Matrix and vector dimensions differ.");
        if (preconditioner.Size != size) throw new ArithmeticException("Matrix and preconditioner dimensions differ.");
        if (ReferenceEquals(rhs, solution)) throw new ArgumentException("RHS and solution vectors must be distinct.");
        State limit = termination ?? TerminationDefaults<T>();
        var transformed = new Vector<T>(size); var direction = new Vector<T>(size);
        var product = new Vector<T>(size); var residual = new Vector<T>(size);
        T norm = rhs.InfinityNorm(); if (norm == T.Zero) norm = T.One;
        matrix.VectorMultiply(solution, residual);
        Axpy(-T.One, residual.AsReadOnlySpan(), rhs.AsReadOnlySpan(), residual.AsSpan());
        result.AbsoluteError = double.CreateTruncating(residual.InfinityNorm());
        result.RelativeError = result.AbsoluteError / double.CreateTruncating(norm);
        if (result.RelativeError <= limit.RelativeError) { result.Success = true; return result; }
        T previousDot = T.One, minimumL2 = Scalar<T>.Maximum;
        for (int iteration = 0; iteration < limit.Iterations; ++iteration)
        {
            cancellationToken.ThrowIfCancellationRequested(); result.Iterations = iteration + 1;
            preconditioner.Apply(residual, transformed);
            T dot = residual.Dot(transformed);
            if (iteration == 0) direction.CopyFrom(transformed);
            else Axpy(dot / previousDot, direction.AsReadOnlySpan(), transformed.AsReadOnlySpan(), direction.AsSpan());
            matrix.VectorMultiply(direction, product);
            T alpha = dot / direction.Dot(product); previousDot = dot;
            Axpy(alpha, direction.AsReadOnlySpan(), solution.AsReadOnlySpan(), solution.AsSpan());
            Axpy(-alpha, product.AsReadOnlySpan(), residual.AsReadOnlySpan(), residual.AsSpan());
            T l2 = residual.L2Norm(); minimumL2 = minimumL2 < l2 ? minimumL2 : l2;
            result.AbsoluteError = double.CreateTruncating(residual.InfinityNorm());
            result.RelativeError = result.AbsoluteError / double.CreateTruncating(norm);
            if (l2 > T.CreateChecked(2) * minimumL2 || !double.IsFinite(result.AbsoluteError)) break;
            if (result.AbsoluteError <= limit.AbsoluteError || result.RelativeError <= limit.RelativeError)
            {
                result.Success = true; break;
            }
        }
        return result;
    }
    private static void Axpy<T>(T scale, ReadOnlySpan<T> x, ReadOnlySpan<T> y, Span<T> result)
        where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (scale == T.One) for (int i = 0; i < result.Length; ++i) result[i] = x[i] + y[i];
        else if (scale == -T.One) for (int i = 0; i < result.Length; ++i) result[i] = -x[i] + y[i];
        else for (int i = 0; i < result.Length; ++i) result[i] = scale * x[i] + y[i];
    }
}
