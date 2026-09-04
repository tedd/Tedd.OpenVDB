// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/LevelSetMeasure.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tools;

/// <summary>The compact cosine approximation to the Dirac delta used by OpenVDB measurements.</summary>
public readonly struct DiracDelta
{
    private readonly double _coefficient, _frequency, _epsilon;
    public DiracDelta(double epsilon)
    {
        _coefficient = 0.5 / epsilon;
        _frequency = 2.0 * System.Math.PI * _coefficient;
        _epsilon = epsilon;
    }
    public double Evaluate(double value) => System.Math.Abs(value) > _epsilon ? 0.0 : _coefficient * (1.0 + System.Math.Cos(_frequency * value));
}

/// <summary>Surface area, enclosed volume and curvature integrals of a uniform-voxel level set.</summary>
/// <remarks>Results are cached. Call Init after modifying the grid, transform or topology. Active leaf voxels contribute; tiles do not. Like the native API, measurements support float and double scalar grids.</remarks>
public sealed class LevelSetMeasure<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private Grid<T> _grid = null!;
    private LeafNode<T>[] _leaves = [];
    private double _dx, _area, _volume, _mean, _gaussian;
    private bool _measured, _curvaturesMeasured;

    public LevelSetMeasure(Grid<T> grid) => Init(grid);

    /// <summary>Rebinds to a grid and discards cached measurements.</summary>
    public void Init(Grid<T> grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double)) throw new NotSupportedException("OpenVDB level-set measurements require float or double values.");
        if (!grid.Transform.HasUniformScale) throw new ArgumentException("Level-set measurement requires uniform voxels.", nameof(grid));
        if (grid.GridClass != GridClass.LevelSet) throw new ArgumentException("The grid must have LevelSet classification.", nameof(grid));
        if (grid.IsEmpty) throw new ArgumentException("Cannot measure an empty grid.", nameof(grid));
        _grid = grid;
        _dx = grid.Transform.VoxelSize.X;
        _leaves = grid.Tree.EnumerateLeaves().ToArray();
        _measured = _curvaturesMeasured = false;
    }

    public double Area(bool useWorldSpace = true) { Measure(); return useWorldSpace ? _area * _dx * _dx : _area; }
    public double Volume(bool useWorldSpace = true) { Measure(); return useWorldSpace ? _volume * _dx * _dx * _dx : _volume; }
    public double TotalMeanCurvature(bool useWorldSpace = true) { MeasureCurvatures(); return useWorldSpace ? _mean * _dx : _mean; }
    public double TotalGaussianCurvature(bool useWorldSpace = true) { MeasureCurvatures(); return _gaussian; }
    public double AverageMeanCurvature(bool useWorldSpace = true) => TotalMeanCurvature(useWorldSpace) / Area(useWorldSpace);
    public double AverageGaussianCurvature(bool useWorldSpace = true) => TotalGaussianCurvature(useWorldSpace) / Area(useWorldSpace);
    public int EulerCharacteristic => checked((int)System.Math.Round(TotalGaussianCurvature() / (2.0 * System.Math.PI), MidpointRounding.AwayFromZero));
    public int Genus => 1 - EulerCharacteristic / 2;

    private void Measure()
    {
        if (_measured) return;
        var areas = new double[_leaves.Length];
        var volumes = new double[_leaves.Length];
        var stencil = new GradStencil<T>(_grid);
        var delta = new DiracDelta(1.5);
        double invDx = 1.0 / _dx;
        for (int n = 0; n < _leaves.Length; ++n)
        {
            var leaf = _leaves[n];
            double area = 0, volume = 0;
            foreach (int offset in leaf.ActiveOffsets)
            {
                double dd = delta.Evaluate(invDx * double.CreateChecked(leaf.GetValue(offset)));
                if (!(dd > 0)) continue;
                Coord coordinate = leaf.OffsetToGlobalCoord(offset);
                stencil.MoveTo(coordinate);
                Vec3d gradient = stencil.Gradient();
                area += dd * NativeLength(gradient);
                volume += dd * (gradient.X * coordinate.X + gradient.Y * coordinate.Y + gradient.Z * coordinate.Z);
            }
            areas[n] = area; volumes[n] = volume;
        }
        _area = SortedSum(areas);
        _volume = SortedSum(volumes) / 3.0;
        _measured = true;
    }

    private void MeasureCurvatures()
    {
        if (_curvaturesMeasured) return;
        var means = new double[_leaves.Length];
        var gaussians = new double[_leaves.Length];
        var stencil = new CurvatureStencil<T>(_grid);
        var delta = new DiracDelta(1.5);
        double invDx = 1.0 / _dx;
        for (int n = 0; n < _leaves.Length; ++n)
        {
            var leaf = _leaves[n];
            double mean = 0, gaussian = 0;
            foreach (int offset in leaf.ActiveOffsets)
            {
                double dd = delta.Evaluate(invDx * double.CreateChecked(leaf.GetValue(offset)));
                if (!(dd > 0)) continue;
                stencil.MoveTo(leaf.OffsetToGlobalCoord(offset));
                double area = dd * NativeLength(stencil.Gradient());
                var curvatures = stencil.Curvatures();
                mean += area * double.CreateChecked(curvatures.Mean) * _dx;
                gaussian += area * double.CreateChecked(curvatures.Gaussian) * _dx * _dx;
            }
            means[n] = mean; gaussians[n] = gaussian;
        }
        _mean = SortedSum(means);
        _gaussian = SortedSum(gaussians);
        _curvaturesMeasured = true;
    }

    private static double NativeLength(Vec3d vector)
    {
        T x = T.CreateChecked(vector.X), y = T.CreateChecked(vector.Y), z = T.CreateChecked(vector.Z);
        return double.CreateChecked(T.Sqrt(x * x + y * y + z * z));
    }
    private static double SortedSum(double[] values)
    {
        Array.Sort(values);
        double result = 0;
        foreach (double value in values) result += value;
        return result;
    }
}

/// <summary>One-shot level-set measurements.</summary>
public static class LevelSetMeasure
{
    public static double Area<T>(Grid<T> grid, bool useWorldSpace = true) where T : unmanaged, IFloatingPointIeee754<T> => new LevelSetMeasure<T>(grid).Area(useWorldSpace);
    public static double Volume<T>(Grid<T> grid, bool useWorldSpace = true) where T : unmanaged, IFloatingPointIeee754<T> => new LevelSetMeasure<T>(grid).Volume(useWorldSpace);
    public static int EulerCharacteristic<T>(Grid<T> grid) where T : unmanaged, IFloatingPointIeee754<T> => new LevelSetMeasure<T>(grid).EulerCharacteristic;
    public static int Genus<T>(Grid<T> grid) where T : unmanaged, IFloatingPointIeee754<T> => new LevelSetMeasure<T>(grid).Genus;
}
