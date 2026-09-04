// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from openvdb/tools/PoissonSolver.h.
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Pcg = Tedd.OpenVDB.Math.Pcg;

namespace Tedd.OpenVDB.Tools;

public static partial class PoissonSolver
{
    private static readonly Coord[] CollocatedOffsets = [new(-2, 0, 0), new(2, 0, 0), new(0, -2, 0), new(0, 2, 0), new(0, 0, -2), new(0, 0, 2)];
    private static readonly Coord[] StaggeredOffsets = [new(-1, 0, 0), new(0, -1, 0), new(0, 0, -1), new(0, 0, 1), new(0, 1, 0), new(1, 0, 0)];

    public static Pcg.SparseStencilMatrix<double> CreateISLaplacian<TMask>(VdbTree<int> index, VdbTree<TMask> interiorMask,
        bool staggered = false, PoissonSolverOptions? options = null) where TMask : unmanaged
    {
        options ??= new(); CheckOptions(options); ArgumentNullException.ThrowIfNull(index);
        var source = new Pcg.Vector<double>(CheckCount(index.ActiveVoxelCount, options.MaximumDegreesOfFreedom), 0);
        return CreateISLaplacianWithBoundaryConditions(index, interiorMask, DirichletBoundary, source, staggered, options);
    }
    /// <summary>Assembles a negative index-space Laplacian, modifying source values through the boundary callback.</summary>
    /// <remarks>Callbacks run serially in native leaf/voxel order. Collocated assembly ignores interiorMask.
    /// For staggered assembly, active interior-mask voxels must have six active immediate neighbors in index.
    /// Invalid masks throw rather than indexing the native invalid sentinel. Out-of-domain Int32 offsets throw.
    /// Cancellation or callback exceptions may leave source partially modified.</remarks>
    public static Pcg.SparseStencilMatrix<double> CreateISLaplacianWithBoundaryConditions<TMask>(VdbTree<int> index,
        VdbTree<TMask> interiorMask, PoissonBoundaryCondition boundary, Pcg.Vector<double> source,
        bool staggered = false, PoissonSolverOptions? options = null) where TMask : unmanaged
    {
        ArgumentNullException.ThrowIfNull(interiorMask); ArgumentNullException.ThrowIfNull(boundary); ArgumentNullException.ThrowIfNull(source);
        options ??= new(); var data = ValidateIndex(index, options);
        if (source.Size != data.Count) throw new ArithmeticException("Index tree and source vector dimensions differ.");
        var matrix = new Pcg.SparseStencilMatrix<double>(data.Count, 7); var vectorIndex = index.GetAccessor(); var interior = interiorMask.GetAccessor();
        ReadOnlySpan<Coord> offsets = staggered ? StaggeredOffsets : CollocatedOffsets;
        foreach (var leaf in data.Leaves)
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            foreach (int offset in leaf.ActiveOffsets)
            {
                Coord coordinate = leaf.OffsetToGlobalCoord(offset); int rowIndex = leaf.GetValue(offset); var row = matrix.GetRowEditor(rowIndex);
                bool isInterior = staggered && interior.IsValueOn(coordinate); double diagonal = 0;
                foreach (Coord direction in offsets)
                {
                    Coord neighbor = new(checked(coordinate.X + direction.X), checked(coordinate.Y + direction.Y), checked(coordinate.Z + direction.Z));
                    if (vectorIndex.ProbeValue(neighbor, out int column)) { row.SetValue(column, 1.0); diagonal -= 1.0; }
                    else
                    {
                        if (isInterior) throw new ArgumentException("An interior-mask voxel has an exterior neighbor.", nameof(interiorMask));
                        boundary(coordinate, neighbor, ref source[rowIndex], ref diagonal);
                    }
                }
                row.SetValue(rowIndex, diagonal);
            }
        }
        return matrix;
    }

    public static VdbTree<T> Solve<T>(VdbTree<T> source, ref Pcg.State state, bool staggered = false, PoissonSolverOptions? options = null)
        where T : unmanaged, IFloatingPointIeee754<T> => SolveWithBoundaryConditions(source, source, DirichletBoundary, ref state, staggered, options);

    public static VdbTree<T> SolveWithBoundaryConditions<T>(VdbTree<T> source, PoissonBoundaryCondition boundary,
        ref Pcg.State state, bool staggered = false, PoissonSolverOptions? options = null)
        where T : unmanaged, IFloatingPointIeee754<T> => SolveWithBoundaryConditions(source, source, boundary, ref state, staggered, options);

    /// <summary>Solves over the active topology of an independent domain, with invalid-preconditioner fallback to Jacobi.</summary>
    /// <remarks>Returns the last approximation even when state.Success is false. Exceptions leave state unchanged.
    /// The source and domain remain unchanged. Boundary values and preconditioner factories are caller-supplied.
    /// Options bound degrees of freedom and active-tile expansion before allocating the dense solve vectors.</remarks>
    public static VdbTree<T> SolveWithBoundaryConditions<T, TDomain>(VdbTree<T> source, VdbTree<TDomain> domain,
        PoissonBoundaryCondition boundary, ref Pcg.State state, bool staggered = false, PoissonSolverOptions? options = null)
        where T : unmanaged, IFloatingPointIeee754<T> where TDomain : unmanaged
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(domain); ArgumentNullException.ThrowIfNull(boundary); Pcg.Scalar<T>.Validate();
        options ??= new(); CheckOptions(options);
        var index = CreateIndexTree(domain, options);
        var rhs = CreateVectorFromTree<double, T>(source, index, options);
        var interior = CopyTopology<int, bool>(index, false);
        Morphology.ErodeActiveValues(interior, 1, NearestNeighbors.Face, TilePolicy.IgnoreTiles);
        var matrix = CreateISLaplacianWithBoundaryConditions(index, interior, boundary, rhs, staggered, options);
        matrix.Scale(-1.0); rhs.Scale(-1.0); var solution = new Pcg.Vector<double>(rhs.Size, 0);
        options.CancellationToken.ThrowIfCancellationRequested();
        Pcg.Preconditioner<double> preconditioner = options.PreconditionerFactory?.Invoke(matrix) ?? new Pcg.IncompleteCholeskyPreconditioner<double>(matrix);
        if (!preconditioner.IsValid) preconditioner = new Pcg.JacobiPreconditioner<double>(matrix);
        Pcg.State result = Pcg.ConjugateGradient.Solve(matrix, rhs, solution, preconditioner, state, options.CancellationToken);
        var output = CreateTreeFromVector(solution, index, T.Zero, options); state = result; return output;
    }
}
