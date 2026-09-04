// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Mat.h::rotation(v1,v2).
namespace Tedd.OpenVDB.Math;

public readonly partial record struct Mat3d
{
    /// <summary>Constructs the native minimal rotation from one direction to another.</summary>
    /// <remarks>Near-parallel directions use two Householder reflections. Zero vectors retain
    /// the native normalization-failure behavior; use nonzero finite directions for a proper rotation.</remarks>
    public static Mat3d CreateRotation(Vec3d from, Vec3d to, double tolerance = 1e-8)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);
        if (!MathComparison.IsApproxEqual(1, from.LengthSquared, tolerance) && from.TryNormalize(out Vec3d normalizedFrom)) from = normalizedFrom;
        if (!MathComparison.IsApproxEqual(1, to.LengthSquared, tolerance) && to.TryNormalize(out Vec3d normalizedTo)) to = normalizedTo;
        Vec3d cross = from.Cross(to);
        if (MathComparison.IsApproxZero(cross.X, tolerance) && MathComparison.IsApproxZero(cross.Y, tolerance) && MathComparison.IsApproxZero(cross.Z, tolerance))
        {
            double x = System.Math.Abs(from.X), y = System.Math.Abs(from.Y), z = System.Math.Abs(from.Z);
            Vec3d p = x < y ? (z < x ? Vec3d.UnitZ : Vec3d.UnitX) : (z < y ? Vec3d.UnitZ : Vec3d.UnitY);
            Vec3d u = p - from, v = p - to;
            double uu = u.Dot(u), vv = v.Dot(v), a = -2 / uu, b = -2 / vv, c = 4 * u.Dot(v) / (uu * vv);
            return Identity + OuterProduct(u, u) * a + OuterProduct(v, v) * b + OuterProduct(u, v) * c;
        }
        else
        {
            double c = from.Dot(to), a = (1 - c) / cross.Dot(cross);
            double a0 = a * cross.X, a1 = a * cross.Y, a2 = a * cross.Z, a01 = a0 * cross.Y, a02 = a0 * cross.Z, a12 = a1 * cross.Z;
            return new(c + a0 * cross.X, a01 + cross.Z, a02 - cross.Y, a01 - cross.Z, c + a1 * cross.Y, a12 + cross.X,
                a02 + cross.Y, a12 - cross.X, c + a2 * cross.Z);
        }
    }
}
public readonly partial record struct Mat4d
{
    public static Mat4d CreateRotation(Vec3d from, Vec3d to, double tolerance = 1e-8) => Mat3d.CreateRotation(from, to, tolerance).ToMat4();
}
