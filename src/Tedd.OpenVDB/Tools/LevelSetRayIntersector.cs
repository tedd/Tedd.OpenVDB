// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Native iterative linear zero-crossing search over trilinearly interpolated level-set values.</summary>
/// <remarks>The grid must remain unchanged while this search is used. Mutable stencil/ray state belongs to one worker.</remarks>
public sealed class LinearSearch<T> : ILevelSetRayTester where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly VdbTree<T> _tree;
    private readonly BoxStencil<T> _stencil;
    private readonly T _minimum, _maximum;
    private T _v0, _v1;
    private double _t0, _t1, _time;
    private bool _hit;
    public LinearSearch(Grid<T> grid, T isoValue = default, int iterations = 0)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double)) throw new NotSupportedException("Level-set ray search supports float and double grids.");
        if (grid.IsEmpty) throw new ArgumentException("Ray search requires a nonempty grid.", nameof(grid));
        if (!T.IsFinite(isoValue) || isoValue <= -grid.Background || isoValue >= grid.Background) throw new ArgumentOutOfRangeException(nameof(isoValue), "The iso-value must be inside the narrow band.");
        ArgumentOutOfRangeException.ThrowIfNegative(iterations);
        Grid = grid; IsoValue = isoValue; Iterations = iterations; _tree = grid.Tree; _stencil = new(grid);
        T width = T.CreateTruncating(2 * grid.Transform.VoxelSize.X); _minimum = isoValue - width; _maximum = isoValue + width;
        _tree.TryGetLeafBoundingBox(out var bounds); BoundingBox = bounds.IsEmpty ? BBoxd.Empty : new(bounds.Min.AsVec3d(), bounds.Max.AsVec3d());
    }
    public Grid<T> Grid { get; }
    public T IsoValue { get; }
    public int Iterations { get; }
    public BBoxd BoundingBox { get; }
    public Rayd Ray { get; private set; }
    public bool SetIndexRay(Rayd ray) { _hit = false; bool clipped = ray.TryClip(BoundingBox, out var result); Ray = result; return clipped; }
    public bool SetWorldRay(Rayd ray) => SetIndexRay(ray.WorldToIndex(Grid.Transform));
    public bool HasNode(Coord coordinate, int level) => _tree.ProbeNode(coordinate, level) is not null;
    public void SetRange(double start, double end) => Ray = new(Ray.Eye, Ray.Direction, start, end);
    public void Initialize(double time) { _t0 = time; _v0 = InterpolateValue(time); }
    public bool Test(Coord coordinate, double time)
    {
        if (!_tree.ProbeValue(coordinate, out T value) || !(value > _minimum && value < _maximum)) return false;
        _t1 = time; _v1 = InterpolateValue(time);
        // Preserve native multiplication, including floating-point underflow and signed zero.
        if (_v0 * _v1 <= T.Zero)
        {
            _time = InterpolateTime();
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                value = InterpolateValue(_time);
                if (_v0 * value <= T.Zero) { _v1 = value; _t1 = _time; } else { _v0 = value; _t0 = _time; }
                _time = InterpolateTime();
            }
            _hit = true; return true;
        }
        _t0 = _t1; _v0 = _v1; return false;
    }
    private double InterpolateTime() => _t0 + (_t1 - _t0) * double.CreateTruncating(_v0) / double.CreateTruncating(_v0 - _v1);
    private T InterpolateValue(double time) { Vec3d point = Ray.At(time); _stencil.MoveTo(point); return _stencil.Interpolate(point) - IsoValue; }
    public double GetIndexTime() { RequireHit(); return _time; }
    public double GetWorldTime() { RequireHit(); return _time * Grid.Transform.ApplyJacobian(Ray.Direction).Length; }
    public Vec3d GetIndexPosition() { RequireHit(); return Ray.At(_time); }
    public Vec3d GetWorldPosition() => Grid.IndexToWorld(GetIndexPosition());
    public void GetWorldPositionAndNormal(out Vec3d position, out Vec3d normal)
    {
        Vec3d index = GetIndexPosition(); _stencil.MoveTo(index); _stencil.Gradient(index).TryNormalize(out normal); position = Grid.IndexToWorld(index);
    }
    private void RequireHit() { if (!_hit) throw new InvalidOperationException("A successful intersection is required before accessing its result."); }
}

/// <summary>Accelerated ray intersection with float or double narrow-band level sets.</summary>
/// <remarks>Copy for each worker. The source grid, including its transform and tree, must remain unchanged during use.</remarks>
public sealed class LevelSetRayIntersector<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly LinearSearch<T> _search;
    public LevelSetRayIntersector(Grid<T> grid, T isoValue = default, int iterations = 0, int nodeLevel = 2)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (!grid.Transform.HasUniformScale) throw new ArgumentException("Level-set intersection requires uniform voxels.", nameof(grid));
        if (grid.GridClass != GridClass.LevelSet) throw new ArgumentException("The grid must be classified as a level set.", nameof(grid));
        if (nodeLevel < -1 || nodeLevel > 2) throw new ArgumentOutOfRangeException(nameof(nodeLevel));
        _search = new(grid, isoValue, iterations); NodeLevel = nodeLevel;
    }
    public LevelSetRayIntersector(LevelSetRayIntersector<T> source) : this((source ?? throw new ArgumentNullException(nameof(source))).Grid, source.IsoValue, source.Iterations, source.NodeLevel) { }
    public Grid<T> Grid => _search.Grid;
    public T IsoValue => _search.IsoValue;
    public int Iterations => _search.Iterations;
    public int NodeLevel { get; }
    public bool IntersectsIndex(Rayd ray) => _search.SetIndexRay(ray) && LevelSetHdda.Test(_search, NodeLevel);
    public bool IntersectsIndex(Rayd ray, out double time) { bool hit = IntersectsIndex(ray); time = hit ? _search.GetIndexTime() : double.NaN; return hit; }
    public bool IntersectsIndex(Rayd ray, out Vec3d position, out double time)
    { bool hit = IntersectsIndex(ray, out time); position = hit ? _search.GetIndexPosition() : default; return hit; }
    public bool IntersectsWorld(Rayd ray) => _search.SetWorldRay(ray) && LevelSetHdda.Test(_search, NodeLevel);
    public bool IntersectsWorld(Rayd ray, out double time) { bool hit = IntersectsWorld(ray); time = hit ? _search.GetWorldTime() : double.NaN; return hit; }
    public bool IntersectsWorld(Rayd ray, out Vec3d position, out double time)
    { bool hit = IntersectsWorld(ray, out time); position = hit ? _search.GetWorldPosition() : default; return hit; }
    public bool IntersectsWorld(Rayd ray, out Vec3d position, out Vec3d normal, out double time)
    { bool hit = IntersectsWorld(ray, out time); if (hit) _search.GetWorldPositionAndNormal(out position, out normal); else { position = default; normal = default; } return hit; }
}
