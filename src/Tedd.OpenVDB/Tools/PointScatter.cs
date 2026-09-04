// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from tools/PointScatter.h. Random stream order matches the pinned MSVC native oracle.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Common state for native point scattering into an append callback.</summary>
/// <remarks>Points are world-space positions. Counters and the copied random engine persist across calls. The input grid must remain read-only during scattering. Cancellation and a point budget retain points already appended. Instances are not reentrant.</remarks>
public abstract class BasePointScatter
{
    private readonly Action<Vec3d> _append;
    protected readonly Random01 Random;
    private MapBase? _map;
    private Action? _validate;
    private CancellationToken _token;
    private ulong _callPointCount;
    private int _busy;
    protected BasePointScatter(Action<Vec3d> append, IRandomEngine32 engine, double spread)
    {
        ArgumentNullException.ThrowIfNull(append); _append = append; Random = new(engine);
        Spread = spread > 0 ? spread < 1 ? spread : 1 : 0;
    }
    /// <summary>Total points successfully appended since construction.</summary>
    public ulong PointCount { get; private set; }
    /// <summary>Active virtual voxels in the most recently reached native counting stage.</summary>
    public ulong VoxelCount { get; protected set; }
    public double Spread { get; }
    /// <summary>Maximum points appended per invocation. Defaults to no additional limit.</summary>
    public ulong MaximumPoints { get; set; } = ulong.MaxValue;
    protected void Begin<T>(Grid<T> grid, CancellationToken token) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(grid); token.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new InvalidOperationException("Scatter instances cannot be used recursively or concurrently.");
        VdbTree<T> tree = grid.Tree; long version = tree.TopologyVersion; Transform transform = grid.Transform; MapBase map = transform.BaseMap;
        _map = map; _token = token; _callPointCount = 0;
        _validate = () => { if (!ReferenceEquals(grid.Tree, tree) || tree.TopologyVersion != version || !ReferenceEquals(grid.Transform, transform) || !ReferenceEquals(transform.BaseMap, map)) throw new InvalidOperationException("The source tree or transform changed during point scattering."); };
    }
    protected void End() { _validate = null; _map = null; Volatile.Write(ref _busy, 0); }
    protected void Check() { _token.ThrowIfCancellationRequested(); _validate!(); }
    protected void Add<T>(VoxelValue<T> value) where T : unmanaged
    {
        Check(); if (_callPointCount >= MaximumPoints || PointCount == ulong.MaxValue) throw new InvalidOperationException("The point scattering budget was exceeded.");
        Coord min = value.Coordinate; var dimensions = value.Bounds.Dimensions;
        // MSVC evaluates the native Vec3 constructor arguments from right to left.
        double z = .5 + Spread * (Random.Next() - .5), y = .5 + Spread * (Random.Next() - .5), x = .5 + Spread * (Random.Next() - .5);
        Vec3d position = new(min.X - .5 + dimensions.X * x, min.Y - .5 + dimensions.Y * y, min.Z - .5 + dimensions.Z * z);
        _append(_map!.ApplyMap(position)); ++PointCount; ++_callPointCount; Check();
    }
    protected void RequireRemaining(ulong count) { if (count > MaximumPoints - System.Math.Min(MaximumPoints, _callPointCount) || count > ulong.MaxValue - PointCount) throw new InvalidOperationException("The point scattering budget was exceeded."); }
    protected static int NativeInteger(double value)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value >= 2147483648.0) throw new ArgumentOutOfRangeException(nameof(value), "The native per-entry point count must fit in a signed 32-bit integer.");
        return (int)value;
    }
}

/// <summary>Uniformly distributes a fixed count or density over active voxels and tiles.</summary>
public sealed class UniformPointScatter : BasePointScatter
{
    public UniformPointScatter(Action<Vec3d> append, ulong pointCount, IRandomEngine32 engine, double spread = 1) : base(append, engine, spread) => TargetPointCount = pointCount;
    public UniformPointScatter(Action<Vec3d> append, float pointsPerVolume, IRandomEngine32 engine, double spread = 1) : base(append, engine, spread) => PointsPerVolume = pointsPerVolume;
    public ulong TargetPointCount { get; private set; }
    /// <summary>Native float density; a fixed-count first call sets this and subsequent positive-density calls recalculate the target.</summary>
    public float PointsPerVolume { get; private set; }
    public bool Scatter<T>(Grid<T> grid, CancellationToken cancellationToken = default) where T : unmanaged
    {
        Begin(grid, cancellationToken);
        try
        {
            VoxelCount = grid.ActiveVoxelCount; if (VoxelCount == 0) return false;
            double volume = grid.Transform.VoxelVolume;
            if (PointsPerVolume > 0)
            {
                double count = PointsPerVolume * volume * (double)VoxelCount;
                if (!double.IsFinite(count) || count < 0 || count >= 18446744073709551616.0) throw new ArgumentOutOfRangeException(nameof(PointsPerVolume), "The density must produce a finite unsigned point count.");
                TargetPointCount = (ulong)count;
            }
            else if (TargetPointCount > 0) PointsPerVolume = (float)TargetPointCount / (float)(volume * (double)VoxelCount);
            else return false;
            RequireRemaining(TargetPointCount);
            if (TargetPointCount > (ulong)Array.MaxLength) throw new ArgumentOutOfRangeException(nameof(TargetPointCount), "Uniform scatter sorts one managed index per point.");
            if (TargetPointCount == 0) return true;
            ulong[] ids = GC.AllocateUninitializedArray<ulong>((int)TargetPointCount); var selection = new RandomUInt64(Random.EngineSnapshot, 0, VoxelCount - 1);
            for (int i = 0; i < ids.Length; ++i) { if ((i & 4095) == 0) Check(); ids[i] = selection.Next(); }
            Array.Sort(ids); Check();
            using var values = grid.Tree.EnumerateValues().GetEnumerator();
            if (!values.MoveNext()) throw new InvalidOperationException("Active topology changed during point scattering.");
            ulong end = values.Current.VoxelCount;
            foreach (ulong id in ids)
            {
                while (end <= id) { Check(); if (!values.MoveNext()) throw new InvalidOperationException("Active topology changed during point scattering."); end = checked(end + values.Current.VoxelCount); }
                Add(values.Current);
            }
            return true;
        }
        finally { End(); }
    }
}

/// <summary>Scatters a fixed expected number of points per active voxel, sampling tiles without voxelization.</summary>
public sealed class DenseUniformPointScatter : BasePointScatter
{
    public DenseUniformPointScatter(Action<Vec3d> append, float pointsPerVoxel, IRandomEngine32 engine, double spread = 1) : base(append, engine, spread) => PointsPerVoxel = pointsPerVoxel;
    public float PointsPerVoxel { get; }
    public bool Scatter<T>(Grid<T> grid, CancellationToken cancellationToken = default) where T : unmanaged
    {
        Begin(grid, cancellationToken);
        try
        {
            if (PointsPerVoxel < 1e-6) return false;
            VoxelCount = grid.ActiveVoxelCount; if (VoxelCount == 0) return false;
            int ppv = NativeInteger(System.Math.Floor(PointsPerVoxel)); double delta = PointsPerVoxel - (float)ppv; bool fractional = !(delta >= -1e-6 && delta <= 1e-6);
            foreach (VoxelValue<T> value in grid.Tree.EnumerateValues())
            {
                Check(); double expected = value.IsVoxel ? ppv : PointsPerVoxel * (float)value.VoxelCount;
                int n = value.IsVoxel ? ppv : NativeInteger(System.Math.Floor(expected)); RequireRemaining((ulong)n);
                for (int i = 0; i < n; ++i) Add(value);
                if (value.IsVoxel ? fractional && Random.Next() < delta : Random.Next() < expected - n) Add(value);
            }
            return true;
        }
        finally { End(); }
    }
}

/// <summary>Scatters according to density multiplied by each stored active scalar value and voxel count.</summary>
public sealed class NonUniformPointScatter : BasePointScatter
{
    public NonUniformPointScatter(Action<Vec3d> append, float pointsPerVolume, IRandomEngine32 engine, double spread = 1) : base(append, engine, spread) => PointsPerVolume = pointsPerVolume;
    public float PointsPerVolume { get; }
    public bool Scatter<T>(Grid<T> grid, CancellationToken cancellationToken = default) where T : unmanaged, INumberBase<T> => Scatter(grid, static x => double.CreateChecked(x), cancellationToken);
    public bool Scatter(Grid<bool> grid, CancellationToken cancellationToken = default) => Scatter(grid, static x => x ? 1.0 : 0.0, cancellationToken);
    /// <summary>Uses an explicit scalar conversion for user-defined stored values.</summary>
    public bool Scatter<T>(Grid<T> grid, Func<T, double> densityValue, CancellationToken cancellationToken = default) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(densityValue); Begin(grid, cancellationToken);
        try
        {
            if (PointsPerVolume <= 0) return false;
            VoxelCount = grid.ActiveVoxelCount; if (VoxelCount == 0) return false;
            Vec3d size = grid.Transform.VoxelSize; double volume = size.X * size.Y * size.Z, ppv = PointsPerVolume * volume;
            foreach (VoxelValue<T> value in grid.Tree.EnumerateValues())
            {
                Check(); double expected = densityValue(value.Value) * ppv * (double)value.VoxelCount; Check(); int n = NativeInteger(expected);
                if (n > 0) RequireRemaining((ulong)n);
                for (int i = 0; i < n; ++i) Add(value);
                if (Random.Next() < expected - n) Add(value);
            }
            return true;
        }
        finally { End(); }
    }
}
