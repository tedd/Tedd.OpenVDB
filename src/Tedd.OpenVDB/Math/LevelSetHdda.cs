// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Math;

/// <summary>Custom voxel intersection policy for the hierarchical level-set DDA.</summary>
public interface ILevelSetRayTester
{
    Rayd Ray { get; }
    bool HasNode(Coord coordinate, int level);
    void SetRange(double start, double end);
    void Initialize(double time);
    bool Test(Coord coordinate, double time);
}

/// <summary>Visits allocated tree nodes and then voxels in native DDA order.</summary>
public static class LevelSetHdda
{
    /// <summary>Node levels 0, 1 and 2 start at leaves, lower nodes and upper nodes; -1 traverses voxels directly.</summary>
    public static bool Test<TTester>(TTester tester, int nodeLevel = 2) where TTester : class, ILevelSetRayTester
    {
        ArgumentNullException.ThrowIfNull(tester);
        if (nodeLevel < -1 || nodeLevel > 2) throw new ArgumentOutOfRangeException(nameof(nodeLevel));
        return Test(ref tester, nodeLevel);
    }
    /// <summary>Accepts a mutable value-type tester without copying or boxing its state.</summary>
    public static bool Test<TTester>(ref TTester tester, int nodeLevel = 2) where TTester : ILevelSetRayTester
    {
        ArgumentNullException.ThrowIfNull(tester);
        if (nodeLevel < -1 || nodeLevel > 2) throw new ArgumentOutOfRangeException(nameof(nodeLevel));
        return Traverse(ref tester, nodeLevel);
    }
    private static bool Traverse<TTester>(ref TTester tester, int level) where TTester : ILevelSetRayTester
    {
        var dda = new Dda(tester.Ray, level switch { -1 => 0, 0 => 3, 1 => 7, _ => 12 });
        if (level == -1)
        {
            tester.Initialize(dda.Time);
            do { if (tester.Test(dda.Voxel, dda.Next)) return true; } while (VolumeHdda.StepWithinDomain(ref dda));
        }
        else
        {
            do
            {
                if (!tester.HasNode(dda.Voxel, level)) continue;
                tester.SetRange(dda.Time, dda.Next);
                if (Traverse(ref tester, level - 1)) return true;
            } while (VolumeHdda.StepWithinDomain(ref dda));
        }
        return false;
    }
}
