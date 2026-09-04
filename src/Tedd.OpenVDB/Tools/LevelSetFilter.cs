// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/LevelSetFilter.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Level-set surface filters with interface tracking after each native update step.</summary>
/// <remarks>Unlike a general volume filter, these operations update and normalize the narrow band. Optional float masks use world-space alignment and native smooth alpha weights. Instances and grids must not be mutated concurrently.</remarks>
public sealed class LevelSetFilter<T>(Grid<T> grid) : LevelSetTracker<T>(grid) where T : unmanaged, IFloatingPointIeee754<T>
{
    public float MinMask { get; private set; }
    public float MaxMask { get; private set; } = 1;
    public bool IsMaskInverted { get; set; }
    public void SetMaskRange(float minimum, float maximum)
    {
        if (!(minimum < maximum)) throw new ArgumentException("Mask minimum must be less than its maximum.");
        MinMask = minimum; MaxMask = maximum;
    }
    public void Mean(int width = 1, Grid<float>? mask = null, CancellationToken cancellationToken = default) => Box(width, mask, cancellationToken);
    public void Gaussian(int width = 1, Grid<float>? mask = null, CancellationToken cancellationToken = default)
    {
        for (int iteration = 0; iteration < 4; ++iteration) Box(width, mask, cancellationToken);
    }
    public void Median(int width = 1, Grid<float>? mask = null, CancellationToken cancellationToken = default) => FilterAndTrack(Kernel.Median, width, mask, cancellationToken);
    public void Laplacian(Grid<float>? mask = null, CancellationToken cancellationToken = default) => FilterAndTrack(Kernel.Laplacian, 1, mask, cancellationToken);
    public void MeanCurvature(Grid<float>? mask = null, CancellationToken cancellationToken = default) => FilterAndTrack(Kernel.MeanCurvature, 1, mask, cancellationToken);
    public void Fillet(Grid<float>? mask = null, CancellationToken cancellationToken = default) => FilterAndTrack(Kernel.Fillet, 1, mask, cancellationToken);

    /// <summary>Offsets the field in world units using half-voxel CFL substeps, with tracking after each substep.</summary>
    public void Offset(T offset, Grid<float>? mask = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!T.IsFinite(offset)) throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be finite.");
        Leaves.Rebuild();
        T cfl = T.CreateChecked(0.5) * VoxelSize, total = T.Abs(offset), distance = T.Zero;
        while (total - distance > T.CreateChecked(0.001) * cfl)
        {
            cancellationToken.ThrowIfCancellationRequested();
            T delta = total - distance < cfl ? total - distance : cfl;
            T next = distance + delta;
            if (next == distance) throw new ArgumentOutOfRangeException(nameof(offset), "Offset exceeds the stepping precision of this scalar type.");
            distance = next;
            delta = T.CopySign(delta, offset);
            // Complete this in-place stage before observing cancellation again.
            RunLeaves((leaf, leafIndex) =>
            {
                AlphaMask<T>? alpha = mask is null ? null : new(Grid, mask, MinMask, MaxMask, IsMaskInverted);
                foreach (int i in leaf.ActiveOffsets)
                    if (alpha is null) leaf.SetValueOnly(i, leaf.GetValue(i) + delta);
                    else if (alpha.Evaluate(leaf.OffsetToGlobalCoord(i), out float a, out _)) leaf.SetValueOnly(i, leaf.GetValue(i) + T.CreateChecked(a) * delta);
            }, CancellationToken.None, allowParallel: mask is null || !ReferenceEquals(mask.Tree, Grid.Tree) || Grid.Transform.Equals(mask.Transform));
            Track(cancellationToken);
        }
    }

    private enum Kernel { X, Y, Z, Median, Laplacian, MeanCurvature, Fillet }
    private void Box(int width, Grid<float>? mask, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        width = System.Math.Max(1, width);
        ValidateWidth(width);
        Leaves.Rebuild(1);
        try
        {
            Cook(Kernel.X, width, mask, token);
            Cook(Kernel.Z, width, mask, token);
            Cook(Kernel.Y, width, mask, token);
            Track(token);
        }
        finally { Leaves.Rebuild(); }
    }
    private void FilterAndTrack(Kernel kernel, int width, Grid<float>? mask, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); width = System.Math.Max(1, width); ValidateWidth(width);
        Leaves.Rebuild(1);
        try { Cook(kernel, width, mask, token); Track(token); }
        finally { Leaves.Rebuild(); }
    }
    private static void ValidateWidth(int width)
    {
        if (width > (int.MaxValue - 1) / 2) throw new ArgumentOutOfRangeException(nameof(width));
    }
    private void Cook(Kernel kernel, int width, Grid<float>? mask, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var manager = Leaves;
        T factor = T.One / T.CreateChecked(checked(2 * width + 1));
        T dt = VoxelSize * VoxelSize / T.CreateChecked(kernel == Kernel.Laplacian ? 6 : 3);
        RunLeaves((leaf, index) =>
        {
            var alpha = mask is null ? null : new AlphaMask<T>(Grid, mask, MinMask, MaxMask, IsMaskInverted);
            var accessor = Grid.GetAccessor();
            var dense = kernel == Kernel.Median ? new DenseStencil<T>(Grid, width) : null;
            var gradient = kernel == Kernel.Laplacian ? new GradStencil<T>(Grid, double.CreateChecked(VoxelSize)) : null;
            var curvature = kernel is Kernel.MeanCurvature or Kernel.Fillet ? new CurvatureStencil<T>(Grid, double.CreateChecked(VoxelSize)) : null;
            Span<T> result = manager.GetBuffer(index, 1);
            foreach (int i in leaf.ActiveOffsets)
            {
                Coord coordinate = leaf.OffsetToGlobalCoord(i);
                float a = 1, b = 0;
                if (alpha is not null && !alpha.Evaluate(coordinate, out a, out b)) continue;
                T original = leaf.GetValue(i), value;
                if (kernel <= Kernel.Z)
                {
                    T sum = T.Zero;
                    for (int step = -width; step <= width; ++step)
                    {
                        Coord sample = kernel switch { Kernel.X => new(checked(coordinate.X + step), coordinate.Y, coordinate.Z), Kernel.Y => new(coordinate.X, checked(coordinate.Y + step), coordinate.Z), _ => new(coordinate.X, coordinate.Y, checked(coordinate.Z + step)) };
                        sum += accessor.GetValue(sample);
                    }
                    value = sum * factor;
                }
                else if (dense is not null) { dense.MoveTo(coordinate); value = dense.Median; }
                else if (gradient is not null) { gradient.MoveTo(coordinate); value = original + dt * gradient.Laplacian(); }
                else
                {
                    curvature!.MoveTo(coordinate);
                    if (kernel == Kernel.MeanCurvature) value = original + dt * curvature.MeanCurvatureNormGradient();
                    else
                    {
                        T kappa = curvature.PrincipalCurvatures().Minimum;
                        if (alpha is null && !(kappa < T.Zero)) continue;
                        T change = dt * kappa;
                        value = original + (change < T.Zero ? change : T.Zero);
                    }
                }
                result[i] = alpha is null ? value : T.CreateChecked(b) * original + T.CreateChecked(a) * value;
            }
        }, token);
        token.ThrowIfCancellationRequested(); manager.SwapBuffer(1);
    }
}
