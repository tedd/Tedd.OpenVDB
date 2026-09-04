// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/VelocityFields.h.
using System.Numerics;
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tools;

/// <summary>A velocity field in the world frame, with independent world and integer-index queries.</summary>
/// <remarks>Single-precision fields return widened float components. Copy creates an independent accessor
/// when needed; a field instance need not support simultaneous calls from multiple threads.</remarks>
public interface IVelocityField
{
    Transform Transform { get; }
    bool IsSinglePrecision { get; }
    Vec3d Sample(Vec3d world, double time);
    Vec3d Sample(Coord index, double time);
    IVelocityField Copy();
}

/// <summary>A captured Vec3f or Vec3d grid tree and transform interpreted as world-space velocities.</summary>
/// <remarks>Replacing the source grid's tree or transform does not retarget the field or its copies.
/// Mutating the captured tree or transform remains visible. Use a separate copy per thread.</remarks>
public sealed class DiscreteField<TVector> : IVelocityField where TVector : unmanaged
{
    private readonly ValueAccessor<TVector> _accessor;
    public DiscreteField(Grid<TVector> grid, SamplingOrder order = SamplingOrder.Box, bool staggered = false)
    {
        ArgumentNullException.ThrowIfNull(grid);
        VelocityValue<TVector>.Validate();
        SamplingKernel<TVector, AccessorSampleSource<TVector>>.Validate(order, staggered);
        _accessor = grid.Tree.GetAccessor(); Transform = grid.Transform; Order = order; Staggered = staggered;
    }
    public DiscreteField(DiscreteField<TVector> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _accessor = other._accessor.Tree.GetAccessor(); Transform = other.Transform; Order = other.Order; Staggered = other.Staggered;
    }
    public Transform Transform { get; }
    public SamplingOrder Order { get; }
    public bool Staggered { get; }
    public bool IsSinglePrecision => typeof(TVector) == typeof(Vec3f);
    public TVector Sample(Vec3d world, double time = 0)
    {
        SamplingKernel<TVector, AccessorSampleSource<TVector>>.Sample(new(_accessor), Transform.WorldToIndex(world), Order, Staggered, out TVector value);
        return value;
    }
    /// <summary>Returns the stored value directly. Time and the interpolation/staggered settings are ignored.</summary>
    public TVector Sample(Coord index, double time = 0) => _accessor.GetValue(index);
    public DiscreteField<TVector> Copy() => new(this);
    Vec3d IVelocityField.Sample(Vec3d world, double time) => VelocityValue<TVector>.ToDouble(Sample(world, time));
    Vec3d IVelocityField.Sample(Coord index, double time) => VelocityValue<TVector>.ToDouble(Sample(index, time));
    IVelocityField IVelocityField.Copy() => Copy();
}

/// <summary>OpenVDB's analytic, divergence-free, time-periodic Enright velocity field.</summary>
/// <remarks>T is float or double. Results retain T arithmetic, widened to Vec3d for the common field API.
/// The field is intended for the world-space unit cube; its formula is also evaluated outside that cube.</remarks>
public sealed class EnrightField<T> : IVelocityField where T : unmanaged, IFloatingPointIeee754<T>
{
    public EnrightField()
    {
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double)) throw new NotSupportedException("Enright fields require float or double precision.");
    }
    /// <summary>Returns an independent identity transform on every query, as in the native field.</summary>
    public Transform Transform => new();
    public bool IsSinglePrecision => typeof(T) == typeof(float);
    public Vec3d Sample(Vec3d world, double time = 0) => IsSinglePrecision ? SampleSingle(world, (float)time) : SampleDouble(world, time);
    public Vec3d Sample(Coord index, double time = 0) => Sample(index.AsVec3d(), time);
    public EnrightField<T> Copy() => new();
    IVelocityField IVelocityField.Copy() => Copy();
    private static Vec3d SampleSingle(Vec3d world, float time)
    {
        const float pi = System.MathF.PI, phase = pi / 3f;
        float px = pi * (float)world.X, py = pi * (float)world.Y, pz = pi * (float)world.Z;
        float tr = System.MathF.Cos(time * phase), a = System.MathF.Sin(2f * py), b = -System.MathF.Sin(2f * px), c = System.MathF.Sin(2f * pz);
        float sx = System.MathF.Sin(px), sy = System.MathF.Sin(py), sz = System.MathF.Sin(pz);
        return new(tr * (2f * (sx * sx) * a * c), tr * (b * (sy * sy) * c), tr * (b * a * (sz * sz)));
    }
    private static Vec3d SampleDouble(Vec3d world, double time)
    {
        const double pi = System.Math.PI, phase = pi / 3.0;
        double px = pi * world.X, py = pi * world.Y, pz = pi * world.Z;
        double tr = System.Math.Cos(time * phase), a = System.Math.Sin(2.0 * py), b = -System.Math.Sin(2.0 * px), c = System.Math.Sin(2.0 * pz);
        double sx = System.Math.Sin(px), sy = System.Math.Sin(py), sz = System.Math.Sin(pz);
        return new(tr * (2.0 * (sx * sx) * a * c), tr * (b * (sy * sy) * c), tr * (b * a * (sz * sz)));
    }
}

internal static class VelocityValue<TVector> where TVector : unmanaged
{
    internal static void Validate()
    {
        if (typeof(TVector) != typeof(Vec3f) && typeof(TVector) != typeof(Vec3d)) throw new NotSupportedException("Velocity grids require Vec3f or Vec3d values.");
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vec3d ToDouble(TVector value) => typeof(TVector) == typeof(Vec3f) ? Unsafe.As<TVector, Vec3f>(ref value).AsVec3d() : Unsafe.As<TVector, Vec3d>(ref value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vec3f ToSingle(TVector value) => Unsafe.As<TVector, Vec3f>(ref value);
}
