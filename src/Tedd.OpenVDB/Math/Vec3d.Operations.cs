// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Math;

public readonly partial record struct Vec3d
{
    public bool Equals(Vec3d other) => X == other.X && Y == other.Y && Z == other.Z;
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static Vec3d operator -(double a, Vec3d b) => new(a - b.X, a - b.Y, a - b.Z);
    public static Vec3d operator /(double a, Vec3d b) => new(a / b.X, a / b.Y, a / b.Z);
    public Vec3f AsVec3f() => new((float)X, (float)Y, (float)Z);
}
public readonly partial record struct Mat4d
{
    public bool Equals(Mat4d other) => Row(0) == other.Row(0) && Row(1) == other.Row(1) && Row(2) == other.Row(2) && Row(3) == other.Row(3);
    public override int GetHashCode() => HashCode.Combine(Row(0), Row(1), Row(2), Row(3));
}
