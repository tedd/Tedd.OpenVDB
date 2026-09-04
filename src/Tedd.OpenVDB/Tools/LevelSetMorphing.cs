// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/LevelSetMorph.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Morphs a source narrow-band level set toward a target field using native scalar speeds and interface tracking.</summary>
/// <remarks>Instances retain source, target and optional same-scalar mask grids by reference. Inputs must remain stable during a call. Buffered cancellation preserves completed stages. Native morphing supports forward time only, despite the upstream method's backward-advection comment.</remarks>
public sealed class LevelSetMorphing<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly LevelSetTracker<T> _tracker;
    private Grid<T> _target;
    private Grid<T>? _mask;
    private T _minimum, _range = T.One;
    private static readonly T Tolerance = T.CreateTruncating(typeof(T) == typeof(float) ? 1e-8 : 1e-15);
    private static readonly T MinimumSpeed = T.CreateTruncating(1e-6);
    private static readonly T Maximum = T.CreateTruncating(typeof(T) == typeof(float) ? float.MaxValue : double.MaxValue);
    public LevelSetMorphing(Grid<T> source, Grid<T> target)
    {
        ArgumentNullException.ThrowIfNull(target); _tracker = new(source); _target = target;
    }
    public Grid<T> Grid => _tracker.Grid;
    public Grid<T> Target => _target;
    public Grid<T>? AlphaMask => _mask;
    public T VoxelSize => _tracker.VoxelSize;
    public BiasedGradientScheme SpatialScheme { get; set; } = BiasedGradientScheme.HJWeno5;
    public TemporalIntegrationScheme TemporalScheme { get; set; } = TemporalIntegrationScheme.TvdRungeKutta2;
    public BiasedGradientScheme TrackerSpatialScheme { get => _tracker.SpatialScheme; set => _tracker.SpatialScheme = value; }
    public TemporalIntegrationScheme TrackerTemporalScheme { get => _tracker.TemporalScheme; set => _tracker.TemporalScheme = value; }
    public int NormalizationCount { get => _tracker.NormalizationCount; set => _tracker.NormalizationCount = value; }
    public int GrainSize { get => _tracker.GrainSize; set => _tracker.GrainSize = value; }
    public T MinMask => _minimum;
    public T MaxMask => _minimum + _range;
    public bool IsMaskInverted { get; set; }
    /// <summary>Bounds the number of leaf-sized auxiliary buffer groups before each CFL step.</summary>
    public int MaximumBufferedLeaves { get; set; } = int.MaxValue;
    public void SetTarget(Grid<T> target) => _target = target ?? throw new ArgumentNullException(nameof(target));
    public void SetAlphaMask(Grid<T>? mask) => _mask = mask;
    public void SetMaskRange(T minimum, T maximum)
    {
        if (!(minimum < maximum)) throw new ArgumentException("Mask minimum must be less than its maximum.");
        _minimum = minimum; _range = maximum - minimum;
    }
    /// <summary>Advances only when time0 is less than time1 and returns the completed CFL iteration count.</summary>
    public ulong Advect(T time0, T time1, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ValidateSchemes(); ArgumentOutOfRangeException.ThrowIfNegative(MaximumBufferedLeaves);
        if (!T.IsFinite(time0)) throw new ArgumentOutOfRangeException(nameof(time0));
        if (!T.IsFinite(time1) || !T.IsFinite(time1 - time0)) throw new ArgumentOutOfRangeException(nameof(time1));
        Transform transform = Grid.Transform; bool uniform = ValidateMap(transform);
        ulong iterations = 0;
        int speedBuffer = TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta3 ? 3 : 2;
        while (time0 < time1)
        {
            cancellationToken.ThrowIfCancellationRequested(); var manager = _tracker.Leaves;
            if (manager.LeafCount == 0) break;
            if (manager.LeafCount > MaximumBufferedLeaves) throw new InvalidOperationException("Morphing exceeds MaximumBufferedLeaves.");
            manager.Rebuild(speedBuffer);
            T next;
            try
            {
                T maximumSpeed = SampleSpeed(manager, speedBuffer, transform, uniform, cancellationToken);
                if (!(T.Abs(maximumSpeed - MinimumSpeed) > Tolerance)) break;
                T cfl = T.CreateTruncating(TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta1 ? .3 : TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta2 ? .9 : 1) / T.Sqrt(T.CreateTruncating(3));
                T remaining = T.Abs(time1 - time0), limit = cfl * VoxelSize / maximumSpeed;
                T delta = limit < remaining ? limit : remaining;
                if (delta == T.Zero) break;
                next = time0 + delta;
                if (next == time0 || !T.IsFinite(next)) throw new ArithmeticException("The morphing CFL step cannot advance time at this scalar precision.");
                Euler(0, 1, 0, 1, delta);
                if (TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta2) Euler(1, 2, 1, 1, delta);
                else if (TemporalScheme == TemporalIntegrationScheme.TvdRungeKutta3) { Euler(3, 4, 1, 2, delta); Euler(1, 3, 1, 2, delta); }
            }
            finally { manager.Rebuild(); }
            time0 = next; iterations = checked(iterations + 1);
            _tracker.Track(cancellationToken);

            void Euler(int numerator, int denominator, int originalBuffer, int resultBuffer, T delta)
            {
                T alpha = T.CreateTruncating(numerator) / T.CreateTruncating(denominator), beta = T.One - alpha;
                RunLeaves(manager, index =>
                {
                    ReadOnlySpan<T> speeds = manager.GetBuffer(index, speedBuffer);
                    if (speeds[0] == Maximum) return;
                    var leaf = manager[index]; Span<T> result = manager.GetBuffer(index, resultBuffer);
                    ReadOnlySpan<T> original = originalBuffer == 0 ? leaf.Values : manager.GetBuffer(index, originalBuffer);
                    Stencil<T> stencil = SpatialScheme == BiasedGradientScheme.First ? new SevenPointStencil<T>(Grid) : new NineteenPointStencil<T>(Grid);
                    double inverseScale = 1.0 / transform.Matrix.M00; T inverseScaleSquared = T.CreateTruncating(inverseScale * inverseScale);
                    foreach (int offset in leaf.ActiveOffsets)
                    {
                        T speed = speeds[offset]; if (ApproximatelyZero(speed)) continue;
                        stencil.MoveTo(leaf.OffsetToGlobalCoord(offset), leaf.GetValue(offset));
                        T norm = uniform ? inverseScaleSquared * DifferentialOperators.IndexNormSquaredGradient(stencil, SpatialScheme) : DifferentialOperators.NormSquaredGradient(transform, stencil, SpatialScheme);
                        // Native GradientNormSqrd returns the squared norm; no square root occurs here.
                        T value = stencil.CenterValue - delta * speed * norm;
                        result[offset] = numerator == 0 ? value : alpha * original[offset] + beta * value;
                    }
                }, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested(); manager.SwapBuffer(resultBuffer);
            }
        }
        return iterations;
    }
    private T SampleSpeed(LeafManager<T> manager, int buffer, Transform transform, bool uniform, CancellationToken token)
    {
        Grid<T> target = _target; Grid<T>? mask = _mask;
        bool aligned = target.Transform.IsEquivalentTo(transform) && (mask is null || mask.Transform.IsEquivalentTo(transform));
        T minimum = _minimum, inverseRange = T.One / _range; bool invert = IsMaskInverted;
        T[] maxima = GC.AllocateUninitializedArray<T>(manager.LeafCount);
        RunLeaves(manager, index =>
        {
            var targetAccessor = target.GetAccessor(); var maskAccessor = mask?.GetAccessor();
            GridSampler<T>? targetSampler = aligned ? null : new(targetAccessor, target.Transform);
            GridSampler<T>? maskSampler = aligned || mask is null ? null : new(maskAccessor!, mask.Transform);
            var leaf = manager[index]; Span<T> speeds = manager.GetBuffer(index, buffer); bool isZero = true; T maximum = MinimumSpeed;
            foreach (int offset in leaf.ActiveOffsets)
            {
                Coord coordinate = leaf.OffsetToGlobalCoord(offset); Vec3d world = default;
                if (!aligned) world = IndexToWorld(transform, uniform, coordinate);
                T speed = speeds[offset] - (aligned ? targetAccessor.GetValue(coordinate) : targetSampler!.WorldSample(world));
                if (mask is not null)
                {
                    T value = aligned ? maskAccessor!.GetValue(coordinate) : maskSampler!.WorldSample(world);
                    T x = (value - minimum) * inverseRange;
                    T alpha = x > T.Zero ? x < T.One ? (T.CreateTruncating(3) - T.CreateTruncating(2) * x) * x * x : T.One : T.Zero;
                    speed *= invert ? T.One - alpha : alpha;
                }
                speeds[offset] = speed;
                if (!ApproximatelyZero(speed)) isZero = false;
                T magnitude = T.Abs(speed); if (maximum < magnitude) maximum = magnitude;
            }
            if (isZero) speeds[0] = Maximum;
            maxima[index] = maximum;
        }, token);
        T result = MinimumSpeed; foreach (T maximum in maxima) if (result < maximum) result = maximum; return result;
    }
    private void ValidateSchemes()
    {
        if (SpatialScheme is not (BiasedGradientScheme.First or BiasedGradientScheme.HJWeno5)) throw new ArgumentOutOfRangeException(nameof(SpatialScheme), "Native morphing supports first-order and HJ-WENO5 differences.");
        if (TemporalScheme < TemporalIntegrationScheme.TvdRungeKutta1 || TemporalScheme > TemporalIntegrationScheme.TvdRungeKutta3) throw new ArgumentOutOfRangeException(nameof(TemporalScheme));
    }
    private static bool ValidateMap(Transform transform) => transform.MapType switch
    {
        "UniformScaleMap" or "UniformScaleTranslateMap" => true,
        "UnitaryMap" or "TranslationMap" => false,
        _ => throw new ArgumentException("Native morphing supports only uniform scale/translation, unitary and translation maps.", nameof(transform))
    };
    private static Vec3d IndexToWorld(Transform transform, bool uniform, Coord coordinate)
    {
        Mat4d matrix = transform.Matrix;
        return uniform ? new(coordinate.X * matrix.M00 + matrix.M30, coordinate.Y * matrix.M00 + matrix.M31, coordinate.Z * matrix.M00 + matrix.M32) : transform.IndexToWorld(coordinate);
    }
    private static bool ApproximatelyZero(T value) => !(value > Tolerance) && !(value < -Tolerance);
    private void RunLeaves(LeafManager<T> manager, Action<int> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (GrainSize > 0) Parallel.For(0, manager.LeafCount, new ParallelOptions { CancellationToken = token }, operation);
        else for (int index = 0; index < manager.LeafCount; ++index) { token.ThrowIfCancellationRequested(); operation(index); }
    }
}
