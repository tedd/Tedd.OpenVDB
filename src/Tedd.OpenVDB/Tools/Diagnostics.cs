// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/Diagnostics.h.
using System.Text;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Accumulates failed diagnostic counts and a sparse Boolean mask.</summary>
/// <remarks>A failed tile increments FailureCount once but contributes its entire volume to
/// ValueCount. Background failures increment only FailureCount. Counts include repeated failures
/// across checks; the mask is their union. Clear follows the source intent of a new identity-transform mask; the native clear template is ill-formed.</remarks>
public sealed class Diagnose<T> where T : unmanaged
{
    public Diagnose(Grid<T> grid) { Grid = grid ?? throw new ArgumentNullException(nameof(grid)); Mask = new Grid<bool> { Transform = grid.Transform.Copy() }; }
    public Grid<T> Grid { get; }
    public Grid<bool> Mask { get; private set; }
    public ulong FailureCount { get; private set; }
    public ulong ValueCount => Mask.ActiveVoxelCount;
    public void Clear() { Mask = new Grid<bool>(); FailureCount = 0; }
    /// <summary>Checks selected stored values. Cancellation leaves prior counts and masks intact.</summary>
    public string Check(DiagnosticCheck<T> check, bool updateMask = false, bool checkVoxels = true, bool checkTiles = true, bool checkBackground = true, bool parallel = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(check); cancellationToken.ThrowIfCancellationRequested();
        DiagnosticCheck<T> predicate = check.Copy(); var message = new StringBuilder(); ulong count = 0;
        VdbTree<bool>? candidate = updateMask ? new VdbTree<bool>() : null;
        if (checkBackground && predicate.Fails(Grid.Background)) { ++count; message.Append("Background is ").Append(predicate.Description).Append('\n'); }
        if (checkTiles)
        {
            ulong tiles = 0;
            foreach (var tile in DiagnosticTraversal<T>.Tiles(Grid.Tree.Root, predicate.IterationMode))
            {
                cancellationToken.ThrowIfCancellationRequested(); if (!predicate.FailsTile(tile.Bounds, tile.Value)) continue;
                tiles = checked(tiles + 1); candidate?.Fill(tile.Bounds, true, true);
            }
            count = checked(count + tiles); AppendFailures(message, tiles, "tile", predicate.Description);
        }
        if (checkVoxels)
        {
            var leaves = Grid.Tree.EnumerateLeaves().ToArray(); var counts = new ulong[leaves.Length]; var masks = updateMask ? new LeafNode<bool>?[leaves.Length] : null;
            void CheckLeaf(int index)
            {
                cancellationToken.ThrowIfCancellationRequested(); var leaf = leaves[index]; var local = predicate.Copy(); ulong failures = 0; LeafNode<bool>? mask = null;
                if (predicate.IterationMode == ValueIterationMode.Active)
                {
                    foreach (int offset in leaf.ActiveOffsets) Inspect(offset);
                }
                else for (int offset = 0; offset < LeafNode<T>.Size; ++offset) if (predicate.IterationMode == ValueIterationMode.All || !leaf.IsValueOn(offset)) Inspect(offset);
                counts[index] = failures; if (masks is not null) masks[index] = mask;
                void Inspect(int offset)
                {
                    if (!local.FailsVoxel(leaf.OffsetToGlobalCoord(offset), leaf.GetValue(offset))) return;
                    ++failures; if (updateMask) { mask ??= new LeafNode<bool>(leaf.Origin); mask.SetValueOn(offset, true); }
                }
            }
            if (parallel && leaves.Length > 1) Parallel.For(0, leaves.Length, new ParallelOptions { CancellationToken = cancellationToken }, CheckLeaf);
            else for (int i = 0; i < leaves.Length; ++i) CheckLeaf(i);
            ulong voxels = 0; for (int i = 0; i < counts.Length; ++i) { voxels = checked(voxels + counts[i]); if (masks?[i] is { } leaf) candidate!.AddLeaf(leaf); }
            count = checked(count + voxels); AppendFailures(message, voxels, "voxel", predicate.Description);
        }
        cancellationToken.ThrowIfCancellationRequested(); ulong total = checked(FailureCount + count);
        if (candidate is not null) Mask.Tree.Merge(candidate, MergePolicy.ActiveStatesAndNodes);
        FailureCount = total; return message.ToString();
    }
    private static void AppendFailures(StringBuilder text, ulong count, string name, string description)
    {
        if (count != 0) text.Append(count).Append(' ').Append(name).Append(count == 1 ? " is " : "s are ").Append(description).Append('\n');
    }
}

internal static class DiagnosticTraversal<T> where T : unmanaged
{
    internal static IEnumerable<(CoordBBox Bounds, T Value)> Tiles(TreeNode<T> node, ValueIterationMode mode)
    {
        foreach (var value in node.EnumerateValues(mode)) yield return (value.Bounds, value.Value);
        if (node.Level <= 1) yield break;
        foreach (var child in node.EnumerateChildren()) foreach (var value in Tiles(child, mode)) yield return value;
    }
}
