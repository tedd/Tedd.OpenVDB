// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Math.h.
using System.Numerics;
namespace Tedd.OpenVDB.Math;

/// <summary>OpenVDB scalar arithmetic conventions, including unordered comparisons and source evaluation order.</summary>
/// <remarks>Signed integer arithmetic must remain within its representable range, as in the native source.
/// Half intermediate promotion is preserved where the native operators promote to float.</remarks>
public static partial class VdbMath
{
    public static T Pi<T>() where T : IFloatingPointIeee754<T> => T.Pi;
    public static T Tolerance<T>() where T : INumber<T> => typeof(T) == typeof(float) ? T.CreateTruncating(1e-8f) : typeof(T) == typeof(double) ? T.CreateTruncating(1e-15) : typeof(T) == typeof(Half) ? T.CreateTruncating(0.00097656f) : T.Zero;
    public static T Delta<T>() where T : INumber<T> => typeof(T) == typeof(float) ? T.CreateTruncating(1e-5f) : typeof(T) == typeof(double) ? T.CreateTruncating(1e-9) : typeof(T) == typeof(Half) ? T.CreateTruncating(0.00390625f) : T.Zero;
    public static T Negative<T>(T value) where T : INumber<T> => -value;
    public static bool Negative(bool value) => !value;
    public static T Clamp<T>(T value, T minimum, T maximum) where T : INumber<T>
    {
        if (minimum > maximum) throw new ArgumentException("Clamp requires minimum <= maximum.");
        return value > minimum ? value < maximum ? value : maximum : minimum;
    }
    public static T Clamp01<T>(T value) where T : INumber<T> => value > T.Zero ? value < T.One ? value : T.One : T.Zero;
    /// <summary>Clamps in place and returns whether the input was outside [0,1]. NaN becomes one.</summary>
    public static bool ClampTest01<T>(ref T value) where T : INumber<T>
    {
        if (value >= T.Zero && value <= T.One) return false;
        value = value < T.Zero ? T.Zero : T.One; return true;
    }
    public static T SmoothUnitStep<T>(T value) where T : INumber<T>
    {
        if (typeof(T) == typeof(Half)) { float x = float.CreateTruncating(value); return T.CreateTruncating(x > 0 ? x < 1 ? (3 - 2 * x) * x * x : 1 : 0); }
        return value > T.Zero ? value < T.One ? (T.CreateTruncating(3) - T.CreateTruncating(2) * value) * value * value : T.One : T.Zero;
    }
    public static T SmoothUnitStep<T>(T value, T minimum, T maximum) where T : INumber<T>
    {
        if (!(minimum < maximum)) throw new ArgumentException("SmoothUnitStep requires minimum < maximum.");
        if (typeof(T) == typeof(Half)) { float min = float.CreateTruncating(minimum); return T.CreateTruncating(SmoothUnitStep((float.CreateTruncating(value) - min) / (float.CreateTruncating(maximum) - min))); }
        return SmoothUnitStep((value - minimum) / (maximum - minimum));
    }
    public static T Pow2<T>(T value) where T : INumber<T> => value * value;
    public static T Pow3<T>(T value) where T : INumber<T> => typeof(T) == typeof(Half) ? T.CreateTruncating(Pow3(float.CreateTruncating(value))) : value * value * value;
    public static T Pow4<T>(T value) where T : INumber<T> => Pow2(Pow2(value));
    /// <summary>Uses native repeated multiplication, including rounding after each product.</summary>
    public static T Pow<T>(T value, int exponent) where T : INumber<T>
    {
        if (exponent == int.MinValue) throw new ArgumentOutOfRangeException(nameof(exponent));
        T result = T.One; if (exponent < 0) { exponent = -exponent; value = T.One / value; }
        while (exponent-- != 0) result *= value;
        return result;
    }
    public static T Inv<T>(T value) where T : INumber<T> => T.One / value;
    public static int Sign<T>(T value) where T : INumber<T> => (T.Zero < value ? 1 : 0) - (value < T.Zero ? 1 : 0);
    public static bool SignChange<T>(T left, T right) where T : INumber<T> => (left < T.Zero) ^ (right < T.Zero);
    public static bool ZeroCrossing<T>(T left, T right) where T : INumber<T>
    {
        if (typeof(T) == typeof(Half)) return float.CreateTruncating(left) * float.CreateTruncating(right) <= 0;
        if (typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(short) || typeof(T) == typeof(ushort)) return int.CreateTruncating(left) * int.CreateTruncating(right) <= 0;
        return left * right <= T.Zero;
    }
    public static T Chop<T>(T value, T delta) where T : INumber<T> => T.Abs(value) < delta ? T.Zero : value;
    public static T Min<T>(T a, T b) where T : INumber<T> => b < a ? b : a;
    public static T Max<T>(T a, T b) where T : INumber<T> => a < b ? b : a;
    /// <summary>Reduces in native overload order for lengths 2–8; larger spans use an ordered left fold.</summary>
    public static T Min<T>(ReadOnlySpan<T> values) where T : INumber<T>
    {
        if (values.IsEmpty) throw new ArgumentException("At least one value is required.", nameof(values));
        return values.Length switch { 1 => values[0], 2 => Min(values[0], values[1]), 3 => Min(Min(values[0], values[1]), values[2]), 4 => Min(Min(values[0], values[1]), Min(values[2], values[3])), 5 => Min(Min(values[0], values[1]), Min(values[2..])), 6 => Min(Min(values[..3]), Min(values[3..])), 7 => Min(Min(values[..4]), Min(values[4..])), 8 => Min(Min(values[..4]), Min(values[4..])), _ => Fold(values, false) };
    }
    public static T Max<T>(ReadOnlySpan<T> values) where T : INumber<T>
    {
        if (values.IsEmpty) throw new ArgumentException("At least one value is required.", nameof(values));
        return values.Length switch { 1 => values[0], 2 => Max(values[0], values[1]), 3 => Max(Max(values[0], values[1]), values[2]), 4 => Max(Max(values[0], values[1]), Max(values[2], values[3])), 5 => Max(Max(values[0], values[1]), Max(values[2..])), 6 => Max(Max(values[..3]), Max(values[3..])), 7 => Max(Max(values[..4]), Max(values[4..])), 8 => Max(Max(values[..4]), Max(values[4..])), _ => Fold(values, true) };
    }
    public static T Abs<T>(T value) where T : INumber<T> => T.Abs(value);
    public static bool Abs(bool value) => value;
    public static bool IsZero<T>(T value) where T : INumber<T> => value == T.Zero;
    public static bool IsNegative<T>(T value) where T : INumber<T> => value < T.Zero;
    public static bool IsNegative(bool value) => false;
    public static bool IsApproxLarger<T>(T a, T b, T tolerance) where T : INumber<T>
    {
        if (typeof(T) == typeof(Half)) return float.CreateTruncating(b) - float.CreateTruncating(a) < float.CreateTruncating(tolerance);
        if (typeof(T) == typeof(byte) || typeof(T) == typeof(sbyte) || typeof(T) == typeof(short) || typeof(T) == typeof(ushort)) return int.CreateTruncating(b) - int.CreateTruncating(a) < int.CreateTruncating(tolerance);
        return b - a < tolerance;
    }
    public static T Min<T>(T a, T b, T c) where T : INumber<T> => Min(Min(a, b), c);
    public static T Max<T>(T a, T b, T c) where T : INumber<T> => Max(Max(a, b), c);
    public static T Min<T>(T a, T b, T c, T d) where T : INumber<T> => Min(Min(a, b), Min(c, d));
    public static T Max<T>(T a, T b, T c, T d) where T : INumber<T> => Max(Max(a, b), Max(c, d));
    public static T Min<T>(T a, T b, T c, T d, T e) where T : INumber<T> => Min(Min(a, b), Min(c, d, e));
    public static T Max<T>(T a, T b, T c, T d, T e) where T : INumber<T> => Max(Max(a, b), Max(c, d, e));
    public static T Min<T>(T a, T b, T c, T d, T e, T f) where T : INumber<T> => Min(Min(a, b, c), Min(d, e, f));
    public static T Max<T>(T a, T b, T c, T d, T e, T f) where T : INumber<T> => Max(Max(a, b, c), Max(d, e, f));
    public static T Min<T>(T a, T b, T c, T d, T e, T f, T g) where T : INumber<T> => Min(Min(a, b, c, d), Min(e, f, g));
    public static T Max<T>(T a, T b, T c, T d, T e, T f, T g) where T : INumber<T> => Max(Max(a, b, c, d), Max(e, f, g));
    public static T Min<T>(T a, T b, T c, T d, T e, T f, T g, T h) where T : INumber<T> => Min(Min(a, b, c, d), Min(e, f, g, h));
    public static T Max<T>(T a, T b, T c, T d, T e, T f, T g, T h) where T : INumber<T> => Max(Max(a, b, c, d), Max(e, f, g, h));

    private static T Fold<T>(ReadOnlySpan<T> values, bool maximum) where T : INumber<T> { T result = values[0]; for (int i = 1; i < values.Length; ++i) result = maximum ? Max(result, values[i]) : Min(result, values[i]); return result; }
    public static int MinIndex<T>(T x, T y, T z) where T : INumber<T> { int index = y <= x ? 1 : 0; return z <= (index == 0 ? x : y) ? 2 : index; }
    public static int MaxIndex<T>(T x, T y, T z) where T : INumber<T> { int index = y >= x ? 1 : 0; return z >= (index == 0 ? x : y) ? 2 : index; }
    public static int MinIndex(Vec3d value) => MinIndex(value.X, value.Y, value.Z);
    public static int MinIndex(Vec3f value) => MinIndex(value.X, value.Y, value.Z);
    public static int MinIndex(Vec3i value) => MinIndex(value.X, value.Y, value.Z);
    public static int MaxIndex(Vec3d value) => MaxIndex(value.X, value.Y, value.Z);
    public static int MaxIndex(Vec3f value) => MaxIndex(value.X, value.Y, value.Z);
    public static int MaxIndex(Vec3i value) => MaxIndex(value.X, value.Y, value.Z);
}
