// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Native prefix-range, exclusive-region and point filtering algorithms from tools/PointIndexGrid.h.
using System.Collections;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

internal readonly record struct PointIndexRange(uint[] Data, int Start, int Count);

/// <summary>Reusable range-query iterator over externally stored point indices.</summary>
/// <remarks>Native-style Current initially refers to the first item; Next advances. Enumeration independently visits the entire result. Accurate radial predicates are strict; bounding-box predicates are inclusive. The native metric assumes linear uniform cubical voxels.</remarks>
public sealed class PointIndexIterator : IEnumerable<uint>
{
    private List<PointIndexRange> _ranges = [];
    private int _range, _offset;
    private ulong _count;
    public PointIndexIterator() { }
    public PointIndexIterator(Coord coordinate, PointIndexGrid grid) => SearchAndUpdate(coordinate, grid);
    public PointIndexIterator(CoordBBox region, PointIndexGrid grid, CancellationToken cancellationToken = default) => SearchAndUpdate(region, grid, cancellationToken);
    public PointIndexIterator(PointIndexIterator other)
    { ArgumentNullException.ThrowIfNull(other); _ranges = [.. other._ranges]; _range = other._range; _offset = other._offset; _count = other._count; }
    public ulong Count => _count;
    public bool IsValid => _range < _ranges.Count;
    public uint Current => IsValid ? _ranges[_range].Data[_ranges[_range].Start + _offset] : throw new InvalidOperationException("The point iterator is exhausted.");
    public void Reset() { _range = 0; _offset = 0; }
    public bool Next() { if (!IsValid) return false; if (++_offset == _ranges[_range].Count) { ++_range; _offset = 0; } return IsValid; }
    internal void SetResults(List<PointIndexRange> ranges, List<uint>? filtered = null)
    { if (filtered is { Count: > 0 }) ranges.Add(new(filtered.ToArray(), 0, filtered.Count)); _ranges = ranges.Where(x => x.Count > 0).ToList(); _count = _ranges.Aggregate(0ul, (sum, x) => sum + (ulong)x.Count); Reset(); }
    public void Clear() { _ranges = []; _count = 0; Reset(); }
    public IEnumerator<uint> GetEnumerator() { foreach (var range in _ranges) for (int i = 0; i < range.Count; ++i) yield return range.Data[range.Start + i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void SearchAndUpdate(Coord coordinate, PointIndexGrid grid)
    { ArgumentNullException.ThrowIfNull(grid); Clear(); var leaf = grid.Tree.ProbeLeaf(coordinate); if (leaf is null) return; int offset = Tree.LeafNode<uint>.CoordToOffset(coordinate); if (!leaf.IsValueOn(offset)) return; var r = leaf.Range(offset); SetResults([new(leaf.IndexArray, r.Start, r.Count)]); }
    public void SearchAndUpdate(CoordBBox region, PointIndexGrid grid, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(grid); cancellationToken.ThrowIfCancellationRequested(); Clear(); List<PointIndexRange> ranges = []; PointIndexSearch.Search(grid.Tree, region, ranges, cancellationToken); SetResults(ranges); }
    public void SearchAndUpdate(BBoxd region, PointIndexGrid grid, IPointSource points, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid); ArgumentNullException.ThrowIfNull(points); cancellationToken.ThrowIfCancellationRequested(); Clear(); if (region.Min.X > region.Max.X || region.Min.Y > region.Max.Y || region.Min.Z > region.Max.Z) return;
        CoordBBox box = new(Coord.Round(region.Min), Coord.Round(region.Max)); List<PointIndexRange> ranges = []; List<uint> filtered = []; List<CoordBBox> regions = []; var size = box.Dimensions;
        if (System.Math.Min(size.X, System.Math.Min(size.Y, size.Z)) > 2) { CoordBBox interior = box.Expand(-1); PointIndexSearch.Search(grid.Tree, interior, ranges, cancellationToken); regions = PointIndexSearch.ExclusiveRegions(box, interior.Expand(1)); } else regions.Add(box);
        var filter = new PointIndexBoxFilter(ranges, filtered, points, grid.Transform, region); foreach (var r in regions) PointIndexSearch.Filter(grid.Tree, r, filter, cancellationToken); SetResults(ranges, filtered);
    }
    public void SearchAndUpdate(Vec3d center, double radius, PointIndexGrid grid, IPointSource points, bool subvoxelAccuracy = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grid); ArgumentNullException.ThrowIfNull(points); PointIndexSearch.ValidateRadius(center, radius); cancellationToken.ThrowIfCancellationRequested(); Clear();
        CoordBBox box = new(Coord.Round(center - new Vec3d(radius)), Coord.Round(center + new Vec3d(radius))); box = box.Expand(1); double innerRadius = radius * (1.0 / System.Math.Sqrt(3));
        List<PointIndexRange> ranges = []; List<uint> filtered = []; List<CoordBBox> regions = [];
        if (innerRadius > 2) { CoordBBox interior = new(Coord.Round(center - new Vec3d(innerRadius)), Coord.Round(center + new Vec3d(innerRadius))); interior = interior.Expand(-1); PointIndexSearch.Search(grid.Tree, interior, ranges, cancellationToken); regions = PointIndexSearch.ExclusiveRegions(box, interior.Expand(1)); } else regions.Add(box);
        var filter = new PointIndexRadialFilter(ranges, filtered, points, grid.Transform, center, radius, subvoxelAccuracy); foreach (var r in regions) PointIndexSearch.Filter(grid.Tree, r, filter, cancellationToken); SetResults(ranges, filtered);
    }
    public void WorldSpaceSearchAndUpdate(BBoxd region, PointIndexGrid grid, IPointSource points, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(grid); if (region.Min.X > region.Max.X || region.Min.Y > region.Max.Y || region.Min.Z > region.Max.Z) { Clear(); return; } SearchAndUpdate(new BBoxd(grid.Transform.WorldToIndex(region.Min), grid.Transform.WorldToIndex(region.Max)), grid, points, cancellationToken); }
    public void WorldSpaceSearchAndUpdate(Vec3d center, double radius, PointIndexGrid grid, IPointSource points, bool subvoxelAccuracy = true, CancellationToken cancellationToken = default)
    { ArgumentNullException.ThrowIfNull(grid); SearchAndUpdate(grid.Transform.WorldToIndex(center), radius / grid.Transform.VoxelSize.X, grid, points, subvoxelAccuracy, cancellationToken); }
}

/// <summary>Applies a world-space squared-distance/index callback to every point strictly inside a radius.</summary>
public sealed class PointIndexFilter
{
    private readonly IPointSource _points; private readonly PointIndexGrid _grid; private readonly double _inverseVoxelSize;
    public PointIndexFilter(IPointSource points, PointIndexGrid grid)
    { ArgumentNullException.ThrowIfNull(points); ArgumentNullException.ThrowIfNull(grid); _points = points; _grid = new(grid.Tree, Transform.FromMap(grid.Transform.BaseMap)); _inverseVoxelSize = 1.0 / _grid.Transform.VoxelSize.X; }
    public PointIndexFilter(PointIndexFilter other) : this(other._points, other._grid) { }
    public void SearchAndApply(Vec3d center, double radius, Action<double, uint> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation); PointIndexSearch.ValidateRadius(center, radius); var it = new PointIndexIterator();
        if (radius * _inverseVoxelSize < 8) it.SearchAndUpdate(new CoordBBox(_grid.Transform.WorldToIndexCellCentered(center - new Vec3d(radius)), _grid.Transform.WorldToIndexCellCentered(center + new Vec3d(radius))), _grid, cancellationToken);
        else it.WorldSpaceSearchAndUpdate(center, radius, _grid, _points, false, cancellationToken);
        double radiusSquared = radius * radius; foreach (uint index in it) { cancellationToken.ThrowIfCancellationRequested(); Vec3d delta = _points.GetPosition(checked((int)index)) - center; double squared = delta.LengthSquared; if (squared < radiusSquared) operation(squared, index); }
    }
}
internal static class PointIndexSearch
{
    internal static void ValidateRadius(Vec3d center, double radius) { if (!center.IsFinite || !double.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius), "Queries require finite centers and nonnegative finite radii."); }
    internal static void AddRange(List<PointIndexRange> ranges, PointIndexLeafNode leaf, int start, int count)
    { if (count == 0) return; if (ranges.Count > 0) { var last = ranges[^1]; if (ReferenceEquals(last.Data, leaf.IndexArray) && last.Start + last.Count == start) { ranges[^1] = last with { Count = last.Count + count }; return; } } ranges.Add(new(leaf.IndexArray, start, count)); }
    internal static void Search(PointIndexTree tree, CoordBBox region, List<PointIndexRange> ranges, CancellationToken token)
    {
        foreach (var leaf in tree.SearchLeaves(region, token))
        {
            CoordBBox box = leaf.Bounds.Intersection(region); if (box == leaf.Bounds) { AddRange(ranges, leaf, 0, leaf.PointCount); continue; }
            // Prefix rows span all selected Z coordinates, including empty voxels.
            for (long x = box.Min.X; x <= box.Max.X; ++x) for (long y = box.Min.Y; y <= box.Max.Y; ++y) { int offset = Tree.LeafNode<uint>.CoordToOffset(new((int)x, (int)y, box.Min.Z)); int start = offset == 0 ? 0 : (int)leaf.GetValue(offset - 1), end = (int)leaf.GetValue(offset + box.Max.Z - box.Min.Z); AddRange(ranges, leaf, start, end - start); }
        }
    }
    internal static void Filter(PointIndexTree tree, CoordBBox region, PointIndexRangeFilter filter, CancellationToken token)
    {
        filter.CancellationToken = token; foreach (var leaf in tree.SearchLeaves(region, token))
        { CoordBBox box = leaf.Bounds.Intersection(region); if (box == leaf.Bounds) filter.FilterLeaf(leaf); else foreach (Coord c in box) { var r = leaf.Range(Tree.LeafNode<uint>.CoordToOffset(c)); if (r.Count > 0) filter.FilterVoxel(leaf, c, r.Start, r.Count); } token.ThrowIfCancellationRequested(); }
    }
    internal static List<CoordBBox> ExclusiveRegions(CoordBBox box, CoordBBox inner)
    {
        Coord min = inner.Min, max = inner.Max; List<CoordBBox> regions = [new(box.Min, box.Max with { Z = min.Z }), new(box.Min with { Z = max.Z }, box.Max)];
        max = max with { Z = checked(max.Z - 1) }; min = min with { Z = checked(min.Z + 1) };
        Coord low = box.Min with { Z = min.Z }, high = box.Max with { Z = max.Z, X = min.X }; regions.Add(new(low, high));
        low = low with { X = max.X }; high = high with { X = box.Max.X }; regions.Add(new(low, high));
        max = max with { X = checked(max.X - 1) }; min = min with { X = checked(min.X + 1) };
        low = low with { X = min.X }; high = high with { X = max.X, Y = min.Y }; regions.Add(new(low, high));
        low = low with { Y = max.Y }; high = high with { Y = box.Max.Y }; regions.Add(new(low, high)); return regions;
    }
}
internal abstract class PointIndexRangeFilter(List<PointIndexRange> ranges, List<uint> filtered)
{
    protected List<PointIndexRange> Ranges { get; } = ranges; protected List<uint> Filtered { get; } = filtered;
    internal CancellationToken CancellationToken { get; set; }
    protected void CheckCancellation(int index) { if ((index & 1023) == 0) CancellationToken.ThrowIfCancellationRequested(); }
    internal virtual void FilterLeaf(PointIndexLeafNode leaf) { for (int offset = 0; offset < 512; ++offset) if (leaf.IsValueOn(offset)) { var r = leaf.Range(offset); FilterVoxel(leaf, leaf.OffsetLeaf.OffsetToGlobalCoord(offset), r.Start, r.Count); } }
    internal abstract void FilterVoxel(PointIndexLeafNode leaf, Coord coordinate, int start, int count);
}
internal sealed class PointIndexBoxFilter(List<PointIndexRange> ranges, List<uint> filtered, IPointSource points, Transform transform, BBoxd box) : PointIndexRangeFilter(ranges, filtered)
{
    internal override void FilterVoxel(PointIndexLeafNode leaf, Coord coordinate, int start, int count)
    { for (int i = start; i < start + count; ++i) { CheckCancellation(i); uint id = leaf.IndexArray[i]; if (box.Contains(transform.WorldToIndex(points.GetPosition(checked((int)id))))) Filtered.Add(id); } }
}
internal sealed class PointIndexRadialFilter : PointIndexRangeFilter
{
    private readonly IPointSource _points; private readonly Vec3d _center, _worldCenter; private readonly double _voxelOuter, _voxelInner, _leafOuter, _leafInner, _worldRadiusSquared; private readonly bool _accurate;
    internal PointIndexRadialFilter(List<PointIndexRange> ranges, List<uint> filtered, IPointSource points, Transform transform, Vec3d center, double radius, bool accurate) : base(ranges, filtered)
    { _points = points; _center = center; _worldCenter = transform.IndexToWorld(center); _accurate = accurate; double voxelRadius = System.Math.Sqrt(3) * .5, leafRadius = 8 * System.Math.Sqrt(3) * .5; _voxelOuter = Square(voxelRadius + radius); _voxelInner = radius > voxelRadius ? Square(radius - voxelRadius) : 0; _leafOuter = Square(leafRadius + radius); _leafInner = radius > leafRadius ? Square(radius - leafRadius) : 0; _worldRadiusSquared = Square(radius * transform.VoxelSize.X); }
    private static double Square(double v) => v * v;
    internal override void FilterLeaf(PointIndexLeafNode leaf)
    { double squared = (leaf.Origin.AsVec3d() + new Vec3d(3.5) - _center).LengthSquared; if (squared > _leafOuter) return; if (_leafInner > 0 && squared < _leafInner) { PointIndexSearch.AddRange(Ranges, leaf, 0, leaf.PointCount); return; } base.FilterLeaf(leaf); }
    internal override void FilterVoxel(PointIndexLeafNode leaf, Coord coordinate, int start, int count)
    { double squared = (_center - coordinate.AsVec3d()).LengthSquared; if (squared > _voxelOuter) return; if (!_accurate || (_voxelInner > 0 && squared < _voxelInner)) { PointIndexSearch.AddRange(Ranges, leaf, start, count); return; } for (int i = start; i < start + count; ++i) { CheckCancellation(i); uint id = leaf.IndexArray[i]; if ((_worldCenter - _points.GetPosition(checked((int)id))).LengthSquared < _worldRadiusSquared) Filtered.Add(id); } }
}
