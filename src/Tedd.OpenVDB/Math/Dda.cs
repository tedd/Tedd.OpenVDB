// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/math/DDA.h.

namespace Tedd.OpenVDB.Math;

/// <summary>Digital differential analyzer traversing voxels or aligned nodes intersected by an index-space ray.</summary>
/// <remarks>
/// Log2Dim zero traverses voxels; three traverses leaf-sized blocks. Time bounds are inclusive.
/// Tied crossings advance Z before Y before X, preserving upstream zero-length edge/corner visits.
/// The mutable value type must be initialized with a constructor or Initialize before use.
/// </remarks>
public struct Dda
{
    private Coord _step;
    private Vec3d _delta;
    private Vec3d _next;
    private bool _initialized;

    public Dda(Rayd ray, int log2Dim = 0) => Initialize(ray, ray.MinTime, ray.MaxTime, log2Dim);
    public Dda(Rayd ray, double startTime, double maxTime, int log2Dim = 0) => Initialize(ray, startTime, maxTime, log2Dim);

    public Coord Voxel { get; private set; }
    public double Time { get; private set; }
    public double MaxTime { get; private set; }
    public int Log2Dim { get; private set; }
    public int Dim => 1 << Log2Dim;

    /// <summary>The earlier of the next crossing and the inclusive end time.</summary>
    public double Next
    {
        get
        {
            EnsureInitialized();
            return System.Math.Min(MaxTime, System.Math.Min(_next.X, System.Math.Min(_next.Y, _next.Z)));
        }
    }

    public void Initialize(Rayd ray, int log2Dim = 0) => Initialize(ray, ray.MinTime, ray.MaxTime, log2Dim);

    public void Initialize(Rayd ray, double startTime, double maxTime, int log2Dim = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(log2Dim);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(log2Dim, 12);
        if (!double.IsFinite(startTime)) throw new ArgumentOutOfRangeException(nameof(startTime), "Start time must be finite.");
        if (!double.IsFinite(maxTime) || maxTime < startTime) throw new ArgumentOutOfRangeException(nameof(maxTime), "End time must be finite and no earlier than start time.");
        if (!ray.Eye.IsFinite || !ray.Direction.IsFinite || ray.Direction == Vec3d.Zero)
            throw new ArgumentException("The ray must have a finite origin and finite nonzero direction.", nameof(ray));
        var position = ray.At(startTime);
        if (!position.IsFinite) throw new ArgumentOutOfRangeException(nameof(startTime), "The ray position must be finite.");
        var floor = Coord.Floor(position);
        var dimension = 1 << log2Dim;
        var voxel = new Coord(floor.X & -dimension, floor.Y & -dimension, floor.Z & -dimension);
        var x = InitializeAxis(voxel.X, position.X, ray.Direction.X, startTime, dimension);
        var y = InitializeAxis(voxel.Y, position.Y, ray.Direction.Y, startTime, dimension);
        var z = InitializeAxis(voxel.Z, position.Z, ray.Direction.Z, startTime, dimension);
        // Publish only after the complete new traversal has been validated.
        Voxel = voxel;
        Time = startTime;
        MaxTime = maxTime;
        Log2Dim = log2Dim;
        _step = new Coord(x.Step, y.Step, z.Step);
        _next = new Vec3d(x.Next, y.Next, z.Next);
        _delta = new Vec3d(x.Delta, y.Delta, z.Delta);
        _initialized = true;
    }

    private static (int Step, double Next, double Delta) InitializeAxis(int voxel, double position, double direction, double startTime, int dimension)
    {
        if (direction == 0) return (0, double.PositiveInfinity, double.PositiveInfinity);
        var step = direction > 0 ? dimension : -dimension;
        // Compute the boundary in double to avoid overflowing the signed coordinate domain.
        var boundary = direction > 0 ? (double)voxel + dimension : voxel;
        var inverse = 1 / direction;
        var offset = boundary - position;
        // Preserve upstream multiply-by-inverse rounding when finite. Division handles
        // subnormal directions whose reciprocal overflows, including an exact boundary hit.
        var next = startTime + (double.IsFinite(inverse) ? offset * inverse : offset / direction);
        return (step, next, double.IsFinite(inverse) ? step * inverse : step / direction);
    }

    /// <summary>Advances one axis and returns whether its crossing time is at or before MaxTime.</summary>
    /// <remarks>
    /// As in OpenVDB, a finite step past MaxTime still advances Time and Voxel before returning false.
    /// Crossing outside the signed 32-bit grid domain throws instead of wrapping coordinates.
    /// </remarks>
    public bool Step()
    {
        EnsureInitialized();
        var axis = _next.Y <= _next.X ? 1 : 0;
        if (_next.Z <= _next[axis]) axis = 2;
        var crossing = _next[axis];
        if (!double.IsFinite(crossing)) return false;
        var voxel = Voxel;
        var step = _step[axis];
        var coordinate = checked(voxel[axis] + step);
        Voxel = axis switch
        {
            0 => new Coord(coordinate, voxel.Y, voxel.Z),
            1 => new Coord(voxel.X, coordinate, voxel.Z),
            _ => new Coord(voxel.X, voxel.Y, coordinate)
        };
        Time = crossing;
        var nextTime = crossing + _delta[axis];
        _next = axis switch
        {
            0 => new Vec3d(nextTime, _next.Y, _next.Z),
            1 => new Vec3d(_next.X, nextTime, _next.Z),
            _ => new Vec3d(_next.X, _next.Y, nextTime)
        };
        return crossing <= MaxTime;
    }

    private readonly void EnsureInitialized()
    {
        if (!_initialized) throw new InvalidOperationException("Initialize the DDA before traversal.");
    }
}
