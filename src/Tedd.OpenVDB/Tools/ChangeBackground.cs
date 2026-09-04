// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/ChangeBackground.h.
using System.Numerics;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.Tools;

/// <summary>Background replacement with native general and level-set traversal policies.</summary>
public static class ChangeBackground
{
    /// <summary>Replaces matching inactive values of either sign and updates the root background.</summary>
    public static void Apply<T>(VdbTree<T> tree, T background) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree);
        T old = tree.Background, oldNegative = ValueArithmetic<T>.Negative(old), newNegative = ValueArithmetic<T>.Negative(background);
        foreach (var value in tree.EnumerateValueHandles(ValueIterationMode.Inactive))
            value.SetValueOnly(ValueArithmetic<T>.Reset(value.Value, old, background, oldNegative, newNegative));
        tree.SetBackground(background);
    }
    public static void LevelSet<T>(VdbTree<T> tree, T background) where T : unmanaged, IFloatingPointIeee754<T> => AsymmetricLevelSet(tree, background, -background);
    /// <summary>Replaces inactive root/leaf values and all internal tiles by their sign, preserving activity.</summary>
    public static void AsymmetricLevelSet<T>(VdbTree<T> tree, T outside, T inside) where T : unmanaged, IFloatingPointIeee754<T>
    {
        ArgumentNullException.ThrowIfNull(tree);
        if (outside < T.Zero) throw new ArgumentOutOfRangeException(nameof(outside));
        if (!(inside < T.Zero)) throw new ArgumentOutOfRangeException(nameof(inside));
        foreach (var value in tree.EnumerateValueHandles(ValueIterationMode.All))
            if (!value.IsActive || value.Level is 1 or 2) value.SetValueOnly(value.Value < T.Zero ? inside : outside);
        tree.SetBackground(outside);
    }
}
