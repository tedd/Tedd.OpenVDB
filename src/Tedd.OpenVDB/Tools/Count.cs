// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of the counting operations in openvdb/tools/Count.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Counts allocated activity and inclusive-region activity without expanding tiles.</summary>
public static partial class Count
{
    public static ulong ActiveVoxels<T>(VdbTree<T> tree) where T : unmanaged => (tree ?? throw new ArgumentNullException(nameof(tree))).ActiveVoxelCount;
    public static ulong ActiveVoxels<T>(VdbTree<T> tree, CoordBBox bounds) where T : unmanaged => new FindActiveValues<T>(tree).CountActiveValues(bounds);
    public static ulong ActiveLeafVoxels<T>(VdbTree<T> tree) where T : unmanaged => (tree ?? throw new ArgumentNullException(nameof(tree))).ActiveLeafVoxelCount;
    public static ulong ActiveLeafVoxels<T>(VdbTree<T> tree, CoordBBox bounds) where T : unmanaged => new FindActiveValues<T>(tree).CountActiveLeafVoxels(bounds);
    public static ulong InactiveVoxels<T>(VdbTree<T> tree) where T : unmanaged => (tree ?? throw new ArgumentNullException(nameof(tree))).InactiveVoxelCount;
    public static ulong InactiveLeafVoxels<T>(VdbTree<T> tree) where T : unmanaged => (tree ?? throw new ArgumentNullException(nameof(tree))).InactiveLeafVoxelCount;
    public static ulong ActiveTiles<T>(VdbTree<T> tree) where T : unmanaged => (tree ?? throw new ArgumentNullException(nameof(tree))).ActiveTileCount;
    /// <summary>Native serial top-down scalar extrema; empty activity returns zero/zero, irrespective of background.</summary>
    public static MinMax<T> MinMax<T>(VdbTree<T> tree) where T : unmanaged, INumber<T>, IMinMaxValue<T>
    {
        ArgumentNullException.ThrowIfNull(tree); T min = T.Zero, max = T.Zero; bool seen = false;
        var manager = new DynamicNodeManager<T>(tree);
        manager.ForEachTopDown((node, index) =>
        {
            foreach (var value in node.EnumerateValues(ValueIterationMode.Active))
            {
                T current = value.Value; if (!seen) { min = max = current; seen = true; continue; }
                if (current < min) min = current; if (current > max) max = current;
            }
            return true;
        });
        return new(min, max);
    }
}
