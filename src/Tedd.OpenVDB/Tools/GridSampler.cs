// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Samples a captured tree and transform in integer, fractional index, or world space.</summary>
/// <remarks>The grid/tree constructor permits concurrent reads. The accessor constructor requires a separate
/// accessor per thread. Replacing the grid's tree or transform does not retarget an existing sampler.</remarks>
public sealed class GridSampler<T> where T : unmanaged
{
    private readonly VdbTree<T> _tree;
    private readonly ValueAccessor<T>? _accessor;
    public GridSampler(Grid<T> grid, SamplingOrder order = SamplingOrder.Box, bool staggered = false)
        : this((grid ?? throw new ArgumentNullException(nameof(grid))).Tree, grid.Transform, order, staggered) { }
    public GridSampler(VdbTree<T> tree, Transform transform, SamplingOrder order = SamplingOrder.Box, bool staggered = false)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree)); Transform = transform ?? throw new ArgumentNullException(nameof(transform));
        SamplingKernel<T, TreeSampleSource<T>>.Validate(order, staggered); Order = order; Staggered = staggered;
    }
    public GridSampler(ValueAccessor<T> accessor, Transform transform, SamplingOrder order = SamplingOrder.Box, bool staggered = false)
        : this((accessor ?? throw new ArgumentNullException(nameof(accessor))).Tree, transform, order, staggered) => _accessor = accessor;
    public Transform Transform { get; }
    public SamplingOrder Order { get; }
    public bool Staggered { get; }
    /// <summary>Integer coordinates return the stored value directly, including for staggered samplers.</summary>
    public T IndexSample(Coord coordinate) { IndexSample(coordinate, out T value); return value; }
    public bool IndexSample(Coord coordinate, out T value) => _accessor is null ? _tree.ProbeValue(coordinate, out value) : _accessor.ProbeValue(coordinate, out value);
    public T IndexSample(Vec3d position) { IndexSample(position, out T value); return value; }
    public bool IndexSample(Vec3d position, out T value) => _accessor is null
        ? SamplingKernel<T, TreeSampleSource<T>>.Sample(new(_tree), position, Order, Staggered, out value)
        : SamplingKernel<T, AccessorSampleSource<T>>.Sample(new(_accessor), position, Order, Staggered, out value);
    public T WorldSample(Vec3d position) => IndexSample(Transform.WorldToIndex(position));
    public bool WorldSample(Vec3d position, out T value) => IndexSample(Transform.WorldToIndex(position), out value);
    public T SampleVoxel(int x, int y, int z) => IndexSample(new Coord(x, y, z));
    public T SampleVoxel(double x, double y, double z) => IndexSample(new Vec3d(x, y, z));
}

/// <summary>Samples a source grid using target index coordinates, with the native aligned-grid fast path.</summary>
/// <remarks>Alignment is tested once at construction. Later transform edits do not update IsAligned.</remarks>
public sealed class DualGridSampler<T> where T : unmanaged
{
    private readonly GridSampler<T> _source;
    public DualGridSampler(Grid<T> source, Transform targetTransform, SamplingOrder order = SamplingOrder.Box, bool staggered = false)
        : this((source ?? throw new ArgumentNullException(nameof(source))).Tree, source.Transform, targetTransform, order, staggered) { }
    public DualGridSampler(VdbTree<T> tree, Transform sourceTransform, Transform targetTransform, SamplingOrder order = SamplingOrder.Box, bool staggered = false)
        : this(new GridSampler<T>(tree, sourceTransform, order, staggered), targetTransform) { }
    public DualGridSampler(ValueAccessor<T> accessor, Transform sourceTransform, Transform targetTransform, SamplingOrder order = SamplingOrder.Box, bool staggered = false)
        : this(new GridSampler<T>(accessor, sourceTransform, order, staggered), targetTransform) { }
    private DualGridSampler(GridSampler<T> source, Transform targetTransform)
    {
        _source = source; TargetTransform = targetTransform ?? throw new ArgumentNullException(nameof(targetTransform));
        IsAligned = source.Transform.IsEquivalentTo(targetTransform);
    }
    public Transform SourceTransform => _source.Transform;
    public Transform TargetTransform { get; }
    public bool IsAligned { get; }
    public T Sample(Coord coordinate) { Sample(coordinate, out T value); return value; }
    public bool Sample(Coord coordinate, out T value) => IsAligned ? _source.IndexSample(coordinate, out value) : _source.WorldSample(TargetTransform.IndexToWorld(coordinate), out value);
    public T this[Coord coordinate] => Sample(coordinate);
}
