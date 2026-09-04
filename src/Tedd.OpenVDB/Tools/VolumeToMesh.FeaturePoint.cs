// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// VolumeToMesh.h findFeaturePoint and its math/Mat3.h Jacobi dependency.
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tools;

public sealed partial class VolumeToMesh
{
    /// <summary>Intersects tangent planes using the native centered pseudoinverse and 1% eigenvalue cutoff.</summary>
    public static Vec3d FindFeaturePoint(ReadOnlySpan<Vec3d> points, ReadOnlySpan<Vec3d> normals)
    {
        if (points.Length != normals.Length) throw new ArgumentException("Point and normal counts must match.", nameof(normals));
        if (points.IsEmpty) return Vec3d.Zero;
        Vec3d average = Vec3d.Zero; foreach (Vec3d p in points) average += p; average /= points.Length;
        double m00 = 0, m01 = 0, m02 = 0, m11 = 0, m12 = 0, m22 = 0; Vec3d rhs = Vec3d.Zero;
        for (int n = 0; n < points.Length; ++n)
        {
            Vec3d normal = normals[n]; m00 += normal.X * normal.X; m11 += normal.Y * normal.Y; m22 += normal.Z * normal.Z; m01 += normal.X * normal.Y; m02 += normal.X * normal.Z; m12 += normal.Y * normal.Z; rhs += normal * normal.Dot(points[n] - average);
        }
        Span<double> s = stackalloc double[9] { m00, m01, m02, m01, m11, m12, m02, m12, m22 }, q = stackalloc double[9] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, d = stackalloc double[3] { m00, m11, m22 };
        for (int iteration = 0; iteration < 300; ++iteration)
        {
            double error = System.Math.Abs(s[1]) + System.Math.Abs(s[2]) + System.Math.Abs(s[5]); if (System.Math.Abs(error) < 1e-15) break;
            double maximum = 0; int ip = 0, jp = 0;
            for (int i = 0; i < 3; ++i) for (int j = i + 1; j < 3; ++j)
            {
                if (System.Math.Abs(d[i]) * (10 * 1e-15) > System.Math.Abs(s[i * 3 + j])) s[i * 3 + j] = 0;
                if (System.Math.Abs(s[i * 3 + j]) > maximum) { maximum = System.Math.Abs(s[i * 3 + j]); ip = i; jp = j; }
            }
            Pivot(ip, jp, s, d, q);
        }
        double tolerance = System.Math.Max(System.Math.Abs(d[0]), System.Math.Max(System.Math.Abs(d[1]), System.Math.Abs(d[2]))) * .01; int clamped = 0;
        for (int i = 0; i < 3; ++i) { if (System.Math.Abs(d[i]) < tolerance) { d[i] = 0; ++clamped; } else d[i] = 1 / d[i]; }
        if (clamped == 3) return average;
        Mat3d vectors = new(q[0], q[1], q[2], q[3], q[4], q[5], q[6], q[7], q[8]); Mat3d inverse = vectors * Mat3d.CreateScale(new Vec3d(d[0], d[1], d[2])) * vectors.Transpose(); return average + inverse * rhs;
    }
    private static void Pivot(int i, int j, Span<double> s, Span<double> d, Span<double> q)
    {
        double value = s[i * 3 + j], difference = d[j] - d[i], tan;
        if (System.Math.Abs(difference) * (10 * 1e-15) > System.Math.Abs(value)) tan = value / difference;
        else { double cotangent = .5 * difference / value; tan = cotangent < 0 ? -1 / (System.Math.Sqrt(1 + cotangent * cotangent) - cotangent) : 1 / (System.Math.Sqrt(1 + cotangent * cotangent) + cotangent); }
        double cosine = 1 / System.Math.Sqrt(1 + tan * tan), sine = cosine * tan, z = tan * value; s[i * 3 + j] = 0; d[i] -= z; d[j] += z;
        for (int k = 0; k < i; ++k) { double temp = s[k * 3 + i]; s[k * 3 + i] = cosine * temp - sine * s[k * 3 + j]; s[k * 3 + j] = sine * temp + cosine * s[k * 3 + j]; }
        for (int k = i + 1; k < j; ++k) { double temp = s[i * 3 + k]; s[i * 3 + k] = cosine * temp - sine * s[k * 3 + j]; s[k * 3 + j] = sine * temp + cosine * s[k * 3 + j]; }
        for (int k = j + 1; k < 3; ++k) { double temp = s[i * 3 + k]; s[i * 3 + k] = cosine * temp - sine * s[j * 3 + k]; s[j * 3 + k] = sine * temp + cosine * s[j * 3 + k]; }
        for (int k = 0; k < 3; ++k) { double temp = q[k * 3 + i]; q[k * 3 + i] = cosine * temp - sine * q[k * 3 + j]; q[k * 3 + j] = sine * temp + cosine * q[k * 3 + j]; }
    }
}
