// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of tools/LevelSetFracture.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

/// <summary>Divides level sets using native CSG fragment validation, optional rigid cutter instances and SDF segmentation.</summary>
/// <remarks>Inputs must have matching transforms and narrow-band widths. Residuals keep their original tree identity. The fragment list persists across calls and can itself be edited by the caller. Concurrent mutation is unsupported.</remarks>
public sealed class LevelSetFracture<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly List<Grid<T>> _fragments = [];
    public LevelSetFracture()
    {
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double)) throw new NotSupportedException("Native level-set fracturing supports float and double grids.");
    }
    public IList<Grid<T>> Fragments => _fragments;
    /// <summary>Bounds samples for each instanced-cutter resampling operation.</summary>
    public ulong MaximumResamplingSamples { get; set; } = ulong.MaxValue;
    public void Clear() => _fragments.Clear();
    /// <summary>Replaces successful input cuts with residuals and appends their intersections to Fragments.</summary>
    /// <remarks>Rotations are used only when their count equals a nonempty point list. Translations use point sampling; rotations use box sampling. Cancellation retains previously completed cuts.</remarks>
    public void Fracture(IList<Grid<T>> grids, Grid<T> cutter, bool segment = false, IReadOnlyList<Vec3f>? points = null, IReadOnlyList<Quatf>? rotations = null, bool cutterOverlap = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(grids); ArgumentNullException.ThrowIfNull(cutter); cancellationToken.ThrowIfCancellationRequested();
        if (grids.IsReadOnly) throw new ArgumentException("The residual grid list must be mutable.", nameof(grids));
        if (points is { Count: > 0 })
        {
            Transform original = cutter.Transform.Copy(); var cutterGrid = new Grid<T>(cutter.Tree) { Transform = cutter.Transform.Copy() };
            foreach (var pair in cutter.Metadata) cutterGrid.SetMetadata(pair.Key, pair.Value);
            bool hasRotations = rotations is not null && points.Count == rotations.Count;
            for (int index = 0; index < points.Count; ++index)
            {
                cancellationToken.ThrowIfCancellationRequested(); var instance = new Grid<T> { Transform = original.Copy() }; var transform = original.Copy();
                if (hasRotations)
                {
                    Vec3f rotation = rotations![index].EulerAnglesXyz(); transform.PreRotate(rotation.X, Axis.X); transform.PreRotate(rotation.Y, Axis.Y); transform.PreRotate(rotation.Z, Axis.Z);
                }
                transform.PostTranslate(points[index].AsVec3d()); cutterGrid.Transform = transform;
                Resampling.DoResampleToMatch(cutterGrid, instance, hasRotations ? SamplingOrder.Box : SamplingOrder.Point, maximumSamples: MaximumResamplingSamples, cancellationToken: cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (cutterOverlap && _fragments.Count != 0) Process(_fragments, instance, cancellationToken);
                Process(grids, instance, cancellationToken);
            }
        }
        else
        {
            if (cutterOverlap && _fragments.Count != 0) Process(_fragments, cutter, cancellationToken);
            Process(grids, cutter, cancellationToken);
        }
        if (segment) { Segment(_fragments, cancellationToken); Segment(grids, cancellationToken); }
    }
    private void Process(IList<Grid<T>> grids, Grid<T> cutter, CancellationToken token)
    {
        List<Grid<T>> created = []; int count = grids.Count;
        try
        {
            for (int index = 0; index < count; ++index)
            {
                token.ThrowIfCancellationRequested(); Grid<T> grid = grids[index]; ArgumentNullException.ThrowIfNull(grid);
                Grid<T> fragment = Composite.CsgIntersectionCopy(grid, cutter); if (!IsValidFragment(fragment)) continue;
                Grid<T> residual = Composite.CsgDifferenceCopy(grid, cutter); if (!IsValidFragment(residual)) continue;
                token.ThrowIfCancellationRequested();
                grid.Tree.Clear(); grid.Tree.Merge(residual.Tree); created.Add(fragment);
            }
        }
        finally { _fragments.AddRange(created); }
    }
    private static bool IsValidFragment(Grid<T> grid)
    {
        if (grid.Tree.LeafCount >= 9) return true;
        var leaves = grid.Tree.EnumerateLeaves().ToArray(); ulong active = 0; foreach (var leaf in leaves) active += (ulong)leaf.OnVoxelCount; if (active < 27) return false;
        T minimum = T.CreateTruncating(typeof(T) == typeof(float) ? float.MaxValue : double.MaxValue), maximum = -minimum;
        foreach (var leaf in leaves) foreach (T value in leaf.Values) { if (value < minimum) minimum = value; if (maximum < value) maximum = value; }
        return (minimum < T.Zero) != (maximum < T.Zero);
    }
    private static void Segment(IList<Grid<T>> grids, CancellationToken token)
    {
        List<Grid<T>> segments = []; foreach (Grid<T> grid in grids) { token.ThrowIfCancellationRequested(); segments.AddRange(Segmentation.SegmentSdf(grid)); }
        token.ThrowIfCancellationRequested(); grids.Clear(); foreach (Grid<T> segment in segments) grids.Add(segment);
    }
}
