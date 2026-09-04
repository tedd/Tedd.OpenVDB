// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/LevelSetTracker.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

public enum TrimMode { None, Interior, Exterior, All }
public readonly record struct LevelSetTrackerState(BiasedGradientScheme SpatialScheme, TemporalIntegrationScheme TemporalScheme, int NormalizationCount, int GrainSize)
{
    public LevelSetTrackerState() : this(BiasedGradientScheme.HJWeno5, TemporalIntegrationScheme.TvdRungeKutta1, 3, 1) { }
}

/// <summary>Normalizes and maintains a narrow-band level set using native biased differences and TVD Runge-Kutta integration.</summary>
/// <remarks>Float and double grids with uniform voxels are supported. The voxel size is captured at construction. Cancellation preserves completed stages; a cancelled buffered stage is not published. Instances and their grids must not be mutated concurrently.</remarks>
public class LevelSetTracker<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private LeafManager<T> _leaves;
    public LevelSetTracker(Grid<T> grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double)) throw new NotSupportedException("Level-set tracking requires float or double values.");
        if (!grid.Transform.HasUniformScale) throw new ArgumentException("Level-set tracking requires uniform voxels.", nameof(grid));
        if (grid.GridClass != GridClass.LevelSet) throw new ArgumentException("The grid must have LevelSet classification.", nameof(grid));
        Grid = grid; VoxelSize = T.CreateChecked(grid.Transform.VoxelSize.X); _leaves = new(grid.Tree);
    }
    public Grid<T> Grid { get; }
    public T VoxelSize { get; }
    public T HalfWidth => Grid.Background / VoxelSize;
    public LevelSetTrackerState State { get; set; } = new();
    public BiasedGradientScheme SpatialScheme { get => State.SpatialScheme; set => State = State with { SpatialScheme = value }; }
    public TemporalIntegrationScheme TemporalScheme { get => State.TemporalScheme; set => State = State with { TemporalScheme = value }; }
    public int NormalizationCount { get => State.NormalizationCount; set => State = State with { NormalizationCount = value }; }
    public int GrainSize { get => State.GrainSize; set => State = State with { GrainSize = value }; }
    public TrimMode Trimming { get; set; } = TrimMode.All;
    public LeafManager<T> Leaves { get { EnsureCurrent(); return _leaves; } }

    public void Normalize(CancellationToken cancellationToken = default) => Normalize<bool>(null, cancellationToken);

    /// <summary>Normalizes active leaf voxels, or all active voxels of the supplied mask leaves, including inactive target voxels.</summary>
    /// <remarks>Mask tiles are ignored. Target activity is preserved.</remarks>
    public void Normalize<TMask>(VdbTree<TMask>? mask, CancellationToken cancellationToken = default) where TMask : unmanaged
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureCurrent();
        if (SpatialScheme < BiasedGradientScheme.First || SpatialScheme > BiasedGradientScheme.HJWeno5) throw new ArgumentOutOfRangeException(nameof(SpatialScheme));
        if (TemporalScheme < TemporalIntegrationScheme.TvdRungeKutta1 || TemporalScheme > TemporalIntegrationScheme.TvdRungeKutta3) throw new ArgumentOutOfRangeException(nameof(TemporalScheme));
        int bufferCount = TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta3 ? 2 : 1;
        _leaves.Rebuild(bufferCount);
        T dt = VoxelSize * T.CreateChecked(TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta1 ? 0.3f : TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta2 ? 0.9f : 1.0f);
        T invDx = T.One / VoxelSize;
        T epsilon = T.CreateChecked(typeof(T) == typeof(float) ? 1e-8 : 1e-15);
        try
        {
            for (int iteration = 0; iteration < NormalizationCount; ++iteration)
            {
                Euler(0, 1, 0, 1);
                if (TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta2) Euler(1, 2, 1, 1);
                else if (TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta3) { Euler(3, 4, 1, 2); Euler(1, 3, 1, 2); }
            }
        }
        finally { _leaves.Rebuild(); }

        void Euler(int numerator, int denominator, int originalBuffer, int resultBuffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            T alpha = T.CreateChecked(numerator) / T.CreateChecked(denominator), beta = T.One - alpha;
            RunLeaves((leaf, index) =>
            {
                Stencil<T> stencil = SpatialScheme switch { BiasedGradientScheme.First => new SevenPointStencil<T>(Grid), BiasedGradientScheme.Second => new ThirteenPointStencil<T>(Grid), _ => new NineteenPointStencil<T>(Grid) };
                var selected = mask?.ProbeLeaf(leaf.Origin)?.ValueMask;
                if (mask is not null && selected is null) return;
                var offsets = selected is null ? leaf.ActiveOffsets : selected.OnIndices;
                Span<T> result = _leaves.GetBuffer(index, resultBuffer);
                ReadOnlySpan<T> original = originalBuffer == 0 ? leaf.Values : _leaves.GetBuffer(index, originalBuffer);
                foreach (int offset in offsets)
                {
                    T phi = leaf.GetValue(offset);
                    stencil.MoveTo(leaf.OffsetToGlobalCoord(offset), phi);
                    T norm = DifferentialOperators.IndexNormSquaredGradient(stencil, SpatialScheme);
                    T speed = phi / (T.Sqrt(phi * phi + norm) + epsilon);
                    T value = phi - dt * speed * (T.Sqrt(norm) * invDx - T.One);
                    result[offset] = numerator != 0 ? alpha * original[offset] + beta * value : value;
                }
            }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _leaves.SwapBuffer(resultBuffer);
        }
    }

    public void Track(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Morphology.DilateActiveValues(Grid.Tree, 1, NearestNeighbors.Face, TilePolicy.IgnoreTiles);
        _leaves.Rebuild(); Normalize(cancellationToken); Prune(cancellationToken);
    }

    public void Prune(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureCurrent();
        if (!Enum.IsDefined(Trimming)) throw new ArgumentOutOfRangeException(nameof(Trimming));
        T gamma = Grid.Background;
        if (Trimming != TrimMode.None)
            RunLeaves((leaf, _) =>
            {
                foreach (int offset in leaf.ValueMask.OnIndices)
                {
                    T value = leaf.GetValue(offset);
                    if (Trimming is TrimMode.All or TrimMode.Interior && value <= -gamma) leaf.SetValueOff(offset, -gamma);
                    else if (Trimming is TrimMode.All or TrimMode.Exterior && value >= gamma) leaf.SetValueOff(offset, gamma);
                }
            }, CancellationToken.None);
        Tools.Prune.PruneLevelSet(Grid.Tree); _leaves.Rebuild();
        cancellationToken.ThrowIfCancellationRequested();
    }

    public void Dilate(int iterations = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureCurrent();
        for (int i = 0; i < iterations; ++i)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VdbTree<bool>? old = null;
            if (NormalizationCount != 0) { old = new(); old.TopologyUnion(Grid.Tree); }
            Morphology.DilateActiveValues(Grid.Tree, 1, NearestNeighbors.Face, TilePolicy.IgnoreTiles);
            ChangeBackground.LevelSet(Grid.Tree, VoxelSize + Grid.Background);
            _leaves.Rebuild();
            if (old is not null) { var added = new VdbTree<bool>(); added.TopologyUnion(Grid.Tree); added.TopologyDifference(old); Normalize(added, cancellationToken); }
        }
    }

    public void Erode(int iterations = 1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); EnsureCurrent();
        T background = Grid.Background - T.CreateChecked(iterations) * VoxelSize;
        // Validate before destructive topology updates; native ChangeBackground rejects this after erosion.
        if (!(background > T.Zero)) throw new ArgumentOutOfRangeException(nameof(iterations), "Erosion must leave a positive narrow-band width.");
        Morphology.ErodeActiveValues(Grid.Tree, iterations, NearestNeighbors.Face, TilePolicy.IgnoreTiles);
        Tools.Prune.PruneLevelSet(Grid.Tree);
        ChangeBackground.LevelSet(Grid.Tree, background); _leaves.Rebuild();
        cancellationToken.ThrowIfCancellationRequested();
    }

    public bool Resize(int halfWidth = 3, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(halfWidth); cancellationToken.ThrowIfCancellationRequested();
        int old = int.CreateChecked(T.Floor(HalfWidth));
        if (old < halfWidth) Dilate(halfWidth - old, cancellationToken);
        else if (old > halfWidth) Erode(old - halfWidth, cancellationToken);
        return old != halfWidth;
    }

    protected void RunLeaves(Action<LeafNode<T>, int> operation, CancellationToken token, bool allowParallel = true)
    {
        token.ThrowIfCancellationRequested();
        if (GrainSize > 0 && allowParallel) Parallel.For(0, _leaves.LeafCount, new ParallelOptions { CancellationToken = token }, i => operation(_leaves[i], i));
        else for (int i = 0; i < _leaves.LeafCount; ++i) { token.ThrowIfCancellationRequested(); operation(_leaves[i], i); }
    }
    private void EnsureCurrent()
    {
        if (!ReferenceEquals(_leaves.Tree, Grid.Tree)) _leaves = new(Grid.Tree);
        else if (!_leaves.IsValid) _leaves.Rebuild();
    }
}
