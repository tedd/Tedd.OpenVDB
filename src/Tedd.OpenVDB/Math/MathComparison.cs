// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Math.h.
namespace Tedd.OpenVDB.Math;

/// <summary>OpenVDB floating-point comparison rules, including the native unordered NaN behavior.</summary>
/// <remarks>Native approximate comparisons accept an unordered (NaN) difference. They are not
/// finiteness checks, and are deliberately different from IEEE equality and CLR Equals.</remarks>
public static class MathComparison
{
    public static bool IsApproxEqual(double a, double b, double tolerance = 1e-15) => !(System.Math.Abs(a - b) > tolerance);
    public static bool IsApproxEqual(float a, float b, float tolerance = 1e-8f) => !(System.MathF.Abs(a - b) > tolerance);
    public static bool IsApproxZero(double value, double tolerance = 1e-15) => !(value > tolerance) && !(value < -tolerance);
    public static bool IsApproxZero(float value, float tolerance = 1e-8f) => !(value > tolerance) && !(value < -tolerance);
    public static bool IsRelOrApproxEqual(double a, double b, double absoluteTolerance, double relativeTolerance)
    {
        if (!(System.Math.Abs(a - b) > absoluteTolerance)) return true;
        double relativeError = System.Math.Abs(b) > System.Math.Abs(a) ? System.Math.Abs((a - b) / b) : System.Math.Abs((a - b) / a);
        return relativeError <= relativeTolerance;
    }
    public static bool IsRelOrApproxEqual(float a, float b, float absoluteTolerance, float relativeTolerance)
    {
        if (!(System.MathF.Abs(a - b) > absoluteTolerance)) return true;
        double relativeError = System.MathF.Abs(b) > System.MathF.Abs(a) ? System.MathF.Abs((a - b) / b) : System.MathF.Abs((a - b) / a);
        return relativeError <= relativeTolerance;
    }
}
