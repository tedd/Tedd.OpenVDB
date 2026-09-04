// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/Interpolation.h.

using System.Numerics;
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tools;

/// <summary>Nearest-neighbor sampling in index space, rounding halfway coordinates away from zero.</summary>
public static class PointSampler
{
    public const string Name = "point";
    public const int Radius = 0;
    public const int Order = 0;
    public const bool Mipmap = false;
    public const bool Consistent = true;
    public const bool Staggered = false;

    public static T Sample<T>(VdbTree<T> tree, Vec3d position) where T : unmanaged
    {
        Sample(tree, position, out T value);
        return value;
    }

    public static bool Sample<T>(VdbTree<T> tree, Vec3d position, out T value) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree);
        return tree.ProbeValue(SamplingCoordinates.Round(position), out value);
    }

    public static T Sample<T>(ValueAccessor<T> accessor, Vec3d position) where T : unmanaged
    {
        Sample(accessor, position, out T value);
        return value;
    }

    public static bool Sample<T>(ValueAccessor<T> accessor, Vec3d position, out T value) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(accessor);
        return accessor.ProbeValue(SamplingCoordinates.Round(position), out value);
    }
}

/// <summary>Trilinear sampling of the eight voxels at floor(position) and its positive neighbors.</summary>
/// <remarks>The sample is active when any support voxel is active, including zero-weight neighbors.</remarks>
public static class BoxSampler
{
    public const string Name = "box";
    public const int Radius = 1;
    public const int Order = 1;
    public const bool Mipmap = true;
    public const bool Consistent = true;
    public const bool Staggered = false;

    public static T Sample<T>(VdbTree<T> tree, Vec3d position) where T : unmanaged, INumber<T>
    {
        Sample(tree, position, out T value);
        return value;
    }

    public static bool Sample<T>(VdbTree<T> tree, Vec3d position, out T value) where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(tree);
        return Sample<T, TreeSampleSource<T>>(new(tree), position, out value);
    }

    public static T Sample<T>(ValueAccessor<T> accessor, Vec3d position) where T : unmanaged, INumber<T>
    {
        Sample(accessor, position, out T value);
        return value;
    }

    public static bool Sample<T>(ValueAccessor<T> accessor, Vec3d position, out T value) where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(accessor);
        return Sample<T, AccessorSampleSource<T>>(new(accessor), position, out value);
    }

    private static bool Sample<T, TSource>(TSource source, Vec3d position, out T value)
        where T : unmanaged, INumber<T> where TSource : struct, ISampleSource<T>
    {
        var origin = SamplingCoordinates.Floor(position, 0, 1);
        var fraction = new Vec3d(position.X - origin.X, position.Y - origin.Y, position.Z - origin.Z);
        Span<T> values = stackalloc T[8];
        var active = ProbeValues<T, TSource>(source, origin, values);
        value = TrilinearInterpolation<T>(values, fraction);
        return active;
    }

    /// <summary>Retrieves eight support values in x-major, z-minor order.</summary>
    public static void GetValues<T>(VdbTree<T> tree, Coord origin, Span<T> destination) where T : unmanaged =>
        ProbeValues(tree, origin, destination);

    public static bool ProbeValues<T>(VdbTree<T> tree, Coord origin, Span<T> destination) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree);
        return ProbeValues<T, TreeSampleSource<T>>(new(tree), origin, destination);
    }

    public static bool ProbeValues<T>(ValueAccessor<T> accessor, Coord origin, Span<T> destination) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(accessor);
        return ProbeValues<T, AccessorSampleSource<T>>(new(accessor), origin, destination);
    }

    private static bool ProbeValues<T, TSource>(TSource source, Coord origin, Span<T> destination)
        where T : unmanaged where TSource : struct, ISampleSource<T>
    {
        if (destination.Length < 8) throw new ArgumentException("Eight destination elements are required.", nameof(destination));
        SamplingCoordinates.ValidateStencil(origin, 0, 1);
        var active = false;
        for (var x = 0; x < 2; ++x)
            for (var y = 0; y < 2; ++y)
                for (var z = 0; z < 2; ++z)
                    active |= source.ProbeValue(new Coord(origin.X + x, origin.Y + y, origin.Z + z), out destination[(x << 2) | (y << 1) | z]);
        return active;
    }

    /// <summary>Interpolates eight x-major, z-minor samples at unit-cell coordinates.</summary>
    public static T TrilinearInterpolation<T>(ReadOnlySpan<T> values, Vec3d fraction) where T : unmanaged, INumber<T>
    {
        if (values.Length < 8) throw new ArgumentException("Eight source elements are required.", nameof(values));
        // Preserve upstream's z, then y, then x order and per-stage conversion to ValueT.
        var lower = SamplingArithmetic<T>.Linear(
            SamplingArithmetic<T>.Linear(values[0], values[1], fraction.Z),
            SamplingArithmetic<T>.Linear(values[2], values[3], fraction.Z), fraction.Y);
        var upper = SamplingArithmetic<T>.Linear(
            SamplingArithmetic<T>.Linear(values[4], values[5], fraction.Z),
            SamplingArithmetic<T>.Linear(values[6], values[7], fraction.Z), fraction.Y);
        return SamplingArithmetic<T>.Linear(lower, upper, fraction.X);
    }

    public static void Extrema<T>(ReadOnlySpan<T> values, out T minimum, out T maximum) where T : unmanaged, INumber<T>
    {
        if (values.Length < 8) throw new ArgumentException("Eight source elements are required.", nameof(values));
        minimum = maximum = values[0];
        for (var index = 1; index < 8; ++index)
        {
            var current = values[index];
            minimum = minimum < current ? minimum : current;
            maximum = maximum < current ? current : maximum;
        }
    }
}

/// <summary>Separable quadratic sampling of the 27 voxels centered on floor(position).</summary>
/// <remarks>The sample is active when any support voxel is active, including zero-weight neighbors.</remarks>
public static class QuadraticSampler
{
    public const string Name = "quadratic";
    public const int Radius = 1;
    public const int Order = 2;
    public const bool Mipmap = true;
    public const bool Consistent = false;
    public const bool Staggered = false;

    public static T Sample<T>(VdbTree<T> tree, Vec3d position) where T : unmanaged, INumber<T>
    {
        Sample(tree, position, out T value);
        return value;
    }

    public static bool Sample<T>(VdbTree<T> tree, Vec3d position, out T value) where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(tree);
        return Sample<T, TreeSampleSource<T>>(new(tree), position, out value);
    }

    public static T Sample<T>(ValueAccessor<T> accessor, Vec3d position) where T : unmanaged, INumber<T>
    {
        Sample(accessor, position, out T value);
        return value;
    }

    public static bool Sample<T>(ValueAccessor<T> accessor, Vec3d position, out T value) where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(accessor);
        return Sample<T, AccessorSampleSource<T>>(new(accessor), position, out value);
    }

    private static bool Sample<T, TSource>(TSource source, Vec3d position, out T value)
        where T : unmanaged, INumber<T> where TSource : struct, ISampleSource<T>
    {
        var center = SamplingCoordinates.Floor(position, 1, 1);
        var fraction = new Vec3d(position.X - center.X, position.Y - center.Y, position.Z - center.Z);
        Span<T> values = stackalloc T[27];
        var active = false;
        for (var x = 0; x < 3; ++x)
            for (var y = 0; y < 3; ++y)
                for (var z = 0; z < 3; ++z)
                    active |= source.ProbeValue(new Coord(center.X + x - 1, center.Y + y - 1, center.Z + z - 1), out values[(x * 3 + y) * 3 + z]);
        value = TriquadraticInterpolation<T>(values, fraction);
        return active;
    }

    /// <summary>Interpolates 27 x-major, z-minor samples at offsets relative to the center sample.</summary>
    public static T TriquadraticInterpolation<T>(ReadOnlySpan<T> values, Vec3d fraction) where T : unmanaged, INumber<T>
    {
        if (values.Length < 27) throw new ArgumentException("Twenty-seven source elements are required.", nameof(values));
        Span<T> alongX = stackalloc T[3];
        Span<T> alongY = stackalloc T[3];
        for (var x = 0; x < 3; ++x)
        {
            for (var y = 0; y < 3; ++y)
            {
                var offset = (x * 3 + y) * 3;
                alongY[y] = SamplingArithmetic<T>.Quadratic(values[offset], values[offset + 1], values[offset + 2], fraction.Z);
            }
            alongX[x] = SamplingArithmetic<T>.Quadratic(alongY[0], alongY[1], alongY[2], fraction.Y);
        }
        return SamplingArithmetic<T>.Quadratic(alongX[0], alongX[1], alongX[2], fraction.X);
    }
}

internal interface ISampleSource<T> where T : unmanaged
{
    bool ProbeValue(Coord coordinate, out T value);
}

internal readonly struct TreeSampleSource<T>(VdbTree<T> tree) : ISampleSource<T> where T : unmanaged
{
    public bool ProbeValue(Coord coordinate, out T value) => tree.ProbeValue(coordinate, out value);
}

internal readonly struct AccessorSampleSource<T>(ValueAccessor<T> accessor) : ISampleSource<T> where T : unmanaged
{
    public bool ProbeValue(Coord coordinate, out T value) => accessor.ProbeValue(coordinate, out value);
}

internal static class SamplingArithmetic<T> where T : unmanaged, INumber<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static T Linear(T a, T b, double weight) => a + T.CreateTruncating(double.CreateChecked(b - a) * weight);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static T Quadratic(T lower, T center, T upper, double weight)
    {
        var a = T.CreateTruncating(0.5 * double.CreateChecked(lower + upper) - double.CreateChecked(center));
        var b = T.CreateTruncating(0.5 * double.CreateChecked(upper - lower));
        return T.CreateTruncating(weight * (weight * double.CreateChecked(a) + double.CreateChecked(b)) + double.CreateChecked(center));
    }
}

internal static class SamplingCoordinates
{
    internal static Coord Round(Vec3d position)
    {
        ValidateFinite(position);
        var x = System.Math.Round(position.X, MidpointRounding.AwayFromZero);
        var y = System.Math.Round(position.Y, MidpointRounding.AwayFromZero);
        var z = System.Math.Round(position.Z, MidpointRounding.AwayFromZero);
        if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue || z < int.MinValue || z > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(position), "The rounded coordinate is outside the grid index domain.");
        return new Coord((int)x, (int)y, (int)z);
    }

    internal static Coord Floor(Vec3d position, int lowerRadius, int upperRadius)
    {
        ValidateFinite(position);
        var x = System.Math.Floor(position.X);
        var y = System.Math.Floor(position.Y);
        var z = System.Math.Floor(position.Z);
        if (x < int.MinValue + lowerRadius || x > int.MaxValue - upperRadius ||
            y < int.MinValue + lowerRadius || y > int.MaxValue - upperRadius ||
            z < int.MinValue + lowerRadius || z > int.MaxValue - upperRadius)
            throw new ArgumentOutOfRangeException(nameof(position), "The sampling stencil exceeds the grid index domain.");
        return new Coord((int)x, (int)y, (int)z);
    }

    internal static void ValidateStencil(Coord origin, int lowerRadius, int upperRadius)
    {
        if (origin.X < int.MinValue + lowerRadius || origin.X > int.MaxValue - upperRadius ||
            origin.Y < int.MinValue + lowerRadius || origin.Y > int.MaxValue - upperRadius ||
            origin.Z < int.MinValue + lowerRadius || origin.Z > int.MaxValue - upperRadius)
            throw new ArgumentOutOfRangeException(nameof(origin), "The sampling stencil exceeds the grid index domain.");
    }

    private static void ValidateFinite(Vec3d position)
    {
        if (!position.IsFinite) throw new ArgumentOutOfRangeException(nameof(position), "Sampling coordinates must be finite.");
    }
}
