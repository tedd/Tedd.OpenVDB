// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Math;

/// <summary>An index-ray parameter interval; validity follows native double-precision TimeSpan.</summary>
public readonly record struct RayTimeSpan(double Start, double End)
{
    public static RayTimeSpan Empty => new(-1, -1);
    public bool IsValid(double epsilon = 1e-9) => End - Start > epsilon;
    public double Midpoint => 0.5 * (Start + End);
    public bool Contains(double time) => time >= Start && time <= End;
}

/// <summary>Hierarchical traversal of allocated leaves and active tiles, stopping at leaf-sized blocks.</summary>
/// <remarks>Allocated leaves count as hits even when all their voxels are inactive. The ray's interval
/// changes during recursion, matching native VolumeHDDA. The tree must remain unchanged during a query.</remarks>
public static class VolumeHdda
{
    public static RayTimeSpan March<T>(VdbTree<T> tree, ref Rayd ray, int nodeLevel = 2) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree); ValidateLevel(nodeLevel);
        var span = RayTimeSpan.Empty;
        if (ray.IsValid()) Traverse(tree, ref ray, nodeLevel, ref span, null);
        return span;
    }
    public static void Hits<T>(VdbTree<T> tree, ref Rayd ray, ICollection<RayTimeSpan> destination, int nodeLevel = 2) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree); ArgumentNullException.ThrowIfNull(destination); ValidateLevel(nodeLevel);
        destination.Clear(); if (ray.MaxTime < ray.MinTime) return;
        var span = RayTimeSpan.Empty; Traverse(tree, ref ray, nodeLevel, ref span, destination);
        if (span.IsValid()) destination.Add(span);
    }
    private static bool Traverse<T>(VdbTree<T> tree, ref Rayd ray, int level, ref RayTimeSpan span, ICollection<RayTimeSpan>? destination) where T : unmanaged
    {
        var dda = new Dda(ray, level switch { 0 => 3, 1 => 7, _ => 12 });
        do
        {
            bool allocated = tree.ProbeNode(dda.Voxel, level) is not null;
            if (level > 0 && allocated)
            {
                ray = new(ray.Eye, ray.Direction, dda.Time, dda.Next);
                if (Traverse(tree, ref ray, level - 1, ref span, destination)) return true;
            }
            else if ((level == 0 && allocated) || tree.IsValueOn(dda.Voxel))
            { if (span.Start < 0) span = new(dda.Time, span.End); }
            else if (span.Start >= 0)
            {
                span = new(span.Start, dda.Time);
                if (span.IsValid()) { if (destination is null) return true; destination.Add(span); }
                span = RayTimeSpan.Empty;
            }
        } while (StepWithinDomain(ref dda));
        if (span.Start >= 0) span = new(span.Start, dda.MaxTime);
        return false;
    }
    internal static bool StepWithinDomain(ref Dda dda)
    {
        // Native signed-coordinate overflow is undefined. A query stops at the grid domain boundary.
        try { return dda.Step(); } catch (OverflowException) { return false; }
    }
    private static void ValidateLevel(int level) { if ((uint)level > 2) throw new ArgumentOutOfRangeException(nameof(level)); }
}
