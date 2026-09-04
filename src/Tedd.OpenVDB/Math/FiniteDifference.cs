// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/FiniteDifference.h. Values are index-space differences (unit spacing).
using System.Numerics;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Math;

public enum DScheme
{
    Unknown = -1, CenteredSecondTwice = 0, CenteredSecond, CenteredFourth, CenteredSixth,
    ForwardFirst, ForwardSecond, ForwardThird, BackwardFirst, BackwardSecond, BackwardThird,
    ForwardWeno5, BackwardWeno5, ForwardHJWeno5, BackwardHJWeno5
}
public enum DDScheme { Unknown = -1, CenteredSecond = 0, CenteredFourth, CenteredSixth }
public enum BiasedGradientScheme { Unknown = -1, First = 0, Second, Third, Weno5, HJWeno5 }
public enum TemporalIntegrationScheme { Unknown = -1, TvdRungeKutta1 = 0, TvdRungeKutta2, TvdRungeKutta3 }

/// <summary>Native scalar finite differences for IEEE floating-point value types.</summary>
/// <remarks>Seven-value spans are ordered from offset -3 through +3. Unused entries are ignored.
/// Coordinate-based operations reject neighborhoods that cross Int32 coordinate limits.</remarks>
public static class FiniteDifference
{
    private static readonly string[] Names = ["cd_2ndt", "cd_2nd", "cd_4th", "cd_6th", "fd_1st", "fd_2nd", "fd_3rd", "bd_1st", "bd_2nd", "bd_3rd", "fd_weno5", "bd_weno5", "fd_hjweno5", "bd_hjweno5"];
    public static string SchemeToString(DScheme scheme) => scheme == DScheme.Unknown ? "unknown_ds" : (uint)scheme < Names.Length ? Names[(int)scheme] : string.Empty;
    public static DScheme ParseScheme(string name)
    {
        ArgumentNullException.ThrowIfNull(name); int index = Array.IndexOf(Names, name.Trim().ToLowerInvariant());
        return index < 0 ? DScheme.Unknown : (DScheme)index;
    }
    public static (int Minimum, int Maximum) OffsetRange(DScheme scheme) => scheme switch
    {
        DScheme.CenteredSecondTwice or DScheme.CenteredSecond => (-1, 1),
        DScheme.CenteredFourth => (-2, 2),
        DScheme.CenteredSixth => (-3, 3),
        DScheme.ForwardFirst => (0, 1),
        DScheme.ForwardSecond => (0, 2),
        DScheme.ForwardThird => (0, 3),
        DScheme.BackwardFirst => (-1, 0),
        DScheme.BackwardSecond => (-2, 0),
        DScheme.BackwardThird => (-3, 0),
        DScheme.ForwardWeno5 or DScheme.ForwardHJWeno5 => (-2, 3),
        DScheme.BackwardWeno5 or DScheme.BackwardHJWeno5 => (-3, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(scheme))
    };
    public static int Radius(DDScheme scheme) => scheme switch
    { DDScheme.CenteredSecond => 1, DDScheme.CenteredFourth => 2, DDScheme.CenteredSixth => 3, _ => throw new ArgumentOutOfRangeException(nameof(scheme)) };
    private static T C<T>(double value) where T : IFloatingPointIeee754<T> => T.CreateTruncating(value);
    private static double D<T>(T value) where T : IFloatingPointIeee754<T> => double.CreateTruncating(value);
    private static T Square<T>(T value) where T : IFloatingPointIeee754<T> => value * value;
    private static double Square(double value) => value * value;
    public static T First<T>(DScheme scheme, ReadOnlySpan<T> values) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (values.Length < 7) throw new ArgumentException("Seven samples are required.", nameof(values));
        T m3 = values[0], m2 = values[1], m1 = values[2], p0 = values[3], p1 = values[4], p2 = values[5], p3 = values[6];
        return scheme switch
        {
            DScheme.CenteredSecondTwice => p1 - m1,
            DScheme.CenteredSecond => (p1 - m1) * C<T>(0.5),
            DScheme.CenteredFourth => C<T>(2.0 / 3) * (p1 - m1) + C<T>(1.0 / 12) * (m2 - p2),
            DScheme.CenteredSixth => C<T>(0.75) * (p1 - m1) - C<T>(0.15) * (p2 - m2) + C<T>(1.0 / 60) * (p3 - m3),
            DScheme.ForwardFirst => p1 - p0,
            DScheme.BackwardFirst => -(m1 - p0),
            DScheme.ForwardSecond => ForwardSecond(p2, p1, p0),
            DScheme.BackwardSecond => -ForwardSecond(m2, m1, p0),
            DScheme.ForwardThird => ForwardThird(p3, p2, p1, p0),
            DScheme.BackwardThird => -ForwardThird(m3, m2, m1, p0),
            DScheme.ForwardWeno5 => Weno5(p3, p2, p1, p0, m1) - Weno5(p2, p1, p0, m1, m2),
            DScheme.BackwardWeno5 => -(Weno5(m3, m2, m1, p0, p1) - Weno5(m2, m1, p0, p1, p2)),
            DScheme.ForwardHJWeno5 => Weno5(p3 - p2, p2 - p1, p1 - p0, p0 - m1, m1 - m2),
            DScheme.BackwardHJWeno5 => -Weno5(m3 - m2, m2 - m1, m1 - p0, p0 - p1, p1 - p2),
            _ => throw new ArgumentOutOfRangeException(nameof(scheme))
        };
    }
    private static T ForwardSecond<T>(T p2, T p1, T p0) where T : IFloatingPointIeee754<T> =>
        C<T>(2) * p1 - (C<T>(0.5) * p2 + C<T>(1.5) * p0);
    private static T ForwardThird<T>(T p3, T p2, T p1, T p0) where T : IFloatingPointIeee754<T> =>
        C<T>(D(p3) / 3 - 1.5 * D(p2) + 3 * D(p1) - 11 * D(p0) / 6);
    public static T Second<T>(DDScheme scheme, ReadOnlySpan<T> values) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (values.Length < 7) throw new ArgumentException("Seven samples are required.", nameof(values));
        T m3 = values[0], m2 = values[1], m1 = values[2], p0 = values[3], p1 = values[4], p2 = values[5], p3 = values[6];
        return scheme switch
        {
            DDScheme.CenteredSecond => p1 + m1 - C<T>(2) * p0,
            DDScheme.CenteredFourth => C<T>(-1.0 / 12) * (p2 + m2) + C<T>(4.0 / 3) * (p1 + m1) - C<T>(2.5) * p0,
            DDScheme.CenteredSixth => C<T>(1.0 / 90) * (p3 + m3) - C<T>(3.0 / 20) * (p2 + m2) + C<T>(1.5) * (p1 + m1) - C<T>(49.0 / 18) * p0,
            _ => throw new ArgumentOutOfRangeException(nameof(scheme))
        };
    }
    /// <summary>Fifth-order WENO numerical flux; reproduces native double intermediates and the intermediate value-type cast.</summary>
    public static T Weno5<T>(T v1, T v2, T v3, T v4, T v5, float scaleSquared = 0.01f) where T : IFloatingPointIeee754<T>
    {
        double constant = 13.0 / 12, eps = 1e-6 * scaleSquared;
        T two = C<T>(2), four = C<T>(4);
        double a1 = 0.1 / Square(constant * D(Square(v1 - two * v2 + v3)) + 0.25 * Square(D(v1 - four * v2) + 3 * D(v3)) + eps);
        double a2 = 0.6 / Square(constant * D(Square(v2 - two * v3 + v4)) + 0.25 * D(Square(v2 - v4)) + eps);
        double a3 = 0.3 / Square(constant * D(Square(v3 - two * v4 + v5)) + 0.25 * Square(3 * D(v3) - D(four * v4) + D(v5)) + eps);
        T numerator = C<T>(a1 * (2 * D(v1) - 7 * D(v2) + 11 * D(v3)) + a2 * (5 * D(v3) - D(v2) + 2 * D(v4)) + a3 * (2 * D(v3) + 5 * D(v4) - D(v5)));
        return C<T>(D(numerator) / (6 * (a1 + a2 + a3)));
    }
    public static T GodunovNormSquared<T>(bool outside, T xm, T xp, T ym, T yp, T zm, T zp) where T : IFloatingPointIeee754<T>
    {
        static T Min(T a, T b) => b < a ? b : a;
        static T Max(T a, T b) => a < b ? b : a;
        T zero = T.Zero;
        return outside
            ? Max(Square(Max(xm, zero)), Square(Min(xp, zero))) + Max(Square(Max(ym, zero)), Square(Min(yp, zero))) + Max(Square(Max(zm, zero)), Square(Min(zp, zero)))
            : Max(Square(Min(xm, zero)), Square(Max(xp, zero))) + Max(Square(Min(ym, zero)), Square(Max(yp, zero))) + Max(Square(Min(zm, zero)), Square(Max(zp, zero)));
    }
    internal static Coord Offset(Coord coordinate, Axis axis, int offset) => axis switch
    {
        Axis.X => coordinate with { X = checked(coordinate.X + offset) },
        Axis.Y => coordinate with { Y = checked(coordinate.Y + offset) },
        Axis.Z => coordinate with { Z = checked(coordinate.Z + offset) },
        _ => throw new ArgumentOutOfRangeException(nameof(axis))
    };
    public static T First<T>(ValueAccessor<T> accessor, Coord coordinate, Axis axis, DScheme scheme = DScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(accessor); var range = OffsetRange(scheme);
        Offset(coordinate, axis, range.Minimum); Offset(coordinate, axis, range.Maximum);
        Span<T> values = stackalloc T[7]; values.Clear();
        for (int offset = range.Minimum; offset <= range.Maximum; ++offset) values[offset + 3] = accessor.GetValue(Offset(coordinate, axis, offset));
        return First<T>(scheme, values);
    }
    public static T Second<T>(ValueAccessor<T> accessor, Coord coordinate, Axis axis, DDScheme scheme = DDScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(accessor); int radius = Radius(scheme);
        Offset(coordinate, axis, -radius); Offset(coordinate, axis, radius);
        Span<T> values = stackalloc T[7]; values.Clear();
        for (int offset = -radius; offset <= radius; ++offset) values[offset + 3] = accessor.GetValue(Offset(coordinate, axis, offset));
        return Second<T>(scheme, values);
    }
    /// <summary>Mixed second derivative using the native random-access nesting order.</summary>
    public static T MixedSecond<T>(ValueAccessor<T> accessor, Coord coordinate, Axis firstAxis, Axis secondAxis, DDScheme scheme = DDScheme.CenteredSecond) where T : unmanaged, IFloatingPointIeee754<T>
    {
        if (firstAxis == secondAxis) throw new ArgumentException("Mixed derivatives require distinct axes.");
        int radius = Radius(scheme);
        Offset(coordinate, firstAxis, -radius); Offset(coordinate, firstAxis, radius);
        Offset(coordinate, secondAxis, -radius); Offset(coordinate, secondAxis, radius);
        if (scheme == DDScheme.CenteredSecond)
        {
            T pp = accessor.GetValue(Offset(Offset(coordinate, firstAxis, 1), secondAxis, 1));
            T pm = accessor.GetValue(Offset(Offset(coordinate, firstAxis, 1), secondAxis, -1));
            T mp = accessor.GetValue(Offset(Offset(coordinate, firstAxis, -1), secondAxis, 1));
            T mm = accessor.GetValue(Offset(Offset(coordinate, firstAxis, -1), secondAxis, -1));
            return C<T>(0.25) * (pp + mm - pm - mp);
        }
        DScheme first = scheme == DDScheme.CenteredFourth ? DScheme.CenteredFourth : DScheme.CenteredSixth;
        T t1 = First(accessor, Offset(coordinate, secondAxis, 1), firstAxis, first) - First(accessor, Offset(coordinate, secondAxis, -1), firstAxis, first);
        T t2 = First(accessor, Offset(coordinate, secondAxis, 2), firstAxis, first) - First(accessor, Offset(coordinate, secondAxis, -2), firstAxis, first);
        if (scheme == DDScheme.CenteredFourth) return C<T>(2.0 / 3) * t1 - C<T>(1.0 / 12) * t2;
        T t3 = First(accessor, Offset(coordinate, secondAxis, 3), firstAxis, first) - First(accessor, Offset(coordinate, secondAxis, -3), firstAxis, first);
        return C<T>(0.75) * t1 - C<T>(0.15) * t2 + C<T>(1.0 / 60) * t3;
    }
}
