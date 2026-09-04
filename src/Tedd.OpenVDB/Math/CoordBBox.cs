// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Coord.h.
using System.Collections;

namespace Tedd.OpenVDB.Math;

/// <summary>Inclusive integer bounds. A single coordinate has volume one; default is empty.</summary>
public readonly struct CoordBBox : IEquatable<CoordBBox>, IEnumerable<Coord>
{
    private readonly Coord _min, _max;
    private readonly bool _initialized;
    public CoordBBox(Coord min, Coord max) { _min = min; _max = max; _initialized = true; }
    public CoordBBox(int xMin, int yMin, int zMin, int xMax, int yMax, int zMax)
        : this(new Coord(xMin, yMin, zMin), new Coord(xMax, yMax, zMax)) { }
    public Coord Min => _initialized ? _min : Coord.MaxValue;
    public Coord Max => _initialized ? _max : Coord.MinValue;
    public static CoordBBox Empty => default;
    public static CoordBBox Infinite => new(Coord.MinValue, Coord.MaxValue);
    public bool IsEmpty => !_initialized || _min.X > _max.X || _min.Y > _max.Y || _min.Z > _max.Z;
    public bool HasVolume => !IsEmpty;
    /// <summary>64-bit components accommodate the full 2^32-coordinate extent.</summary>
    public (long X, long Y, long Z) Dimensions => IsEmpty ? (0, 0, 0) : ((long)_max.X - _min.X + 1, (long)_max.Y - _min.Y + 1, (long)_max.Z - _min.Z + 1);
    /// <summary>Exact volume, including the full coordinate domain (2^96).</summary>
    public UInt128 Volume128 { get { var d = Dimensions; return (UInt128)d.X * (UInt128)d.Y * (UInt128)d.Z; } }
    /// <summary>Volume; throws OverflowException when greater than UInt64.MaxValue.</summary>
    public ulong Volume => checked((ulong)Volume128);
    public Vec3d Center => new(((double)Min.X + Max.X) * 0.5, ((double)Min.Y + Max.Y) * 0.5, ((double)Min.Z + Max.Z) * 0.5);
    public bool Contains(Coord coordinate) => !IsEmpty && !Coord.LessThan(coordinate, _min) && !Coord.LessThan(_max, coordinate);
    public bool Contains(CoordBBox other) => !IsEmpty && !other.IsEmpty && Contains(other.Min) && Contains(other.Max);
    public bool Intersects(CoordBBox other) => !IsEmpty && !other.IsEmpty && !Coord.LessThan(_max, other.Min) && !Coord.LessThan(other.Max, _min);
    public CoordBBox Intersection(CoordBBox other) => !Intersects(other) ? Empty : new(Coord.Max(Min, other.Min), Coord.Min(Max, other.Max));
    public CoordBBox Expand(Coord coordinate) => IsEmpty ? new(coordinate, coordinate) : new(Coord.Min(_min, coordinate), Coord.Max(_max, coordinate));
    public CoordBBox Expand(CoordBBox other) => other.IsEmpty ? this : IsEmpty ? other : new(Coord.Min(_min, other.Min), Coord.Max(_max, other.Max));
    /// <summary>Positive padding expands; negative padding contracts. Coordinate overflow throws.</summary>
    public CoordBBox Expand(int padding) => IsEmpty ? this : new(
        new Coord(checked((int)((long)_min.X - padding)), checked((int)((long)_min.Y - padding)), checked((int)((long)_min.Z - padding))),
        new Coord(checked((int)((long)_max.X + padding)), checked((int)((long)_max.Y + padding)), checked((int)((long)_max.Z + padding))));
    public CoordBBox Translate(Coord offset) => IsEmpty ? this : new(
        new Coord(checked(_min.X + offset.X), checked(_min.Y + offset.Y), checked(_min.Z + offset.Z)),
        new Coord(checked(_max.X + offset.X), checked(_max.Y + offset.Y), checked(_max.Z + offset.Z)));
    public static CoordBBox CreateCube(Coord min, int dimension)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dimension);
        if (dimension == 0) return Empty;
        int delta = dimension - 1;
        return new(min, new Coord(checked(min.X + delta), checked(min.Y + delta), checked(min.Z + delta)));
    }
    public bool Equals(CoordBBox other) => Min == other.Min && Max == other.Max;
    public override bool Equals(object? obj) => obj is CoordBBox other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Min, Max);
    public static bool operator ==(CoordBBox left, CoordBBox right) => left.Equals(right);
    public static bool operator !=(CoordBBox left, CoordBBox right) => !left.Equals(right);
    public override string ToString() => IsEmpty ? "[]" : $"[{Min} -> {Max}]";
    /// <summary>Allocation-free enumeration with Z changing fastest.</summary>
    public Enumerator GetEnumerator() => new(this, true);
    IEnumerator<Coord> IEnumerable<Coord>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public CoordinateEnumerable EnumerateXYZ() => new(this, false);
    public CoordinateEnumerable EnumerateZYX() => new(this, true);
    public readonly struct CoordinateEnumerable(CoordBBox box, bool zFastest) : IEnumerable<Coord>
    {
        public Enumerator GetEnumerator() => new(box, zFastest);
        IEnumerator<Coord> IEnumerable<Coord>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public struct Enumerator : IEnumerator<Coord>
    {
        private readonly CoordBBox _box;
        private readonly bool _zFastest;
        private Coord _current;
        private byte _state;
        internal Enumerator(CoordBBox box, bool zFastest) { _box = box; _zFastest = zFastest; _current = default; _state = 0; }
        public readonly Coord Current => _state == 1 ? _current : throw new InvalidOperationException("The enumerator is not positioned on a coordinate.");
        readonly object IEnumerator.Current => Current;
        public bool MoveNext()
        {
            if (_state == 2) return false;
            if (_state == 0)
            {
                if (_box.IsEmpty) { _state = 2; return false; }
                _current = _box.Min; _state = 1; return true;
            }
            Coord min = _box.Min, max = _box.Max, current = _current;
            if (_zFastest)
            {
                if (current.Z < max.Z) { _current = current with { Z = current.Z + 1 }; return true; }
                if (current.Y < max.Y) { _current = new(current.X, current.Y + 1, min.Z); return true; }
                if (current.X < max.X) { _current = new(current.X + 1, min.Y, min.Z); return true; }
            }
            else
            {
                if (current.X < max.X) { _current = current with { X = current.X + 1 }; return true; }
                if (current.Y < max.Y) { _current = new(min.X, current.Y + 1, current.Z); return true; }
                if (current.Z < max.Z) { _current = new(min.X, min.Y, current.Z + 1); return true; }
            }
            _state = 2; return false;
        }
        public void Reset() { _state = 0; _current = default; }
        public readonly void Dispose() { }
    }
}
