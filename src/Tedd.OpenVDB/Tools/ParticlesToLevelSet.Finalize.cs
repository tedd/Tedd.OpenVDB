// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Attribute extraction/finalization from tools/ParticlesToLevelSet.h.
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

internal sealed partial class ParticleRasterizer<T, TAttribute>
{
    internal void Finalize(bool prune, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_blind is null) { if (prune) PruneOutput(Grid.Tree); return; }
        if (prune) Prune.Apply(_blind.Tree);
        var attributes = CopyTopology(_blind.Tree, default(TAttribute), default(TAttribute));
        T background = _blind.Background.Visible;
        var values = CopyTopology(_blind.Tree, typeof(T) == typeof(bool) ? default : background, typeof(T) == typeof(bool) ? ParticleScalar<T>.FromDouble(1) : background);
        foreach (var source in _blind.Tree.EnumerateLeaves())
        {
            token.ThrowIfCancellationRequested(); var output = values.ProbeLeaf(source.Origin)!; var attribute = attributes.ProbeLeaf(source.Origin)!;
            if (typeof(T) != typeof(bool) && source.IsEmpty)
            {
                for (int offset = 0; offset < LeafNode<T>.Size; ++offset) { var value = source.GetValue(offset); output.SetValueOnly(offset, value.Visible); attribute.SetValueOnly(offset, value.Attribute); }
            }
            else foreach (int offset in source.ActiveOffsets)
            {
                var value = source.GetValue(offset); if (typeof(T) != typeof(bool)) output.SetValueOnly(offset, value.Visible); attribute.SetValueOnly(offset, value.Attribute);
            }
        }
        if (typeof(T) == typeof(bool))
        {
            foreach (var value in _blind.Tree.EnumerateValues()) if (!value.IsVoxel) attributes.AddTile(value.Level, value.Coordinate, value.Value.Attribute, true);
        }
        else FloodFill(values);
        token.ThrowIfCancellationRequested();
        var attributeGrid = new Grid<TAttribute>(attributes) { Transform = _blind.Transform.Copy() };
        if (Grid.IsEmpty) Grid.SetTree(values);
        else if (typeof(T) == typeof(bool)) { Grid.Tree.TopologyUnion(values); Prune.Apply(Grid.Tree); }
        else Union(Grid.Tree, values);
        AttributeGrid = attributeGrid;
    }
    private static void PruneOutput(VdbTree<T> tree)
    {
        if (typeof(T) == typeof(float)) Prune.PruneLevelSet((VdbTree<float>)(object)tree);
        else if (typeof(T) == typeof(double)) Prune.PruneLevelSet((VdbTree<double>)(object)tree);
        else Prune.Apply(tree);
    }
    private static void FloodFill(VdbTree<T> tree)
    {
        if (typeof(T) == typeof(float)) SignedFloodFill.Apply((VdbTree<float>)(object)tree);
        else SignedFloodFill.Apply((VdbTree<double>)(object)tree);
    }
    private static void Union(VdbTree<T> left, VdbTree<T> right)
    {
        if (typeof(T) == typeof(float)) Composite.CsgUnion((VdbTree<float>)(object)left, (VdbTree<float>)(object)right);
        else Composite.CsgUnion((VdbTree<double>)(object)left, (VdbTree<double>)(object)right);
    }
    private static VdbTree<TOut> CopyTopology<TIn, TOut>(VdbTree<TIn> input, TOut off, TOut on) where TIn : unmanaged where TOut : unmanaged
    {
        var result = new VdbTree<TOut>(off);
        foreach (var (origin, entry) in input.Entries) result.Entries.Add(origin, entry.Child is null ? new(entry.Active ? on : off, entry.Active) : new(default, false, CopyNode(entry.Child)));
        return result;
        InternalNode<TOut> CopyNode(InternalNode<TIn> source)
        {
            var target = new InternalNode<TOut>(source.Level, source.Origin, off, false); target.Active.OrWith(source.Active);
            foreach (int offset in source.Active.OnIndices) target.Values[offset] = on;
            foreach (var (offset, child) in source.Children)
            {
                if (child is InternalNode<TIn> node) target.AddChild(offset, CopyNode(node));
                else { var leaf = (LeafNode<TIn>)child; var output = new LeafNode<TOut>(leaf.Origin, off); foreach (int position in leaf.ActiveOffsets) output.SetValueOn(position, on); target.AddChild(offset, output); }
            }
            return target;
        }
    }
}
/// <summary>Native convenience entry points that rasterize then prune particle fields.</summary>
public static class ParticleConversion
{
    public static void ParticlesToSdf<T>(IParticleSource particles, Grid<T> grid, CancellationToken cancellationToken = default) where T : unmanaged, System.Numerics.IFloatingPointIeee754<T>
    { var raster = new ParticlesToLevelSet<T>(grid); raster.RasterizeSpheres(particles, cancellationToken); raster.Finalize(true, cancellationToken); }
    public static void ParticlesToSdf<T>(IPointSource points, Grid<T> grid, double radius, CancellationToken cancellationToken = default) where T : unmanaged, System.Numerics.IFloatingPointIeee754<T>
    { var raster = new ParticlesToLevelSet<T>(grid); raster.RasterizeSpheres(points, radius, cancellationToken); raster.Finalize(true, cancellationToken); }
    public static void ParticleTrailsToSdf<T>(IParticleSource particles, Grid<T> grid, double delta = 1, CancellationToken cancellationToken = default) where T : unmanaged, System.Numerics.IFloatingPointIeee754<T>
    { var raster = new ParticlesToLevelSet<T>(grid); raster.RasterizeTrails(particles, delta, cancellationToken); raster.Finalize(true, cancellationToken); }
    public static void ParticlesToMask(IParticleSource particles, Grid<bool> grid, CancellationToken cancellationToken = default)
    { var raster = new ParticlesToLevelSet<bool>(grid); raster.RasterizeSpheres(particles, cancellationToken); raster.Finalize(true, cancellationToken); }
    public static void ParticlesToMask(IPointSource points, Grid<bool> grid, double radius, CancellationToken cancellationToken = default)
    { var raster = new ParticlesToLevelSet<bool>(grid); raster.RasterizeSpheres(points, radius, cancellationToken); raster.Finalize(true, cancellationToken); }
    public static void ParticleTrailsToMask(IParticleSource particles, Grid<bool> grid, double delta = 1, CancellationToken cancellationToken = default)
    { var raster = new ParticlesToLevelSet<bool>(grid); raster.RasterizeTrails(particles, delta, cancellationToken); raster.Finalize(true, cancellationToken); }
}
