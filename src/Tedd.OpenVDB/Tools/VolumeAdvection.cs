// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/VolumeAdvect.h.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

public enum VolumeAdvectionScheme { SemiLagrangian, Midpoint, RungeKutta3, RungeKutta4, MacCormack, Bfecc }
public enum VolumeAdvectionLimiter { None, Clamp, Revert }

/// <summary>Sparse semi-Lagrangian volume advection in a static Vec3f or Vec3d velocity grid.</summary>
/// <remarks>The maximum velocity is captured at construction; tracing reads the velocity grid when
/// Advect is called. Each call returns an independent grid and leaves both inputs unchanged, including
/// on cancellation. Grids and settings must not be modified concurrently. The native whole-vector
/// limiter extrema, scalar background tolerances, and masked topology restoration are preserved.</remarks>
public sealed class VolumeAdvection<TVelocity> where TVelocity : unmanaged
{
    private readonly Grid<TVelocity> _velocity;
    private int _subSteps = 1;
    public VolumeAdvection(Grid<TVelocity> velocity, bool staggeredVelocity = false)
    {
        ArgumentNullException.ThrowIfNull(velocity); VelocityValue<TVelocity>.Validate();
        _velocity = velocity; StaggeredVelocity = staggeredVelocity;
        double maximum = -double.MaxValue;
        foreach (var entry in velocity.Tree.EnumerateValues(ValueIterationMode.All))
        {
            double magnitude = Magnitude(entry.Value); maximum = magnitude < maximum ? maximum : magnitude;
        }
        double background = Magnitude(velocity.Background); MaxVelocity = background < maximum ? maximum : background;
    }
    public bool StaggeredVelocity { get; }
    public double MaxVelocity { get; }
    public VolumeAdvectionScheme Scheme { get; set; }
    public VolumeAdvectionLimiter Limiter { get; set; } = VolumeAdvectionLimiter.Clamp;
    public int GrainSize { get; set; } = 128;
    public int SubSteps { get => _subSteps; set => _subSteps = System.Math.Max(1, value); }
    public int SpatialOrder => Scheme is VolumeAdvectionScheme.MacCormack or VolumeAdvectionScheme.Bfecc ? 2 : 1;
    public int TemporalOrder => Scheme switch { VolumeAdvectionScheme.SemiLagrangian => 1, VolumeAdvectionScheme.Midpoint or VolumeAdvectionScheme.MacCormack or VolumeAdvectionScheme.Bfecc => 2, VolumeAdvectionScheme.RungeKutta3 => 3, VolumeAdvectionScheme.RungeKutta4 => 4, _ => 0 };
    public bool IsLimiterOn => SpatialOrder == 2 && Limiter != VolumeAdvectionLimiter.None;
    /// <summary>Optional ceiling on each substep's face-connected dilation radius.</summary>
    public int MaximumDilation { get; set; } = int.MaxValue;
    /// <summary>Optional ceiling on leaves allocated while materializing existing active tiles.</summary>
    public ulong MaximumVoxelizedLeaves { get; set; } = ulong.MaxValue;
    public int GetMaxDistance<T>(Grid<T> input, double timeStep) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.Transform.HasUniformScale) throw new ArgumentException("The volume grid must have uniform voxels.", nameof(input));
        if (!double.IsFinite(timeStep)) throw new ArgumentOutOfRangeException(nameof(timeStep));
        double distance = System.Math.Ceiling(MaxVelocity * System.Math.Abs(timeStep) / input.Transform.VoxelSize.X);
        if (!double.IsFinite(distance) || distance < 0 || distance > int.MaxValue) throw new ArithmeticException("The advection dilation distance is not a finite Int32.");
        return (int)distance;
    }
    /// <summary>Advects supported scalar/vector values using point, box or quadratic volume sampling.</summary>
    public Grid<T> Advect<T>(Grid<T> input, double timeStep, SamplingOrder sampleOrder = SamplingOrder.Box, CancellationToken cancellationToken = default) where T : unmanaged =>
        AdvectCore<T, byte>(input, null, timeStep, sampleOrder, cancellationToken);
    public Grid<T> Advect<T, TMask>(Grid<T> input, Grid<TMask> mask, double timeStep, SamplingOrder sampleOrder = SamplingOrder.Box, CancellationToken cancellationToken = default) where T : unmanaged where TMask : unmanaged
    {
        ArgumentNullException.ThrowIfNull(mask); return AdvectCore(input, mask, timeStep, sampleOrder, cancellationToken);
    }
    public Grid<T> Advect<T>(Grid<T> input, MaskGrid mask, double timeStep, SamplingOrder sampleOrder = SamplingOrder.Box, CancellationToken cancellationToken = default) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(mask); return AdvectCore(input, mask.Storage, timeStep, sampleOrder, cancellationToken);
    }
    private Grid<T> AdvectCore<T, TMask>(Grid<T> input, Grid<TMask>? mask, double timeStep, SamplingOrder sampleOrder, CancellationToken token) where T : unmanaged where TMask : unmanaged
    {
        ArgumentNullException.ThrowIfNull(input); token.ThrowIfCancellationRequested();
        VolumeAdvectionArithmetic<T>.Validate(); SamplingKernel<T, AccessorSampleSource<T>>.Validate(sampleOrder, false);
        if ((uint)Scheme > (uint)VolumeAdvectionScheme.Bfecc) throw new ArgumentOutOfRangeException(nameof(Scheme));
        if ((uint)Limiter > (uint)VolumeAdvectionLimiter.Revert) throw new ArgumentOutOfRangeException(nameof(Limiter));
        ArgumentOutOfRangeException.ThrowIfNegative(GrainSize); ArgumentOutOfRangeException.ThrowIfNegative(MaximumDilation);
        if (mask is not null && !input.Transform.IsEquivalentTo(mask.Transform)) throw new ArgumentException("The volume and mask transforms must be aligned.", nameof(mask));
        double dt = timeStep / SubSteps; int distance = GetMaxDistance(input, dt);
        if (distance > MaximumDilation) throw new InvalidOperationException("Advection exceeds MaximumDilation.");
        Grid<T> source = input;
        for (int step = 0; step < SubSteps; ++step)
        {
            token.ThrowIfCancellationRequested(); Grid<T> result = source.DeepCopy();
            if (distance > 0)
            {
                result.Tree.VoxelizeActiveTiles(MaximumVoxelizedLeaves);
                for (int n = 0; n < distance; ++n) { token.ThrowIfCancellationRequested(); Morphology.DilateActiveValues(result.Tree, 1, NearestNeighbors.Face, TilePolicy.IgnoreTiles); }
            }
            if (mask is not null) { result.Tree.TopologyIntersection(mask.Tree); Prune.PruneInactive(result.Tree); }
            Cook(result, source, dt, sampleOrder, token);
            if (mask is not null) result.Tree.TopologyUnion(input.Tree);
            source = result;
        }
        token.ThrowIfCancellationRequested(); return source;
    }
    private void Cook<T>(Grid<T> output, Grid<T> input, double timeStep, SamplingOrder sampleOrder, CancellationToken token) where T : unmanaged
    {
        var leaves = new LeafManager<T>(output.Tree, SpatialOrder == 2 ? 1 : 0);
        bool single = typeof(TVelocity) == typeof(Vec3f);
        double dt = single ? (float)-timeStep : -timeStep;
        int order = SpatialOrder == 2 ? 1 : TemporalOrder;
        var integrator = new VelocityIntegrator<TVelocity>(_velocity, SamplingOrder.Box, StaggeredVelocity);
        Trace(input, 0, dt);
        if (SpatialOrder == 2)
        {
            Trace(output, 1, -dt);
            RunLeaves(leaves, index =>
            {
                var leaf = leaves[index]; var accessor = input.Tree.GetAccessor(); ReadOnlySpan<T> backward = leaves.GetBuffer(index, 1);
                foreach (int offset in leaf.ActiveOffsets)
                {
                    T initial = accessor.GetValue(leaf.OffsetToGlobalCoord(offset));
                    leaf.SetValueOnly(offset, VolumeAdvectionArithmetic<T>.Correct(leaf.GetValue(offset), initial, backward[offset], Scheme == VolumeAdvectionScheme.Bfecc, single));
                }
            }, token);
            if (Scheme == VolumeAdvectionScheme.Bfecc) { Trace(output, 1, dt); leaves.SwapBuffer(1); }
        }
        RunLeaves(leaves, index =>
        {
            var leaf = leaves[index]; var accessor = input.Tree.GetAccessor(); var velocity = integrator.Copy(); Span<T> data = stackalloc T[8];
            foreach (int offset in leaf.ActiveOffsets)
            {
                T value = leaf.GetValue(offset);
                if (IsLimiterOn)
                {
                    Vec3d position = input.WorldToIndex(velocity.RungeKutta(dt, input.IndexToWorld(leaf.OffsetToGlobalCoord(offset)), 1));
                    Coord cell = SamplingCoordinates.Floor(position, 0, 1); int i = 0;
                    for (int x = 0; x < 2; ++x) for (int y = 0; y < 2; ++y) for (int z = 0; z < 2; ++z) data[i++] = accessor.GetValue(cell + new Coord(x, y, z));
                    T minimum = data[0], maximum = data[0];
                    for (i = 1; i < 8; ++i)
                    {
                        minimum = VolumeAdvectionArithmetic<T>.Less(minimum, data[i]) ? minimum : data[i];
                        maximum = VolumeAdvectionArithmetic<T>.Less(maximum, data[i]) ? data[i] : maximum;
                    }
                    if (Limiter == VolumeAdvectionLimiter.Clamp) value = VolumeAdvectionArithmetic<T>.Clamp(value, minimum, maximum);
                    else if (VolumeAdvectionArithmetic<T>.Less(value, minimum) || VolumeAdvectionArithmetic<T>.Less(maximum, value)) value = SamplingKernel<T, AccessorSampleSource<T>>.Interpolate(data, position - cell.AsVec3d(), SamplingOrder.Box);
                }
                if (VolumeAdvectionArithmetic<T>.IsBackground(value, input.Background)) leaf.SetValueOff(offset, input.Background);
                else leaf.SetValueOnly(offset, value);
            }
        }, token);
        Prune.PruneInactive(output.Tree);
        void Trace(Grid<T> sampled, int buffer, double traceTime)
        {
            RunLeaves(leaves, index =>
            {
                var leaf = leaves[index]; var source = new AccessorSampleSource<T>(sampled.Tree.GetAccessor()); var velocity = integrator.Copy();
                Span<T> destination = buffer == 0 ? [] : leaves.GetBuffer(index, buffer);
                foreach (int offset in leaf.ActiveOffsets)
                {
                    Vec3d world = velocity.RungeKutta(traceTime, input.IndexToWorld(leaf.OffsetToGlobalCoord(offset)), order);
                    SamplingKernel<T, AccessorSampleSource<T>>.Sample(source, input.WorldToIndex(world), sampleOrder, false, out T value);
                    if (buffer == 0) leaf.SetValueOnly(offset, value); else destination[offset] = value;
                }
            }, token);
        }
    }
    private void RunLeaves<T>(LeafManager<T> leaves, Action<int> operation, CancellationToken token) where T : unmanaged
    {
        if (GrainSize > 0 && leaves.LeafCount > GrainSize)
            Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, leaves.LeafCount, GrainSize), new ParallelOptions { CancellationToken = token }, range => { for (int i = range.Item1; i < range.Item2; ++i) { token.ThrowIfCancellationRequested(); operation(i); } });
        else for (int i = 0; i < leaves.LeafCount; ++i) { token.ThrowIfCancellationRequested(); operation(i); }
    }
    private static double Magnitude(TVelocity value)
    {
        if (typeof(TVelocity) == typeof(Vec3f)) return (float)System.Math.Sqrt(VelocityValue<TVelocity>.ToSingle(value).LengthSquared);
        return VelocityValue<TVelocity>.ToDouble(value).Length;
    }
}
