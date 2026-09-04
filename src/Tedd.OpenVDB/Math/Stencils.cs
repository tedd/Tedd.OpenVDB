// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Stencils.h.
using System.Numerics;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Math;

/// <summary>A reusable scalar stencil snapshot with native layout ordering.</summary>
/// <remarks>MoveTo(Coord) refreshes cached values; MoveTo(Vec3d) refreshes only when the floored
/// center changes. Local SetValue operations do not write to the source grid. Instances are not thread-safe.</remarks>
public abstract class Stencil<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly Coord[] _offsets;
    private readonly Dictionary<Coord, int> _positions;
    private readonly Coord _minimumOffset, _maximumOffset;
    protected readonly T[] Buffer;
    protected readonly ValueAccessor<T> Accessor;
    private bool _initialized;
    protected Stencil(Grid<T> grid, Coord[] offsets)
    {
        Grid = grid ?? throw new ArgumentNullException(nameof(grid)); Accessor = grid.GetAccessor();
        _offsets = offsets; Buffer = new T[offsets.Length]; _positions = new(offsets.Length);
        Coord minimum = offsets[0], maximum = offsets[0];
        for (int i = 0; i < offsets.Length; ++i) { _positions.Add(offsets[i], i); minimum = Coord.Min(minimum, offsets[i]); maximum = Coord.Max(maximum, offsets[i]); }
        _minimumOffset = minimum; _maximumOffset = maximum; CenterIndex = Position(Coord.Zero);
    }
    public Grid<T> Grid { get; }
    public int Count => Buffer.Length;
    public int CenterIndex { get; }
    public Coord Center { get; private set; } = new(int.MaxValue);
    public bool IsInitialized => _initialized;
    public ReadOnlySpan<T> Values { get { RequireInitialized(); return Buffer; } }
    public T CenterValue => GetValue(CenterIndex);
    public int Position(Coord offset) => _positions.TryGetValue(offset, out int position) ? position : throw new ArgumentOutOfRangeException(nameof(offset), "Offset is not present in this stencil.");
    public Coord OffsetAt(int position) => _offsets[position];
    protected void RequireInitialized() { if (!_initialized) throw new InvalidOperationException("Move the stencil to a coordinate before reading it."); }
    public T GetValue(int position = 0) { RequireInitialized(); return Buffer[position]; }
    public T GetValue(Coord offset) => GetValue(Position(offset));
    public T GetValue(int x, int y, int z) => GetValue(new Coord(x, y, z));
    public void SetValue(Coord offset, T value) { RequireInitialized(); Buffer[Position(offset)] = value; }
    public void MoveTo(Coord center)
    {
        // Validate the whole neighborhood before changing the existing snapshot.
        AddChecked(center, _minimumOffset); AddChecked(center, _maximumOffset);
        var accessor = Accessor; T[] buffer = Buffer; Coord[] offsets = _offsets;
        for (int i = 0; i < buffer.Length; ++i) buffer[i] = accessor.GetValue(AddChecked(center, offsets[i]));
        Center = center; _initialized = true;
    }
    public void MoveTo(Coord center, T centerValue) { MoveTo(center); Buffer[CenterIndex] = centerValue; }
    public void MoveTo(Vec3d position) { Coord center = Coord.Floor(position); if (!_initialized || center != Center) MoveTo(center); }
    private static Coord AddChecked(Coord a, Coord b) => new(checked(a.X + b.X), checked(a.Y + b.Y), checked(a.Z + b.Z));
    public T Mean
    {
        get { RequireInitialized(); T sum = T.Zero; T[] values = Buffer; for (int i = 0; i < values.Length; ++i) sum += values[i]; return sum / T.CreateChecked(values.Length); }
    }
    public T Min
    {
        get { RequireInitialized(); T[] values = Buffer; T result = values[0]; for (int i = 1; i < values.Length; ++i) if (values[i] < result) result = values[i]; return result; }
    }
    public T Max
    {
        get { RequireInitialized(); T[] values = Buffer; T result = values[0]; for (int i = 1; i < values.Length; ++i) if (result < values[i]) result = values[i]; return result; }
    }
    public T Median
    {
        get { RequireInitialized(); T[] values = (T[])Buffer.Clone(); Array.Sort(values, static (a, b) => a < b ? -1 : b < a ? 1 : 0); return values[(values.Length - 1) >> 1]; }
    }
    /// <summary>Face crossing bits ordered -X,+X,-Y,+Y,-Z,+Z.</summary>
    public byte IntersectionMask(T isoValue)
    {
        bool less = CenterValue < isoValue; byte mask = 0;
        for (int axis = 0; axis < 3; ++axis)
        {
            if (less != (GetValue(FiniteDifference.Offset(Coord.Zero, (Axis)axis, -1)) < isoValue)) mask |= (byte)(1 << (axis * 2));
            if (less != (GetValue(FiniteDifference.Offset(Coord.Zero, (Axis)axis, 1)) < isoValue)) mask |= (byte)(2 << (axis * 2));
        }
        return mask;
    }
    public byte IntersectionMask() => IntersectionMask(T.Zero);
    public virtual bool Intersects(T isoValue) => IntersectionMask(isoValue) != 0;
    public bool Intersects() => Intersects(T.Zero);
    public T FirstDerivative(Axis axis, DScheme scheme = DScheme.CenteredSecond)
    {
        var range = FiniteDifference.OffsetRange(scheme); Span<T> values = stackalloc T[7]; values.Clear();
        for (int i = range.Minimum; i <= range.Maximum; ++i) values[i + 3] = GetValue(FiniteDifference.Offset(Coord.Zero, axis, i));
        return FiniteDifference.First<T>(scheme, values);
    }
    public T SecondDerivative(Axis axis, DDScheme scheme = DDScheme.CenteredSecond)
    {
        int radius = FiniteDifference.Radius(scheme); Span<T> values = stackalloc T[7]; values.Clear();
        for (int i = -radius; i <= radius; ++i) values[i + 3] = GetValue(FiniteDifference.Offset(Coord.Zero, axis, i));
        return FiniteDifference.Second<T>(scheme, values);
    }
    /// <summary>Mixed second derivative using native stencil crossdifference arithmetic ordering.</summary>
    public T MixedSecondDerivative(Axis firstAxis, Axis secondAxis, DDScheme scheme = DDScheme.CenteredSecond)
    {
        if (firstAxis == secondAxis) throw new ArgumentException("Mixed derivatives require distinct axes.");
        FiniteDifference.Radius(scheme);
        T Sample(int x, int y) => GetValue(FiniteDifference.Offset(FiniteDifference.Offset(Coord.Zero, firstAxis, x), secondAxis, y));
        T Cross(int x, int y) => Sample(x, y) - Sample(-x, y) - Sample(x, -y) + Sample(-x, -y);
        T C(double value) => T.CreateTruncating(value);
        if (scheme == DDScheme.CenteredSecond) return C(0.25) * (Sample(1, 1) + Sample(-1, -1) - Sample(1, -1) - Sample(-1, 1));
        T Inner(int y) => scheme == DDScheme.CenteredFourth
            ? C(2.0 / 3) * Cross(1, y) - C(1.0 / 12) * Cross(2, y)
            : C(0.75) * Cross(1, y) - C(0.15) * Cross(2, y) + C(1.0 / 60) * Cross(3, y);
        return scheme == DDScheme.CenteredFourth ? C(2.0 / 3) * Inner(1) - C(1.0 / 12) * Inner(2) :
            C(0.75) * Inner(1) - C(0.15) * Inner(2) + C(1.0 / 60) * Inner(3);
    }
}

public sealed class SevenPointStencil<T>(Grid<T> grid) : Stencil<T>(grid, StencilLayouts.SevenPt) where T : unmanaged, IFloatingPointIeee754<T>;
public sealed class ThirteenPointStencil<T>(Grid<T> grid) : Stencil<T>(grid, StencilLayouts.ThirteenPt) where T : unmanaged, IFloatingPointIeee754<T>;
public sealed class NineteenPointStencil<T>(Grid<T> grid) : Stencil<T>(grid, StencilLayouts.NineteenPt) where T : unmanaged, IFloatingPointIeee754<T>;
public sealed class SecondOrderDenseStencil<T>(Grid<T> grid) : Stencil<T>(grid, StencilLayouts.DensePt) where T : unmanaged, IFloatingPointIeee754<T>;
public sealed class FourthOrderDenseStencil<T>(Grid<T> grid) : Stencil<T>(grid, StencilLayouts.FourthDensePt) where T : unmanaged, IFloatingPointIeee754<T>;
public sealed class SixthOrderDenseStencil<T>(Grid<T> grid) : Stencil<T>(grid, StencilLayouts.SixthDensePt) where T : unmanaged, IFloatingPointIeee754<T>;

/// <summary>Full cubic stencil with X outermost, Z innermost, and center at (Count-1)/2.</summary>
public sealed class DenseStencil<T> : Stencil<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    public DenseStencil(Grid<T> grid, int halfWidth) : base(grid, MakeOffsets(halfWidth)) { HalfWidth = halfWidth; }
    public int HalfWidth { get; }
    private static Coord[] MakeOffsets(int radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        int width = checked(2 * radius + 1), count = checked(width * width * width); var result = new Coord[count]; int n = 0;
        for (int x = -radius; x <= radius; ++x) for (int y = -radius; y <= radius; ++y) for (int z = -radius; z <= radius; ++z) result[n++] = new(x, y, z);
        return result;
    }
}

/// <summary>Eight-corner stencil with native BoxPt ordering and trilinear interpolation.</summary>
public sealed class BoxStencil<T>(Grid<T> grid) : Stencil<T>(grid, StencilLayouts.BoxPt) where T : unmanaged, IFloatingPointIeee754<T>
{
    public override bool Intersects(T isoValue)
    {
        bool less = CenterValue < isoValue; for (int i = 1; i < 8; ++i) if (less != (GetValue(i) < isoValue)) return true; return false;
    }
    private (T X, T Y, T Z) Fraction(Vec3d indexPosition)
    {
        RequireInitialized();
        T x = T.CreateTruncating(indexPosition.X) - T.CreateTruncating(Center.X),
          y = T.CreateTruncating(indexPosition.Y) - T.CreateTruncating(Center.Y),
          z = T.CreateTruncating(indexPosition.Z) - T.CreateTruncating(Center.Z);
        if (!(x >= T.Zero && x <= T.One && y >= T.Zero && y <= T.One && z >= T.Zero && z <= T.One))
            throw new ArgumentOutOfRangeException(nameof(indexPosition), "Interpolation must lie within the buffered unit cell.");
        return (x, y, z);
    }
    public T Interpolate(Vec3d indexPosition)
    {
        var (x, y, z) = Fraction(indexPosition);
        T a = GetValue(0) + (GetValue(1) - GetValue(0)) * z, b = GetValue(3) + (GetValue(2) - GetValue(3)) * z;
        T c = a + (b - a) * y;
        a = GetValue(4) + (GetValue(5) - GetValue(4)) * z; b = GetValue(7) + (GetValue(6) - GetValue(7)) * z;
        T d = a + (b - a) * y;
        return c + (d - c) * x;
    }
    public Vec3d Gradient(Vec3d indexPosition)
    {
        var (x, y, z) = Fraction(indexPosition);
        T d0 = GetValue(1) - GetValue(0), d1 = GetValue(2) - GetValue(3), d2 = GetValue(5) - GetValue(4), d3 = GetValue(6) - GetValue(7);
        T a = d0 + (d1 - d0) * y, b = d2 + (d3 - d2) * y, gz = a + (b - a) * x;
        d0 = GetValue(0) + d0 * z; d1 = GetValue(3) + d1 * z; d2 = GetValue(4) + d2 * z; d3 = GetValue(7) + d3 * z;
        a = d0 + (d1 - d0) * y; b = d2 + (d3 - d2) * y; T gx = b - a;
        a = d1 - d0; b = d3 - d2; T gy = a + (b - a) * x;
        var result = Grid.Transform.ApplyInverseJacobianTranspose(new Vec3d(double.CreateTruncating(gx), double.CreateTruncating(gy), double.CreateTruncating(gz)), indexPosition);
        return new(double.CreateTruncating(T.CreateTruncating(result.X)), double.CreateTruncating(T.CreateTruncating(result.Y)), double.CreateTruncating(T.CreateTruncating(result.Z)));
    }
}
