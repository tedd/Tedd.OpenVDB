// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/ConjGradient.h.
using System.Numerics;

namespace Tedd.OpenVDB.Math.Pcg;

/// <summary>A square sparse matrix with sorted columns and a fixed number of stored entries per row.</summary>
/// <remarks>Explicit zeros remain stored entries. Distinct rows may be edited concurrently, but a row
/// must not be read while it is edited. Row views refer to live matrix storage. Runtime row capacity
/// replaces the native compile-time STENCIL_SIZE. Dimensions use checked CLR array limits.</remarks>
public sealed class SparseStencilMatrix<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly T[] _values;
    private readonly int[] _columns;
    private readonly int[] _counts;
    public SparseStencilMatrix(int size, int rowCapacity)
    {
        Scalar<T>.Validate(); ArgumentOutOfRangeException.ThrowIfNegative(size);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rowCapacity);
        int length = checked(size * rowCapacity);
        Size = size; RowCapacity = rowCapacity;
        _values = GC.AllocateUninitializedArray<T>(length);
        _columns = GC.AllocateUninitializedArray<int>(length);
        _counts = new int[size];
    }
    public SparseStencilMatrix(SparseStencilMatrix<T> other)
    {
        ArgumentNullException.ThrowIfNull(other); Size = other.Size; RowCapacity = other.RowCapacity;
        _values = (T[])other._values.Clone(); _columns = (int[])other._columns.Clone(); _counts = (int[])other._counts.Clone();
    }
    public int Size { get; }
    public int RowCapacity { get; }
    public T this[int row, int column] { get => GetValue(row, column); set => SetValue(row, column, value); }
    public SparseStencilMatrix<T> DeepCopy() => new(this);
    private void CheckIndex(int index) { if ((uint)index >= (uint)Size) throw new ArgumentOutOfRangeException(nameof(index)); }
    public Row GetConstRow(int row) { CheckIndex(row); return new(this, row); }
    public RowEditor GetRowEditor(int row) { CheckIndex(row); return new(this, row); }
    public T GetValue(int row, int column) => GetConstRow(row).GetValue(column);
    public void SetValue(int row, int column, T value) => GetRowEditor(row).SetValue(column, value);
    public void Scale(T scalar) => Scale<T>(scalar);
    public void Scale<TOther>(TOther scalar) where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        Scalar<TOther>.Validate(); for (int row = 0; row < Size; ++row) GetRowEditor(row).Scale(scalar);
    }
    public bool IsFinite()
    {
        for (int row = 0; row < Size; ++row) foreach (Entry entry in GetConstRow(row)) if (!T.IsFinite(entry.Value)) return false;
        return true;
    }
    public bool IsApproxEqual<TOther>(SparseStencilMatrix<TOther> other, T? tolerance = null) where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Size != other.Size || RowCapacity != other.RowCapacity) return false;
        T epsilon = tolerance ?? Scalar<T>.Tolerance;
        for (int row = 0; row < Size; ++row)
            if (!GetConstRow(row).IsApproxEqual(other.GetConstRow(row), epsilon)) return false;
        return true;
    }
    public void VectorMultiply<TOther>(Vector<TOther> input, Vector<TOther> result) where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(result);
        if (input.Size != Size || result.Size != Size) throw new ArithmeticException("Matrix and vector dimensions differ.");
        VectorMultiply(input.AsReadOnlySpan(), result.AsSpan());
    }
    /// <summary>Multiplies the first Size elements. Input and output must not overlap.</summary>
    public void VectorMultiply<TOther>(ReadOnlySpan<TOther> input, Span<TOther> result) where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        Scalar<TOther>.Validate();
        if (input.Length < Size || result.Length < Size) throw new ArithmeticException("Matrix and vector dimensions differ.");
        if (input[..Size].Overlaps(result[..Size])) throw new ArgumentException("Matrix multiplication input and output must not overlap.");
        for (int row = 0; row < Size; ++row) result[row] = GetConstRow(row).Dot(input[..Size]);
    }
    public readonly record struct Entry(int Column, T Value);
    public readonly struct Row
    {
        private readonly SparseStencilMatrix<T> _matrix;
        private readonly int _row;
        internal Row(SparseStencilMatrix<T> matrix, int row) { _matrix = matrix; _row = row; }
        public int Count => _matrix._counts[_row];
        public int Capacity => _matrix.RowCapacity;
        public bool IsEmpty => Count == 0;
        public ReadOnlySpan<T> Values => _matrix._values.AsSpan(_row * Capacity, Count);
        public ReadOnlySpan<int> Columns => _matrix._columns.AsSpan(_row * Capacity, Count);
        public Entry this[int index]
        {
            get { if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index)); return new(Columns[index], Values[index]); }
        }
        internal int Find(int column)
        {
            ReadOnlySpan<int> columns = Columns; int low = 0, high = columns.Length;
            while (low < high) { int middle = low + ((high - low) >> 1); if (columns[middle] < column) low = middle + 1; else high = middle; }
            return low;
        }
        public T GetValue(int column) => GetValue(column, out _);
        public T GetValue(int column, out bool active)
        {
            _matrix.CheckIndex(column); int offset = Find(column);
            active = offset < Count && Columns[offset] == column;
            return active ? Values[offset] : T.Zero;
        }
        public TOther Dot<TOther>(Vector<TOther> vector) where TOther : unmanaged, IFloatingPointIeee754<TOther>
        {
            ArgumentNullException.ThrowIfNull(vector); return Dot(vector.AsReadOnlySpan());
        }
        public TOther Dot<TOther>(ReadOnlySpan<TOther> vector) where TOther : unmanaged, IFloatingPointIeee754<TOther>
        {
            Scalar<TOther>.Validate(); TOther result = TOther.Zero;
            int count = System.Math.Min(vector.Length, Count); ReadOnlySpan<T> values = Values; ReadOnlySpan<int> columns = Columns;
            // The native loop limits entry count, not column indices. Check each actual column before use.
            for (int i = 0; i < count; ++i) result += Scalar<T>.Product(values[i], vector[columns[i]]);
            return result;
        }
        public bool IsApproxEqual<TOther>(SparseStencilMatrix<TOther>.Row other, T? tolerance = null) where TOther : unmanaged, IFloatingPointIeee754<TOther>
        {
            if (Count != other.Count) return false; T epsilon = tolerance ?? Scalar<T>.Tolerance;
            for (int i = 0; i < Count; ++i) if (Columns[i] != other.Columns[i] || !Scalar<T>.ApproximatelyEqual(Values[i], other.Values[i], epsilon)) return false;
            return true;
        }
        public Enumerator GetEnumerator() => new(this);
    }
    public struct Enumerator
    {
        private readonly Row _row;
        private int _index;
        internal Enumerator(Row row) { _row = row; _index = -1; }
        public Entry Current => _row[_index];
        public bool MoveNext() => ++_index < _row.Count;
        public void Reset() => _index = -1;
    }
    public readonly struct RowEditor
    {
        private readonly SparseStencilMatrix<T> _matrix;
        private readonly int _row;
        internal RowEditor(SparseStencilMatrix<T> matrix, int row) { _matrix = matrix; _row = row; }
        public Row AsReadOnly() => new(_matrix, _row);
        public int Count => _matrix._counts[_row];
        public int Capacity => _matrix.RowCapacity;
        public void Clear() => _matrix._counts[_row] = 0;
        public T GetValue(int column) => AsReadOnly().GetValue(column);
        public int SetValue(int column, T value)
        {
            _matrix.CheckIndex(column); Row view = AsReadOnly(); int offset = view.Find(column), count = Count;
            int start = _row * Capacity; Span<T> values = _matrix._values.AsSpan(start, Capacity); Span<int> columns = _matrix._columns.AsSpan(start, Capacity);
            if (offset < count && columns[offset] == column) { values[offset] = value; return count; }
            if (count == Capacity) throw new InvalidOperationException("Sparse row capacity exceeded.");
            values.Slice(offset, count - offset).CopyTo(values[(offset + 1)..]);
            columns.Slice(offset, count - offset).CopyTo(columns[(offset + 1)..]);
            values[offset] = value; columns[offset] = column; return ++_matrix._counts[_row];
        }
        public void Scale(T scalar) => Scale<T>(scalar);
        public void Scale<TOther>(TOther scalar) where TOther : unmanaged, IFloatingPointIeee754<TOther>
        {
            Vector<T>.ScaleElements(_matrix._values.AsSpan(_row * Capacity, Count), scalar);
        }
    }
}
