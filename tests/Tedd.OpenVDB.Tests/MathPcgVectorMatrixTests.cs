using System.Numerics;
using Pcg = Tedd.OpenVDB.Math.Pcg;
namespace Tedd.OpenVDB.Tests;

public class MathPcgVectorMatrixTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VectorStorageCopyResizeAndSwapSemantics(bool single)
    {
        if (single) Storage<float>(); else Storage<double>();
    }
    private static void Storage<T>() where T : unmanaged, IFloatingPointIeee754<T>
    {
        var vector = new Pcg.Vector<T>(3, T.One); var copy = vector.DeepCopy(); copy[0] = T.Zero;
        Assert.Equal(T.One, vector[0]); Assert.Equal(3, vector.Size); Assert.False(vector.IsEmpty);
        Span<T> original = vector.AsSpan(); vector.Resize(3); Assert.True(original.Overlaps(vector.AsSpan()));
        vector.Resize(4); Assert.False(original.Overlaps(vector.AsSpan())); vector.Fill(T.CreateChecked(2));
        var other = new Pcg.Vector<T>(1, T.CreateChecked(3)); vector.Swap(other);
        Assert.Equal(T.CreateChecked(3), vector[0]); Assert.Equal(4, other.Size);
        vector.CopyFrom(other); vector[0] = T.Zero; Assert.Equal(T.CreateChecked(2), other[0]);
        other.CopyFrom(other); other.Swap(other); Assert.Equal(T.CreateChecked(2), other[0]);
        vector.Resize(0); Assert.True(vector.IsEmpty); Assert.Empty(vector.AsSpan().ToArray());
        var fromSpan = new Pcg.Vector<T>(original); original[0] = T.Zero; Assert.Equal(T.One, fromSpan[0]);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VectorIEEEAndApproximateComparisonSemantics(bool single)
    {
        if (single) Ieee<float>(); else Ieee<double>();
    }
    private static void Ieee<T>() where T : unmanaged, IFloatingPointIeee754<T>
    {
        var a = new Pcg.Vector<T>(3, T.Zero); var b = new Pcg.Vector<T>(3, T.One);
        a[0] = T.NaN; Assert.False(a.IsFinite()); Assert.True(T.IsNaN(a.Dot(b))); Assert.True(T.IsNaN(a.L2Norm()));
        Assert.Equal(T.Zero, a.InfinityNorm()); a.Fill(T.NaN); Assert.True(a.IsApproxEqual(b));
        a[1] = T.PositiveInfinity; Assert.Equal(T.PositiveInfinity, a.InfinityNorm());
        a.Fill(T.One); Assert.False(a.IsApproxEqual(b, -T.One)); Assert.True(a.IsApproxEqual(b, T.Zero));
        b.Resize(4); Assert.False(a.IsApproxEqual(b));
        var empty = new Pcg.Vector<T>(); Assert.Equal(T.Zero, empty.Dot(empty)); Assert.Equal(T.Zero, empty.InfinityNorm()); Assert.Equal(T.Zero, empty.L2Norm()); Assert.True(empty.IsFinite());
    }
    [Fact]
    public void VectorRejectsInvalidDimensionsAndUnsupportedTypes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pcg.Vector<float>(-1));
        Assert.Throws<NotSupportedException>(() => new Pcg.Vector<Half>());
        var a = new Pcg.Vector<double>(3, 0); var b = new Pcg.Vector<double>(2, 0);
        Assert.Throws<ArithmeticException>(() => a.Dot(b)); Assert.Throws<ArgumentOutOfRangeException>(() => a.Resize(-1));
        Assert.Throws<IndexOutOfRangeException>(() => a[-1]); Assert.Throws<IndexOutOfRangeException>(() => a[3]);
        Assert.Throws<ArgumentNullException>(() => a.CopyFrom(null!));
    }
    [Theory]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(10037)]
    public void DotPreservesNativeBinBoundariesWithoutAllocating(int count)
    {
        var a = new Pcg.Vector<double>(count); var b = new Pcg.Vector<double>(count, 1);
        for (int i = 0; i < count; ++i) a[i] = i % 4 == 0 ? 1e20 : i % 4 == 2 ? -1e20 : 1;
        double expected = 0;
        if (count < 1024) foreach (double value in a.AsReadOnlySpan()) expected += value;
        else for (int bin = 0; bin < 100; ++bin)
        {
            double partial = 0; int begin = bin * (count / 100), end = bin == 99 ? count : begin + count / 100;
            for (int i = begin; i < end; ++i) partial += a[i]; expected += partial;
        }
        Assert.Equal(expected, a.Dot(b));
        for (int i = 0; i < 5; ++i) _ = a.Dot(b);
        long before = GC.GetAllocatedBytesForCurrentThread(); double last = 0;
        for (int i = 0; i < 20; ++i) last = a.Dot(b);
        long allocation = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(expected, last); Assert.Equal(0, allocation);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SparseRowsRetainStoredZerosAndSortedColumns(bool single)
    {
        if (single) Rows<float>(); else Rows<double>();
    }
    private static void Rows<T>() where T : unmanaged, IFloatingPointIeee754<T>
    {
        var matrix = new Pcg.SparseStencilMatrix<T>(8, 4); var row = matrix.GetRowEditor(2); var view = row.AsReadOnly();
        Assert.True(view.IsEmpty); Assert.Equal(1, row.SetValue(7, T.One)); row.SetValue(2, T.Zero); row.SetValue(0, -T.One); row.SetValue(5, T.CreateChecked(2));
        Assert.Equal(new[] { 0, 2, 5, 7 }, view.Columns.ToArray()); Assert.Equal(4, view.Count);
        Assert.Equal(T.Zero, view.GetValue(2, out bool stored)); Assert.True(stored);
        Assert.Equal(T.Zero, view.GetValue(3, out stored)); Assert.False(stored);
        Assert.Equal(4, row.SetValue(7, T.CreateChecked(3)));
        Assert.Throws<InvalidOperationException>(() => row.SetValue(6, T.One)); Assert.Equal(4, row.Count); Assert.Equal(T.Zero, matrix[2, 6]);
        var copy = matrix.DeepCopy(); copy.SetValue(2, 0, T.NaN); Assert.True(matrix.IsFinite()); Assert.False(copy.IsFinite());
        Assert.True(matrix.IsApproxEqual(copy)); copy.GetRowEditor(2).Clear(); Assert.False(matrix.IsApproxEqual(copy)); Assert.True(copy.IsFinite());
        int count = 0; foreach (var entry in view) { Assert.Equal(view[count++], entry); }
        Assert.Equal(4, count);
        var enumerator = view.GetEnumerator(); Assert.Throws<ArgumentOutOfRangeException>(() => enumerator.Current); Assert.True(enumerator.MoveNext()); enumerator.Reset(); Assert.True(enumerator.MoveNext());
        row.Clear(); Assert.True(view.IsEmpty); Assert.Equal(T.Zero, matrix[2, 7]); Assert.True(matrix.IsFinite());
    }
    [Fact]
    public void SparseShapeBoundsAndOverlappingBuffersAreCheckedBeforeMutation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pcg.SparseStencilMatrix<double>(-1, 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Pcg.SparseStencilMatrix<double>(1, 0));
        Assert.Throws<OverflowException>(() => new Pcg.SparseStencilMatrix<double>(int.MaxValue, 7));
        var a = new Pcg.SparseStencilMatrix<double>(3, 3); for (int i = 0; i < 3; ++i) a[i, i] = 2;
        Assert.Throws<ArgumentOutOfRangeException>(() => a[-1, 0]); Assert.Throws<ArgumentOutOfRangeException>(() => a[0, 3]);
        var vector = new Pcg.Vector<double>(3, 2); Assert.Throws<ArgumentException>(() => a.VectorMultiply(vector, vector)); Assert.Equal(new[] { 2.0, 2, 2 }, vector.AsSpan().ToArray());
        Assert.Throws<ArithmeticException>(() => a.VectorMultiply(new Pcg.Vector<double>(2), vector));
        double[] buffer = [1, 2, 3, 4, 5, 6, 999];
        Assert.Throws<ArgumentException>(() => a.VectorMultiply<double>(buffer.AsSpan(0, 3), buffer.AsSpan(1, 3)));
        a.VectorMultiply<double>(buffer.AsSpan(0, 3), buffer.AsSpan(3)); Assert.Equal(new[] { 1.0, 2, 3, 2, 4, 6, 999 }, buffer);
        Assert.Throws<IndexOutOfRangeException>(() => a.GetConstRow(2).Dot<double>(new double[1]));
        Assert.Equal(0, a.GetConstRow(2).Dot<double>(ReadOnlySpan<double>.Empty));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RandomSparseProductsMatchIndependentDenseReference(bool single)
    {
        if (single) Products<float>(); else Products<double>();
    }
    private static void Products<T>() where T : unmanaged, IFloatingPointIeee754<T>
    {
        const int n = 37; var random = new Random(88512); var matrix = new Pcg.SparseStencilMatrix<T>(n, 7); var dense = new T[n, n];
        for (int row = 0; row < n; ++row)
        {
            var columns = Enumerable.Range(0, n).OrderBy(_ => random.Next()).Take(7).ToArray();
            foreach (int column in columns) matrix[row, column] = dense[row, column] = T.CreateChecked(random.Next(-32, 33)) / T.CreateChecked(8);
        }
        var input = new Pcg.Vector<T>(n); var output = new Pcg.Vector<T>(n);
        for (int i = 0; i < n; ++i) input[i] = T.CreateChecked(random.Next(-32, 33)) / T.CreateChecked(8);
        matrix.VectorMultiply(input, output);
        for (int row = 0; row < n; ++row)
        {
            T expected = T.Zero; for (int column = 0; column < n; ++column) expected += dense[row, column] * input[column]; Assert.Equal(expected, output[row]);
        }
        matrix.Scale(T.CreateChecked(2)); matrix.VectorMultiply(input, output);
        for (int row = 0; row < n; ++row) { T expected = T.Zero; for (int column = 0; column < n; ++column) expected += (dense[row, column] * T.CreateChecked(2)) * input[column]; Assert.Equal(expected, output[row]); }
    }
    [Fact]
    public void DistinctSparseRowsCanBeBuiltConcurrently()
    {
        var matrix = new Pcg.SparseStencilMatrix<double>(200, 3);
        Parallel.For(0, matrix.Size, row => { matrix[row, row] = 3; if (row > 0) matrix[row, row - 1] = -1; if (row + 1 < matrix.Size) matrix[row, row + 1] = -1; });
        var result = new Pcg.Vector<double>(200); matrix.VectorMultiply(new Pcg.Vector<double>(200, 1), result);
        Assert.Equal(2, result[0]); Assert.Equal(2, result[199]); for (int i = 1; i < 199; ++i) Assert.Equal(1, result[i]);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(33)]
    [InlineData(257)]
    public void SimdScalePreservesScalarRoundingSignedZeroAndTails(int count)
    {
        Scale<float>(count); Scale<double>(count);
        static void Scale<T>(int size) where T : unmanaged, IFloatingPointIeee754<T>
        {
            foreach (double scale in new[] { 0.0, -0.0, 1.0, -1.0, 1.125, double.PositiveInfinity, double.NaN })
            {
                T factor = T.CreateTruncating(scale); var input = new Pcg.Vector<T>(size);
                for (int i = 0; i < size; ++i) input[i] = i % 7 == 0 ? -T.Zero : i % 7 == 1 ? T.PositiveInfinity : T.CreateTruncating(i % 17 - 8) / T.CreateTruncating(7);
                T[] expected = input.AsSpan().ToArray(); for (int i = 0; i < size; ++i) expected[i] = factor * expected[i];
                input.Scale(factor);
                for (int i = 0; i < size; ++i)
                {
                    if (T.IsNaN(expected[i])) Assert.True(T.IsNaN(input[i]));
                    else Assert.Equal(BitConverter.DoubleToInt64Bits(double.CreateTruncating(expected[i])), BitConverter.DoubleToInt64Bits(double.CreateTruncating(input[i])));
                }
            }
        }
    }
}
