// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/PoissonSolver.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Pcg = Tedd.OpenVDB.Math.Pcg;

namespace Tedd.OpenVDB.Tools;

/// <summary>Modifies the source and center coefficient for one exterior neighbor.</summary>
public delegate void PoissonBoundaryCondition(Coord coordinate, Coord exteriorNeighbor, ref double source, ref double diagonal);

public sealed record PoissonSolverOptions
{
    public int MaximumDegreesOfFreedom { get; init; } = int.MaxValue;
    public ulong MaximumNewLeaves { get; init; } = ulong.MaxValue;
    public CancellationToken CancellationToken { get; init; }
    /// <summary>Defaults to incomplete Cholesky. An invalid result falls back to Jacobi, as upstream.</summary>
    public Func<Pcg.SparseStencilMatrix<double>, Pcg.Preconditioner<double>>? PreconditionerFactory { get; init; }
}

/// <summary>Index-space Poisson assembly, scalar tree/vector conversion and PCG solving.</summary>
/// <remarks>Native collocated assembly uses neighbors two voxels away, with unit coefficients;
/// staggered assembly uses immediate face neighbors. Transforms and world-space voxel size are
/// not applied. Solving always uses double arithmetic and returns zero-background tree values
/// narrowed to the input type. Callers must not mutate input topology during an operation.</remarks>
public static partial class PoissonSolver
{
    public static void DirichletBoundary(Coord coordinate, Coord exteriorNeighbor, ref double source, ref double diagonal) => diagonal -= 1.0;

    /// <summary>Copies all stored topology, expands active tiles and indexes active leaf voxels in native tree order.</summary>
    public static VdbTree<int> CreateIndexTree<T>(VdbTree<T> tree, PoissonSolverOptions? options = null) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(tree); options ??= new(); CheckOptions(options);
        CheckCount(tree.ActiveVoxelCount, options.MaximumDegreesOfFreedom);
        var result = CopyTopology<T, int>(tree, -1);
        result.VoxelizeActiveTiles(options.MaximumNewLeaves);
        PopulateIndexTree(result, options); return result;
    }
    /// <summary>Assigns sequential indices to existing active leaf voxels. Active tiles must already be expanded.</summary>
    /// <remarks>Cancellation during this explicitly mutating operation can leave a partially indexed tree.</remarks>
    public static void PopulateIndexTree(VdbTree<int> tree, PoissonSolverOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tree); options ??= new(); CheckOptions(options);
        if (tree.ActiveTileCount != 0) throw new ArgumentException("Expand active tiles before populating an index tree.", nameof(tree));
        CheckCount(tree.ActiveVoxelCount, options.MaximumDegreesOfFreedom); int index = 0;
        foreach (var leaf in tree.EnumerateLeaves())
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            foreach (int offset in leaf.ActiveOffsets) leaf.SetValueOnly(offset, index++);
        }
    }
    /// <summary>Copies source values at indexed coordinates, including inactive stored values and backgrounds.</summary>
    public static Pcg.Vector<TVector> CreateVectorFromTree<TVector, TSource>(VdbTree<TSource> source, VdbTree<int> index,
        PoissonSolverOptions? options = null)
        where TVector : unmanaged, IFloatingPointIeee754<TVector> where TSource : unmanaged, INumberBase<TSource>
    {
        ArgumentNullException.ThrowIfNull(source); Pcg.Scalar<TVector>.Validate();
        options ??= new(); var data = ValidateIndex(index, options); var result = new Pcg.Vector<TVector>(data.Count);
        foreach (var leaf in data.Leaves)
        {
            options.CancellationToken.ThrowIfCancellationRequested(); var sourceLeaf = source.ProbeLeaf(leaf.Origin);
            if (sourceLeaf is not null)
            {
                foreach (int offset in leaf.ActiveOffsets) result[leaf.GetValue(offset)] = TVector.CreateTruncating(sourceLeaf.GetValue(offset));
            }
            else
            {
                TVector value = TVector.CreateTruncating(source.GetValue(leaf.Origin));
                foreach (int offset in leaf.ActiveOffsets) result[leaf.GetValue(offset)] = value;
            }
        }
        return result;
    }
    /// <summary>Copies index topology and replaces active voxel values from a vector.</summary>
    public static VdbTree<TTree> CreateTreeFromVector<TTree, TVector>(Pcg.Vector<TVector> values, VdbTree<int> index,
        TTree background, PoissonSolverOptions? options = null)
        where TTree : unmanaged, INumberBase<TTree> where TVector : unmanaged, IFloatingPointIeee754<TVector>
    {
        ArgumentNullException.ThrowIfNull(values);
        options ??= new(); var data = ValidateIndex(index, options);
        if (values.Size != data.Count) throw new ArithmeticException("Index tree and vector dimensions differ.");
        var result = CopyTopology<int, TTree>(index, background);
        foreach (var leaf in data.Leaves)
        {
            options.CancellationToken.ThrowIfCancellationRequested(); var output = result.ProbeLeaf(leaf.Origin)!;
            foreach (int offset in leaf.ActiveOffsets) output.SetValueOnly(offset, ConvertValue<TTree, TVector>(values[leaf.GetValue(offset)]));
        }
        return result;
    }
    /// <summary>Copies boolean source values as numeric zero or one.</summary>
    public static Pcg.Vector<TVector> CreateVectorFromTree<TVector>(VdbTree<bool> source, VdbTree<int> index,
        PoissonSolverOptions? options = null) where TVector : unmanaged, IFloatingPointIeee754<TVector>
    {
        ArgumentNullException.ThrowIfNull(source); Pcg.Scalar<TVector>.Validate(); options ??= new();
        var data = ValidateIndex(index, options); var result = new Pcg.Vector<TVector>(data.Count); var accessor = source.GetAccessor();
        foreach (var leaf in data.Leaves)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            foreach (int offset in leaf.ActiveOffsets) result[leaf.GetValue(offset)] = accessor.GetValue(leaf.OffsetToGlobalCoord(offset)) ? TVector.One : TVector.Zero;
        }
        return result;
    }
    /// <summary>Converts numeric vector values to bool using native nonzero semantics, including NaN.</summary>
    public static VdbTree<bool> CreateTreeFromVector<TVector>(Pcg.Vector<TVector> values, VdbTree<int> index,
        bool background, PoissonSolverOptions? options = null) where TVector : unmanaged, IFloatingPointIeee754<TVector>
    {
        ArgumentNullException.ThrowIfNull(values); options ??= new(); var data = ValidateIndex(index, options);
        if (values.Size != data.Count) throw new ArithmeticException("Index tree and vector dimensions differ.");
        var result = CopyTopology<int, bool>(index, background);
        foreach (var leaf in data.Leaves)
        {
            options.CancellationToken.ThrowIfCancellationRequested(); var output = result.ProbeLeaf(leaf.Origin)!;
            foreach (int offset in leaf.ActiveOffsets) output.SetValueOnly(offset, values[leaf.GetValue(offset)] != TVector.Zero);
        }
        return result;
    }
    private static TDestination ConvertValue<TDestination, TSource>(TSource value)
        where TDestination : unmanaged, INumberBase<TDestination> where TSource : unmanaged, INumberBase<TSource>
    {
        // Imath half(double) first narrows to float; direct CLR double-to-Half can round differently.
        if (typeof(TDestination) == typeof(Half) && typeof(TSource) == typeof(double))
            return TDestination.CreateTruncating(float.CreateTruncating(value));
        return TDestination.CreateTruncating(value);
    }
    private static VdbTree<TOut> CopyTopology<TIn, TOut>(VdbTree<TIn> tree, TOut background) where TIn : unmanaged where TOut : unmanaged
    {
        var result = new VdbTree<TOut>(background);
        foreach (var (coordinate, entry) in tree.Entries)
            result.Entries.Add(coordinate, entry.Child is null ? new(background, entry.Active) :
                new(default, false, InternalNode<TOut>.CopyTopology(entry.Child, background)));
        return result;
    }
    private static void CheckOptions(PoissonSolverOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaximumDegreesOfFreedom);
        options.CancellationToken.ThrowIfCancellationRequested();
    }
    private static int CheckCount(ulong count, int maximum)
    {
        if (count > (ulong)maximum) throw new InvalidOperationException("Poisson degree-of-freedom budget exceeded.");
        return checked((int)count);
    }
    private static (int Count, LeafNode<int>[] Leaves) ValidateIndex(VdbTree<int> index, PoissonSolverOptions options)
    {
        ArgumentNullException.ThrowIfNull(index); CheckOptions(options);
        if (index.ActiveTileCount != 0) throw new ArgumentException("A Poisson index tree cannot contain active tiles.", nameof(index));
        int count = CheckCount(index.ActiveVoxelCount, options.MaximumDegreesOfFreedom);
        var leaves = index.EnumerateLeaves().ToArray();
        // Require a dense permutation, preventing duplicate writes or uninitialized vector holes.
        int wordCount = (count >> 5) + ((count & 31) == 0 ? 0 : 1);
        Span<uint> seen = wordCount <= 128 ? stackalloc uint[wordCount] : new uint[wordCount]; seen.Clear();
        foreach (var leaf in leaves)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            foreach (int offset in leaf.ActiveOffsets)
            {
                int value = leaf.GetValue(offset);
                if ((uint)value >= (uint)count) throw new ArgumentException("Active index values must be in [0, active voxel count).", nameof(index));
                uint bit = 1U << (value & 31); int word = value >> 5;
                if ((seen[word] & bit) != 0) throw new ArgumentException("Active Poisson indices must be unique.", nameof(index));
                seen[word] |= bit;
            }
        }
        return (count, leaves);
    }
}
