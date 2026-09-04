// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of tools/ParticlesToLevelSet.h, using the deterministic native serial raster order.
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.Tools;

/// <summary>Rasterizes world-space particle spheres and trails into float/double level sets or boolean masks.</summary>
/// <remarks>Radius bounds and HalfWidth use voxel units. Geometry uses native double intermediates. Raster order is deterministic and serial; GrainSize is retained as configuration for native API correspondence. Existing grid values are unioned in place.</remarks>
public sealed class ParticlesToLevelSet<T> where T : unmanaged
{
    private readonly ParticleRasterizer<T, byte> _raster;
    public ParticlesToLevelSet(Grid<T> grid) => _raster = new(grid, false);
    public Grid<T> Grid => _raster.Grid;
    public double VoxelSize => _raster.VoxelSize;
    public double HalfWidth => _raster.HalfWidth;
    public double MinimumRadius { get => _raster.MinimumRadius; set => _raster.MinimumRadius = value; }
    public double MaximumRadius { get => _raster.MaximumRadius; set => _raster.MaximumRadius = value; }
    public ulong MinimumRadiusCount => _raster.MinimumRadiusCount;
    public ulong MaximumRadiusCount => _raster.MaximumRadiusCount;
    public bool IgnoredParticles => _raster.IgnoredParticles;
    public int GrainSize { get => _raster.GrainSize; set => _raster.GrainSize = value; }
    public ulong MaximumVoxelEvaluations { get => _raster.MaximumVoxelEvaluations; set => _raster.MaximumVoxelEvaluations = value; }
    public ulong MaximumSphereInstances { get => _raster.MaximumSphereInstances; set => _raster.MaximumSphereInstances = value; }
    public void RasterizeSpheres(IParticleSource particles, CancellationToken cancellationToken = default) => _raster.Rasterize(particles, particles, null, 0, null, cancellationToken);
    public void RasterizeSpheres(IPointSource points, double radius, CancellationToken cancellationToken = default) => _raster.Rasterize(points, null, radius, 0, null, cancellationToken);
    public void RasterizeTrails(IParticleSource particles, double delta = 1, CancellationToken cancellationToken = default) { if (!(delta > 0) || !double.IsFinite(delta)) throw new ArgumentOutOfRangeException(nameof(delta)); _raster.Rasterize(particles, particles, null, delta, null, cancellationToken); }
    public void Finalize(bool prune = false, CancellationToken cancellationToken = default) => _raster.Finalize(prune, cancellationToken);
}
/// <summary>Particle rasterization with unmanaged attribute transfer onto a separately finalized grid.</summary>
/// <remarks>Attributes belong only to particle-generated topology, even when the destination already contains values. Finalize must be called after rasterization; matching the native API, repeated finalization is discouraged.</remarks>
public sealed class ParticlesToLevelSet<T, TAttribute> where T : unmanaged where TAttribute : unmanaged
{
    private readonly ParticleRasterizer<T, TAttribute> _raster;
    public ParticlesToLevelSet(Grid<T> grid) => _raster = new(grid, true);
    public Grid<T> Grid => _raster.Grid;
    public Grid<TAttribute>? AttributeGrid => _raster.AttributeGrid;
    public double VoxelSize => _raster.VoxelSize;
    public double HalfWidth => _raster.HalfWidth;
    public double MinimumRadius { get => _raster.MinimumRadius; set => _raster.MinimumRadius = value; }
    public double MaximumRadius { get => _raster.MaximumRadius; set => _raster.MaximumRadius = value; }
    public ulong MinimumRadiusCount => _raster.MinimumRadiusCount;
    public ulong MaximumRadiusCount => _raster.MaximumRadiusCount;
    public bool IgnoredParticles => _raster.IgnoredParticles;
    public int GrainSize { get => _raster.GrainSize; set => _raster.GrainSize = value; }
    public ulong MaximumVoxelEvaluations { get => _raster.MaximumVoxelEvaluations; set => _raster.MaximumVoxelEvaluations = value; }
    public ulong MaximumSphereInstances { get => _raster.MaximumSphereInstances; set => _raster.MaximumSphereInstances = value; }
    public void RasterizeSpheres(IParticleSource<TAttribute> particles, CancellationToken cancellationToken = default) => _raster.Rasterize(particles, particles, null, 0, particles, cancellationToken);
    public void RasterizeSpheres(IParticleSource<TAttribute> particles, double radius, CancellationToken cancellationToken = default) => _raster.Rasterize(particles, null, radius, 0, particles, cancellationToken);
    public void RasterizeTrails(IParticleSource<TAttribute> particles, double delta = 1, CancellationToken cancellationToken = default) { if (!(delta > 0) || !double.IsFinite(delta)) throw new ArgumentOutOfRangeException(nameof(delta)); _raster.Rasterize(particles, particles, null, delta, particles, cancellationToken); }
    public void Finalize(bool prune = false, CancellationToken cancellationToken = default) => _raster.Finalize(prune, cancellationToken);
}

internal sealed partial class ParticleRasterizer<T, TAttribute> where T : unmanaged where TAttribute : unmanaged
{
    private readonly Grid<ParticleValue<T, TAttribute>>? _blind;
    private double _minimum = 1.5, _maximum = 100;
    private ulong _evaluations, _instances;
    internal ParticleRasterizer(Grid<T> grid, bool attributes)
    {
        ArgumentNullException.ThrowIfNull(grid); ParticleScalar<T>.Validate(); if (!grid.Transform.HasUniformScale) throw new ArgumentException("Particle rasterization requires uniform voxels.", nameof(grid));
        Grid = grid; VoxelSize = grid.Transform.VoxelSize.X; HalfWidth = ParticleScalar<T>.ToDouble(grid.Background) / VoxelSize;
        if (attributes) _blind = new(new ParticleValue<T, TAttribute>(grid.Background, default)) { Transform = grid.Transform.Copy() };
    }
    internal Grid<T> Grid { get; }
    internal Grid<TAttribute>? AttributeGrid { get; private set; }
    internal double VoxelSize { get; }
    internal double HalfWidth { get; }
    internal double MinimumRadius { get => _minimum; set => _minimum = 0 > value ? 0 : value; }
    internal double MaximumRadius { get => _maximum; set => _maximum = _minimum > value ? _minimum : value; }
    internal ulong MinimumRadiusCount { get; private set; }
    internal ulong MaximumRadiusCount { get; private set; }
    internal bool IgnoredParticles => MinimumRadiusCount > 0 || MaximumRadiusCount > 0;
    internal int GrainSize { get; set; } = 1;
    internal ulong MaximumVoxelEvaluations { get; set; } = ulong.MaxValue;
    internal ulong MaximumSphereInstances { get; set; } = ulong.MaxValue;
    internal void Rasterize(IPointSource points, IParticleSource? particles, double? fixedRadius, double delta, IParticleSource<TAttribute>? attributes, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(points); token.ThrowIfCancellationRequested();
        if (delta != 0 && (!(delta > 0) || !double.IsFinite(delta))) throw new ArgumentOutOfRangeException(nameof(delta));
        Transform transform = _blind?.Transform ?? Grid.Transform; MapBase map = transform.BaseMap;
        PointPartitioner partition = PointPartitioner.Create(points, transform, cancellationToken: token);
        MinimumRadiusCount = MaximumRadiusCount = 0; _evaluations = _instances = 0;
        double fixedVoxelRadius = fixedRadius.GetValueOrDefault() / VoxelSize;
        if (fixedRadius.HasValue)
        {
            if (fixedVoxelRadius < MinimumRadius) MinimumRadiusCount = (ulong)points.Count;
            if (fixedVoxelRadius > MaximumRadius) MaximumRadiusCount = (ulong)points.Count;
            if (IgnoredParticles) return;
        }
        var access = new RasterAccess(Grid.GetAccessor(), _blind?.GetAccessor()); double inverseDx = 1 / VoxelSize;
        Vec3d origin = delta != 0 ? map.ApplyInverseMap(Vec3d.Zero) : default;
        for (int bucket = 0; bucket < partition.Count; ++bucket)
        {
            foreach (int index in partition.Indices(bucket))
            {
                token.ThrowIfCancellationRequested(); double radius = fixedRadius.HasValue ? fixedVoxelRadius : inverseDx * particles!.GetRadius(index);
                if (!fixedRadius.HasValue)
                {
                    if (radius < MinimumRadius) { ++MinimumRadiusCount; continue; }
                    if (radius > MaximumRadius) { ++MaximumRadiusCount; continue; }
                }
                if (!double.IsFinite(radius)) throw new ArgumentOutOfRangeException(nameof(particles), "A rasterized particle radius must be finite.");
                Vec3d position = map.ApplyInverseMap(points.GetPosition(index)); TAttribute attribute = attributes is null ? default : attributes.GetAttribute(index);
                if (delta == 0) { MakeSphere(position, radius, attribute, access, token); continue; }
                Vec3d velocity = map.ApplyInverseMap(particles!.GetVelocity(index)) - origin; double speed = velocity.Length, inverseSpeed = 1 / speed;
                Vec3d direction = -velocity * inverseSpeed, center = position; double currentRadius = radius, distance = 0;
                while (distance <= speed)
                {
                    MakeSphere(center, currentRadius, attribute, access, token); Vec3d next = center + (.5 * delta * currentRadius) * direction;
                    if (speed == 0) break;
                    if (next == center) throw new ArithmeticException("The particle trail cannot advance at this precision.");
                    center = next; distance = (center - position).Length; currentRadius = radius - (radius - MinimumRadius) * distance * inverseSpeed;
                }
            }
        }
    }
    private void MakeSphere(Vec3d center, double radius, TAttribute attribute, RasterAccess access, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (_instances == MaximumSphereInstances) throw new InvalidOperationException("Particle rasterization exceeds MaximumSphereInstances."); ++_instances;
        if (typeof(T) == typeof(bool)) { MakeMask(center, radius, attribute, access, token); return; }
        double maximum = radius + HalfWidth, maximumSquared = maximum * maximum, minimum = System.Math.Max(0, radius - HalfWidth), minimumSquared = minimum * minimum;
        Coord lower = Coord.Floor(center - new Vec3d(maximum)), upper = Coord.Ceiling(center + new Vec3d(maximum));
        var inside = new ParticleValue<T, TAttribute>(ParticleScalar<T>.FromDouble(-ParticleScalar<T>.ToDouble(_blind is null ? Grid.Background : _blind.Background.Visible)), default);
        for (long x = lower.X; x <= upper.X; ++x)
        {
            token.ThrowIfCancellationRequested(); double xx = (x - center.X) * (x - center.X);
            for (long y = lower.Y; y <= upper.Y; ++y)
            {
                double xy = xx + (y - center.Y) * (y - center.Y);
                for (long z = lower.Z; z <= upper.Z; ++z)
                {
                    Evaluate(); double squared = xy + (z - center.Z) * (z - center.Z); if (squared >= maximumSquared) continue;
                    Coord coordinate = new((int)x, (int)y, (int)z); ParticleValue<T, TAttribute> prior = access.Probe(coordinate, out bool active);
                    if (!active && ParticleScalar<T>.ToDouble(prior.Visible) < 0) continue;
                    if (squared <= minimumSquared) { access.SetOff(coordinate, inside); continue; }
                    T distance = ParticleScalar<T>.FromDouble(VoxelSize * (System.Math.Sqrt(squared) - radius));
                    if (ParticleScalar<T>.ToDouble(distance) < ParticleScalar<T>.ToDouble(prior.Visible)) access.SetOn(coordinate, new(distance, attribute));
                }
            }
        }
    }
    private void MakeMask(Vec3d center, double radius, TAttribute attribute, RasterAccess access, CancellationToken token)
    {
        double squaredRadius = radius * radius, innerWidth = radius / System.Math.Sqrt(6);
        Coord lower = Coord.Floor(center - new Vec3d(radius)), upper = Coord.Ceiling(center + new Vec3d(radius)), innerLower = Coord.Ceiling(center - new Vec3d(innerWidth)), innerUpper = Coord.Floor(center + new Vec3d(innerWidth));
        var on = new ParticleValue<T, TAttribute>(ParticleScalar<T>.FromDouble(1), attribute); CoordBBox interior = new(innerLower, innerUpper);
        if (_blind is null) Grid.Tree.Fill(interior, on.Visible, true); else _blind.Tree.Fill(interior, on, true);
        // Native slabs overlap at edges. Repeated visits preserve attribute overwrite ordering.
        CoordBBox[] padding = [new(lower, new(checked(innerLower.X - 1), upper.Y, upper.Z)), new(new(checked(innerUpper.X + 1), lower.Y, lower.Z), upper),
            new(lower, new(upper.X, checked(innerLower.Y - 1), upper.Z)), new(new(lower.X, checked(innerUpper.Y + 1), lower.Z), upper),
            new(lower, new(upper.X, upper.Y, checked(innerLower.Z - 1))), new(new(lower.X, lower.Y, checked(innerUpper.Z + 1)), upper)];
        foreach (CoordBBox box in padding)
        {
            token.ThrowIfCancellationRequested(); if (box.IsEmpty) continue;
            for (long x = box.Min.X; x <= box.Max.X; ++x)
            {
                token.ThrowIfCancellationRequested(); double xx = (x - center.X) * (x - center.X);
                for (long y = box.Min.Y; y <= box.Max.Y; ++y)
                {
                    double xy = xx + (y - center.Y) * (y - center.Y);
                    for (long z = box.Min.Z; z <= box.Max.Z; ++z) { Evaluate(); if (xy + (z - center.Z) * (z - center.Z) < squaredRadius) access.SetOn(new((int)x, (int)y, (int)z), on); }
                }
            }
        }
    }
    private void Evaluate() { if (_evaluations == MaximumVoxelEvaluations) throw new InvalidOperationException("Particle rasterization exceeds MaximumVoxelEvaluations."); ++_evaluations; }
    private readonly struct RasterAccess(ValueAccessor<T> scalar, ValueAccessor<ParticleValue<T, TAttribute>>? blind)
    {
        internal ParticleValue<T, TAttribute> Probe(Coord coordinate, out bool active)
        {
            if (blind is not null) { active = blind.ProbeValue(coordinate, out var value); return value; }
            active = scalar.ProbeValue(coordinate, out T scalarValue); return new(scalarValue, default);
        }
        internal void SetOn(Coord coordinate, ParticleValue<T, TAttribute> value) { if (blind is null) scalar.SetValueOn(coordinate, value.Visible); else blind.SetValueOn(coordinate, value); }
        internal void SetOff(Coord coordinate, ParticleValue<T, TAttribute> value) { if (blind is null) scalar.SetValueOff(coordinate, value.Visible); else blind.SetValueOff(coordinate, value); }
    }
}
internal readonly struct ParticleValue<T, TAttribute>(T visible, TAttribute attribute) : IEquatable<ParticleValue<T, TAttribute>> where T : unmanaged where TAttribute : unmanaged
{
    internal T Visible { get; } = visible;
    internal TAttribute Attribute { get; } = attribute;
    public bool Equals(ParticleValue<T, TAttribute> other) => ValueEquality<T>.Equals(Visible, other.Visible);
    public override bool Equals(object? obj) => obj is ParticleValue<T, TAttribute> other && Equals(other);
    public override int GetHashCode() => Visible.GetHashCode();
}
internal static class ParticleScalar<T> where T : unmanaged
{
    internal static void Validate() { if (typeof(T) != typeof(float) && typeof(T) != typeof(double) && typeof(T) != typeof(bool)) throw new NotSupportedException("Particle rasterization supports float, double and bool grids."); }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static double ToDouble(T value) => typeof(T) == typeof(float) ? Unsafe.As<T, float>(ref value) : typeof(T) == typeof(double) ? Unsafe.As<T, double>(ref value) : Unsafe.As<T, bool>(ref value) ? 1 : 0;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static T FromDouble(double value)
    {
        if (typeof(T) == typeof(float)) { float converted = (float)value; return Unsafe.As<float, T>(ref converted); }
        if (typeof(T) == typeof(double)) return Unsafe.As<double, T>(ref value);
        bool boolean = value != 0; return Unsafe.As<bool, T>(ref boolean);
    }
}
