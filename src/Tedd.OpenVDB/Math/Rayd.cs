// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Ray.h; explicit handling of parallel boundary rays and repeated sphere roots.
namespace Tedd.OpenVDB.Math;

/// <summary>Double-precision ray with an inclusive parameter interval; direction need not be unit length for intersection.</summary>
public readonly record struct Rayd
{
    public Rayd() : this(Vec3d.Zero, Vec3d.UnitX) { }
    public Rayd(Vec3d eye, Vec3d direction, double minTime = 1e-9, double maxTime = double.MaxValue)
    {
        if (!eye.IsFinite) throw new ArgumentException("Ray origin must be finite.", nameof(eye));
        if (!direction.IsFinite || direction == Vec3d.Zero) throw new ArgumentException("Ray direction must be finite and nonzero.", nameof(direction));
        if (double.IsNaN(minTime) || double.IsNaN(maxTime)) throw new ArgumentException("Ray times cannot be NaN.");
        Eye = eye; Direction = direction; MinTime = minTime; MaxTime = maxTime;
    }
    public Vec3d Eye { get; }
    public Vec3d Direction { get; }
    public Vec3d InverseDirection => new(1 / Direction.X, 1 / Direction.Y, 1 / Direction.Z);
    public double MinTime { get; }
    public double MaxTime { get; }
    public Vec3d At(double time) => Eye + Direction * time;
    public Vec3d Start => At(MinTime);
    public Vec3d End => At(MaxTime);
    public Vec3d Midpoint => At(MinTime * 0.5 + MaxTime * 0.5);
    public bool IsValid(double epsilon = 1e-5) => MaxTime - MinTime > epsilon;
    public bool Test(double time) => time >= MinTime && time <= MaxTime;
    public bool Intersects(BBoxd box) => Intersects(box, out _, out _);
    /// <summary>Slab intersection; zero-direction components explicitly avoid 0 * infinity on box faces.</summary>
    public bool Intersects(BBoxd box, out double enter, out double exit)
    {
        enter = MinTime; exit = MaxTime;
        if (!box.IsValid || enter > exit) return false;
        for (int axis = 0; axis < 3; ++axis)
        {
            double direction = Direction[axis], eye = Eye[axis], min = box.Min[axis], max = box.Max[axis];
            if (direction == 0)
            {
                if (eye < min || eye > max) return false;
                continue;
            }
            double a = (min - eye) / direction, b = (max - eye) / direction;
            if (a > b) (a, b) = (b, a);
            if (a > enter) enter = a;
            if (b < exit) exit = b;
            if (enter > exit) return false;
        }
        return true;
    }
    public bool Intersects(CoordBBox box, out double enter, out double exit) =>
        Intersects(box.IsEmpty ? BBoxd.Empty : new BBoxd(box.Min.AsVec3d(), box.Max.AsVec3d()), out enter, out exit);
    public bool TryClip(BBoxd box, out Rayd clipped)
    {
        bool hit = Intersects(box, out double enter, out double exit);
        clipped = hit ? new Rayd(Eye, Direction, enter, exit) : this;
        return hit;
    }
    public bool IntersectsSphere(Vec3d center, double radius, out double enter, out double exit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        enter = MinTime; exit = MaxTime;
        Vec3d origin = Eye - center;
        double a = Direction.LengthSquared, b = 2 * Direction.Dot(origin), c = origin.LengthSquared - radius * radius;
        double discriminant = b * b - 4 * a * c;
        if (discriminant < 0 || MinTime > MaxTime) return false;
        if (discriminant == 0) enter = exit = -b / (2 * a);
        else
        {
            double root = System.Math.Sqrt(discriminant);
            double q = -0.5 * (b + System.Math.CopySign(root, b));
            enter = q / a; exit = c / q;
            if (enter > exit) (enter, exit) = (exit, enter);
        }
        enter = System.Math.Max(enter, MinTime); exit = System.Math.Min(exit, MaxTime);
        return enter <= exit;
    }
    public bool IntersectsPlane(Vec3d normal, double distance, out double time)
    {
        double angle = Direction.Dot(normal);
        time = double.NaN;
        if (System.Math.Abs(angle) <= 1e-15) return false;
        time = (distance - Eye.Dot(normal)) / angle;
        return Test(time);
    }
    public bool IntersectsPlane(Vec3d normal, Vec3d point, out double time) => IntersectsPlane(normal, point.Dot(normal), out time);
    /// <summary>Transforms a ray with unit direction, normalizing its image and scaling both parameter bounds.</summary>
    public Rayd IndexToWorld(Transform transform)
    {
        if (!transform.IsLinear) throw new ArgumentException("A ray transform must be linear.", nameof(transform));
        RequireUnitDirection();
        Vec3d direction = transform.ApplyJacobian(Direction);
        double length = direction.Length;
        return new(transform.IndexToWorld(Eye), direction / length, MinTime * length, MaxTime * length);
    }
    public Rayd WorldToIndex(Transform transform)
    {
        if (!transform.IsLinear) throw new ArgumentException("A ray transform must be linear.", nameof(transform));
        RequireUnitDirection();
        Vec3d direction = transform.ApplyInverseJacobian(Direction);
        double length = direction.Length;
        return new(transform.WorldToIndex(Eye), direction / length, MinTime * length, MaxTime * length);
    }
    private void RequireUnitDirection()
    {
        if (System.Math.Abs(Direction.Length - 1) > 1e-9) throw new InvalidOperationException("Ray transformation requires a normalized direction.");
    }
}
