// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/math/Transform.cc.
namespace Tedd.OpenVDB.Math;

public sealed partial class Transform
{
    /// <summary>Transforms coordinate centers; does not extend the box to voxel cell faces.</summary>
    public BBoxd IndexToWorld(CoordBBox box) => box.IsEmpty ? BBoxd.Empty : IndexToWorld(new BBoxd(box.Min.AsVec3d(), box.Max.AsVec3d()));
    public BBoxd IndexToWorld(BBoxd box) => TransformBounds(box, _matrix);
    public BBoxd WorldToIndex(BBoxd box) => TransformBounds(box, _inverse);
    public CoordBBox WorldToIndexCellCentered(BBoxd box)
    {
        BBoxd bounds = WorldToIndex(box);
        return bounds.IsValid ? new(Coord.Round(bounds.Min), Coord.Round(bounds.Max)) : CoordBBox.Empty;
    }
    public CoordBBox WorldToIndexNodeCentered(BBoxd box)
    {
        BBoxd bounds = WorldToIndex(box);
        return bounds.IsValid ? new(Coord.Floor(bounds.Min), Coord.Floor(bounds.Max)) : CoordBBox.Empty;
    }
    private static BBoxd TransformBounds(BBoxd box, Mat4d matrix)
    {
        if (!box.IsValid) return BBoxd.Empty;
        BBoxd result = BBoxd.Empty;
        Vec3d min = box.Min, max = box.Max;
        for (int corner = 0; corner < 8; ++corner)
        {
            var point = new Vec3d((corner & 1) == 0 ? min.X : max.X, (corner & 2) == 0 ? min.Y : max.Y, (corner & 4) == 0 ? min.Z : max.Z);
            result = result.Expand(matrix.TransformPoint(point));
        }
        return result;
    }
}
