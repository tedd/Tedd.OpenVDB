// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of tools/Statistics.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Aggregates virtual voxels without expanding constant tiles.</summary>
public static class Statistics
{
    public static Stats Compute<T>(VdbTree<T> tree, ValueIterationMode mode = ValueIterationMode.Active) where T : unmanaged, INumber<T> => Compute(tree, static v => double.CreateChecked(v), mode);
    public static Stats Compute(VdbTree<Vec3d> tree, ValueIterationMode mode = ValueIterationMode.Active) => Compute(tree, static v => v.Length, mode);
    public static Stats Compute(VdbTree<Vec3f> tree, ValueIterationMode mode = ValueIterationMode.Active) => Compute(tree, static v => v.Length, mode);
    public static Stats Compute<T>(VdbTree<T> tree, Func<T, double> selector, ValueIterationMode mode = ValueIterationMode.Active) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree); ArgumentNullException.ThrowIfNull(selector); var stats = new Stats();
        foreach (var value in tree.EnumerateValues(mode))
        { double sample = selector(value.Value); if (value.IsVoxel) stats.Add(sample); else stats.Add(sample, value.VoxelCount); }
        return stats;
    }
    public static Extrema Extrema<T>(VdbTree<T> tree, ValueIterationMode mode = ValueIterationMode.Active) where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(tree); var extrema = new Extrema(); foreach (var value in tree.EnumerateValues(mode)) extrema.Add(double.CreateChecked(value.Value), value.VoxelCount); return extrema;
    }
    public static Histogram Histogram<T>(VdbTree<T> tree, double minimum, double maximum, int binCount = 10, ValueIterationMode mode = ValueIterationMode.Active) where T : unmanaged, INumber<T>
    {
        ArgumentNullException.ThrowIfNull(tree); var histogram = new Histogram(minimum, maximum, binCount); foreach (var value in tree.EnumerateValues(mode)) histogram.Add(double.CreateChecked(value.Value), value.VoxelCount); return histogram;
    }
}
