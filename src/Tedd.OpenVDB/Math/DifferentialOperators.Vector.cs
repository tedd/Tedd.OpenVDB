// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Operators.h and FiniteDifference.h.
using System.Numerics;
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Math;

public static partial class DifferentialOperators
{
    public static float IndexDivergence(ValueAccessor<Vec3f> accessor, Coord coordinate, DScheme scheme = DScheme.CenteredSecond) => DivergenceCore<Vec3f, float>(accessor, coordinate, null, scheme);
    public static float Divergence(Transform transform, ValueAccessor<Vec3f> accessor, Coord coordinate, DScheme scheme = DScheme.CenteredSecond) => DivergenceCore<Vec3f, float>(accessor, coordinate, transform ?? throw new ArgumentNullException(nameof(transform)), scheme);
    public static Vec3f IndexCurl(ValueAccessor<Vec3f> accessor, Coord coordinate, DScheme scheme = DScheme.CenteredSecond) => VectorComponents<Vec3f, float>.Create(CurlCore<Vec3f, float>(accessor, coordinate, null, scheme));
    public static Vec3f Curl(Transform transform, ValueAccessor<Vec3f> accessor, Coord coordinate, DScheme scheme = DScheme.CenteredSecond) => VectorComponents<Vec3f, float>.Create(CurlCore<Vec3f, float>(accessor, coordinate, transform ?? throw new ArgumentNullException(nameof(transform)), scheme));
    public static double IndexDivergence(ValueAccessor<Vec3d> accessor, Coord coordinate, DScheme scheme = DScheme.CenteredSecond) => DivergenceCore<Vec3d, double>(accessor, coordinate, null, scheme);
    public static double Divergence(Transform transform, ValueAccessor<Vec3d> accessor, Coord coordinate, DScheme scheme = DScheme.CenteredSecond) => DivergenceCore<Vec3d, double>(accessor, coordinate, transform ?? throw new ArgumentNullException(nameof(transform)), scheme);
    public static Vec3d IndexCurl(ValueAccessor<Vec3d> accessor, Coord coordinate, DScheme scheme = DScheme.CenteredSecond) => VectorComponents<Vec3d, double>.Create(CurlCore<Vec3d, double>(accessor, coordinate, null, scheme));
    public static Vec3d Curl(Transform transform, ValueAccessor<Vec3d> accessor, Coord coordinate, DScheme scheme = DScheme.CenteredSecond) => VectorComponents<Vec3d, double>.Create(CurlCore<Vec3d, double>(accessor, coordinate, transform ?? throw new ArgumentNullException(nameof(transform)), scheme));

    internal static T DivergenceCore<V, T>(ValueAccessor<V> accessor, Coord coordinate, Transform? transform, DScheme scheme)
        where V : unmanaged where T : unmanaged, IFloatingPointIeee754<T>
    {
        T result = T.Zero;
        for (int component = 0; component < 3; ++component)
        {
            var source = new VectorComponentSource<V, T>(accessor, coordinate, component);
            if (transform is null) result += source.First((Axis)component, scheme);
            else result += T.CreateTruncating(transform.ApplyInverseJacobianTranspose(IndexGradientCore<T, VectorComponentSource<V, T>>(source, scheme), coordinate.AsVec3d())[component]);
        }
        return result;
    }
    internal static Vec3d CurlCore<V, T>(ValueAccessor<V> accessor, Coord coordinate, Transform? transform, DScheme scheme)
        where V : unmanaged where T : unmanaged, IFloatingPointIeee754<T>
    {
        Span<Vec3d> derivatives = stackalloc Vec3d[3];
        for (int component = 0; component < 3; ++component)
        {
            var source = new VectorComponentSource<V, T>(accessor, coordinate, component);
            derivatives[component] = transform is null ? IndexGradientCore<T, VectorComponentSource<V, T>>(source, scheme) : GradientCore<T, VectorComponentSource<V, T>>(source, transform, scheme);
        }
        return StencilArithmetic<T>.Vector(T.CreateTruncating(derivatives[2].Y) - T.CreateTruncating(derivatives[1].Z),
            T.CreateTruncating(derivatives[0].Z) - T.CreateTruncating(derivatives[2].X), T.CreateTruncating(derivatives[1].X) - T.CreateTruncating(derivatives[0].Y));
    }
}

internal readonly struct VectorComponentSource<V, T>(ValueAccessor<V> accessor, Coord coordinate, int component) : IScalarDifferentialSource<T>
    where V : unmanaged where T : unmanaged, IFloatingPointIeee754<T>
{
    public Coord Center => coordinate;
    public T Sample(Coord offset) => VectorComponents<V, T>.Get(accessor.GetValue(new(checked(coordinate.X + offset.X), checked(coordinate.Y + offset.Y), checked(coordinate.Z + offset.Z))), component);
    public T First(Axis axis, DScheme scheme)
    {
        var range = FiniteDifference.OffsetRange(scheme);
        FiniteDifference.Offset(coordinate, axis, range.Minimum); FiniteDifference.Offset(coordinate, axis, range.Maximum);
        Span<T> values = stackalloc T[7]; values.Clear();
        for (int i = range.Minimum; i <= range.Maximum; ++i) values[i + 3] = Sample(FiniteDifference.Offset(Coord.Zero, axis, i));
        return FiniteDifference.First<T>(scheme, values);
    }
    public T Second(Axis axis, DDScheme scheme) => throw new NotSupportedException("Vector component source is used for first derivatives.");
    public T Mixed(Axis first, Axis second, DDScheme scheme) => throw new NotSupportedException("Vector component source is used for first derivatives.");
}
internal static class VectorComponents<V, T> where V : unmanaged where T : unmanaged, IFloatingPointIeee754<T>
{
    internal static T Get(V value, int component)
    {
        if (typeof(V) == typeof(Vec3f) && typeof(T) == typeof(float)) return T.CreateTruncating(Unsafe.As<V, Vec3f>(ref value)[component]);
        if (typeof(V) == typeof(Vec3d) && typeof(T) == typeof(double)) return T.CreateTruncating(Unsafe.As<V, Vec3d>(ref value)[component]);
        throw new NotSupportedException("Only Vec3f/float and Vec3d/double pairs are supported.");
    }
    internal static V Create(Vec3d value)
    {
        if (typeof(V) == typeof(Vec3f) && typeof(T) == typeof(float)) { var v = new Vec3f((float)value.X, (float)value.Y, (float)value.Z); return Unsafe.As<Vec3f, V>(ref v); }
        if (typeof(V) == typeof(Vec3d) && typeof(T) == typeof(double)) return Unsafe.As<Vec3d, V>(ref value);
        throw new NotSupportedException("Only Vec3f/float and Vec3d/double pairs are supported.");
    }
    internal static T Magnitude(V value)
    {
        if (typeof(V) == typeof(Vec3f) && typeof(T) == typeof(float)) return T.CreateTruncating(Unsafe.As<V, Vec3f>(ref value).Length);
        if (typeof(V) == typeof(Vec3d) && typeof(T) == typeof(double)) return T.CreateTruncating(Unsafe.As<V, Vec3d>(ref value).Length);
        throw new NotSupportedException();
    }
    internal static V Normalize(V value)
    {
        if (typeof(V) == typeof(Vec3f) && typeof(T) == typeof(float)) { var v = Unsafe.As<V, Vec3f>(ref value); v = v.TryNormalize(out var normalized) ? normalized : Vec3f.Zero; return Unsafe.As<Vec3f, V>(ref v); }
        if (typeof(V) == typeof(Vec3d) && typeof(T) == typeof(double)) { var v = Unsafe.As<V, Vec3d>(ref value); v = v.TryNormalize(out var normalized) ? normalized : Vec3d.Zero; return Unsafe.As<Vec3d, V>(ref v); }
        throw new NotSupportedException();
    }
}
