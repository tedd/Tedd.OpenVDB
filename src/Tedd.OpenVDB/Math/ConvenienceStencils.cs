// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Stencils.h.
using System.Numerics;
namespace Tedd.OpenVDB.Math;

/// <summary>Native seven-point gradient, upwind, Laplacian and closest-point stencil.</summary>
/// <remarks>Voxel size is cached at construction. These convenience operators assume uniform
/// scaling; use DifferentialOperators for general affine or nonlinear maps.</remarks>
public sealed class GradStencil<T> : Stencil<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly T _inv2Dx, _invDx2;
    public GradStencil(Grid<T> grid) : this(grid, grid.Transform.VoxelSize.X) { }
    public GradStencil(Grid<T> grid, double voxelSize) : base(grid, StencilLayouts.GradPt)
    {
        _inv2Dx = T.CreateTruncating(0.5 / voxelSize);
        _invDx2 = T.CreateTruncating(4.0 * double.CreateTruncating(_inv2Dx) * double.CreateTruncating(_inv2Dx));
    }
    public Vec3d Gradient()
    {
        var v = Values;
        return StencilArithmetic<T>.Vector((v[2] - v[1]) * _inv2Dx, (v[4] - v[3]) * _inv2Dx, (v[6] - v[5]) * _inv2Dx);
    }
    public Vec3d Gradient(Vec3d velocity)
    {
        var v = Values; T two = T.CreateTruncating(2);
        return StencilArithmetic<T>.Vector(
            (velocity.X > 0 ? v[0] - v[1] : v[2] - v[0]) * two * _inv2Dx,
            (velocity.Y > 0 ? v[0] - v[3] : v[4] - v[0]) * two * _inv2Dx,
            (velocity.Z > 0 ? v[0] - v[5] : v[6] - v[0]) * two * _inv2Dx);
    }
    public T NormSquaredGradient()
    {
        var v = Values;
        return _invDx2 * FiniteDifference.GodunovNormSquared(v[0] > T.Zero, v[0] - v[1], v[2] - v[0], v[0] - v[3], v[4] - v[0], v[0] - v[5], v[6] - v[0]);
    }
    public T Laplacian() { var v = Values; return _invDx2 * (v[1] + v[2] + v[3] + v[4] + v[5] + v[6] - T.CreateTruncating(6) * v[0]); }
    public bool ZeroCrossing()
    {
        var v = Values;
        return v[0] > T.Zero ? v[1] < T.Zero || v[2] < T.Zero || v[3] < T.Zero || v[4] < T.Zero || v[5] < T.Zero || v[6] < T.Zero :
            v[1] > T.Zero || v[2] > T.Zero || v[3] > T.Zero || v[4] > T.Zero || v[5] > T.Zero || v[6] > T.Zero;
    }
    /// <summary>Closest surface point in index space, assuming a world-distance level set.</summary>
    public Vec3d ClosestPoint()
    {
        var v = Values; T d = T.CreateTruncating(double.CreateTruncating(v[0]) * 0.5 * double.CreateTruncating(_invDx2));
        return StencilArithmetic<T>.Vector(T.CreateTruncating(Center.X) - d * (v[2] - v[1]),
            T.CreateTruncating(Center.Y) - d * (v[4] - v[3]), T.CreateTruncating(Center.Z) - d * (v[6] - v[5]));
    }
}

/// <summary>Native nineteen-point WENO upwind stencil with uniform voxel scaling.</summary>
public sealed class WenoStencil<T> : Stencil<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private static readonly Coord[] Offsets = MakeOffsets();
    private readonly T _inv2Dx, _invDx2;
    private readonly float _dx2;
    public WenoStencil(Grid<T> grid) : this(grid, grid.Transform.VoxelSize.X) { }
    public WenoStencil(Grid<T> grid, double voxelSize) : base(grid, Offsets)
    {
        T dx2 = T.CreateTruncating(voxelSize * voxelSize);
        _inv2Dx = T.CreateTruncating(0.5 / voxelSize); _invDx2 = T.CreateTruncating(1.0 / double.CreateTruncating(dx2)); _dx2 = float.CreateTruncating(dx2);
    }
    private static Coord[] MakeOffsets()
    {
        var result = new Coord[19]; int index = 1;
        for (int axis = 0; axis < 3; ++axis) for (int distance = -3; distance <= 3; ++distance)
            if (distance != 0) result[index++] = FiniteDifference.Offset(Coord.Zero, (Axis)axis, distance);
        return result;
    }
    private T Upwind(int axis, bool backward)
    {
        var v = Values; int i = axis * 6; T center = v[0];
        return backward ? FiniteDifference.Weno5(v[i + 2] - v[i + 1], v[i + 3] - v[i + 2], center - v[i + 3], v[i + 4] - center, v[i + 5] - v[i + 4], _dx2) :
            FiniteDifference.Weno5(v[i + 6] - v[i + 5], v[i + 5] - v[i + 4], v[i + 4] - center, center - v[i + 3], v[i + 3] - v[i + 2], _dx2);
    }
    public T NormSquaredGradient() => NormSquaredGradient(T.Zero);
    public T NormSquaredGradient(T isoValue)
    {
        // Native non-SIMD WenoStencil promotes each WENO output to Real before Godunov.
        double norm = FiniteDifference.GodunovNormSquared(CenterValue > isoValue,
            double.CreateTruncating(Upwind(0, true)), double.CreateTruncating(Upwind(0, false)),
            double.CreateTruncating(Upwind(1, true)), double.CreateTruncating(Upwind(1, false)),
            double.CreateTruncating(Upwind(2, true)), double.CreateTruncating(Upwind(2, false)));
        return T.CreateTruncating(double.CreateTruncating(_invDx2) * norm);
    }
    public Vec3d Gradient(Vec3d velocity)
    {
        T scale = T.CreateTruncating(2) * _inv2Dx;
        return StencilArithmetic<T>.Vector(scale * Upwind(0, velocity.X > 0), scale * Upwind(1, velocity.Y > 0), scale * Upwind(2, velocity.Z > 0));
    }
    public Vec3d Gradient() { var v = Values; return StencilArithmetic<T>.Vector(_inv2Dx * (v[4] - v[3]), _inv2Dx * (v[10] - v[9]), _inv2Dx * (v[16] - v[15])); }
    public T Laplacian() { var v = Values; return _invDx2 * (v[3] + v[4] + v[9] + v[10] + v[15] + v[16] - T.CreateTruncating(6) * v[0]); }
    public bool ZeroCrossing()
    {
        var v = Values;
        return v[0] > T.Zero ? v[3] < T.Zero || v[4] < T.Zero || v[9] < T.Zero || v[10] < T.Zero || v[15] < T.Zero || v[16] < T.Zero :
            v[3] > T.Zero || v[4] > T.Zero || v[9] > T.Zero || v[10] > T.Zero || v[15] > T.Zero || v[16] > T.Zero;
    }
}

/// <summary>Native nineteen-point mean, Gaussian and principal curvature stencil.</summary>
/// <remarks>Uses native double intermediates and a cached uniform voxel size. Zero gradient
/// returns zero curvature. Principal curvature discriminants are not clamped.</remarks>
public sealed class CurvatureStencil<T> : Stencil<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private static readonly Coord[] Offsets = MakeOffsets();
    private readonly T _inv2Dx, _invDx2;
    public CurvatureStencil(Grid<T> grid) : this(grid, grid.Transform.VoxelSize.X) { }
    public CurvatureStencil(Grid<T> grid, double voxelSize) : base(grid, Offsets)
    {
        _inv2Dx = T.CreateTruncating(0.5 / voxelSize);
        _invDx2 = T.CreateTruncating(4.0 * double.CreateTruncating(_inv2Dx) * double.CreateTruncating(_inv2Dx));
    }
    private static Coord[] MakeOffsets()
    {
        Coord[] result = [new(0, 0, 0), new(-1, 0, 0), new(1, 0, 0), new(0, -1, 0), new(0, 1, 0), new(0, 0, -1), new(0, 0, 1), new(-1, -1, 0), new(1, -1, 0), new(-1, 1, 0), new(1, 1, 0), new(-1, 0, -1), new(1, 0, -1), new(-1, 0, 1), new(1, 0, 1), new(0, -1, -1), new(0, 1, -1), new(0, -1, 1), new(0, 1, 1)];
        StencilLayouts.GradPt.CopyTo(result, 0);
        return result;
    }
    public Vec3d Gradient() { var v = Values; return StencilArithmetic<T>.Vector((v[2] - v[1]) * _inv2Dx, (v[4] - v[3]) * _inv2Dx, (v[6] - v[5]) * _inv2Dx); }
    public T Laplacian() { var v = Values; return _invDx2 * (v[1] + v[2] + v[3] + v[4] + v[5] + v[6] - T.CreateTruncating(6) * v[0]); }
    private (double MeanAlpha, double GaussianAlpha, double Norm) Compute()
    {
        var v = Values; T two = T.CreateTruncating(2);
        double x = 0.5 * double.CreateTruncating(v[2] - v[1]), y = 0.5 * double.CreateTruncating(v[4] - v[3]), z = 0.5 * double.CreateTruncating(v[6] - v[5]);
        double x2 = x * x, y2 = y * y, z2 = z * z, norm2 = x2 + y2 + z2;
        if (norm2 <= 1e-15) return (0, 0, 0);
        double xx = double.CreateTruncating(v[2] - two * v[0] + v[1]), yy = double.CreateTruncating(v[4] - two * v[0] + v[3]), zz = double.CreateTruncating(v[6] - two * v[0] + v[5]),
            xy = 0.25 * double.CreateTruncating(v[10] - v[8] + v[7] - v[9]), xz = 0.25 * double.CreateTruncating(v[14] - v[12] + v[11] - v[13]), yz = 0.25 * double.CreateTruncating(v[18] - v[16] + v[15] - v[17]);
        double mean = x2 * (yy + zz) + y2 * (xx + zz) + z2 * (xx + yy) - 2 * (x * (y * xy + z * xz) + y * z * yz);
        double gauss = x2 * (yy * zz - yz * yz) + y2 * (xx * zz - xz * xz) + z2 * (xx * yy - xy * xy) +
            2 * (y * z * (xy * xz - yz * xx) + x * z * (xy * yz - xz * yy) + x * y * (xz * yz - xy * zz));
        return (mean, gauss, System.Math.Sqrt(norm2));
    }
    private (double Mean, double Gaussian) ComputeCurvatures(bool multiplyNorm)
    {
        var (mean, gauss, n) = Compute(); if (n == 0) return (0, 0);
        double inv2dx = double.CreateTruncating(_inv2Dx), invDx2 = double.CreateTruncating(_invDx2), n2 = n * n, n3 = n2 * n;
        return multiplyNorm ? (mean * invDx2 / (2 * n2), 2 * gauss * inv2dx * invDx2 / n3) : (mean * inv2dx / n3, gauss * invDx2 / (n2 * n2));
    }
    public T MeanCurvature() => T.CreateTruncating(ComputeCurvatures(false).Mean);
    public T GaussianCurvature() => T.CreateTruncating(ComputeCurvatures(false).Gaussian);
    public (T Mean, T Gaussian) Curvatures() { var v = ComputeCurvatures(false); return (T.CreateTruncating(v.Mean), T.CreateTruncating(v.Gaussian)); }
    public T MeanCurvatureNormGradient() => T.CreateTruncating(ComputeCurvatures(true).Mean);
    public T GaussianCurvatureNormGradient() => T.CreateTruncating(ComputeCurvatures(true).Gaussian);
    public (T Mean, T Gaussian) CurvaturesNormGradient() { var v = ComputeCurvatures(true); return (T.CreateTruncating(v.Mean), T.CreateTruncating(v.Gaussian)); }
    public (T Minimum, T Maximum) PrincipalCurvatures()
    {
        var v = ComputeCurvatures(false); double discriminant = System.Math.Sqrt(v.Mean * v.Mean - v.Gaussian);
        return (T.CreateTruncating(v.Mean - discriminant), T.CreateTruncating(v.Mean + discriminant));
    }
}

internal static class StencilArithmetic<T> where T : IFloatingPointIeee754<T>
{
    internal static Vec3d Vector(T x, T y, T z) => new(double.CreateTruncating(x), double.CreateTruncating(y), double.CreateTruncating(z));
}
