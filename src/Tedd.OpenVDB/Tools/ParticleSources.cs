// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed point/particle adapters for PointPartitioner.h and ParticlesToLevelSet.h.
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

/// <summary>Indexed world-space point source. Positions are read while partitioning and again during rasterization.</summary>
public interface IPointSource
{
    int Count { get; }
    Vec3d GetPosition(int index);
}
/// <summary>Particle positions, world-space radii and world-space velocity vectors.</summary>
public interface IParticleSource : IPointSource
{
    double GetRadius(int index);
    Vec3d GetVelocity(int index);
}
public interface IParticleSource<TAttribute> : IParticleSource where TAttribute : unmanaged
{
    TAttribute GetAttribute(int index);
}
public readonly record struct Particle(Vec3d Position, double Radius, Vec3d Velocity = default);
public sealed class PointArray : IPointSource
{
    private readonly ReadOnlyMemory<Vec3d> _points;
    public PointArray(Vec3d[] points) : this((ReadOnlyMemory<Vec3d>)points) { ArgumentNullException.ThrowIfNull(points); }
    public PointArray(ReadOnlyMemory<Vec3d> points) => _points = points;
    public int Count => _points.Length;
    public Vec3d GetPosition(int index) => _points.Span[index];
}
public sealed class ParticleArray : IParticleSource
{
    private readonly ReadOnlyMemory<Particle> _particles;
    public ParticleArray(Particle[] particles) : this((ReadOnlyMemory<Particle>)particles) { ArgumentNullException.ThrowIfNull(particles); }
    public ParticleArray(ReadOnlyMemory<Particle> particles) => _particles = particles;
    public int Count => _particles.Length;
    public Vec3d GetPosition(int index) => _particles.Span[index].Position;
    public double GetRadius(int index) => _particles.Span[index].Radius;
    public Vec3d GetVelocity(int index) => _particles.Span[index].Velocity;
}
public sealed class ParticleArray<TAttribute> : IParticleSource<TAttribute> where TAttribute : unmanaged
{
    private readonly ReadOnlyMemory<Particle> _particles;
    private readonly ReadOnlyMemory<TAttribute> _attributes;
    public ParticleArray(Particle[] particles, TAttribute[] attributes) : this((ReadOnlyMemory<Particle>)particles, (ReadOnlyMemory<TAttribute>)attributes) { ArgumentNullException.ThrowIfNull(particles); ArgumentNullException.ThrowIfNull(attributes); }
    public ParticleArray(ReadOnlyMemory<Particle> particles, ReadOnlyMemory<TAttribute> attributes)
    {
        if (particles.Length != attributes.Length) throw new ArgumentException("Each particle requires one attribute.", nameof(attributes)); _particles = particles; _attributes = attributes;
    }
    public int Count => _particles.Length;
    public Vec3d GetPosition(int index) => _particles.Span[index].Position;
    public double GetRadius(int index) => _particles.Span[index].Radius;
    public Vec3d GetVelocity(int index) => _particles.Span[index].Velocity;
    public TAttribute GetAttribute(int index) => _attributes.Span[index];
}
