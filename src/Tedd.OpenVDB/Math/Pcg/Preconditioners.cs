// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/ConjGradient.h.
using System.Numerics;

namespace Tedd.OpenVDB.Math.Pcg;

public abstract class Preconditioner<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    protected Preconditioner(int size)
    {
        Scalar<T>.Validate(); ArgumentOutOfRangeException.ThrowIfNegative(size); Size = size;
    }
    public int Size { get; }
    public virtual bool IsValid => true;
    public abstract void Apply(Vector<T> residual, Vector<T> result);
    protected void ValidateVectors(Vector<T> residual, Vector<T> result)
    {
        ArgumentNullException.ThrowIfNull(residual); ArgumentNullException.ThrowIfNull(result);
        if (residual.Size != Size || result.Size != Size) throw new ArithmeticException("Preconditioner and vector dimensions differ.");
    }
}

/// <summary>Diagonal preconditioning. A zero diagonal produces an infinite reciprocal, as in native release builds.</summary>
public sealed class JacobiPreconditioner<T> : Preconditioner<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly Vector<T> _diagonal;
    public JacobiPreconditioner(SparseStencilMatrix<T> matrix) : base((matrix ?? throw new ArgumentNullException(nameof(matrix))).Size)
    {
        _diagonal = new(Size);
        for (int i = 0; i < Size; ++i) _diagonal[i] = T.CreateTruncating(1.0 / double.CreateTruncating(matrix.GetValue(i, i)));
    }
    public bool IsFinite() => _diagonal.IsFinite();
    public override void Apply(Vector<T> residual, Vector<T> result)
    {
        ValidateVectors(residual, result);
        ReadOnlySpan<T> diagonal = _diagonal.AsReadOnlySpan(), input = residual.AsReadOnlySpan(); Span<T> output = result.AsSpan();
        for (int i = 0; i < output.Length; ++i) output[i] = diagonal[i] * input[i];
    }
}

/// <summary>The native incomplete-Cholesky preconditioner for sparse stencil systems.</summary>
/// <remarks>Like native OpenVDB, each triangular row holds at most four entries and pivots below
/// 1e-5 invalidate the decomposition. This is not a general-purpose dense Cholesky decomposition.
/// The input must be symmetric; no expensive symmetry test is performed. Apply reuses a temporary
/// vector, so instances cannot be applied concurrently. Input and output vectors must be distinct.</remarks>
public sealed class IncompleteCholeskyPreconditioner<T> : Preconditioner<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly SparseStencilMatrix<T> _lower;
    private readonly SparseStencilMatrix<T> _upper;
    private readonly Vector<T> _temporary;
    private readonly bool _isValid;
    public IncompleteCholeskyPreconditioner(SparseStencilMatrix<T> matrix) : base((matrix ?? throw new ArgumentNullException(nameof(matrix))).Size)
    {
        _lower = new(Size, 4); _upper = new(Size, 4); _temporary = new(Size);
        for (int row = 0; row < Size; ++row)
        {
            int lowerCount = 0, upperCount = 0;
            foreach (var entry in matrix.GetConstRow(row))
            {
                if (entry.Column <= row) ++lowerCount;
                if (entry.Column >= row) ++upperCount;
            }
            if (lowerCount > 4 || upperCount > 4) throw new ArgumentException("Native incomplete Cholesky requires at most four entries per triangular row.", nameof(matrix));
            var output = _lower.GetRowEditor(row);
            foreach (var entry in matrix.GetConstRow(row)) if (entry.Column <= row) output.SetValue(entry.Column, entry.Value);
        }
        bool valid = true;
        for (int k = 0; k < Size; ++k)
        {
            T diagonal = _lower.GetValue(k, k);
            if (double.CreateTruncating(diagonal) < 1e-5) { valid = false; break; }
            diagonal = T.Sqrt(diagonal); _lower.SetValue(k, k, diagonal);
            var source = matrix.GetConstRow(k);
            foreach (var entry in source)
            {
                if (entry.Column <= k) continue;
                // Native uses the original upper-triangle coefficient at this stage.
                _lower.SetValue(entry.Column, k, entry.Value / diagonal);
            }
            foreach (var entry in source)
            {
                int j = entry.Column; if (j <= k) continue;
                T ajk = _lower.GetValue(j, k);
                foreach (var mask in matrix.GetConstRow(j))
                {
                    int i = mask.Column; if (i < j) continue;
                    T aij = _lower.GetValue(i, j), aik = _lower.GetValue(i, k);
                    aij -= aik * ajk; _lower.SetValue(i, j, aij);
                }
            }
        }
        _isValid = valid;
        for (int row = 0; row < Size; ++row)
            foreach (var entry in matrix.GetConstRow(row))
                if (entry.Column >= row) _upper.SetValue(row, entry.Column, _lower.GetValue(entry.Column, row));
    }
    public override bool IsValid => _isValid;
    /// <summary>Returns an independent copy of the lower factor, including partial factors on failure.</summary>
    public SparseStencilMatrix<T> LowerMatrix => _lower.DeepCopy();
    /// <summary>Returns an independent copy of the upper factor.</summary>
    public SparseStencilMatrix<T> UpperMatrix => _upper.DeepCopy();
    public override void Apply(Vector<T> residual, Vector<T> result)
    {
        if (!IsValid) throw new ArithmeticException("Invalid Cholesky decomposition.");
        ValidateVectors(residual, result);
        if (ReferenceEquals(residual, result)) throw new ArgumentException("Incomplete Cholesky input and output must be distinct.");
        result.Fill(T.Zero); _temporary.Fill(T.Zero);
        for (int i = 0; i < Size; ++i)
        {
            var row = _lower.GetConstRow(i);
            _temporary[i] = (residual[i] - row.Dot(_temporary)) / row.GetValue(i);
        }
        for (int i = Size - 1; i >= 0; --i)
        {
            var row = _upper.GetConstRow(i);
            result[i] = (_temporary[i] - row.Dot(result)) / row.GetValue(i);
        }
    }
}
