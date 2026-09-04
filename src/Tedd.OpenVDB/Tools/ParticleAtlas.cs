// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Radius partitioning and query filters from tools/ParticleAtlas.h.
using System.Collections;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

/// <summary>Multiresolution point-index grids that partition external particles by radius.</summary>
/// <remarks>Construct appends levels, matching native behavior. The native splitting loop can create maxLevels + 1 levels. Positions and radii remain external and must agree with the constructed partitions during queries.</remarks>
public sealed class ParticleAtlas
{
    private readonly List<PointIndexGrid> _grids = []; private readonly List<double> _minimum = [], _maximum = [];
    public int Levels => _grids.Count;
    public bool IsEmpty => Levels == 0;
    public double MinimumRadius(int level) => _minimum[level];
    public double MaximumRadius(int level) => _maximum[level];
    public PointIndexGrid PointIndexGrid(int level) => _grids[level];
    public static ParticleAtlas Create(IParticleSource particles, double minimumVoxelSize, int maximumLevels = 50, CancellationToken cancellationToken = default)
    { var atlas = new ParticleAtlas(); atlas.Construct(particles, minimumVoxelSize, maximumLevels, cancellationToken); return atlas; }
    public void Construct(IParticleSource particles, double minimumVoxelSize, int maximumLevels = 50, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(particles); ArgumentOutOfRangeException.ThrowIfNegative(maximumLevels); if (!double.IsFinite(minimumVoxelSize)) throw new ArgumentOutOfRangeException(nameof(minimumVoxelSize)); cancellationToken.ThrowIfCancellationRequested();
        int count = particles.Count; ArgumentOutOfRangeException.ThrowIfNegative(count); int[] all = GC.AllocateUninitializedArray<int>(count); for (int i = 0; i < count; ++i) all[i] = i;
        var first = new ParticleSubset(particles, all, cancellationToken); double voxelSize = System.Math.Max(minimumVoxelSize, first.Minimum); List<ParticleSubset> parts = [first]; List<double> sizes = [voxelSize];
        if (!(first.Maximum < voxelSize * 2) && maximumLevels > 1)
        {
            for (int n = 0; n < maximumLevels; ++n)
            {
                cancellationToken.ThrowIfCancellationRequested(); ParticleSubset current = parts[^1]; double limit = sizes[^1] * 2; if (current.Maximum < limit) break;
                List<int> remaining = [], next = []; foreach (int index in current.Indices) { double radius = particles.GetRadius(index); if (radius < limit) remaining.Add(index); else next.Add(index); }
                if (next.Count == 0) break;
                parts[^1] = new(particles, remaining.ToArray(), cancellationToken); var higher = new ParticleSubset(particles, next.ToArray(), cancellationToken); parts.Add(higher); sizes.Add(higher.Minimum);
            }
        }
        List<PointIndexGrid> grids = []; for (int i = 0; i < parts.Count; ++i) { cancellationToken.ThrowIfCancellationRequested(); var grid = global::Tedd.OpenVDB.Tools.PointIndexGrid.Create(parts[i], sizes[i], cancellationToken); grid.RemapIndices(parts[i].Indices); grids.Add(grid); }
        cancellationToken.ThrowIfCancellationRequested(); for (int i = 0; i < grids.Count; ++i) { _grids.Add(grids[i]); _minimum.Add(parts[i].Minimum); _maximum.Add(parts[i].Maximum); }
    }
    public Iterator CreateIterator() => new(this);
    private sealed class ParticleSubset : IPointSource
    {
        private readonly IParticleSource _source; internal readonly int[] Indices; internal double Minimum { get; }
        internal double Maximum { get; }
        internal ParticleSubset(IParticleSource source, int[] indices, CancellationToken token)
        { _source = source; Indices = indices; double min = double.MaxValue, max = -double.MaxValue; foreach (int index in indices) { token.ThrowIfCancellationRequested(); double r = source.GetRadius(index); if (!double.IsFinite(r)) throw new ArgumentOutOfRangeException(nameof(source), "Particle radii must be finite."); min = System.Math.Min(r, min); max = System.Math.Max(r, max); } Minimum = min; Maximum = max; }
        public int Count => Indices.Length; public Vec3d GetPosition(int index) => _source.GetPosition(Indices[index]);
    }
    /// <summary>Reusable particle queries. Existing iterators retain their original level count when Construct appends more levels.</summary>
    public sealed class Iterator : IEnumerable<uint>
    {
        private readonly ParticleAtlas _atlas; private readonly int _levelCount; private readonly PointIndexIterator _results = new();
        public Iterator(ParticleAtlas atlas) { ArgumentNullException.ThrowIfNull(atlas); _atlas = atlas; _levelCount = atlas.Levels; }
        public int Levels => _atlas.Levels;
        public ulong Count => _results.Count;
        public bool IsValid => _results.IsValid;
        public uint Current => _results.Current;
        public bool Next() => _results.Next();
        public void Reset() => _results.Reset();
        public IEnumerator<uint> GetEnumerator() => _results.GetEnumerator(); IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void UpdateFromLevel(int level)
        { ArgumentOutOfRangeException.ThrowIfNegative(level); _results.Clear(); if (_levelCount == 0) return; var grid = _atlas.PointIndexGrid(System.Math.Min(_levelCount - 1, level)); List<PointIndexRange> ranges = []; foreach (var leaf in grid.Tree.Leaves) if (leaf.PointCount > 0) ranges.Add(new(leaf.IndexArray, 0, leaf.PointCount)); _results.SetResults(ranges); }
        public void WorldSpaceSearchAndUpdate(Vec3d center, double radius, IParticleSource particles, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(particles); PointIndexSearch.ValidateRadius(center, radius); cancellationToken.ThrowIfCancellationRequested(); _results.Clear(); List<PointIndexRange> ranges = []; List<uint> filtered = []; double inner = radius * (1.0 / System.Math.Sqrt(3));
            for (int level = 0; level < _levelCount; ++level)
            {
                var grid = _atlas.PointIndexGrid(level); double max = _atlas.MaximumRadius(level); bool uniform = System.Math.Abs(_atlas.MinimumRadius(level) - max) <= 1e-15;
                CoordBBox interior = new(grid.Transform.WorldToIndexCellCentered(center - new Vec3d(inner)), grid.Transform.WorldToIndexCellCentered(center + new Vec3d(inner))); interior = interior.Expand(-1); PointIndexSearch.Search(grid.Tree, interior, ranges, cancellationToken);
                CoordBBox region = new(grid.Transform.WorldToIndexCellCentered(center - new Vec3d(radius) - new Vec3d(max)), grid.Transform.WorldToIndexCellCentered(center + new Vec3d(radius) + new Vec3d(max)));
                // Expanding an eroded, possibly inverted box must preserve its endpoints as native CoordBBox does.
                CoordBBox innerBoundary = ExpandRaw(interior, 1); var filter = new ParticleRadiusFilter(ranges, filtered, particles, center, radius, uniform);
                foreach (var box in PointIndexSearch.ExclusiveRegions(region, innerBoundary)) PointIndexSearch.Filter(grid.Tree, box, filter, cancellationToken);
            }
            _results.SetResults(ranges, filtered);
        }
        public void WorldSpaceSearchAndUpdate(BBoxd box, IParticleSource particles, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(particles); cancellationToken.ThrowIfCancellationRequested(); _results.Clear(); if (!box.Min.IsFinite || !box.Max.IsFinite) throw new ArgumentOutOfRangeException(nameof(box)); List<PointIndexRange> ranges = []; List<uint> filtered = [];
            for (int level = 0; level < _levelCount; ++level)
            {
                var grid = _atlas.PointIndexGrid(level); double max = _atlas.MaximumRadius(level); bool uniform = System.Math.Abs(_atlas.MinimumRadius(level) - max) <= 1e-15;
                CoordBBox interior = new(grid.Transform.WorldToIndexCellCentered(box.Min), grid.Transform.WorldToIndexCellCentered(box.Max)); interior = interior.Expand(-1); PointIndexSearch.Search(grid.Tree, interior, ranges, cancellationToken);
                CoordBBox region = new(grid.Transform.WorldToIndexCellCentered(box.Min - new Vec3d(max)), grid.Transform.WorldToIndexCellCentered(box.Max + new Vec3d(max)));
                var filter = new ParticleBoxFilter(ranges, filtered, particles, box, uniform); foreach (var b in PointIndexSearch.ExclusiveRegions(region, ExpandRaw(interior, 1))) PointIndexSearch.Filter(grid.Tree, b, filter, cancellationToken);
            }
            _results.SetResults(ranges, filtered);
        }
        private static CoordBBox ExpandRaw(CoordBBox box, int n) => new(new Coord(checked(box.Min.X - n), checked(box.Min.Y - n), checked(box.Min.Z - n)), new Coord(checked(box.Max.X + n), checked(box.Max.Y + n), checked(box.Max.Z + n)));
    }
}
internal abstract class ParticleRangeFilter(List<PointIndexRange> ranges, List<uint> filtered, IParticleSource particles) : PointIndexRangeFilter(ranges, filtered)
{
    protected IParticleSource Particles { get; } = particles;
    internal override void FilterLeaf(PointIndexLeafNode leaf) { if (leaf.PointCount > 0) FilterVoxel(leaf, leaf.Origin, 0, leaf.PointCount); }
}
internal sealed class ParticleRadiusFilter : ParticleRangeFilter
{
    private readonly Vec3d _center; private readonly double _radius; private readonly bool _uniform;
    internal ParticleRadiusFilter(List<PointIndexRange> ranges, List<uint> filtered, IParticleSource particles, Vec3d center, double radius, bool uniform) : base(ranges, filtered, particles)
    { _center = center; _uniform = uniform; double combined = uniform ? radius + particles.GetRadius(0) : radius; _radius = uniform ? combined * combined : radius; }
    internal override void FilterVoxel(PointIndexLeafNode leaf, Coord coordinate, int start, int count)
    { for (int i = start; i < start + count; ++i) { CheckCancellation(i); uint index = leaf.IndexArray[i]; int id = checked((int)index); double radiusSquared = _uniform ? _radius : _radius + Particles.GetRadius(id); if (!_uniform) radiusSquared *= radiusSquared; if ((_center - Particles.GetPosition(id)).LengthSquared < radiusSquared) Filtered.Add(index); } }
}
internal sealed class ParticleBoxFilter : ParticleRangeFilter
{
    private readonly BBoxd _box; private readonly double _uniformRadiusSquared; private readonly bool _uniform;
    internal ParticleBoxFilter(List<PointIndexRange> ranges, List<uint> filtered, IParticleSource particles, BBoxd box, bool uniform) : base(ranges, filtered, particles)
    { _box = box; _uniform = uniform; if (uniform) { double radius = particles.GetRadius(0); _uniformRadiusSquared = radius * radius; } }
    internal override void FilterVoxel(PointIndexLeafNode leaf, Coord coordinate, int start, int count)
    { for (int i = start; i < start + count; ++i) { CheckCancellation(i); uint index = leaf.IndexArray[i]; int id = checked((int)index); Vec3d position = Particles.GetPosition(id); if (_box.Contains(position)) { Filtered.Add(index); continue; } double squared = 0; for (int axis = 0; axis < 3; ++axis) { double value = position[axis], min = _box.Min[axis], max = _box.Max[axis]; if (value < min) { double delta = min - value; squared += delta * delta; } if (value > max) { double delta = value - max; squared += delta * delta; } } double r = _uniform ? _uniformRadiusSquared : Particles.GetRadius(id); if (!_uniform) r *= r; if (!(squared > r)) Filtered.Add(index); } }
}
