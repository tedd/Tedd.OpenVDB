// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/TopologyToLevelSet.h.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Builds a float signed-distance narrow band around arbitrary input active topology.</summary>
public static class TopologyToLevelSet
{
    /// <summary>Applies morphological closing/dilation and propagates distance from the resulting active/inactive interface.</summary>
    /// <remarks>Input values and classification do not affect the interface. Widths/counts are clamped as in native code; only uniform voxel transforms are accepted. The output owns its tree and transform and does not copy input metadata. MaximumNewLeaves bounds explicit active-tile voxelization; it does not bound preexisting input leaves or normalization work.</remarks>
    public static Grid<float> Create<T>(Grid<T> grid, int halfWidth = 3, int closingSteps = 1, int dilation = 0,
        int smoothingSteps = 0, ulong maximumNewLeaves = ulong.MaxValue, CancellationToken cancellationToken = default) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(grid); cancellationToken.ThrowIfCancellationRequested();
        halfWidth = System.Math.Max(halfWidth, 1); closingSteps = System.Math.Max(closingSteps, 0); dilation = System.Math.Max(dilation, 0);
        int normalizationCount = checked(3 * halfWidth), expansion = checked(closingSteps + dilation);
        if (!grid.Transform.HasUniformScale) throw new ArgumentException("Topology-to-level-set conversion requires uniform voxels.", nameof(grid));
        var mask = CopyTopology<T, bool>(grid.Tree, false, false);
        Morphology.DilateActiveValues(mask, expansion, NearestNeighbors.Face, TilePolicy.PreserveTiles);
        cancellationToken.ThrowIfCancellationRequested();
        Morphology.ErodeActiveValues(mask, closingSteps, NearestNeighbors.Face, TilePolicy.PreserveTiles);
        Prune.PruneInactive(mask);
        float background = (float)grid.Transform.VoxelSize.X * halfWidth;
        var distance = CopyTopology<bool, float>(mask, background, -background);
        cancellationToken.ThrowIfCancellationRequested();
        Morphology.ErodeActiveValues(mask, halfWidth, NearestNeighbors.Face, TilePolicy.PreserveTiles);
        Prune.PruneInactive(mask);
        cancellationToken.ThrowIfCancellationRequested();
        Morphology.DilateActiveValues(distance, halfWidth, NearestNeighbors.Face, TilePolicy.PreserveTiles);
        distance.TopologyDifference(mask);
        cancellationToken.ThrowIfCancellationRequested();
        distance.VoxelizeActiveTiles(maximumNewLeaves);
        Prune.PruneLevelSet(distance);
        var result = new Grid<float>(distance) { Transform = grid.Transform.Copy(), GridClass = GridClass.LevelSet };
        Normalize(result, normalizationCount, cancellationToken);
        if (smoothingSteps > 0) Smooth(result, smoothingSteps, halfWidth, cancellationToken);
        return result;
    }
    private static void Normalize(Grid<float> grid, int count, CancellationToken token)
    {
        var filter = new LevelSetFilter<float>(grid) { SpatialScheme = BiasedGradientScheme.First, NormalizationCount = count };
        filter.Normalize(token); filter.Prune(token);
    }
    private static void Smooth(Grid<float> grid, int iterations, int width, CancellationToken token)
    {
        var filtered = grid.DeepCopy(); var filter = new LevelSetFilter<float>(filtered) { SpatialScheme = BiasedGradientScheme.First };
        for (int n = 0; n < iterations; ++n) { token.ThrowIfCancellationRequested(); filter.Mean(1, cancellationToken: token); }
        float offset = -(float)(.5 * grid.Transform.VoxelSize.X);
        foreach (var leaf in grid.Tree.EnumerateLeaves())
        {
            token.ThrowIfCancellationRequested(); var other = filtered.Tree.ProbeLeaf(leaf.Origin); if (other is null) continue;
            foreach (int position in leaf.ActiveOffsets)
            {
                float original = leaf.GetValue(position), candidate = offset + other.GetValue(position);
                leaf.SetValueOnly(position, candidate < original ? candidate : original);
            }
        }
        Normalize(grid, width, token);
    }
    private static VdbTree<TOut> CopyTopology<TIn, TOut>(VdbTree<TIn> input, TOut background, TOut foreground)
        where TIn : unmanaged where TOut : unmanaged
    {
        var result = new VdbTree<TOut>(background);
        foreach (var (origin, entry) in input.Entries)
            result.Entries.Add(origin, entry.Child is null ? new(entry.Active ? foreground : background, entry.Active) : new(default, false, CopyNode(entry.Child)));
        return result;

        InternalNode<TOut> CopyNode(InternalNode<TIn> source)
        {
            var target = new InternalNode<TOut>(source.Level, source.Origin, background, false);
            target.Active.OrWith(source.Active);
            foreach (int offset in source.Active.OnIndices) target.Values[offset] = foreground;
            foreach (var (offset, child) in source.Children)
            {
                if (child is InternalNode<TIn> node) target.AddChild(offset, CopyNode(node));
                else
                {
                    var sourceLeaf = (LeafNode<TIn>)child;
                    var targetLeaf = new LeafNode<TOut>(sourceLeaf.Origin, background);
                    foreach (int position in sourceLeaf.ActiveOffsets) targetLeaf.SetValueOn(position, foreground);
                    target.AddChild(offset, targetLeaf);
                }
            }
            return target;
        }
    }
}
