// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Coarse sparse-volume ray intervals at leaf/tile resolution, with optional voxel dilation.</summary>
/// <remarks>Construction snapshots topology; later grid value edits do not change that snapshot.
/// Instances carry mutable ray state. Copy the intersector for each worker; copies share only the topology snapshot.</remarks>
public sealed class VolumeRayIntersector<T> where T : unmanaged
{
    private readonly VdbTree<bool> _topology;
    private Rayd _ray;
    private double _maxTime;
    private bool _hasRay;
    public VolumeRayIntersector(Grid<T> grid, int dilationCount = 0, int nodeLevel = 2)
    {
        Validate(grid, nodeLevel); Grid = grid; NodeLevel = nodeLevel;
        _topology = new(); _topology.TopologyUnion(grid.Tree, preserveTiles: true);
        Morphology.DilateActiveValues(_topology, dilationCount, NearestNeighbors.Face, TilePolicy.IgnoreTiles);
        _topology.TryGetLeafBoundingBox(out CoordBBox bounds);
        BoundingBox = bounds.IsEmpty ? BBoxd.Empty : new BBoxd(bounds.Min.AsVec3d(), bounds.Max.AsVec3d() + Vec3d.One);
    }
    /// <summary>Uses explicitly supplied index bounds, including the upper node face (origin + dimension).</summary>
    public VolumeRayIntersector(Grid<T> grid, BBoxd boundingBox, int nodeLevel = 2)
    {
        Validate(grid, nodeLevel); if (!boundingBox.IsValid || !boundingBox.Min.IsFinite || !boundingBox.Max.IsFinite) throw new ArgumentException("Bounds must be finite and ordered.", nameof(boundingBox));
        Grid = grid; NodeLevel = nodeLevel; BoundingBox = boundingBox; _topology = new(); _topology.TopologyUnion(grid.Tree, preserveTiles: true);
    }
    public VolumeRayIntersector(VolumeRayIntersector<T> source)
    {
        ArgumentNullException.ThrowIfNull(source); Grid = source.Grid; NodeLevel = source.NodeLevel; BoundingBox = source.BoundingBox;
        _topology = source._topology; _ray = source._ray; _maxTime = source._maxTime; _hasRay = source._hasRay;
    }
    public Grid<T> Grid { get; }
    public int NodeLevel { get; }
    public BBoxd BoundingBox { get; }
    public VdbTree<bool> CopyTopology() => _topology.DeepCopy();
    public bool SetIndexRay(Rayd ray)
    {
        _hasRay = ray.TryClip(BoundingBox, out _ray);
        if (_hasRay) _maxTime = _ray.MaxTime;
        return _hasRay;
    }
    /// <summary>Transforms a normalized world ray; subsequent hit times remain in index-ray units.</summary>
    public bool SetWorldRay(Rayd ray) => SetIndexRay(ray.WorldToIndex(Grid.Transform));
    public RayTimeSpan March()
    {
        if (!_hasRay) return RayTimeSpan.Empty;
        RayTimeSpan span = VolumeHdda.March(_topology, ref _ray, NodeLevel);
        if (span.End > 0)
        {
            double next = span.End + 1e-9; if (next <= span.End) next = double.BitIncrement(span.End);
            _ray = new(_ray.Eye, _ray.Direction, next, _maxTime);
        }
        return span;
    }
    public bool March(out double enter, out double exit)
    { var span = March(); enter = span.Start; exit = span.End; return span.IsValid(); }
    public void Hits(ICollection<RayTimeSpan> destination)
    {
        ArgumentNullException.ThrowIfNull(destination); if (!_hasRay) { destination.Clear(); return; }
        VolumeHdda.Hits(_topology, ref _ray, destination, NodeLevel);
    }
    public Vec3d GetIndexPosition(double time) { RequireRay(); return _ray.At(time); }
    public Vec3d GetWorldPosition(double time) => Grid.IndexToWorld(GetIndexPosition(time));
    public double GetWorldTime(double indexTime) { RequireRay(); return indexTime * Grid.Transform.ApplyJacobian(_ray.Direction).Length; }
    private void RequireRay() { if (!_hasRay) throw new InvalidOperationException("Set a ray that intersects the bounds before querying its position."); }
    private static void Validate(Grid<T> grid, int nodeLevel)
    {
        ArgumentNullException.ThrowIfNull(grid); if ((uint)nodeLevel > 2) throw new ArgumentOutOfRangeException(nameof(nodeLevel));
        if (!grid.Transform.HasUniformScale) throw new ArgumentException("Volume intersection requires uniform voxels.", nameof(grid));
        if (grid.IsEmpty) throw new ArgumentException("Volume intersection requires a nonempty grid.", nameof(grid));
    }
}
