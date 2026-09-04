// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/LevelSetAdvect.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tools;

/// <summary>Advects a float or double narrow-band level set in a world-space velocity field.</summary>
/// <remarks>Supports native biased differences, TVD Runge-Kutta integration, and interface tracking.
/// Advection maps must be uniform scale/translation or unitary without translation. The tracker captures
/// voxel size at construction. A cancelled buffered stage is not published; completed stages and topology
/// tracking remain visible. Instances, fields and input grids must not be modified concurrently.</remarks>
public sealed class LevelSetAdvection<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly LevelSetTracker<T> _tracker;
    private readonly IVelocityField _field;
    public LevelSetAdvection(Grid<T> grid) : this(grid, new EnrightField<T>()) { }
    public LevelSetAdvection(Grid<T> grid, IVelocityField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        _tracker = new(grid); _field = field.Copy();
    }
    public Grid<T> Grid => _tracker.Grid;
    public T VoxelSize => _tracker.VoxelSize;
    public BiasedGradientScheme SpatialScheme { get; set; } = BiasedGradientScheme.HJWeno5;
    public TemporalIntegrationScheme TemporalScheme { get; set; } = TemporalIntegrationScheme.TvdRungeKutta2;
    public BiasedGradientScheme TrackerSpatialScheme { get => _tracker.SpatialScheme; set => _tracker.SpatialScheme = value; }
    public TemporalIntegrationScheme TrackerTemporalScheme { get => _tracker.TemporalScheme; set => _tracker.TemporalScheme = value; }
    public int NormalizationCount { get => _tracker.NormalizationCount; set => _tracker.NormalizationCount = value; }
    public int GrainSize { get => _tracker.GrainSize; set => _tracker.GrainSize = value; }
    /// <summary>Limits the active-voxel velocity buffer before allocating or updating a CFL step.</summary>
    public int MaximumActiveVoxels { get; set; } = int.MaxValue;

    /// <summary>Advects forward or backward and returns the number of completed CFL steps.</summary>
    public ulong Advect(T time0, T time1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateSchemes(); ArgumentOutOfRangeException.ThrowIfNegative(MaximumActiveVoxels);
        if (!T.IsFinite(time0)) throw new ArgumentOutOfRangeException(nameof(time0));
        if (!T.IsFinite(time1) || !T.IsFinite(time1 - time0)) throw new ArgumentOutOfRangeException(nameof(time1));
        Transform transform = Grid.Transform; bool uniform = ValidateMap(transform);
        if (time0 == time1) return 0;
        if (Grid.Tree.ActiveTileCount != 0) throw new ArgumentException("Advection requires voxelized active topology.", nameof(Grid));
        bool forward = time0 < time1, singleVelocity = _field.IsSinglePrecision;
        ulong count = 0; int[] offsets = []; Vec3f[] velocitySingle = []; Vec3d[] velocityDouble = [];
        while (forward ? time0 < time1 : time0 > time1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leaves = _tracker.Leaves;
            if (leaves.LeafCount == 0) break;
            int voxelCount = checked((int)Grid.Tree.ActiveLeafVoxelCount);
            if (voxelCount > MaximumActiveVoxels) throw new InvalidOperationException("The active velocity buffer exceeds MaximumActiveVoxels.");
            if (offsets.Length < leaves.LeafCount + 1) offsets = GC.AllocateUninitializedArray<int>(checked(leaves.LeafCount + 1));
            offsets[0] = 0;
            for (int leaf = 0; leaf < leaves.LeafCount; ++leaf) offsets[leaf + 1] = checked(offsets[leaf] + leaves[leaf].OnVoxelCount);
            if (singleVelocity) { if (velocitySingle.Length < voxelCount) velocitySingle = GC.AllocateUninitializedArray<Vec3f>(voxelCount); }
            else if (velocityDouble.Length < voxelCount) velocityDouble = GC.AllocateUninitializedArray<Vec3d>(voxelCount);
            bool aligned = _field.Transform.IsEquivalentTo(transform);
            double fieldTime = double.CreateTruncating(time0); if (singleVelocity) fieldTime = (float)fieldTime;
            RunLeaves(leaves, leafIndex =>
            {
                IVelocityField field = _field.Copy(); var leaf = leaves[leafIndex]; int offset = offsets[leafIndex];
                foreach (int active in leaf.ActiveOffsets)
                {
                    Coord coordinate = leaf.OffsetToGlobalCoord(active);
                    Vec3d value = aligned ? field.Sample(coordinate, fieldTime) : field.Sample(IndexToWorld(transform, uniform, coordinate), fieldTime);
                    if (!forward) value = -value;
                    if (singleVelocity) velocitySingle[offset++] = (Vec3f)value; else velocityDouble[offset++] = value;
                }
            }, cancellationToken);
            T maximumNorm = T.Zero;
            for (int i = 0; i < voxelCount; ++i)
            {
                T norm = singleVelocity ? T.CreateTruncating(velocitySingle[i].LengthSquared) : T.CreateTruncating(velocityDouble[i].LengthSquared);
                if (maximumNorm < norm) maximumNorm = norm;
            }
            T delta = T.CreateTruncating(typeof(T) == typeof(float) ? 1e-5 : 1e-9);
            if (!(maximumNorm > delta) && !(maximumNorm < -delta)) break;
            T cfl = T.CreateTruncating(TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta1 ? 0.3 : TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta2 ? 0.9 : 1.0) / T.Sqrt(T.CreateTruncating(3));
            T remaining = T.Abs(time1 - time0), limit = cfl * VoxelSize / T.Sqrt(maximumNorm);
            T dt = limit < remaining ? limit : remaining;
            if (dt == T.Zero) break;
            T next = time0 + (forward ? dt : -dt);
            if (next == time0 || !T.IsFinite(next)) throw new ArithmeticException("The CFL step cannot advance time at this scalar precision.");
            leaves.Rebuild(TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta3 ? 2 : 1);
            try
            {
                Euler(0, 1, 0, 1);
                if (TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta2) Euler(1, 2, 1, 1);
                else if (TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta3) { Euler(3, 4, 1, 2); Euler(1, 3, 1, 2); }
            }
            finally { leaves.Rebuild(); }
            time0 = next; count = checked(count + 1);
            _tracker.Track(cancellationToken);

            void Euler(int numerator, int denominator, int originalBuffer, int resultBuffer)
            {
                cancellationToken.ThrowIfCancellationRequested();
                T alpha = T.CreateTruncating(numerator) / T.CreateTruncating(denominator), beta = T.One - alpha;
                RunLeaves(leaves, leafIndex =>
                {
                    Stencil<T> stencil = SpatialScheme switch { BiasedGradientScheme.First => new SevenPointStencil<T>(Grid), BiasedGradientScheme.Second => new ThirteenPointStencil<T>(Grid), _ => new NineteenPointStencil<T>(Grid) };
                    var leaf = leaves[leafIndex]; int velocityOffset = offsets[leafIndex];
                    Span<T> result = leaves.GetBuffer(leafIndex, resultBuffer);
                    ReadOnlySpan<T> original = originalBuffer == 0 ? leaf.Values : leaves.GetBuffer(leafIndex, originalBuffer);
                    foreach (int active in leaf.ActiveOffsets)
                    {
                        Vec3d velocity = singleVelocity ? velocitySingle[velocityOffset++].AsVec3d() : velocityDouble[velocityOffset++];
                        stencil.MoveTo(leaf.OffsetToGlobalCoord(active), leaf.GetValue(active));
                        // Native GradientBiased receives velocity converted to the grid's scalar type.
                        Vec3d gradient = DifferentialOperators.IndexBiasedGradient(stencil, DifferentialOperators.Round<T>(velocity), SpatialScheme);
                        gradient = DifferentialOperators.Round<T>(uniform ? gradient * (1.0 / transform.Matrix.M00) : transform.ApplyInverseJacobianTranspose(gradient));
                        // Vec3::dot converts the gradient to the field's scalar precision, even for mixed grids.
                        double dot = singleVelocity ? ((Vec3f)velocity).Dot((Vec3f)gradient) : velocity.Dot(gradient);
                        T value = typeof(T) == typeof(float) && singleVelocity
                            ? T.CreateTruncating(float.CreateTruncating(stencil.CenterValue) - float.CreateTruncating(dt) * (float)dot)
                            : T.CreateTruncating(double.CreateTruncating(stencil.CenterValue) - double.CreateTruncating(dt) * dot);
                        result[active] = numerator == 0 ? value : alpha * original[active] + beta * value;
                    }
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested(); leaves.SwapBuffer(resultBuffer);
            }
        }
        return count;
    }
    private void ValidateSchemes()
    {
        if (SpatialScheme < BiasedGradientScheme.First || SpatialScheme > BiasedGradientScheme.HJWeno5) throw new ArgumentOutOfRangeException(nameof(SpatialScheme));
        if (TemporalScheme < TemporalIntegrationScheme.TvdRungeKutta1 || TemporalScheme > TemporalIntegrationScheme.TvdRungeKutta3) throw new ArgumentOutOfRangeException(nameof(TemporalScheme));
        if (TrackerSpatialScheme < BiasedGradientScheme.First || TrackerSpatialScheme > BiasedGradientScheme.HJWeno5) throw new ArgumentOutOfRangeException(nameof(TrackerSpatialScheme));
        if (TrackerTemporalScheme < TemporalIntegrationScheme.TvdRungeKutta1 || TrackerTemporalScheme > TemporalIntegrationScheme.TvdRungeKutta3) throw new ArgumentOutOfRangeException(nameof(TrackerTemporalScheme));
    }
    private static bool ValidateMap(Transform transform)
    {
        if (!transform.IsLinear || !transform.HasUniformScale) throw new ArgumentException("Advection requires uniform-scale/translation or unitary maps.", nameof(transform));
        Mat4d matrix = transform.Matrix; Mat3d linear = matrix.GetMat3();
        if (linear.IsDiagonal && MathComparison.IsApproxEqual(matrix.M00, matrix.M11) && MathComparison.IsApproxEqual(matrix.M00, matrix.M22)) return true;
        if (linear.IsUnitary && MathComparison.IsApproxZero(matrix.M30) && MathComparison.IsApproxZero(matrix.M31) && MathComparison.IsApproxZero(matrix.M32)) return false;
        throw new ArgumentException("Native advection does not support a general affine map, including combined rotation and scaling or rotation and translation.", nameof(transform));
    }
    private static Vec3d IndexToWorld(Transform transform, bool uniform, Coord coordinate)
    {
        Mat4d matrix = transform.Matrix;
        return uniform ? new(coordinate.X * matrix.M00 + matrix.M30, coordinate.Y * matrix.M00 + matrix.M31, coordinate.Z * matrix.M00 + matrix.M32) : transform.IndexToWorld(coordinate);
    }
    private void RunLeaves(LeafManager<T> leaves, Action<int> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (GrainSize > 0) Parallel.For(0, leaves.LeafCount, new ParallelOptions { CancellationToken = token }, operation);
        else for (int i = 0; i < leaves.LeafCount; ++i) { token.ThrowIfCancellationRequested(); operation(i); }
    }
}
