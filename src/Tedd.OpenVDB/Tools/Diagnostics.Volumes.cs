// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/Diagnostics.h.
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Ordered validation of symmetric narrow-band level sets, stopping at the first failing check.</summary>
public sealed class CheckLevelSet<T> where T : unmanaged
{
    private readonly Diagnose<T> _diagnose;
    public CheckLevelSet(Grid<T> grid) => _diagnose = new(grid);
    public Grid<T> Grid => _diagnose.Grid;
    public Grid<bool> Mask => _diagnose.Mask;
    public ulong ValueCount => _diagnose.ValueCount;
    public ulong FailureCount => _diagnose.FailureCount;
    public void Clear() => _diagnose.Clear();
    public static string CheckValueType() => DiagnosticNumeric<T>.FloatingScalar ? "" : "Value type is not floating point\n";
    public string CheckClassType() => Grid.GridClass == GridClass.LevelSet ? "" : "Class type is not \"GRID_LEVEL_SET\"\n";
    public string CheckTransform() => Grid.Transform.HasUniformScale ? "" : "Does not have uniform voxels\n";
    public string CheckBackground(double halfWidth = 3)
    {
        DiagnosticNumeric<T>.Validate(); if (DiagnosticNumeric<T>.Components != 1) throw new NotSupportedException("Level-set background checks require scalar values.");
        double width = DiagnosticNumeric<T>.Component(Grid.Background, 0) / Grid.Transform.VoxelSize.X;
        return width < halfWidth * (1 - 1e-6) ? $"The background value ({DiagnosticNumeric<T>.Format(Grid.Background)}) is less than {DiagnosticNumeric<double>.FormatDouble(halfWidth)} voxel units\n" : "";
    }
    public string CheckTiles() => Grid.Tree.ActiveTileCount != 0 ? "Has active tile values\n" : "";
    public string CheckFinite(bool updateMask = false, CancellationToken cancellationToken = default) => _diagnose.Check(new CheckFinite<T>(ValueIterationMode.All), updateMask, cancellationToken: cancellationToken);
    public string CheckRange(bool updateMask = false, CancellationToken cancellationToken = default) => _diagnose.Check(new CheckRange<T>(DiagnosticNumeric<T>.Negate(Grid.Background), Grid.Background), updateMask, checkTiles: false, checkBackground: false, cancellationToken: cancellationToken);
    public string CheckInactiveValues(bool updateMask = false, CancellationToken cancellationToken = default) => _diagnose.Check(new CheckMagnitude<T>(Grid.Background), updateMask, checkBackground: false, cancellationToken: cancellationToken);
    public string CheckEikonal(bool updateMask = false, T? minimum = null, T? maximum = null, EikonalStencilKind stencil = EikonalStencilKind.Weno, CancellationToken cancellationToken = default)
    {
        T min = minimum ?? DiagnosticNumeric<T>.Broadcast(0.5), max = maximum ?? DiagnosticNumeric<T>.Broadcast(1.5);
        DiagnosticCheck<T> check = typeof(T) == typeof(float) ? (DiagnosticCheck<T>)(object)new CheckEikonal<float>((Grid<float>)(object)Grid, (float)(object)min, (float)(object)max, stencil) : typeof(T) == typeof(double) ? (DiagnosticCheck<T>)(object)new CheckEikonal<double>((Grid<double>)(object)Grid, (double)(object)min, (double)(object)max, stencil) : throw new NotSupportedException("Eikonal checks require float or double scalar values.");
        return _diagnose.Check(check, updateMask, checkTiles: false, checkBackground: false, cancellationToken: cancellationToken);
    }
    public string Check(int number = 9, bool updateMask = false, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number); cancellationToken.ThrowIfCancellationRequested();
        string result = CheckValueType();
        if (result.Length == 0 && number > 1) result = CheckClassType();
        if (result.Length == 0 && number > 2) result = CheckTransform();
        if (result.Length == 0 && number > 3) result = CheckBackground();
        if (result.Length == 0 && number > 4) result = CheckTiles();
        if (result.Length == 0 && number > 5) result = CheckFinite(updateMask, cancellationToken);
        if (result.Length == 0 && number > 6) result = CheckRange(updateMask, cancellationToken);
        if (result.Length == 0 && number > 7) result = CheckInactiveValues(updateMask, cancellationToken);
        if (result.Length == 0 && number > 8) result = CheckEikonal(updateMask, cancellationToken: cancellationToken);
        return result;
    }
}

/// <summary>Ordered validation of a fog volume, stopping at the first failing check.</summary>
public sealed class CheckFogVolume<T> where T : unmanaged
{
    private readonly Diagnose<T> _diagnose;
    public CheckFogVolume(Grid<T> grid) => _diagnose = new(grid);
    public Grid<T> Grid => _diagnose.Grid;
    public Grid<bool> Mask => _diagnose.Mask;
    public ulong ValueCount => _diagnose.ValueCount;
    public ulong FailureCount => _diagnose.FailureCount;
    public void Clear() => _diagnose.Clear();
    public static string CheckValueType() => DiagnosticNumeric<T>.FloatingScalar ? "" : "Value type is not floating point";
    // Preserve the native diagnostic's literal text, including its level-set label for a fog-class failure.
    public string CheckClassType() => Grid.GridClass == GridClass.FogVolume ? "" : "Class type is not \"GRID_LEVEL_SET\"";
    public string CheckBackground()
    {
        DiagnosticNumeric<T>.Validate();
        if (DiagnosticNumeric<T>.Components != 1) throw new NotSupportedException("Fog background checks require scalar values.");
        double value = DiagnosticNumeric<T>.Component(Grid.Background, 0), tolerance = DiagnosticNumeric<T>.Component(DiagnosticNumeric<T>.Tolerance(), 0);
        return value > tolerance || value < -tolerance ? $"The background value ({DiagnosticNumeric<T>.Format(Grid.Background)}) is not zero" : "";
    }
    public string CheckFinite(bool updateMask = false, CancellationToken cancellationToken = default) => _diagnose.Check(new CheckFinite<T>(ValueIterationMode.All), updateMask, cancellationToken: cancellationToken);
    public string CheckInactiveValues(bool updateMask = false, CancellationToken cancellationToken = default) => _diagnose.Check(new CheckMagnitude<T>(DiagnosticNumeric<T>.Broadcast(0)), updateMask, cancellationToken: cancellationToken);
    public string CheckRange(bool updateMask = false, CancellationToken cancellationToken = default) => _diagnose.Check(new CheckRange<T>(DiagnosticNumeric<T>.Broadcast(0), DiagnosticNumeric<T>.Broadcast(1)), updateMask, checkBackground: false, cancellationToken: cancellationToken);
    public string Check(int number = 6, bool updateMask = false, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number); cancellationToken.ThrowIfCancellationRequested();
        string result = CheckValueType();
        if (result.Length == 0 && number > 1) result = CheckClassType();
        if (result.Length == 0 && number > 2) result = CheckBackground();
        if (result.Length == 0 && number > 3) result = CheckFinite(updateMask, cancellationToken);
        if (result.Length == 0 && number > 4) result = CheckInactiveValues(updateMask, cancellationToken);
        if (result.Length == 0 && number > 5) result = CheckRange(updateMask, cancellationToken);
        return result;
    }
}

public static class Diagnostics
{
    public static string CheckLevelSet<T>(Grid<T> grid, int number = 9, CancellationToken cancellationToken = default) where T : unmanaged => new CheckLevelSet<T>(grid).Check(number, cancellationToken: cancellationToken);
    public static string CheckFogVolume<T>(Grid<T> grid, int number = 6, CancellationToken cancellationToken = default) where T : unmanaged => new CheckFogVolume<T>(grid).Check(number, cancellationToken: cancellationToken);
    /// <summary>Finds sorted distinct stored inactive values, excluding the implicit background.</summary>
    /// <remarks>On failure, returns a deterministic partial set containing more than maximumValues;
    /// native parallel cancellation can return a different partial set. Complete successful results
    /// follow native scalar/lexicographic vector ordering. Signed zeros compare equal.</remarks>
    public static bool UniqueInactiveValues<T>(Grid<T> grid, out T[] values, int maximumValues, CancellationToken cancellationToken = default) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(grid); ArgumentOutOfRangeException.ThrowIfNegative(maximumValues); DiagnosticNumeric<T>.Validate(); cancellationToken.ThrowIfCancellationRequested();
        var unique = new SortedSet<T>(DiagnosticValueComparer<T>.Instance);
        foreach (var leaf in grid.Tree.EnumerateLeaves())
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int offset = 0; offset < LeafNode<T>.Size; ++offset) if (!leaf.IsValueOn(offset)) unique.Add(leaf.GetValue(offset));
            if (unique.Count > maximumValues) break;
        }
        if (unique.Count <= maximumValues)
            foreach (var tile in DiagnosticTraversal<T>.Tiles(grid.Tree.Root, ValueIterationMode.Inactive)) { cancellationToken.ThrowIfCancellationRequested(); unique.Add(tile.Value); }
        values = unique.ToArray(); return values.Length <= maximumValues;
    }
}
internal sealed class DiagnosticValueComparer<T> : IComparer<T> where T : unmanaged
{
    internal static readonly DiagnosticValueComparer<T> Instance = new();
    public int Compare(T a, T b) => DiagnosticNumeric<T>.Less(a, b) ? -1 : DiagnosticNumeric<T>.Less(b, a) ? 1 : 0;
}
