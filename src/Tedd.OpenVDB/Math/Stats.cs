// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of math/Stats.h.
using System.Numerics;
namespace Tedd.OpenVDB.Math;

public sealed class MinMax<T> where T : unmanaged, INumber<T>, IMinMaxValue<T>
{
    public MinMax() { Minimum = T.MaxValue; Maximum = T.MinValue; }
    public MinMax(T minimum, T maximum) { Minimum = minimum; Maximum = maximum; }
    public T Minimum { get; private set; }
    public T Maximum { get; private set; }
    public void Add(T value) { if (value < Minimum) Minimum = value; if (Maximum < value) Maximum = value; }
    public void Add(MinMax<T> other) { ArgumentNullException.ThrowIfNull(other); if (other.Minimum < Minimum) Minimum = other.Minimum; if (Maximum < other.Maximum) Maximum = other.Maximum; }
}

/// <summary>Incremental extrema with a 64-bit sample count.</summary>
public class Extrema
{
    public ulong Count { get; protected set; }
    public double Minimum { get; protected set; } = double.MaxValue;
    public double Maximum { get; protected set; } = -double.MaxValue;
    public double Range => Maximum - Minimum;
    public virtual void Add(double value) => Add(value, 1);
    public virtual void Add(double value, ulong count)
    {
        Count = checked(Count + count);
        // std::min/max take the new value as their first operand, preserving native NaN ordering.
        Minimum = Minimum < value ? Minimum : value;
        Maximum = value < Maximum ? Maximum : value;
    }
    public virtual void Add(Extrema other)
    {
        ArgumentNullException.ThrowIfNull(other); if (other.Count == 0) return;
        Join(other.Count, other.Minimum, other.Maximum);
    }
    protected void Join(ulong count, double minimum, double maximum)
    {
        Count = checked(Count + count);
        if (minimum < Minimum) Minimum = minimum;
        if (Maximum < maximum) Maximum = maximum;
    }
}

/// <summary>Stable online mean and population variance, supporting weighted samples and partition merges.</summary>
public sealed class Stats : Extrema
{
    private double _mean, _auxiliary;
    public double Mean => _mean;
    public double Variance => Count < 2 ? 0 : _auxiliary / Count;
    public double StandardDeviation => System.Math.Sqrt(Variance);
    public override void Add(double value)
    {
        base.Add(value, 1); double delta = value - _mean; _mean += delta / Count; _auxiliary += delta * (value - _mean);
    }
    public override void Add(double value, ulong count)
    {
        ulong total = checked(Count + count); double denominator = 1.0 / total; double delta = value - _mean;
        _mean += denominator * delta * count; _auxiliary += denominator * delta * delta * Count * count; base.Add(value, count);
    }
    /// <summary>Merges full statistics; merging extrema alone cannot recover a population variance.</summary>
    public override void Add(Extrema other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other is not Stats stats) throw new ArgumentException("Statistics require another Stats population.", nameof(other));
        Add(stats);
    }
    public void Add(Stats other)
    {
        ArgumentNullException.ThrowIfNull(other); if (other.Count == 0) return;
        ulong size = other.Count; double mean = other._mean, aux = other._auxiliary, min = other.Minimum, max = other.Maximum;
        ulong total = checked(Count + size); double denominator = 1.0 / total, delta = mean - _mean;
        _mean += denominator * delta * size; _auxiliary += aux + denominator * delta * delta * Count * size; Join(size, min, max);
    }
}

/// <summary>Uniform-width histogram, including the native upper-bound epsilon and final-bin clamp.</summary>
public sealed class Histogram
{
    private readonly ulong[] _bins;
    private readonly double _scale;
    public Histogram(double minimum, double maximum, int binCount = 10)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || !(minimum < maximum)) throw new ArgumentException("Histogram bounds must be finite and increasing.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binCount);
        Minimum = minimum; Maximum = maximum + 1e-10; _scale = CalculateScale(maximum - minimum, binCount); _bins = new ulong[binCount];
    }
    public Histogram(Stats stats, int binCount = 10)
    {
        ArgumentNullException.ThrowIfNull(stats); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(binCount);
        Minimum = stats.Minimum; Maximum = stats.Maximum + 1e-10;
        if (stats.Count == 0 || !double.IsFinite(Minimum) || !double.IsFinite(Maximum) || !(Minimum < Maximum)) throw new ArgumentException("Statistics must have a finite nonempty range.", nameof(stats));
        _scale = CalculateScale(Maximum - Minimum, binCount); _bins = new ulong[binCount];
    }
    public double Minimum { get; }
    public double Maximum { get; }
    public int BinCount => _bins.Length;
    public ulong Count { get; private set; }
    public ReadOnlySpan<ulong> Bins => _bins;
    public ulong this[int index] => _bins[index];
    public double BinMinimum(int index) { ValidateBin(index); return Minimum + index / _scale; }
    public double BinMaximum(int index) { ValidateBin(index); return Minimum + (index + 1.0) / _scale; }
    public bool Add(double value, ulong count = 1)
    {
        if (double.IsNaN(value) || value < Minimum || value > Maximum) return false;
        double scaled = _scale * (value - Minimum); int index = scaled >= _bins.Length ? _bins.Length - 1 : (int)scaled;
        ulong total = checked(Count + count), bin = checked(_bins[index] + count); _bins[index] = bin; Count = total; return true;
    }
    public bool Add(Histogram other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (System.Math.Abs(Minimum - other.Minimum) > 1e-15 || System.Math.Abs(Maximum - other.Maximum) > 1e-15 || BinCount != other.BinCount) return false;
        ulong total = checked(Count + other.Count);
        for (int i = 0; i < _bins.Length; i++) _bins[i] = checked(_bins[i] + other._bins[i]); Count = total; return true;
    }
    private static double CalculateScale(double width, int binCount)
    {
        double scale = binCount / width;
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(scale) || scale <= 0)
            throw new ArgumentException("Histogram range must have a finite positive width and scale.");
        return scale;
    }
    private void ValidateBin(int index) { if ((uint)index >= (uint)_bins.Length) throw new ArgumentOutOfRangeException(nameof(index)); }
}
