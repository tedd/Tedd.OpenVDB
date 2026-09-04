// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Operators.h (generic AffineMap/NonlinearFrustumMap paths).
using System.Numerics;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Math;

/// <summary>Finite-difference operators in index space and in a transform's world space.</summary>
/// <remarks>Scalar vector results use Vec3d storage with each component rounded to the input
/// scalar precision. Managed transforms use native generic AffineMap arithmetic.
/// Stencil overloads require the selected scheme's complete neighborhood.</remarks>
public static partial class DifferentialOperators
{
    public static Vec3d IndexGradient<T>(ValueAccessor<T> accessor, Coord coordinate, DScheme scheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => IndexGradientCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), scheme);
    public static Vec3d IndexBiasedGradient<T>(ValueAccessor<T> accessor, Coord coordinate, Vec3d velocity, BiasedGradientScheme scheme = BiasedGradientScheme.First) where T : unmanaged, IFloatingPointIeee754<T> => BiasedGradientCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), velocity, scheme);
    public static T IndexNormSquaredGradient<T>(ValueAccessor<T> accessor, Coord coordinate, BiasedGradientScheme scheme = BiasedGradientScheme.First) where T : unmanaged, IFloatingPointIeee754<T> => NormSquaredCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), null, scheme);
    public static T IndexLaplacian<T>(ValueAccessor<T> accessor, Coord coordinate, DDScheme scheme = DDScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => IndexLaplacianCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), scheme);
    public static Vec3d Gradient<T>(Transform transform, ValueAccessor<T> accessor, Coord coordinate, DScheme scheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => GradientCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), transform, scheme);
    public static Vec3d BiasedGradient<T>(Transform transform, ValueAccessor<T> accessor, Coord coordinate, Vec3d velocity, BiasedGradientScheme scheme = BiasedGradientScheme.First) where T : unmanaged, IFloatingPointIeee754<T> => Round<T>(transform.ApplyInverseJacobianTranspose(BiasedGradientCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), velocity, scheme), new AccessorDifferentialSource<T>(accessor, coordinate).Center.AsVec3d()));
    public static T NormSquaredGradient<T>(Transform transform, ValueAccessor<T> accessor, Coord coordinate, BiasedGradientScheme scheme = BiasedGradientScheme.First) where T : unmanaged, IFloatingPointIeee754<T> => NormSquaredCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), transform, scheme);
    public static T Laplacian<T>(Transform transform, ValueAccessor<T> accessor, Coord coordinate, DDScheme scheme = DDScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => LaplacianCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), transform, scheme);
    public static T MeanCurvature<T>(Transform transform, ValueAccessor<T> accessor, Coord coordinate, DDScheme secondScheme = DDScheme.CenteredSecond, DScheme firstScheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => MeanCurvatureCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), transform, secondScheme, firstScheme, false);
    public static T MeanCurvatureNormGradient<T>(Transform transform, ValueAccessor<T> accessor, Coord coordinate, DDScheme secondScheme = DDScheme.CenteredSecond, DScheme firstScheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => MeanCurvatureCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), transform, secondScheme, firstScheme, true);
    public static Vec3d ClosestPoint<T>(Transform transform, ValueAccessor<T> accessor, Coord coordinate, bool worldSpace = true, DScheme scheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => ClosestPointCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), transform, worldSpace, scheme);
    public static bool IndexMeanCurvatureParts<T>(ValueAccessor<T> accessor, Coord coordinate, out T alpha, out T beta, DDScheme secondScheme = DDScheme.CenteredSecond, DScheme firstScheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => IndexMeanCurvatureCore<T, AccessorDifferentialSource<T>>(new AccessorDifferentialSource<T>(accessor, coordinate), secondScheme, firstScheme, out alpha, out beta);
    public static Vec3d IndexGradient<T>(Stencil<T> stencil, DScheme scheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => IndexGradientCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), scheme);
    public static Vec3d IndexBiasedGradient<T>(Stencil<T> stencil, Vec3d velocity, BiasedGradientScheme scheme = BiasedGradientScheme.First) where T : unmanaged, IFloatingPointIeee754<T> => BiasedGradientCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), velocity, scheme);
    public static T IndexNormSquaredGradient<T>(Stencil<T> stencil, BiasedGradientScheme scheme = BiasedGradientScheme.First) where T : unmanaged, IFloatingPointIeee754<T> => NormSquaredCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), null, scheme);
    public static T IndexLaplacian<T>(Stencil<T> stencil, DDScheme scheme = DDScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => IndexLaplacianCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), scheme);
    public static Vec3d Gradient<T>(Transform transform, Stencil<T> stencil, DScheme scheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => GradientCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), transform, scheme);
    public static Vec3d BiasedGradient<T>(Transform transform, Stencil<T> stencil, Vec3d velocity, BiasedGradientScheme scheme = BiasedGradientScheme.First) where T : unmanaged, IFloatingPointIeee754<T> => Round<T>(transform.ApplyInverseJacobianTranspose(BiasedGradientCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), velocity, scheme), new StencilDifferentialSource<T>(stencil).Center.AsVec3d()));
    public static T NormSquaredGradient<T>(Transform transform, Stencil<T> stencil, BiasedGradientScheme scheme = BiasedGradientScheme.First) where T : unmanaged, IFloatingPointIeee754<T> => NormSquaredCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), transform, scheme);
    public static T Laplacian<T>(Transform transform, Stencil<T> stencil, DDScheme scheme = DDScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => LaplacianCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), transform, scheme);
    public static T MeanCurvature<T>(Transform transform, Stencil<T> stencil, DDScheme secondScheme = DDScheme.CenteredSecond, DScheme firstScheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => MeanCurvatureCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), transform, secondScheme, firstScheme, false);
    public static T MeanCurvatureNormGradient<T>(Transform transform, Stencil<T> stencil, DDScheme secondScheme = DDScheme.CenteredSecond, DScheme firstScheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => MeanCurvatureCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), transform, secondScheme, firstScheme, true);
    public static Vec3d ClosestPoint<T>(Transform transform, Stencil<T> stencil, bool worldSpace = true, DScheme scheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => ClosestPointCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), transform, worldSpace, scheme);
    public static bool IndexMeanCurvatureParts<T>(Stencil<T> stencil, out T alpha, out T beta, DDScheme secondScheme = DDScheme.CenteredSecond, DScheme firstScheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T> => IndexMeanCurvatureCore<T, StencilDifferentialSource<T>>(new StencilDifferentialSource<T>(stencil), secondScheme, firstScheme, out alpha, out beta);

    internal static Vec3d Round<T>(Vec3d v) where T : IFloatingPointIeee754<T> =>
        new(double.CreateTruncating(T.CreateTruncating(v.X)), double.CreateTruncating(T.CreateTruncating(v.Y)), double.CreateTruncating(T.CreateTruncating(v.Z)));
    internal static Vec3d IndexGradientCore<T, S>(S source, DScheme scheme) where T : unmanaged, IFloatingPointIeee754<T> where S : struct, IScalarDifferentialSource<T> =>
        StencilArithmetic<T>.Vector(source.First(Axis.X, scheme), source.First(Axis.Y, scheme), source.First(Axis.Z, scheme));
    private static (DScheme Forward, DScheme Backward) BiasSchemes(BiasedGradientScheme scheme) => scheme switch
    {
        BiasedGradientScheme.First => (DScheme.ForwardFirst, DScheme.BackwardFirst),
        BiasedGradientScheme.Second => (DScheme.ForwardSecond, DScheme.BackwardSecond),
        BiasedGradientScheme.Third => (DScheme.ForwardThird, DScheme.BackwardThird),
        BiasedGradientScheme.Weno5 => (DScheme.ForwardWeno5, DScheme.BackwardWeno5),
        BiasedGradientScheme.HJWeno5 => (DScheme.ForwardHJWeno5, DScheme.BackwardHJWeno5),
        _ => throw new ArgumentOutOfRangeException(nameof(scheme))
    };
    private static Vec3d BiasedGradientCore<T, S>(S source, Vec3d velocity, BiasedGradientScheme scheme) where T : unmanaged, IFloatingPointIeee754<T> where S : struct, IScalarDifferentialSource<T>
    {
        var (forward, backward) = BiasSchemes(scheme);
        // Operators.h selects backward at zero velocity; GradStencil/WenoStencil select forward.
        return StencilArithmetic<T>.Vector(source.First(Axis.X, velocity.X < 0 ? forward : backward),
            source.First(Axis.Y, velocity.Y < 0 ? forward : backward), source.First(Axis.Z, velocity.Z < 0 ? forward : backward));
    }
    internal static Vec3d GradientCore<T, S>(S source, Transform transform, DScheme scheme) where T : unmanaged, IFloatingPointIeee754<T> where S : struct, IScalarDifferentialSource<T>
    {
        ArgumentNullException.ThrowIfNull(transform);
        return Round<T>(transform.ApplyInverseJacobianTranspose(IndexGradientCore<T, S>(source, scheme), source.Center.AsVec3d()));
    }
    private static T NormSquaredCore<T, S>(S source, Transform? transform, BiasedGradientScheme scheme) where T : unmanaged, IFloatingPointIeee754<T> where S : struct, IScalarDifferentialSource<T>
    {
        var (forward, backward) = BiasSchemes(scheme);
        Vec3d up = transform is null ? IndexGradientCore<T, S>(source, forward) : GradientCore<T, S>(source, transform, forward),
            down = transform is null ? IndexGradientCore<T, S>(source, backward) : GradientCore<T, S>(source, transform, backward);
        return FiniteDifference.GodunovNormSquared(source.Sample(Coord.Zero) > T.Zero,
            T.CreateTruncating(down.X), T.CreateTruncating(up.X), T.CreateTruncating(down.Y), T.CreateTruncating(up.Y), T.CreateTruncating(down.Z), T.CreateTruncating(up.Z));
    }
    private static T IndexLaplacianCore<T, S>(S source, DDScheme scheme) where T : unmanaged, IFloatingPointIeee754<T> where S : struct, IScalarDifferentialSource<T>
    {
        FiniteDifference.Radius(scheme);
        T Sum(int n) => source.Sample(new(n, 0, 0)) + source.Sample(new(-n, 0, 0)) + source.Sample(new(0, n, 0)) + source.Sample(new(0, -n, 0)) + source.Sample(new(0, 0, n)) + source.Sample(new(0, 0, -n));
        T center = source.Sample(Coord.Zero);
        return scheme switch
        {
            DDScheme.CenteredSecond => Sum(1) - T.CreateTruncating(6) * center,
            DDScheme.CenteredFourth => T.CreateTruncating((-1.0 / 12) * double.CreateTruncating(Sum(2)) + (4.0 / 3) * double.CreateTruncating(Sum(1)) - 7.5 * double.CreateTruncating(center)),
            _ => T.CreateTruncating((1.0 / 90) * double.CreateTruncating(Sum(3)) - (3.0 / 20) * double.CreateTruncating(Sum(2)) + 1.5 * double.CreateTruncating(Sum(1)) - (3 * 49 / 18.0) * double.CreateTruncating(center))
        };
    }
    private static Mat3d Hessian<T, S>(S source, DDScheme scheme) where T : unmanaged, IFloatingPointIeee754<T> where S : struct, IScalarDifferentialSource<T>
    {
        double xx = double.CreateTruncating(source.Second(Axis.X, scheme)), yy = double.CreateTruncating(source.Second(Axis.Y, scheme)), zz = double.CreateTruncating(source.Second(Axis.Z, scheme)),
            xy = double.CreateTruncating(source.Mixed(Axis.X, Axis.Y, scheme)), yz = double.CreateTruncating(source.Mixed(Axis.Y, Axis.Z, scheme)), xz = double.CreateTruncating(source.Mixed(Axis.X, Axis.Z, scheme));
        return new(xx, xy, xz, xy, yy, yz, xz, yz, zz);
    }
    private static T LaplacianCore<T, S>(S source, Transform transform, DDScheme scheme) where T : unmanaged, IFloatingPointIeee754<T> where S : struct, IScalarDifferentialSource<T>
    {
        ArgumentNullException.ThrowIfNull(transform); Mat3d hessian = Hessian<T, S>(source, scheme);
        Mat3d world = transform.IsLinear ? transform.ApplyInverseJacobianCurvature(hessian) :
            transform.ApplyInverseJacobianCurvature(hessian, IndexGradientCore<T, S>(source, DScheme.CenteredSecond), source.Center.AsVec3d());
        return T.CreateTruncating(world[0, 0] + world[1, 1] + world[2, 2]);
    }
    private static bool IndexMeanCurvatureCore<T, S>(S source, DDScheme second, DScheme first, out T alpha, out T beta) where T : unmanaged, IFloatingPointIeee754<T> where S : struct, IScalarDifferentialSource<T>
    {
        T x = source.First(Axis.X, first), y = source.First(Axis.Y, first), z = source.First(Axis.Z, first), x2 = x * x, y2 = y * y, z2 = z * z, norm = x2 + y2 + z2;
        if (norm <= T.CreateTruncating(typeof(T) == typeof(float) ? 1e-8 : typeof(T) == typeof(Half) ? 0.00097656 : 1e-15)) { alpha = beta = T.Zero; return false; }
        T xx = source.Second(Axis.X, second), yy = source.Second(Axis.Y, second), zz = source.Second(Axis.Z, second),
            xy = source.Mixed(Axis.X, Axis.Y, second), yz = source.Mixed(Axis.Y, Axis.Z, second), xz = source.Mixed(Axis.X, Axis.Z, second);
        alpha = x2 * (yy + zz) + y2 * (xx + zz) + z2 * (xx + yy) - T.CreateTruncating(2) * (x * (y * xy + z * xz) + y * z * yz);
        beta = T.CreateTruncating(System.Math.Sqrt(double.CreateTruncating(norm))); return true;
    }
    private static T MeanCurvatureCore<T, S>(S source, Transform transform, DDScheme second, DScheme first, bool multiplyNorm) where T : unmanaged, IFloatingPointIeee754<T> where S : struct, IScalarDifferentialSource<T>
    {
        ArgumentNullException.ThrowIfNull(transform);
        Vec3d indexGradient = IndexGradientCore<T, S>(source, first), gradient = transform.ApplyInverseJacobianTranspose(indexGradient, source.Center.AsVec3d());
        double x = gradient.X, y = gradient.Y, z = gradient.Z, x2 = x * x, y2 = y * y, z2 = z * z, norm2 = x2 + y2 + z2;
        if (norm2 <= 1e-15) return T.Zero;
        Mat3d indexHessian = Hessian<T, S>(source, second), h = transform.IsLinear ? transform.ApplyInverseJacobianCurvature(indexHessian) :
            transform.ApplyInverseJacobianCurvature(indexHessian, indexGradient, source.Center.AsVec3d());
        double alpha = x2 * (h[1, 1] + h[2, 2]) + y2 * (h[0, 0] + h[2, 2]) + z2 * (h[0, 0] + h[1, 1]) - 2 * (x * (y * h[0, 1] + z * h[0, 2]) + y * z * h[1, 2]);
        double beta = System.Math.Sqrt(norm2), denominator = multiplyNorm ? 2 * (beta * beta) : 2 * (beta * beta * beta);
        return T.CreateTruncating(alpha / denominator);
    }
    private static Vec3d ClosestPointCore<T, S>(S source, Transform transform, bool worldSpace, DScheme scheme) where T : unmanaged, IFloatingPointIeee754<T> where S : struct, IScalarDifferentialSource<T>
    {
        T distance = source.Sample(Coord.Zero); Vec3d gradient = GradientCore<T, S>(source, transform, scheme);
        Vec3d displacement = StencilArithmetic<T>.Vector(distance * T.CreateTruncating(gradient.X), distance * T.CreateTruncating(gradient.Y), distance * T.CreateTruncating(gradient.Z));
        Vec3d index = source.Center.AsVec3d();
        // Native CPT applies the inverse point map (including translation) to the displacement.
        // Preserve that formula; CPT_RANGE below uses the actual world-space point.
        return Round<T>(worldSpace ? transform.IndexToWorld(index) - displacement :
            transform.IsLinear ? index - transform.WorldToIndex(displacement) : transform.WorldToIndex(transform.IndexToWorld(index) - displacement));
    }
}

internal interface IScalarDifferentialSource<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    Coord Center { get; }
    T Sample(Coord offset);
    T First(Axis axis, DScheme scheme);
    T Second(Axis axis, DDScheme scheme);
    T Mixed(Axis first, Axis second, DDScheme scheme);
}
internal readonly struct AccessorDifferentialSource<T>(ValueAccessor<T> accessor, Coord coordinate) : IScalarDifferentialSource<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    public Coord Center => coordinate;
    public T Sample(Coord offset) => accessor.GetValue(new(checked(coordinate.X + offset.X), checked(coordinate.Y + offset.Y), checked(coordinate.Z + offset.Z)));
    public T First(Axis axis, DScheme scheme) => FiniteDifference.First(accessor, coordinate, axis, scheme);
    public T Second(Axis axis, DDScheme scheme) => FiniteDifference.Second(accessor, coordinate, axis, scheme);
    public T Mixed(Axis first, Axis second, DDScheme scheme) => FiniteDifference.MixedSecond(accessor, coordinate, first, second, scheme);
}
internal readonly struct StencilDifferentialSource<T>(Stencil<T> stencil) : IScalarDifferentialSource<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    public Coord Center => stencil.Center;
    public T Sample(Coord offset) => stencil.GetValue(offset);
    public T First(Axis axis, DScheme scheme) => stencil.FirstDerivative(axis, scheme);
    public T Second(Axis axis, DDScheme scheme) => stencil.SecondDerivative(axis, scheme);
    public T Mixed(Axis first, Axis second, DDScheme scheme) => stencil.MixedSecondDerivative(first, second, scheme);
}
