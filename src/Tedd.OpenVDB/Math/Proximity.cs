// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Proximity.cc.
namespace Tedd.OpenVDB.Math;

/// <summary>Closest points on segments and triangles, with native degeneracy and barycentric rules.</summary>
public static class Proximity
{
    public static Vec3d ClosestPointOnSegmentToPoint(Vec3d a, Vec3d b, Vec3d point, out double t)
    {
        Vec3d ab = b - a; t = (point - a).Dot(ab);
        if (t <= 0) { t = 0; return a; }
        double denominator = ab.Dot(ab);
        if (t >= denominator) { t = 1; return b; }
        t /= denominator; return a + ab * t;
    }
    public static Vec3d ClosestPointOnTriangleToPoint(Vec3d a, Vec3d b, Vec3d c, Vec3d point, out Vec3d barycentric)
    {
        if (a.ApproxEquals(b) && a.ApproxEquals(c)) { barycentric = Vec3d.UnitX; return a; }
        Vec3d ab = b - a, ac = c - a, ap = point - a;
        double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
        if (a.ApproxEquals(b))
        {
            Vec3d result = ClosestPointOnSegmentToPoint(a, c, point, out double t); barycentric = new(1 - t, 0, t); return result;
        }
        if (a.ApproxEquals(c) || b.ApproxEquals(c))
        {
            Vec3d result = ClosestPointOnSegmentToPoint(a, b, point, out double t); barycentric = new(1 - t, t, 0); return result;
        }
        if (d1 <= 0 && d2 <= 0) { barycentric = Vec3d.UnitX; return a; }
        Vec3d bp = point - b;
        double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
        if (d3 >= 0 && d4 <= d3) { barycentric = Vec3d.UnitY; return b; }
        double vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        { double v = d1 / (d1 - d3); barycentric = new(1 - v, v, 0); return a + v * ab; }
        Vec3d cp = point - c;
        double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
        if (d6 >= 0 && d5 <= d6) { barycentric = Vec3d.UnitZ; return c; }
        double vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        { double w = d2 / (d2 - d6); barycentric = new(1 - w, 0, w); return a + w * ac; }
        double va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        { double w = (d4 - d3) / (d4 - d3 + (d5 - d6)); barycentric = new(0, 1 - w, w); return b + w * (c - b); }
        double denominator = 1 / (va + vb + vc), faceW = vc * denominator, faceV = vb * denominator;
        barycentric = new(1 - faceV - faceW, faceV, faceW);
        return a + ab * faceV + ac * faceW;
    }
}
