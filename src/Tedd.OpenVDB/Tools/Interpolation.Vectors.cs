// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.Tools;

public static partial class BoxSampler
{
    public static Vec2d Sample(VdbTree<Vec2d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(VdbTree<Vec2d> source, Vec3d position, out Vec2d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec2d, TreeSampleSource<Vec2d>>.Sample(new(source), position, SamplingOrder.Box, false, out value); }
    public static Vec2d Sample(ValueAccessor<Vec2d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(ValueAccessor<Vec2d> source, Vec3d position, out Vec2d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec2d, AccessorSampleSource<Vec2d>>.Sample(new(source), position, SamplingOrder.Box, false, out value); }
    public static Vec2d TrilinearInterpolation(ReadOnlySpan<Vec2d> values, Vec3d fraction) => SamplingKernel<Vec2d, TreeSampleSource<Vec2d>>.Interpolate(values, fraction, SamplingOrder.Box);
    public static void Extrema(ReadOnlySpan<Vec2d> values, out Vec2d minimum, out Vec2d maximum)
    {
        if (values.Length < 8) throw new ArgumentException("Eight values are required.", nameof(values));
        minimum = maximum = values[0];
        for (int i = 1; i < 8; i++) { if (ValueComparer<Vec2d>.Default.Compare(minimum, values[i]) >= 0) minimum = values[i]; if (ValueComparer<Vec2d>.Default.Compare(maximum, values[i]) < 0) maximum = values[i]; }
    }
    public static Vec3d Sample(VdbTree<Vec3d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(VdbTree<Vec3d> source, Vec3d position, out Vec3d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3d, TreeSampleSource<Vec3d>>.Sample(new(source), position, SamplingOrder.Box, false, out value); }
    public static Vec3d Sample(ValueAccessor<Vec3d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(ValueAccessor<Vec3d> source, Vec3d position, out Vec3d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3d, AccessorSampleSource<Vec3d>>.Sample(new(source), position, SamplingOrder.Box, false, out value); }
    public static Vec3d TrilinearInterpolation(ReadOnlySpan<Vec3d> values, Vec3d fraction) => SamplingKernel<Vec3d, TreeSampleSource<Vec3d>>.Interpolate(values, fraction, SamplingOrder.Box);
    public static void Extrema(ReadOnlySpan<Vec3d> values, out Vec3d minimum, out Vec3d maximum)
    {
        if (values.Length < 8) throw new ArgumentException("Eight values are required.", nameof(values));
        minimum = maximum = values[0];
        for (int i = 1; i < 8; i++) { if (ValueComparer<Vec3d>.Default.Compare(minimum, values[i]) >= 0) minimum = values[i]; if (ValueComparer<Vec3d>.Default.Compare(maximum, values[i]) < 0) maximum = values[i]; }
    }
    public static Vec3f Sample(VdbTree<Vec3f> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(VdbTree<Vec3f> source, Vec3d position, out Vec3f value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3f, TreeSampleSource<Vec3f>>.Sample(new(source), position, SamplingOrder.Box, false, out value); }
    public static Vec3f Sample(ValueAccessor<Vec3f> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(ValueAccessor<Vec3f> source, Vec3d position, out Vec3f value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3f, AccessorSampleSource<Vec3f>>.Sample(new(source), position, SamplingOrder.Box, false, out value); }
    public static Vec3f TrilinearInterpolation(ReadOnlySpan<Vec3f> values, Vec3d fraction) => SamplingKernel<Vec3f, TreeSampleSource<Vec3f>>.Interpolate(values, fraction, SamplingOrder.Box);
    public static void Extrema(ReadOnlySpan<Vec3f> values, out Vec3f minimum, out Vec3f maximum)
    {
        if (values.Length < 8) throw new ArgumentException("Eight values are required.", nameof(values));
        minimum = maximum = values[0];
        for (int i = 1; i < 8; i++) { if (ValueComparer<Vec3f>.Default.Compare(minimum, values[i]) >= 0) minimum = values[i]; if (ValueComparer<Vec3f>.Default.Compare(maximum, values[i]) < 0) maximum = values[i]; }
    }
    public static Vec3i Sample(VdbTree<Vec3i> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(VdbTree<Vec3i> source, Vec3d position, out Vec3i value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3i, TreeSampleSource<Vec3i>>.Sample(new(source), position, SamplingOrder.Box, false, out value); }
    public static Vec3i Sample(ValueAccessor<Vec3i> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(ValueAccessor<Vec3i> source, Vec3d position, out Vec3i value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3i, AccessorSampleSource<Vec3i>>.Sample(new(source), position, SamplingOrder.Box, false, out value); }
    public static Vec3i TrilinearInterpolation(ReadOnlySpan<Vec3i> values, Vec3d fraction) => SamplingKernel<Vec3i, TreeSampleSource<Vec3i>>.Interpolate(values, fraction, SamplingOrder.Box);
    public static void Extrema(ReadOnlySpan<Vec3i> values, out Vec3i minimum, out Vec3i maximum)
    {
        if (values.Length < 8) throw new ArgumentException("Eight values are required.", nameof(values));
        minimum = maximum = values[0];
        for (int i = 1; i < 8; i++) { if (ValueComparer<Vec3i>.Default.Compare(minimum, values[i]) >= 0) minimum = values[i]; if (ValueComparer<Vec3i>.Default.Compare(maximum, values[i]) < 0) maximum = values[i]; }
    }
    public static Vec4d Sample(VdbTree<Vec4d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(VdbTree<Vec4d> source, Vec3d position, out Vec4d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec4d, TreeSampleSource<Vec4d>>.Sample(new(source), position, SamplingOrder.Box, false, out value); }
    public static Vec4d Sample(ValueAccessor<Vec4d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(ValueAccessor<Vec4d> source, Vec3d position, out Vec4d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec4d, AccessorSampleSource<Vec4d>>.Sample(new(source), position, SamplingOrder.Box, false, out value); }
    public static Vec4d TrilinearInterpolation(ReadOnlySpan<Vec4d> values, Vec3d fraction) => SamplingKernel<Vec4d, TreeSampleSource<Vec4d>>.Interpolate(values, fraction, SamplingOrder.Box);
    public static void Extrema(ReadOnlySpan<Vec4d> values, out Vec4d minimum, out Vec4d maximum)
    {
        if (values.Length < 8) throw new ArgumentException("Eight values are required.", nameof(values));
        minimum = maximum = values[0];
        for (int i = 1; i < 8; i++) { if (ValueComparer<Vec4d>.Default.Compare(minimum, values[i]) >= 0) minimum = values[i]; if (ValueComparer<Vec4d>.Default.Compare(maximum, values[i]) < 0) maximum = values[i]; }
    }
}

public static partial class QuadraticSampler
{
    public static Vec2d Sample(VdbTree<Vec2d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(VdbTree<Vec2d> source, Vec3d position, out Vec2d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec2d, TreeSampleSource<Vec2d>>.Sample(new(source), position, SamplingOrder.Quadratic, false, out value); }
    public static Vec2d Sample(ValueAccessor<Vec2d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(ValueAccessor<Vec2d> source, Vec3d position, out Vec2d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec2d, AccessorSampleSource<Vec2d>>.Sample(new(source), position, SamplingOrder.Quadratic, false, out value); }
    public static Vec2d TriquadraticInterpolation(ReadOnlySpan<Vec2d> values, Vec3d fraction) => SamplingKernel<Vec2d, TreeSampleSource<Vec2d>>.Interpolate(values, fraction, SamplingOrder.Quadratic);
    public static Vec3d Sample(VdbTree<Vec3d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(VdbTree<Vec3d> source, Vec3d position, out Vec3d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3d, TreeSampleSource<Vec3d>>.Sample(new(source), position, SamplingOrder.Quadratic, false, out value); }
    public static Vec3d Sample(ValueAccessor<Vec3d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(ValueAccessor<Vec3d> source, Vec3d position, out Vec3d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3d, AccessorSampleSource<Vec3d>>.Sample(new(source), position, SamplingOrder.Quadratic, false, out value); }
    public static Vec3d TriquadraticInterpolation(ReadOnlySpan<Vec3d> values, Vec3d fraction) => SamplingKernel<Vec3d, TreeSampleSource<Vec3d>>.Interpolate(values, fraction, SamplingOrder.Quadratic);
    public static Vec3f Sample(VdbTree<Vec3f> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(VdbTree<Vec3f> source, Vec3d position, out Vec3f value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3f, TreeSampleSource<Vec3f>>.Sample(new(source), position, SamplingOrder.Quadratic, false, out value); }
    public static Vec3f Sample(ValueAccessor<Vec3f> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(ValueAccessor<Vec3f> source, Vec3d position, out Vec3f value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3f, AccessorSampleSource<Vec3f>>.Sample(new(source), position, SamplingOrder.Quadratic, false, out value); }
    public static Vec3f TriquadraticInterpolation(ReadOnlySpan<Vec3f> values, Vec3d fraction) => SamplingKernel<Vec3f, TreeSampleSource<Vec3f>>.Interpolate(values, fraction, SamplingOrder.Quadratic);
    public static Vec3i Sample(VdbTree<Vec3i> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(VdbTree<Vec3i> source, Vec3d position, out Vec3i value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3i, TreeSampleSource<Vec3i>>.Sample(new(source), position, SamplingOrder.Quadratic, false, out value); }
    public static Vec3i Sample(ValueAccessor<Vec3i> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(ValueAccessor<Vec3i> source, Vec3d position, out Vec3i value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec3i, AccessorSampleSource<Vec3i>>.Sample(new(source), position, SamplingOrder.Quadratic, false, out value); }
    public static Vec3i TriquadraticInterpolation(ReadOnlySpan<Vec3i> values, Vec3d fraction) => SamplingKernel<Vec3i, TreeSampleSource<Vec3i>>.Interpolate(values, fraction, SamplingOrder.Quadratic);
    public static Vec4d Sample(VdbTree<Vec4d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(VdbTree<Vec4d> source, Vec3d position, out Vec4d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec4d, TreeSampleSource<Vec4d>>.Sample(new(source), position, SamplingOrder.Quadratic, false, out value); }
    public static Vec4d Sample(ValueAccessor<Vec4d> source, Vec3d position) { Sample(source, position, out var value); return value; }
    public static bool Sample(ValueAccessor<Vec4d> source, Vec3d position, out Vec4d value)
    { ArgumentNullException.ThrowIfNull(source); return SamplingKernel<Vec4d, AccessorSampleSource<Vec4d>>.Sample(new(source), position, SamplingOrder.Quadratic, false, out value); }
    public static Vec4d TriquadraticInterpolation(ReadOnlySpan<Vec4d> values, Vec3d fraction) => SamplingKernel<Vec4d, TreeSampleSource<Vec4d>>.Interpolate(values, fraction, SamplingOrder.Quadratic);
}

public static class StaggeredPointSampler
{
    public const string Name = "point";
    public const int Radius = 0;
    public const int Order = 0;
    public const bool Mipmap = false;
    public const bool Consistent = false;
    public const bool Staggered = true;
    public static T Sample<T>(VdbTree<T> source, Vec3d position) where T : unmanaged { Sample(source, position, out T value); return value; }
    public static bool Sample<T>(VdbTree<T> source, Vec3d position, out T value) where T : unmanaged
    { ArgumentNullException.ThrowIfNull(source); SamplingKernel<T, TreeSampleSource<T>>.Validate(SamplingOrder.Point, true); return SamplingKernel<T, TreeSampleSource<T>>.Sample(new(source), position, SamplingOrder.Point, true, out value); }
    public static T Sample<T>(ValueAccessor<T> source, Vec3d position) where T : unmanaged { Sample(source, position, out T value); return value; }
    public static bool Sample<T>(ValueAccessor<T> source, Vec3d position, out T value) where T : unmanaged
    { ArgumentNullException.ThrowIfNull(source); SamplingKernel<T, AccessorSampleSource<T>>.Validate(SamplingOrder.Point, true); return SamplingKernel<T, AccessorSampleSource<T>>.Sample(new(source), position, SamplingOrder.Point, true, out value); }
}

public static class StaggeredBoxSampler
{
    public const string Name = "box";
    public const int Radius = 1;
    public const int Order = 1;
    public const bool Mipmap = true;
    public const bool Consistent = false;
    public const bool Staggered = true;
    public static T Sample<T>(VdbTree<T> source, Vec3d position) where T : unmanaged { Sample(source, position, out T value); return value; }
    public static bool Sample<T>(VdbTree<T> source, Vec3d position, out T value) where T : unmanaged
    { ArgumentNullException.ThrowIfNull(source); SamplingKernel<T, TreeSampleSource<T>>.Validate(SamplingOrder.Box, true); return SamplingKernel<T, TreeSampleSource<T>>.Sample(new(source), position, SamplingOrder.Box, true, out value); }
    public static T Sample<T>(ValueAccessor<T> source, Vec3d position) where T : unmanaged { Sample(source, position, out T value); return value; }
    public static bool Sample<T>(ValueAccessor<T> source, Vec3d position, out T value) where T : unmanaged
    { ArgumentNullException.ThrowIfNull(source); SamplingKernel<T, AccessorSampleSource<T>>.Validate(SamplingOrder.Box, true); return SamplingKernel<T, AccessorSampleSource<T>>.Sample(new(source), position, SamplingOrder.Box, true, out value); }
}

public static class StaggeredQuadraticSampler
{
    public const string Name = "quadratic";
    public const int Radius = 1;
    public const int Order = 2;
    public const bool Mipmap = true;
    public const bool Consistent = false;
    public const bool Staggered = true;
    public static T Sample<T>(VdbTree<T> source, Vec3d position) where T : unmanaged { Sample(source, position, out T value); return value; }
    public static bool Sample<T>(VdbTree<T> source, Vec3d position, out T value) where T : unmanaged
    { ArgumentNullException.ThrowIfNull(source); SamplingKernel<T, TreeSampleSource<T>>.Validate(SamplingOrder.Quadratic, true); return SamplingKernel<T, TreeSampleSource<T>>.Sample(new(source), position, SamplingOrder.Quadratic, true, out value); }
    public static T Sample<T>(ValueAccessor<T> source, Vec3d position) where T : unmanaged { Sample(source, position, out T value); return value; }
    public static bool Sample<T>(ValueAccessor<T> source, Vec3d position, out T value) where T : unmanaged
    { ArgumentNullException.ThrowIfNull(source); SamplingKernel<T, AccessorSampleSource<T>>.Validate(SamplingOrder.Quadratic, true); return SamplingKernel<T, AccessorSampleSource<T>>.Sample(new(source), position, SamplingOrder.Quadratic, true, out value); }
}
