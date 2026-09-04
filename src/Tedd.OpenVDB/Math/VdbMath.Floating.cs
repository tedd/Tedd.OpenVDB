// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Math.h.
namespace Tedd.OpenVDB.Math;

public static partial class VdbMath
{
    public static float Pow(float value, float exponent) => System.MathF.Pow(value, exponent);
    public static double Pow(double value, double exponent) => System.Math.Pow(value, exponent);
    public static Half Pow(Half value, Half exponent) => (Half)System.MathF.Pow((float)value, (float)exponent);
    public static float RoundUp(float value) => System.MathF.Ceiling(value);
    public static double RoundUp(double value) => System.Math.Ceiling(value);
    public static float RoundDown(float value) => System.MathF.Floor(value);
    public static double RoundDown(double value) => System.Math.Floor(value);
    public static float Round(float value) => RoundDown(value + 0.5f);
    public static double Round(double value) => RoundDown(value + 0.5);
    public static float RoundUp(float value, float basis) { float remainder = value % basis; return remainder != 0 ? value - remainder + basis : value; }
    public static double RoundUp(double value, double basis) { double remainder = value % basis; return remainder != 0 ? value - remainder + basis : value; }
    public static int RoundUp(int value, int basis) { int remainder = value % basis; return remainder != 0 ? checked(value - remainder + basis) : value; }
    public static float RoundDown(float value, float basis) { float remainder = value % basis; return remainder != 0 ? value - remainder : value; }
    public static double RoundDown(double value, double basis) { double remainder = value % basis; return remainder != 0 ? value - remainder : value; }
    public static int RoundDown(int value, int basis) { int remainder = value % basis; return remainder != 0 ? checked(value - remainder) : value; }
    public static float Mod(float value, float divisor) => value % divisor;
    public static double Mod(double value, double divisor) => value % divisor;
    public static int Mod(int value, int divisor) => value % divisor;
    public static float Remainder(float value, float divisor) => Mod(value, divisor);
    public static double Remainder(double value, double divisor) => Mod(value, divisor);
    public static int Remainder(int value, int divisor) => Mod(value, divisor);
    public static float EuclideanRemainder(float value) => value - RoundDown(value);
    public static double EuclideanRemainder(double value) => value - RoundDown(value);
    public static float IntegerPart(float value) => value > 0 ? RoundDown(value) : RoundUp(value);
    public static double IntegerPart(double value) => value > 0 ? RoundDown(value) : RoundUp(value);
    public static float FractionalPart(float value) => Mod(value, 1);
    public static double FractionalPart(double value) => Mod(value, 1);
    public static int Floor(float value) => checked((int)RoundDown(value));
    public static int Floor(double value) => checked((int)RoundDown(value));
    public static int Ceil(float value) => checked((int)RoundUp(value));
    public static int Ceil(double value) => checked((int)RoundUp(value));
    /// <summary>Native decimal rounding named Truncate: ties round upward, including negative inputs.</summary>
    public static float Truncate(float value, uint digits) { float tenth = DecimalFactor(digits), product = value * tenth; return (float)(RoundDown((double)product + 0.5) / tenth); }
    public static double Truncate(double value, uint digits) { double tenth = DecimalFactor(digits); return RoundDown(value * tenth + 0.5) / tenth; }
    private static ulong DecimalFactor(uint digits)
    {
        if (digits > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(digits));
        if (digits >= 64) return 0; // Native size_t multiplication is modulo 2^64 on the supported x64 ABI.
        ulong result = 1; for (uint i = 0; i < digits; ++i) result = unchecked(result * 10); return result;
    }
    public static float Sqrt(float value) => System.MathF.Sqrt(value);
    public static double Sqrt(double value) => System.Math.Sqrt(value);
    public static float Cbrt(float value) => System.MathF.Cbrt(value);
    public static double Cbrt(double value) => System.Math.Cbrt(value);
    public static float Exp(float value) => System.MathF.Exp(value);
    public static double Exp(double value) => System.Math.Exp(value);
    public static float Sin(float value) => System.MathF.Sin(value);
    public static double Sin(double value) => System.Math.Sin(value);
    public static float Cos(float value) => System.MathF.Cos(value);
    public static double Cos(double value) => System.Math.Cos(value);
    public static int FloatToInt32(float value) => BitConverter.SingleToInt32Bits(value);
    public static long DoubleToInt64(double value) => BitConverter.DoubleToInt64Bits(value);
    /// <summary>Compares ordered representations; identical NaN payloads can compare equal, as in native code.</summary>
    public static bool IsUlpsEqual(float left, float right, int maximumUlps)
    {
        int a = FloatToInt32(left), b = FloatToInt32(right); if (a < 0) a = unchecked(int.MinValue - a); if (b < 0) b = unchecked(int.MinValue - b);
        long distance = System.Math.Abs((long)a - b); return distance <= maximumUlps;
    }
    public static bool IsUlpsEqual(double left, double right, long maximumUlps)
    {
        long a = DoubleToInt64(left), b = DoubleToInt64(right); if (a < 0) a = unchecked(long.MinValue - a); if (b < 0) b = unchecked(long.MinValue - b);
        ulong distance = a >= b ? unchecked((ulong)a - (ulong)b) : unchecked((ulong)b - (ulong)a); return maximumUlps >= 0 && distance <= (ulong)maximumUlps;
    }
}
