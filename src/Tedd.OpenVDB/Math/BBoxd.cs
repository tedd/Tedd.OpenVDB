// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/BBox.h.
namespace Tedd.OpenVDB.Math;

/// <summary>Floating bounds; equal min/max bounds have zero volume but still describe a point.</summary>
public readonly struct BBoxd : IEquatable<BBoxd>
{
    private readonly Vec3d _min, _max;
    private readonly bool _initialized;
    public BBoxd(Vec3d min, Vec3d max) { _min = min; _max = max; _initialized = true; }
    public BBoxd(Vec3d min, Vec3d max, bool sorted) : this(sorted ? min : Vec3d.Min(min, max), sorted ? max : Vec3d.Max(min, max)) { }
    public BBoxd(Vec3d min, double length) : this(min, min + length) { }
    public Vec3d Min => _initialized ? _min : new Vec3d(double.MaxValue);
    public Vec3d Max => _initialized ? _max : new Vec3d(-double.MaxValue);
    public static BBoxd Empty => default;
    public bool IsEmpty => !_initialized || _min.X >= _max.X || _min.Y >= _max.Y || _min.Z >= _max.Z;
    public bool HasVolume => !IsEmpty;
    /// <summary>True for ordered bounds, including points and planes; unlike HasVolume, no positive extent is required.</summary>
    public bool IsValid => _initialized && _min.X <= _max.X && _min.Y <= _max.Y && _min.Z <= _max.Z;
    public Vec3d Extents => Max - Min;
    public double Volume => Extents.Product;
    public Vec3d Center => Min * 0.5 + Max * 0.5;
    /// <summary>Upstream floating point containment, using a 1e-15 absolute tolerance.</summary>
    public bool Contains(Vec3d point) => point.X > Min.X - 1e-15 && point.X < Max.X + 1e-15 &&
        point.Y > Min.Y - 1e-15 && point.Y < Max.Y + 1e-15 && point.Z > Min.Z - 1e-15 && point.Z < Max.Z + 1e-15;
    /// <summary>Upstream strict bounding-box containment, requiring a tolerance margin on every face.</summary>
    public bool Contains(BBoxd other) => other.Min.X - 1e-15 > Min.X && other.Max.X + 1e-15 < Max.X &&
        other.Min.Y - 1e-15 > Min.Y && other.Max.Y + 1e-15 < Max.Y && other.Min.Z - 1e-15 > Min.Z && other.Max.Z + 1e-15 < Max.Z;
    public bool Intersects(BBoxd other) => Max.X > other.Min.X - 1e-15 && Min.X < other.Max.X + 1e-15 &&
        Max.Y > other.Min.Y - 1e-15 && Min.Y < other.Max.Y + 1e-15 && Max.Z > other.Min.Z - 1e-15 && Min.Z < other.Max.Z + 1e-15;
    public BBoxd Sorted() => new(Vec3d.Min(Min, Max), Vec3d.Max(Min, Max));
    public BBoxd Expand(Vec3d point) => new(Vec3d.Min(Min, point), Vec3d.Max(Max, point));
    public BBoxd Expand(BBoxd other) => new(Vec3d.Min(Min, other.Min), Vec3d.Max(Max, other.Max));
    /// <summary>OpenVDB floating boxes expand by the absolute padding, including negative inputs.</summary>
    public BBoxd Expand(double padding) => new(Min - System.Math.Abs(padding), Max + System.Math.Abs(padding));
    public BBoxd Translate(Vec3d offset) => new(Min + offset, Max + offset);
    public BBoxd Intersection(BBoxd other) => new(Vec3d.Max(Min, other.Min), Vec3d.Min(Max, other.Max));
    /// <summary>Approximate comparison matching OpenVDB. CLR equality remains exact to preserve hash-map invariants.</summary>
    public bool ApproxEquals(BBoxd other, double tolerance = 1e-15) => Min.ApproxEquals(other.Min, tolerance) && Max.ApproxEquals(other.Max, tolerance);
    public bool Equals(BBoxd other) => Min == other.Min && Max == other.Max;
    public override bool Equals(object? obj) => obj is BBoxd other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Min, Max);
    public static bool operator ==(BBoxd a, BBoxd b) => a.Equals(b);
    public static bool operator !=(BBoxd a, BBoxd b) => !a.Equals(b);
    public override string ToString() => $"[{Min} -> {Max}]";
}
