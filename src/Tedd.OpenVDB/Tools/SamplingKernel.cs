// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

public enum SamplingOrder { Point, Box, Quadratic }

internal static class SamplingKernel<T, TSource> where T : unmanaged where TSource : struct, ISampleSource<T>
{
    internal static void Validate(SamplingOrder order, bool staggered)
    {
        if (!Enum.IsDefined(order)) throw new ArgumentOutOfRangeException(nameof(order));
        if (staggered && !GeneralSamplingArithmetic<T>.IsVector3) throw new NotSupportedException("Staggered sampling requires Vec3d, Vec3f or Vec3i.");
        if (order != SamplingOrder.Point && !GeneralSamplingArithmetic<T>.Supported) throw new NotSupportedException($"Interpolation is not defined for {typeof(T)}.");
    }
    internal static bool Sample(TSource source, Vec3d position, SamplingOrder order, bool staggered, out T value)
    {
        if (staggered)
        {
            bool active = Sample(source, position + new Vec3d(0.5, 0, 0), order, false, out T x);
            active |= Sample(source, position + new Vec3d(0, 0.5, 0), order, false, out T y);
            active |= Sample(source, position + new Vec3d(0, 0, 0.5), order, false, out T z);
            value = GeneralSamplingArithmetic<T>.JoinComponents(x, y, z); return active;
        }
        if (order == SamplingOrder.Point) return source.ProbeValue(SamplingCoordinates.Round(position), out value);
        int lower = order == SamplingOrder.Quadratic ? 1 : 0, size = lower + 2;
        Coord origin = SamplingCoordinates.Floor(position, lower, 1);
        var fraction = position - origin.AsVec3d();
        Span<T> values = stackalloc T[size * size * size]; bool found = false;
        for (int x = 0; x < size; x++) for (int y = 0; y < size; y++) for (int z = 0; z < size; z++)
            found |= source.ProbeValue(new(origin.X + x - lower, origin.Y + y - lower, origin.Z + z - lower), out values[(x * size + y) * size + z]);
        value = Interpolate(values, fraction, order); return found;
    }
    internal static T Interpolate(ReadOnlySpan<T> values, Vec3d fraction, SamplingOrder order)
    {
        if (order == SamplingOrder.Box)
        {
            if (values.Length < 8) throw new ArgumentException("Eight source elements are required.", nameof(values));
            return GeneralSamplingArithmetic<T>.Linear(
                GeneralSamplingArithmetic<T>.Linear(GeneralSamplingArithmetic<T>.Linear(values[0], values[1], fraction.Z), GeneralSamplingArithmetic<T>.Linear(values[2], values[3], fraction.Z), fraction.Y),
                GeneralSamplingArithmetic<T>.Linear(GeneralSamplingArithmetic<T>.Linear(values[4], values[5], fraction.Z), GeneralSamplingArithmetic<T>.Linear(values[6], values[7], fraction.Z), fraction.Y), fraction.X);
        }
        if (values.Length < 27) throw new ArgumentException("Twenty-seven source elements are required.", nameof(values));
        Span<T> alongX = stackalloc T[3], alongY = stackalloc T[3];
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            { int offset = (x * 3 + y) * 3; alongY[y] = GeneralSamplingArithmetic<T>.Quadratic(values[offset], values[offset + 1], values[offset + 2], fraction.Z); }
            alongX[x] = GeneralSamplingArithmetic<T>.Quadratic(alongY[0], alongY[1], alongY[2], fraction.Y);
        }
        return GeneralSamplingArithmetic<T>.Quadratic(alongX[0], alongX[1], alongX[2], fraction.X);
    }
}
