// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/RayTracer.h.
using System.Runtime.CompilerServices;
using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.Tools;

public abstract class BaseShader
{
    public abstract Film.Rgba Shade(Vec3d position, Vec3d normal, Vec3d rayDirection);
    public abstract BaseShader Copy();
}

public sealed class MatteShader : BaseShader
{
    public MatteShader(Film.Rgba? color = null) => Color = color ?? new Film.Rgba(1f);
    public Film.Rgba Color { get; }
    public override Film.Rgba Shade(Vec3d position, Vec3d normal, Vec3d rayDirection) => Color;
    public override BaseShader Copy() => new MatteShader(Color);
}

public sealed class NormalShader : BaseShader
{
    private readonly Film.Rgba _color;
    public NormalShader(Film.Rgba? color = null) => _color = (color ?? new Film.Rgba(1f)) * 0.5f;
    private NormalShader(Film.Rgba scaledColor, bool _) => _color = scaledColor;
    public override Film.Rgba Shade(Vec3d position, Vec3d normal, Vec3d rayDirection) =>
        _color * new Film.Rgba(normal.X + 1, normal.Y + 1, normal.Z + 1);
    public override BaseShader Copy() => new NormalShader(_color, true);
}

public sealed class PositionShader : BaseShader
{
    private readonly BBoxd _bounds;
    private readonly Vec3d _inverseExtents;
    public PositionShader(BBoxd bounds, Film.Rgba? color = null)
    {
        _bounds = bounds;
        _inverseExtents = 1.0 / bounds.Extents;
        Color = color ?? new Film.Rgba(1f);
    }
    public Film.Rgba Color { get; }
    public override Film.Rgba Shade(Vec3d position, Vec3d normal, Vec3d rayDirection)
    {
        Vec3d rgb = (position - _bounds.Min) * _inverseExtents;
        return Color * new Film.Rgba(rgb.X, rgb.Y, rgb.Z);
    }
    public override BaseShader Copy() => new PositionShader(_bounds, Color);
}

public sealed class DiffuseShader : BaseShader
{
    public DiffuseShader(Film.Rgba? color = null) => Color = color ?? new Film.Rgba(1f);
    public Film.Rgba Color { get; }
    public override Film.Rgba Shade(Vec3d position, Vec3d normal, Vec3d rayDirection) =>
        Color * (float)System.Math.Abs(normal.Dot(rayDirection));
    public override BaseShader Copy() => new DiffuseShader(Color);
}

public abstract class GridShader<TVector> : BaseShader where TVector : unmanaged
{
    private readonly GridSampler<TVector> _sampler;
    protected GridShader(Grid<TVector> grid, SamplingOrder order)
    {
        if (typeof(TVector) != typeof(Vec3f) && typeof(TVector) != typeof(Vec3d) && typeof(TVector) != typeof(Vec3i))
            throw new NotSupportedException("Color shaders require a Vec3f, Vec3d or Vec3i grid.");
        Grid = grid ?? throw new ArgumentNullException(nameof(grid));
        Order = order;
        _sampler = new(Grid, order);
    }
    protected Film.Rgba Sample(Vec3d position)
    {
        TVector value = _sampler.WorldSample(position);
        if (typeof(TVector) == typeof(Vec3f))
        {
            Vec3f vector = Unsafe.As<TVector, Vec3f>(ref value);
            return new Film.Rgba(vector.X, vector.Y, vector.Z);
        }
        if (typeof(TVector) == typeof(Vec3d))
        {
            Vec3d vector = Unsafe.As<TVector, Vec3d>(ref value);
            return new Film.Rgba(vector.X, vector.Y, vector.Z);
        }
        Vec3i integer = Unsafe.As<TVector, Vec3i>(ref value);
        return new Film.Rgba((float)integer.X, (float)integer.Y, (float)integer.Z);
    }
    protected Grid<TVector> Grid { get; }
    protected SamplingOrder Order { get; }
}

public sealed class MatteGridShader<TVector> : GridShader<TVector> where TVector : unmanaged
{
    public MatteGridShader(Grid<TVector> grid, SamplingOrder order = SamplingOrder.Point) : base(grid, order) { }
    public override Film.Rgba Shade(Vec3d position, Vec3d normal, Vec3d rayDirection) => Sample(position);
    public override BaseShader Copy() => new MatteGridShader<TVector>(Grid, Order);
}

public sealed class NormalGridShader<TVector> : GridShader<TVector> where TVector : unmanaged
{
    public NormalGridShader(Grid<TVector> grid, SamplingOrder order = SamplingOrder.Point) : base(grid, order) { }
    public override Film.Rgba Shade(Vec3d position, Vec3d normal, Vec3d rayDirection) =>
        Sample(position) * new Film.Rgba(normal.X + 1, normal.Y + 1, normal.Z + 1);
    public override BaseShader Copy() => new NormalGridShader<TVector>(Grid, Order);
}

public sealed class PositionGridShader<TVector> : GridShader<TVector> where TVector : unmanaged
{
    private readonly BBoxd _bounds;
    private readonly Vec3d _inverseExtents;
    public PositionGridShader(BBoxd bounds, Grid<TVector> grid, SamplingOrder order = SamplingOrder.Point) : base(grid, order)
    {
        _bounds = bounds;
        _inverseExtents = 1.0 / bounds.Extents;
    }
    public override Film.Rgba Shade(Vec3d position, Vec3d normal, Vec3d rayDirection)
    {
        Vec3d rgb = (position - _bounds.Min) * _inverseExtents;
        return Sample(position) * new Film.Rgba(rgb.X, rgb.Y, rgb.Z);
    }
    public override BaseShader Copy() => new PositionGridShader<TVector>(_bounds, Grid, Order);
}

public sealed class DiffuseGridShader<TVector> : GridShader<TVector> where TVector : unmanaged
{
    public DiffuseGridShader(Grid<TVector> grid, SamplingOrder order = SamplingOrder.Point) : base(grid, order) { }
    public override Film.Rgba Shade(Vec3d position, Vec3d normal, Vec3d rayDirection) =>
        Sample(position) * (float)System.Math.Abs(normal.Dot(rayDirection));
    public override BaseShader Copy() => new DiffuseGridShader<TVector>(Grid, Order);
}