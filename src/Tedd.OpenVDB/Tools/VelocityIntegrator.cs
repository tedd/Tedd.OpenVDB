// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/VelocityFields.h.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tools;

/// <summary>Samples a Vec3f or Vec3d velocity grid in world space with a cached accessor.</summary>
/// <remarks>The accessor captures the construction-time tree, while every query uses the grid's current
/// transform. A copy captures the grid's current tree. Use a separate copy per thread.</remarks>
public sealed class VelocitySampler<TVector> where TVector : unmanaged
{
    private readonly Grid<TVector> _grid;
    private readonly ValueAccessor<TVector> _accessor;
    public VelocitySampler(Grid<TVector> grid, SamplingOrder order = SamplingOrder.Box, bool staggered = false)
    {
        ArgumentNullException.ThrowIfNull(grid); VelocityValue<TVector>.Validate();
        SamplingKernel<TVector, AccessorSampleSource<TVector>>.Validate(order, staggered);
        _grid = grid; _accessor = grid.Tree.GetAccessor(); Order = order; Staggered = staggered;
    }
    public VelocitySampler(VelocitySampler<TVector> other)
        : this((other ?? throw new ArgumentNullException(nameof(other)))._grid, other.Order, other.Staggered) { }
    public SamplingOrder Order { get; }
    public bool Staggered { get; }
    public bool Sample(Vec3d world, out TVector result) => SamplingKernel<TVector, AccessorSampleSource<TVector>>.Sample(new(_accessor), _grid.WorldToIndex(world), Order, Staggered, out result);
    public bool Sample(Vec3f world, out TVector result) => Sample(world.AsVec3d(), out result);
    public TVector Sample(Vec3d world) { Sample(world, out TVector result); return result; }
    public TVector Sample(Vec3f world) => Sample(world.AsVec3d());
    public VelocitySampler<TVector> Copy() => new(this);
}

/// <summary>Order-zero through order-four Runge-Kutta integration in a static velocity grid.</summary>
/// <remarks>Intermediate positions, time steps, and vector arithmetic use the grid's component precision.
/// The resulting displacement is added to the original location at that location's precision.
/// Sampling uses a cached accessor; use a separate copy per thread.</remarks>
public sealed class VelocityIntegrator<TVector> where TVector : unmanaged
{
    private readonly VelocitySampler<TVector> _sampler;
    public VelocityIntegrator(Grid<TVector> grid, SamplingOrder sampleOrder = SamplingOrder.Box, bool staggered = false) => _sampler = new(grid, sampleOrder, staggered);
    public VelocityIntegrator(VelocityIntegrator<TVector> other) => _sampler = (other ?? throw new ArgumentNullException(nameof(other)))._sampler.Copy();
    public SamplingOrder SampleOrder => _sampler.Order;
    public bool Staggered => _sampler.Staggered;
    public VelocityIntegrator<TVector> Copy() => new(this);
    public Vec3d RungeKutta(double dt, Vec3d world, int order = 4)
    {
        ValidateOrder(order); if (order == 0) return world;
        return world + (typeof(TVector) == typeof(Vec3f) ? Displacement((float)dt, (Vec3f)world, order).AsVec3d() : Displacement(dt, world, order));
    }
    public Vec3f RungeKutta(double dt, Vec3f world, int order = 4)
    {
        ValidateOrder(order); if (order == 0) return world;
        return world + (typeof(TVector) == typeof(Vec3f) ? Displacement((float)dt, world, order) : (Vec3f)Displacement(dt, world.AsVec3d(), order));
    }
    public void RungeKutta(double dt, ref Vec3d world, int order = 4) => world = RungeKutta(dt, world, order);
    public void RungeKutta(double dt, ref Vec3f world, int order = 4) => world = RungeKutta(dt, world, order);
    /// <summary>Updates locations in place without intermediate allocations. On an invalid later sample,
    /// locations completed before that sample retain their updates.</summary>
    public void RungeKutta(double dt, Span<Vec3d> world, int order = 4)
    {
        ValidateOrder(order); if (order == 0) return;
        for (int i = 0; i < world.Length; ++i) world[i] = RungeKutta(dt, world[i], order);
    }
    public void RungeKutta(double dt, Span<Vec3f> world, int order = 4)
    {
        ValidateOrder(order); if (order == 0) return;
        for (int i = 0; i < world.Length; ++i) world[i] = RungeKutta(dt, world[i], order);
    }
    private static void ValidateOrder(int order) { if ((uint)order > 4) throw new ArgumentOutOfRangeException(nameof(order), "Runge-Kutta order must be between zero and four."); }
    private Vec3f Single(Vec3f world) => VelocityValue<TVector>.ToSingle(_sampler.Sample(world));
    private Vec3d Double(Vec3d world) => VelocityValue<TVector>.ToDouble(_sampler.Sample(world));
    private Vec3f Displacement(float dt, Vec3f p, int order)
    {
        Vec3f v0 = Single(p);
        if (order == 1) return dt * v0;
        Vec3f v1 = Single(p + 0.5f * dt * v0);
        if (order == 2) return dt * v1;
        if (order == 3)
        {
            Vec3f v2 = Single(p + dt * (2f * v1 - v0));
            return dt * (v0 + 4f * v1 + v2) * (float)(1.0 / 6.0);
        }
        else
        {
            Vec3f v2 = Single(p + 0.5f * dt * v1), v3 = Single(p + dt * v2);
            return dt * (v0 + 2f * (v1 + v2) + v3) * (float)(1.0 / 6.0);
        }
    }
    private Vec3d Displacement(double dt, Vec3d p, int order)
    {
        Vec3d v0 = Double(p);
        if (order == 1) return dt * v0;
        Vec3d v1 = Double(p + 0.5 * dt * v0);
        if (order == 2) return dt * v1;
        if (order == 3)
        {
            Vec3d v2 = Double(p + dt * (2.0 * v1 - v0));
            return dt * (v0 + 4.0 * v1 + v2) * (1.0 / 6.0);
        }
        else
        {
            Vec3d v2 = Double(p + 0.5 * dt * v1), v3 = Double(p + dt * v2);
            return dt * (v0 + 2.0 * (v1 + v2) + v3) * (1.0 / 6.0);
        }
    }
}
