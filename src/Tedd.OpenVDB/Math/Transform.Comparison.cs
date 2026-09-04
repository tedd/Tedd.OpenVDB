// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Math;

public sealed partial class Transform
{
    /// <summary>Compares maps using native Transform equality, including voxel size and cached inverse matrices.</summary>
    public bool IsEquivalentTo(Transform? other)
    {
        if (other is null || !VoxelSize.ApproxEquals(other.VoxelSize)) return false;
        if (_frustum is null || other._frustum is null)
            return _frustum is null && other._frustum is null && _matrix.ApproxEquals(other._matrix) && _inverse.ApproxEquals(other._inverse);
        var a = _frustum; var b = other._frustum;
        if (!a.BoundingBox.Min.ApproxEquals(b.BoundingBox.Min) || !a.BoundingBox.Max.ApproxEquals(b.BoundingBox.Max)) return false;
        if (!MathComparison.IsApproxEqual(a.Taper, b.Taper, 1e-15) || !MathComparison.IsApproxEqual(a.Depth, b.Depth, 1e-15)) return false;
        return a.SecondMap.TransformPoint(Vec3d.Zero).ApproxEquals(b.SecondMap.TransformPoint(Vec3d.Zero)) &&
            a.SecondMap.TransformPoint(Vec3d.UnitX).ApproxEquals(b.SecondMap.TransformPoint(Vec3d.UnitX)) &&
            a.SecondMap.TransformPoint(Vec3d.UnitY).ApproxEquals(b.SecondMap.TransformPoint(Vec3d.UnitY)) &&
            a.SecondMap.TransformPoint(Vec3d.UnitZ).ApproxEquals(b.SecondMap.TransformPoint(Vec3d.UnitZ));
    }
}
