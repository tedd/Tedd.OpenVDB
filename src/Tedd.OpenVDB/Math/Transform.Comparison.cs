// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Math;

public sealed partial class Transform
{
    /// <summary>Native transform equality, comparing equal map types directly and promoting differing linear types to affine form.</summary>
    public bool IsEquivalentTo(Transform? other)
    {
        if (other is null || !VoxelSize.ApproxEquals(other.VoxelSize)) return false;
        if (MapType == other.MapType) return _map.IsEqual(other._map);
        return IsLinear && other.IsLinear ? _map.GetAffineMap().IsEqual(other._map.GetAffineMap()) : _map.IsEqual(other._map);
    }
}
