// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed adaptation of openvdb/tools/MultiResGrid.h.
using System.Globalization;
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tools;

/// <summary>Bounds materialization per level. Cancellation can leave completed in-place transfer stages visible.</summary>
public sealed record MultiResGridOptions
{
    public ulong MaximumActiveVoxels { get; init; } = 100_000_000;
    public ulong MaximumLeaves { get; init; } = 1_000_000;
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>Native float/double powers-of-two level-of-detail trees with restriction, prolongation and fractional grids.</summary>
/// <remarks>Level zero is finest. GetGrid shares a tree; CreateGrid returns an independent tree.
/// Concurrent reads require unchanged trees and metadata. Transferring values edits only active leaf voxels.</remarks>
public sealed class MultiResGrid<T> where T : unmanaged, IFloatingPointIeee754<T>
{
    private readonly VdbTree<T>[] _trees;
    private readonly MultiResGridOptions _options;
    public MultiResGrid(int levels, T background, double voxelSize = 1, MultiResGridOptions? options = null)
    {
        ValidateLevels(levels); _options = options ?? new(); _options.CancellationToken.ThrowIfCancellationRequested();
        _trees = new VdbTree<T>[levels]; for (int i = 0; i < levels; i++) _trees[i] = new(background);
        Transform = Transform.CreateLinearTransform(voxelSize); Metadata = new(); InitializeMetadata();
    }
    /// <summary>Copies the input tree unless stealTree is true, in which case the input grid receives an empty replacement.</summary>
    /// <remarks>Stealing preserves the original tree identity. A later cancellation or failure can leave the input empty, as native ownership transfer does.</remarks>
    public MultiResGrid(int levels, Grid<T> grid, bool useInjection = false, bool stealTree = false, MultiResGridOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(grid); ValidateLevels(levels); _options = options ?? new(); _options.CancellationToken.ThrowIfCancellationRequested();
        CheckTreeBudget(grid.Tree);
        _trees = new VdbTree<T>[levels]; Transform = grid.Transform.Copy(); Metadata = new(grid.Metadata); InitializeMetadata();
        _trees[0] = stealTree ? grid.Tree : grid.Tree.DeepCopy();
        ulong existing = (ulong)_trees[0].LeafCount;
        _trees[0].VoxelizeActiveTiles(_options.MaximumLeaves - existing);
        if (stealTree) grid.SetTree(new(grid.Background));
        for (int level = 1; level < levels; level++)
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            var fine = _trees[level - 1]; var coarse = _trees[level] = new(fine.Background); ulong active = 0, leaves = 0;
            foreach (var leaf in fine.EnumerateLeaves())
            {
                _options.CancellationToken.ThrowIfCancellationRequested();
                foreach (int offset in leaf.ActiveOffsets)
                {
                    Coord c = leaf.OffsetToGlobalCoord(offset);
                    if (useInjection)
                    {
                        if (((c.X | c.Y | c.Z) & 1) == 0) Activate(coarse, new(c.X >> 1, c.Y >> 1, c.Z >> 1), leaf.GetValue(offset), ref active, ref leaves);
                    }
                    else
                    {
                        // Dilate by 26 neighbors, then retain even coordinates and divide by two.
                        // Forming the equivalent coarse mask directly avoids an intermediate expanded fine mask.
                        int x0 = c.X >> 1, y0 = c.Y >> 1, z0 = c.Z >> 1;
                        for (int x = 0; x < 1 + (c.X & 1); x++) for (int y = 0; y < 1 + (c.Y & 1); y++) for (int z = 0; z < 1 + (c.Z & 1); z++)
                            Activate(coarse, new(checked(x0 + x), checked(y0 + y), checked(z0 + z)), coarse.Background, ref active, ref leaves);
                    }
                }
            }
            if (!useInjection) RestrictActiveVoxels(level);
            FinishLevelSet(coarse);
        }
    }
    public int NumLevels => _trees.Length;
    public static int FinestLevel => 0;
    public int CoarsestLevel => _trees.Length - 1;
    public VdbTree<T> FinestTree => _trees[0];
    public VdbTree<T> CoarsestTree => _trees[^1];
    public Transform Transform { get; }
    public MetaMap Metadata { get; }
    public string Name
    {
        get => Metadata.GetMetadata(Grid<T>.NameMetadataKey)?.ToString() ?? string.Empty;
        set { ArgumentNullException.ThrowIfNull(value); Metadata.RemoveMeta(Grid<T>.NameMetadataKey); Metadata.InsertMeta(Grid<T>.NameMetadataKey, value); }
    }
    public GridClass GridClass
    {
        get => Metadata.TryGetMetadata<string>(Grid<T>.ClassMetadataKey, out var text) ? Grid<T>.ParseGridClass(text) : GridClass.Unknown;
        set => Metadata.InsertMeta(Grid<T>.ClassMetadataKey, Grid<T>.GridClassToString(value));
    }
    public void ClearGridClass() => Metadata.RemoveMeta(Grid<T>.ClassMetadataKey);
    public VdbTree<T> GetTree(int level) { ValidateLevel(level); return _trees[level]; }
    public Grid<T> GetGrid(int level)
    {
        ValidateLevel(level); var grid = new Grid<T>(_trees[level]) { Transform = Transform.Copy() };
        if (level > 0) grid.Transform.PreScale(1 << level);
        CopyMetadata(grid); SetGeneratedMetadata(grid, "MultiResGrid_Level", new MetadataValue((long)level)); grid.Name = Name + "_level_" + level.ToString(CultureInfo.InvariantCulture); return grid;
    }
    public IReadOnlyList<Grid<T>> GetGrids()
    {
        var result = new Grid<T>[NumLevels]; for (int i = 0; i < result.Length; i++) result[i] = GetGrid(i); return result;
    }
    public Grid<T> CreateGrid(float level, SamplingOrder order = SamplingOrder.Box)
    {
        ValidateFractionalLevel(level); ValidateOrder(order); _options.CancellationToken.ThrowIfCancellationRequested();
        var grid = new Grid<T>(FinestTree.Background) { Transform = Transform.Copy() }; grid.Transform.PreScale(System.MathF.Pow(2, level));
        CopyMetadata(grid); SetGeneratedMetadata(grid, "MultiResGrid_Level", new MetadataValue(level)); grid.Name = Name + "_level_" + new MetadataValue(level).ToString();
        int lower = (int)System.MathF.Floor(level), upper = (int)System.MathF.Ceiling(level);
        if (lower == upper) { CheckTreeBudget(_trees[lower]); grid.SetTree(_trees[lower].DeepCopy()); return grid; }
        float b = level - System.MathF.Floor(level), a = 1 - b;
        float scale = System.MathF.Pow(2, 1 - b), scale0 = System.MathF.Pow(2, b), scale1 = System.MathF.Pow(2, -a);
        ulong active = 0, leaves = 0;
        // Native fractional topology derives only from active coarse leaf voxels, then dilates once.
        foreach (var leaf in _trees[upper].EnumerateLeaves())
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            foreach (int offset in leaf.ActiveOffsets)
            {
                Coord c = leaf.OffsetToGlobalCoord(offset); int x = Round(c.X * scale), y = Round(c.Y * scale), z = Round(c.Z * scale);
                for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) for (int k = -1; k <= 1; k++)
                    Activate(grid.Tree, new(checked(x + i), checked(y + j), checked(z + k)), grid.Background, ref active, ref leaves);
            }
        }
        var source0 = new AccessorSampleSource<T>(_trees[lower].GetAccessor()); var source1 = new AccessorSampleSource<T>(_trees[upper].GetAccessor());
        T wa = T.CreateTruncating(a), wb = T.CreateTruncating(b);
        foreach (var leaf in grid.Tree.EnumerateLeaves())
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            foreach (int offset in leaf.ActiveOffsets)
            {
                Vec3d p = leaf.OffsetToGlobalCoord(offset).AsVec3d();
                SamplingKernel<T, AccessorSampleSource<T>>.Sample(source0, p * scale0, order, false, out T v0);
                SamplingKernel<T, AccessorSampleSource<T>>.Sample(source1, p * scale1, order, false, out T v1);
                leaf.SetValueOnly(offset, wa * v0 + wb * v1);
            }
        }
        FinishLevelSet(grid.Tree); return grid;
    }
    public static Vec3d Xyz(Coord coordinate, int inputLevel, int outputLevel) => Xyz(coordinate.AsVec3d(), inputLevel, outputLevel);
    public static Vec3d Xyz(Vec3d coordinate, int inputLevel, int outputLevel)
    {
        ValidateCoordinateLevel(inputLevel); ValidateCoordinateLevel(outputLevel);
        return coordinate * (1 << inputLevel) / (1 << outputLevel);
    }
    public static Vec3d Xyz(Vec3d coordinate, double inputLevel, double outputLevel)
    {
        if (!double.IsFinite(inputLevel) || !double.IsFinite(outputLevel)) throw new ArgumentOutOfRangeException(nameof(inputLevel));
        return coordinate * System.Math.Pow(2, inputLevel - outputLevel);
    }
    public T SampleValue(Coord coordinate, int inputLevel, int outputLevel, SamplingOrder order = SamplingOrder.Box) => SampleValue(coordinate.AsVec3d(), inputLevel, outputLevel, order);
    public T SampleValue(Vec3d coordinate, int inputLevel, int outputLevel, SamplingOrder order = SamplingOrder.Box)
    {
        ValidateLevel(inputLevel); ValidateLevel(outputLevel); ValidateOrder(order);
        return Sample(_trees[outputLevel], Xyz(coordinate, inputLevel, outputLevel), order);
    }
    public T SampleValue(Coord coordinate, double level, SamplingOrder order = SamplingOrder.Box) => SampleValue(coordinate.AsVec3d(), level, order);
    public T SampleValue(Vec3d coordinate, double level, SamplingOrder order = SamplingOrder.Box)
    {
        ValidateFractionalLevel(level); int lower = (int)System.Math.Floor(level), upper = (int)System.Math.Ceiling(level);
        T v0 = SampleValue(coordinate, 0, lower, order); if (lower == upper) return v0;
        T v1 = SampleValue(coordinate, 0, upper, order), a = T.CreateTruncating(upper - level); return a * v0 + (T.One - a) * v1;
    }
    public T RestrictVoxel(Coord coordinate, int destinationLevel, bool useInjection = false)
    {
        ValidateRestriction(destinationLevel); var source = _trees[destinationLevel - 1].GetAccessor();
        if (useInjection) return source.GetValue(Double(coordinate));
        return Restrict(coordinate, source);
    }
    public T ProlongateVoxel(Coord coordinate, int destinationLevel)
    {
        ValidateProlongation(destinationLevel); return Prolongate(coordinate, _trees[destinationLevel + 1].GetAccessor());
    }
    public void RestrictActiveVoxels(int destinationLevel)
    {
        ValidateRestriction(destinationLevel); Transfer(_trees[destinationLevel - 1], _trees[destinationLevel], false);
    }
    public void ProlongateActiveVoxels(int destinationLevel)
    {
        ValidateProlongation(destinationLevel); Transfer(_trees[destinationLevel + 1], _trees[destinationLevel], true);
    }
    private void Transfer(VdbTree<T> source, VdbTree<T> destination, bool prolongate)
    {
        _options.CancellationToken.ThrowIfCancellationRequested(); CheckTreeBudget(destination); var accessor = source.GetAccessor();
        foreach (var leaf in destination.EnumerateLeaves())
        {
            _options.CancellationToken.ThrowIfCancellationRequested();
            foreach (int offset in leaf.ActiveOffsets)
            {
                Coord c = leaf.OffsetToGlobalCoord(offset); leaf.SetValueOnly(offset, prolongate ? Prolongate(c, accessor) : Restrict(c, accessor));
            }
        }
    }
    private static T Restrict(Coord c, ValueAccessor<T> source)
    {
        Coord p = Double(c);
        T At(int x, int y, int z) => source.GetValue(new(checked(p.X + x), checked(p.Y + y), checked(p.Z + z)));
        T v = T.CreateTruncating(8) * source.GetValue(p);
        v += T.CreateTruncating(4) * (At(-1, 0, 0) + At(1, 0, 0) + At(0, -1, 0) + At(0, 1, 0) + At(0, 0, -1) + At(0, 0, 1));
        v += T.CreateTruncating(2) * (At(-1, -1, 0) + At(-1, 1, 0) + At(1, -1, 0) + At(1, 1, 0) + At(-1, 0, -1) + At(-1, 0, 1) + At(1, 0, -1) + At(1, 0, 1) + At(0, -1, -1) + At(0, -1, 1) + At(0, 1, -1) + At(0, 1, 1));
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2) v += At(x, y, z);
        return v * T.CreateTruncating(1f / 64f);
    }
    private static T Prolongate(Coord c, ValueAccessor<T> source)
    {
        int oddX = c.X & 1, oddY = c.Y & 1, oddZ = c.Z & 1, n = oddX + oddY + oddZ;
        if (n == 0) return source.GetValue(new(c.X >> 1, c.Y >> 1, c.Z >> 1));
        T sum = T.Zero; bool seen = n == 3;
        // Native cases with one/two odd axes initialize directly from the first value; all-odd starts at zero.
        for (int x = -oddX; x <= oddX; x += 2) for (int y = -oddY; y <= oddY; y += 2) for (int z = -oddZ; z <= oddZ; z += 2)
        {
            var p = new Coord((int)(((long)c.X + x) >> 1), (int)(((long)c.Y + y) >> 1), (int)(((long)c.Z + z) >> 1));
            T value = source.GetValue(p); if (seen) sum += value; else { sum = value; seen = true; }
        }
        return T.CreateTruncating(n == 1 ? 0.5 : n == 2 ? 0.25 : 0.125) * sum;
    }
    private void Activate(VdbTree<T> tree, Coord c, T value, ref ulong active, ref ulong leaves)
    {
        if (tree.IsValueOn(c)) return;
        if (active >= _options.MaximumActiveVoxels) throw new InvalidOperationException("Multiresolution active voxel budget exceeded.");
        if (tree.ProbeLeaf(c) is null) { if (leaves >= _options.MaximumLeaves) throw new InvalidOperationException("Multiresolution leaf budget exceeded."); leaves++; }
        tree.SetValueOn(c, value); active++;
    }
    private void CheckTreeBudget(VdbTree<T> tree)
    {
        if (tree.ActiveVoxelCount > _options.MaximumActiveVoxels || (ulong)tree.LeafCount > _options.MaximumLeaves) throw new InvalidOperationException("Multiresolution materialization budget exceeded.");
    }
    private void FinishLevelSet(VdbTree<T> tree)
    {
        if (GridClass != GridClass.LevelSet) return; _options.CancellationToken.ThrowIfCancellationRequested(); SignedFloodFill.Apply(tree); Prune.PruneLevelSet(tree);
    }
    private static T Sample(VdbTree<T> tree, Vec3d coordinate, SamplingOrder order)
    {
        SamplingKernel<T, TreeSampleSource<T>>.Sample(new(tree), coordinate, order, false, out T value); return value;
    }
    private static Coord Double(Coord c) => new(checked(c.X * 2), checked(c.Y * 2), checked(c.Z * 2));
    private static int Round(float value) => checked((int)System.MathF.Floor(value + 0.5f));
    private void CopyMetadata(Grid<T> grid) { foreach (var pair in Metadata) grid.SetMetadata(pair.Key, pair.Value.DeepCopy()); }
    private static void SetGeneratedMetadata(Grid<T> grid, string name, MetadataValue value)
    {
        if (grid.Metadata.TryGetValue(name, out var previous) && previous.TypeName != value.TypeName)
            throw new InvalidCastException($"Cannot assign metadata type '{value.TypeName}' to '{name}' of type '{previous.TypeName}'.");
        grid.SetMetadata(name, value);
    }
    private void InitializeMetadata() => Metadata.InsertMeta("MultiResGrid_Levels", (long)NumLevels);
    private static void ValidateLevels(int levels)
    {
        if (typeof(T) != typeof(float) && typeof(T) != typeof(double)) throw new NotSupportedException("Multiresolution grids require float or double values.");
        if (levels < 2 || levels > 31) throw new ArgumentOutOfRangeException(nameof(levels), "Between 2 and 31 levels are required.");
    }
    private static void ValidateCoordinateLevel(int level) { if ((uint)level > 30) throw new ArgumentOutOfRangeException(nameof(level)); }
    private void ValidateLevel(int level) { if ((uint)level >= (uint)NumLevels) throw new ArgumentOutOfRangeException(nameof(level)); }
    private void ValidateRestriction(int level) { ValidateLevel(level); if (level == 0) throw new ArgumentOutOfRangeException(nameof(level)); }
    private void ValidateProlongation(int level) { ValidateLevel(level); if (level == CoarsestLevel) throw new ArgumentOutOfRangeException(nameof(level)); }
    private void ValidateFractionalLevel(double level) { if (!double.IsFinite(level) || level < 0 || level > CoarsestLevel) throw new ArgumentOutOfRangeException(nameof(level)); }
    private static void ValidateOrder(SamplingOrder order) => SamplingKernel<T, TreeSampleSource<T>>.Validate(order, false);
}