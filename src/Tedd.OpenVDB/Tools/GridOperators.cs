// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/GridOperators.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Execution and densification controls for grid operators.</summary>
public sealed record GridOperatorOptions
{
    public bool Threaded { get; init; } = true;
    /// <summary>Maximum newly materialized leaves for operators that densify active tiles.</summary>
    public ulong MaximumNewLeaves { get; init; } = ulong.MaxValue;
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>Creates derived grids with native active topology, background and map semantics.</summary>
/// <remarks>Input grids remain unchanged. Masks intersect index-space activity, ignoring mask transforms.
/// Differential operations voxelize active tiles then prune; magnitude, normalization and closest-point
/// operations retain tiles and evaluate each tile at its origin, as native OpenVDB does.
/// Output grids have independent transforms and operator metadata; input names/classes are not copied.
/// Inputs and transforms must remain unchanged during execution.</remarks>
public static class GridOperators
{
    public static Grid<Vec3f> Gradient(Grid<float> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<float, Vec3f, GradientOperation<float, Vec3f>>(grid, mask, options, new GradientOperation<float, Vec3f>(), true);
        result.SetMetadata("vector_type", "covariant");
        return result;
    }
    public static Grid<float> Laplacian(Grid<float> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<float, float, LaplacianOperation<float>>(grid, mask, options, new LaplacianOperation<float>(), true);
        result.SetMetadata("vector_type", "covariant");
        return result;
    }
    public static Grid<float> MeanCurvature(Grid<float> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<float, float, MeanCurvatureOperation<float>>(grid, mask, options, new MeanCurvatureOperation<float>(), true);
        result.SetMetadata("vector_type", "covariant");
        return result;
    }
    public static Grid<float> Divergence(Grid<Vec3f> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var result = Process<Vec3f, float, DivergenceOperation<Vec3f, float>>(grid, mask, options, new DivergenceOperation<Vec3f, float>(grid.GridClass == GridClass.Staggered ? DScheme.ForwardFirst : DScheme.CenteredSecond), true);
        return result;
    }
    public static Grid<Vec3f> Curl(Grid<Vec3f> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<Vec3f, Vec3f, CurlOperation<Vec3f, float>>(grid, mask, options, new CurlOperation<Vec3f, float>(), true);
        result.SetMetadata("vector_type", "covariant");
        return result;
    }
    public static Grid<float> Magnitude(Grid<Vec3f> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<Vec3f, float, MagnitudeOperation<Vec3f, float>>(grid, mask, options, new MagnitudeOperation<Vec3f, float>(), false);
        return result;
    }
    public static Grid<Vec3f> Normalize(Grid<Vec3f> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<Vec3f, Vec3f, NormalizeOperation<Vec3f, float>>(grid, mask, options, new NormalizeOperation<Vec3f, float>(), false);
        string vectorType = grid.TryGetMetadata<string>("vector_type", out var text) ? text.Trim().ToLowerInvariant() : "invariant";
        if (vectorType is not ("invariant" or "covariant" or "covariant normalize" or "contravariant relative" or "contravariant absolute")) vectorType = "invariant";
        result.SetMetadata("vector_type", vectorType == "covariant" ? "covariant normalize" : vectorType);
        return result;
    }
    public static Grid<Vec3f> ClosestPoint(Grid<float> grid, bool worldSpace = true, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<float, Vec3f, ClosestPointOperation<float, Vec3f>>(grid, mask, options, new ClosestPointOperation<float, Vec3f>(worldSpace), false);
        result.SetMetadata("vector_type", "contravariant absolute"); return result;
    }
    public static Grid<Vec3d> Gradient(Grid<double> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<double, Vec3d, GradientOperation<double, Vec3d>>(grid, mask, options, new GradientOperation<double, Vec3d>(), true);
        result.SetMetadata("vector_type", "covariant");
        return result;
    }
    public static Grid<double> Laplacian(Grid<double> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<double, double, LaplacianOperation<double>>(grid, mask, options, new LaplacianOperation<double>(), true);
        result.SetMetadata("vector_type", "covariant");
        return result;
    }
    public static Grid<double> MeanCurvature(Grid<double> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<double, double, MeanCurvatureOperation<double>>(grid, mask, options, new MeanCurvatureOperation<double>(), true);
        result.SetMetadata("vector_type", "covariant");
        return result;
    }
    public static Grid<double> Divergence(Grid<Vec3d> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var result = Process<Vec3d, double, DivergenceOperation<Vec3d, double>>(grid, mask, options, new DivergenceOperation<Vec3d, double>(grid.GridClass == GridClass.Staggered ? DScheme.ForwardFirst : DScheme.CenteredSecond), true);
        return result;
    }
    public static Grid<Vec3d> Curl(Grid<Vec3d> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<Vec3d, Vec3d, CurlOperation<Vec3d, double>>(grid, mask, options, new CurlOperation<Vec3d, double>(), true);
        result.SetMetadata("vector_type", "covariant");
        return result;
    }
    public static Grid<double> Magnitude(Grid<Vec3d> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<Vec3d, double, MagnitudeOperation<Vec3d, double>>(grid, mask, options, new MagnitudeOperation<Vec3d, double>(), false);
        return result;
    }
    public static Grid<Vec3d> Normalize(Grid<Vec3d> grid, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<Vec3d, Vec3d, NormalizeOperation<Vec3d, double>>(grid, mask, options, new NormalizeOperation<Vec3d, double>(), false);
        string vectorType = grid.TryGetMetadata<string>("vector_type", out var text) ? text.Trim().ToLowerInvariant() : "invariant";
        if (vectorType is not ("invariant" or "covariant" or "covariant normalize" or "contravariant relative" or "contravariant absolute")) vectorType = "invariant";
        result.SetMetadata("vector_type", vectorType == "covariant" ? "covariant normalize" : vectorType);
        return result;
    }
    public static Grid<Vec3d> ClosestPoint(Grid<double> grid, bool worldSpace = true, MaskGrid? mask = null, GridOperatorOptions? options = null)
    {
        var result = Process<double, Vec3d, ClosestPointOperation<double, Vec3d>>(grid, mask, options, new ClosestPointOperation<double, Vec3d>(worldSpace), false);
        result.SetMetadata("vector_type", "contravariant absolute"); return result;
    }

    private static Grid<O> Process<I, O, P>(Grid<I> grid, MaskGrid? mask, GridOperatorOptions? options, P operation, bool densify)
        where I : unmanaged where O : unmanaged where P : struct, IGridPointOperation<I, O>
    {
        ArgumentNullException.ThrowIfNull(grid); options ??= new();
        var cancellation = options.CancellationToken; cancellation.ThrowIfCancellationRequested();
        Transform transform = grid.Transform.Copy();
        O background = operation.Evaluate(new VdbTree<I>(grid.Background).GetAccessor(), Coord.Zero, transform);
        var tree = new VdbTree<O>(background);
        // Native TopologyCopy retains inactive root entries and all allocated child topology.
        foreach (var (coordinate, entry) in grid.Tree.Entries)
            tree.Entries.Add(coordinate, entry.Child is null ? new(background, entry.Active) :
                new(default, false, InternalNode<O>.CopyTopology(entry.Child, background)));
        if (densify) tree.VoxelizeActiveTiles(options.MaximumNewLeaves);
        if (mask is not null) tree.TopologyIntersection(mask.Storage.Tree);
        var result = new Grid<O>(tree) { Transform = transform };
        var leaves = tree.EnumerateLeaves().ToArray();
        void ProcessLeaf(LeafNode<O> leaf, ValueAccessor<I> accessor)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (int offset in leaf.ActiveOffsets)
                leaf.SetValueOnly(offset, operation.Evaluate(accessor, leaf.OffsetToGlobalCoord(offset), transform));
        }
        if (options.Threaded && leaves.Length > 1)
            Parallel.ForEach(leaves, new ParallelOptions { CancellationToken = cancellation },
                grid.GetAccessor, (leaf, _, accessor) => { ProcessLeaf(leaf, accessor); return accessor; }, static _ => { });
        else
        {
            var accessor = grid.GetAccessor(); foreach (var leaf in leaves) ProcessLeaf(leaf, accessor);
        }
        if (!densify)
        {
            var accessor = grid.GetAccessor();
            foreach (var value in tree.EnumerateValueHandles(ValueIterationMode.Active, 0, 2))
            {
                cancellation.ThrowIfCancellationRequested();
                value.SetValueOnly(operation.Evaluate(accessor, value.Coordinate, transform));
            }
        }
        if (densify) tree.Prune();
        return result;
    }
}
internal interface IGridPointOperation<I, O> where I : unmanaged where O : unmanaged
{
    O Evaluate(ValueAccessor<I> accessor, Coord coordinate, Transform transform);
}
internal readonly struct GradientOperation<T, V> : IGridPointOperation<T, V> where T : unmanaged, IFloatingPointIeee754<T> where V : unmanaged
{
    public V Evaluate(ValueAccessor<T> accessor, Coord coordinate, Transform transform) => VectorComponents<V, T>.Create(DifferentialOperators.Gradient(transform, accessor, coordinate));
}
internal readonly struct LaplacianOperation<T> : IGridPointOperation<T, T> where T : unmanaged, IFloatingPointIeee754<T>
{
    public T Evaluate(ValueAccessor<T> accessor, Coord coordinate, Transform transform) => DifferentialOperators.Laplacian(transform, accessor, coordinate);
}
internal readonly struct MeanCurvatureOperation<T> : IGridPointOperation<T, T> where T : unmanaged, IFloatingPointIeee754<T>
{
    public T Evaluate(ValueAccessor<T> accessor, Coord coordinate, Transform transform) => DifferentialOperators.MeanCurvature(transform, accessor, coordinate);
}
internal readonly struct ClosestPointOperation<T, V>(bool worldSpace) : IGridPointOperation<T, V> where T : unmanaged, IFloatingPointIeee754<T> where V : unmanaged
{
    public V Evaluate(ValueAccessor<T> accessor, Coord coordinate, Transform transform) => VectorComponents<V, T>.Create(DifferentialOperators.ClosestPoint(transform, accessor, coordinate, worldSpace));
}
internal readonly struct DivergenceOperation<V, T>(DScheme scheme) : IGridPointOperation<V, T> where T : unmanaged, IFloatingPointIeee754<T> where V : unmanaged
{
    public T Evaluate(ValueAccessor<V> accessor, Coord coordinate, Transform transform) => DifferentialOperators.DivergenceCore<V, T>(accessor, coordinate, transform, scheme);
}
internal readonly struct CurlOperation<V, T> : IGridPointOperation<V, V> where T : unmanaged, IFloatingPointIeee754<T> where V : unmanaged
{
    public V Evaluate(ValueAccessor<V> accessor, Coord coordinate, Transform transform) => VectorComponents<V, T>.Create(DifferentialOperators.CurlCore<V, T>(accessor, coordinate, transform, DScheme.CenteredSecond));
}
internal readonly struct MagnitudeOperation<V, T> : IGridPointOperation<V, T> where T : unmanaged, IFloatingPointIeee754<T> where V : unmanaged
{
    public T Evaluate(ValueAccessor<V> accessor, Coord coordinate, Transform transform) => VectorComponents<V, T>.Magnitude(accessor.GetValue(coordinate));
}
internal readonly struct NormalizeOperation<V, T> : IGridPointOperation<V, V> where T : unmanaged, IFloatingPointIeee754<T> where V : unmanaged
{
    public V Evaluate(ValueAccessor<V> accessor, Coord coordinate, Transform transform) => VectorComponents<V, T>.Normalize(accessor.GetValue(coordinate));
}
