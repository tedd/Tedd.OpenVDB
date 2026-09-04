// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/ConjGradient.h.
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tedd.OpenVDB.Math.Pcg;

internal static class Scalar<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    public static void Validate()
    {
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double))
            throw new NotSupportedException("PCG supports float and double scalars.");
    }
    public static T Tolerance => T.CreateChecked(typeof(T) == typeof(float) ? 1e-8 : 1e-15);
    public static T Maximum => T.CreateChecked(typeof(T) == typeof(float) ? float.MaxValue : double.MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TOther Product<TOther>(T a, TOther b) where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        // C++ promotes a mixed float/double multiplication to double before casting its result.
        if (typeof(T) == typeof(double) || typeof(TOther) == typeof(double))
            return TOther.CreateTruncating(double.CreateTruncating(a) * double.CreateTruncating(b));
        return TOther.CreateTruncating(a) * b;
    }
    public static bool ApproximatelyEqual<TOther>(T a, TOther b, T tolerance) where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        if (typeof(T) == typeof(double) || typeof(TOther) == typeof(double))
            return !(System.Math.Abs(double.CreateTruncating(a) - double.CreateTruncating(b)) > double.CreateTruncating(tolerance));
        return !(T.Abs(a - T.CreateTruncating(b)) > tolerance);
    }
}

/// <summary>A contiguous, variable-length float or double vector for the OpenVDB PCG solver.</summary>
/// <remarks>Construction by size and resizing allocate unspecified contents, as in native OpenVDB.
/// Resizing to the same size preserves contents. Spans remain attached to their original storage
/// after resizing or swapping; they do not follow the vector. Concurrent mutation is unsupported.</remarks>
public sealed class Vector<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private T[] _data;
    public Vector() : this(0) { }
    public Vector(int size)
    {
        Scalar<T>.Validate(); ArgumentOutOfRangeException.ThrowIfNegative(size);
        _data = GC.AllocateUninitializedArray<T>(size);
    }
    public Vector(int size, T value) : this(size) => Fill(value);
    public Vector(ReadOnlySpan<T> values) : this(values.Length) => values.CopyTo(_data);
    public Vector(Vector<T> other)
    {
        ArgumentNullException.ThrowIfNull(other); _data = (T[])other._data.Clone();
    }
    public int Size => _data.Length;
    public bool IsEmpty => Size == 0;
    public ref T this[int index] => ref _data[index];
    public Span<T> AsSpan() => _data;
    public ReadOnlySpan<T> AsReadOnlySpan() => _data;
    public Vector<T> DeepCopy() => new(this);
    public void CopyFrom(Vector<T> other)
    {
        ArgumentNullException.ThrowIfNull(other); Resize(other.Size); other.AsReadOnlySpan().CopyTo(_data);
    }
    public void Resize(int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (size != Size) _data = GC.AllocateUninitializedArray<T>(size);
    }
    public void Swap(Vector<T> other)
    {
        ArgumentNullException.ThrowIfNull(other); (_data, other._data) = (other._data, _data);
    }
    public void Fill(T value) => _data.AsSpan().Fill(value);
    public void Scale(T scalar) => Scale<T>(scalar);
    public void Scale<TOther>(TOther scalar) where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        ScaleElements(_data, scalar);
    }
    internal static void ScaleElements<TOther>(Span<T> data, TOther scalar) where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        Scalar<TOther>.Validate(); int i = 0;
        // SIMD changes no reduction order: every lane performs one independent multiplication.
        // Mixed float/double operations retain native promotion through the scalar fallback.
        if (typeof(T) == typeof(TOther) && System.Numerics.Vector.IsHardwareAccelerated)
        {
            int width = System.Numerics.Vector<T>.Count, last = data.Length - width;
            var factor = new System.Numerics.Vector<T>(T.CreateTruncating(scalar));
            for (; i <= last; i += width)
            {
                var values = new System.Numerics.Vector<T>(data.Slice(i, width));
                (factor * values).CopyTo(data.Slice(i, width));
            }
        }
        for (; i < data.Length; ++i) data[i] = Scalar<TOther>.Product(scalar, data[i]);
    }
    /// <summary>Preserves native serial accumulation below 1,024 values and its ordered 100-bin reduction above it.</summary>
    public T Dot(Vector<T> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Size != other.Size) throw new ArithmeticException("Dot product vectors have incompatible sizes.");
        return Dot(_data, other._data);
    }
    private static T Dot(ReadOnlySpan<T> a, ReadOnlySpan<T> b)
    {
        T result = T.Zero; int size = a.Length;
        if (size < 1024)
        {
            for (int i = 0; i < size; ++i) result += a[i] * b[i];
        }
        else
        {
            // Keeping bin sums on the stack avoids a managed allocation per solver reduction.
            Span<T> partial = stackalloc T[100]; int binSize = size / 100;
            for (int bin = 0; bin < 100; ++bin)
            {
                T sum = T.Zero; int begin = bin * binSize, end = bin == 99 ? size : begin + binSize;
                for (int i = begin; i < end; ++i) sum += a[i] * b[i];
                partial[bin] = sum;
            }
            for (int bin = 0; bin < 100; ++bin) result += partial[bin];
        }
        return result;
    }
    public T InfinityNorm()
    {
        T result = T.Zero; ReadOnlySpan<T> data = _data;
        foreach (T value in data) { T absolute = T.Abs(value); result = result < absolute ? absolute : result; }
        return result;
    }
    public T L2Norm() => T.Sqrt(Dot(this));
    public bool IsFinite()
    {
        foreach (T value in _data) if (!T.IsFinite(value)) return false;
        return true;
    }
    public bool IsApproxEqual<TOther>(Vector<TOther> other, T? tolerance = null) where TOther : unmanaged, IFloatingPointIeee754<TOther>
    {
        ArgumentNullException.ThrowIfNull(other); if (Size != other.Size) return false;
        T epsilon = tolerance ?? Scalar<T>.Tolerance;
        for (int i = 0; i < Size; ++i) if (!Scalar<T>.ApproximatelyEqual(_data[i], other[i], epsilon)) return false;
        return true;
    }
}
