using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
using Pcg = Tedd.OpenVDB.Math.Pcg;
namespace Tedd.OpenVDB.Tests;

public class PoissonTests
{
    [Fact]
    public void IndexingDensifiesTilesAndPreservesAllAllocatedTopology()
    {
        var source = new VdbTree<float>(2); source.AddTile(1, new(-8), 7, true);
        source.SetValueOff(new(4096, 0, 0), 91); source.SetValueOn(new(-4097, -1, 9), 12);
        var index = PoissonSolver.CreateIndexTree(source, new() { MaximumNewLeaves = 1, MaximumDegreesOfFreedom = 513 });
        Assert.Equal(-1, index.Background); Assert.Equal(513UL, index.ActiveVoxelCount); Assert.Equal(0UL, index.ActiveTileCount); Assert.Equal(3, index.LeafCount);
        Assert.Equal(1UL, source.ActiveTileCount); Assert.Equal(2, source.LeafCount); Assert.Equal(-1, index.GetValue(new(4096, 0, 0))); Assert.False(index.IsValueOn(new(4096, 0, 0)));
        int expected = 0; foreach (var leaf in index.EnumerateLeaves()) foreach (int offset in leaf.ActiveOffsets) Assert.Equal(expected++, leaf.GetValue(offset));
        var vector = PoissonSolver.CreateVectorFromTree<double, float>(source, index); var restored = PoissonSolver.CreateTreeFromVector(vector, index, -17f);
        Assert.Equal(-17f, restored.Background); Assert.Equal(-17f, restored.GetValue(new(4096, 0, 0))); Assert.Equal(index.LeafCount, restored.LeafCount); Assert.True(index.HasSameTopology(restored));
        foreach (var (coordinate, _) in index.EnumerateActiveVoxels()) Assert.Equal(source.GetValue(coordinate), restored.GetValue(coordinate));
    }
    [Fact]
    public void IndexBudgetsRejectBeforeMutatingSource()
    {
        var source = new VdbTree<float>(); source.AddTile(1, Coord.Zero, 8, true);
        Assert.Throws<InvalidOperationException>(() => PoissonSolver.CreateIndexTree(source, new() { MaximumDegreesOfFreedom = 511 }));
        Assert.Throws<InvalidOperationException>(() => PoissonSolver.CreateIndexTree(source, new() { MaximumNewLeaves = 0 }));
        Assert.Equal(1UL, source.ActiveTileCount); Assert.Equal(0, source.LeafCount); Assert.Equal(8, source.GetValue(Coord.Zero));
        source.AddTile(3, new(4096, 0, 0), 9, true);
        Assert.Throws<InvalidOperationException>(() => PoissonSolver.CreateIndexTree(source)); Assert.Equal(0, source.LeafCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => PoissonSolver.CreateIndexTree(new VdbTree<float>(), new() { MaximumDegreesOfFreedom = -1 }));
    }
    [Fact]
    public void PopulateRejectsUnexpandedTilesAndOnlyChangesActiveValues()
    {
        var index = new VdbTree<int>(-1); index.AddTile(1, Coord.Zero, 17, true);
        Assert.Throws<ArgumentException>(() => PoissonSolver.PopulateIndexTree(index)); Assert.Equal(17, index.GetValue(Coord.Zero));
        index = new VdbTree<int>(-91); index.SetValueOff(new(0, 0, 1), 12); index.SetValueOn(new(0, 0, 2), 19); index.SetValueOn(new(0, 0, 3), 28);
        PoissonSolver.PopulateIndexTree(index); Assert.Equal(12, index.GetValue(new(0, 0, 1))); Assert.Equal(0, index.GetValue(new(0, 0, 2))); Assert.Equal(1, index.GetValue(new(0, 0, 3))); Assert.Equal(-91, index.Background);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(0)]
    public void MalformedIndicesNeverProducePartiallyUninitializedVectors(int secondIndex)
    {
        var index = new VdbTree<int>(-1); index[Coord.Zero] = 0; index[new(0, 0, 1)] = secondIndex;
        Assert.Throws<ArgumentException>(() => PoissonSolver.CreateVectorFromTree<double, float>(new(), index));
        Assert.Throws<ArgumentException>(() => PoissonSolver.CreateTreeFromVector(new Pcg.Vector<double>(2, 1), index, 0f));
        Assert.Throws<ArgumentException>(() => PoissonSolver.CreateISLaplacian(index, new VdbTree<bool>()));
    }
    [Fact]
    public void PermutedValidIndicesRoundTripAndAssembleCorrectly()
    {
        var index = new VdbTree<int>(-1); index[new(0, 0, 0)] = 2; index[new(0, 0, 1)] = 0; index[new(0, 0, 2)] = 1;
        var source = new VdbTree<float>(91); source.SetValueOff(new(0, 0, 0), 3); source[new(0, 0, 1)] = 5; source[new(0, 0, 2)] = 7;
        var values = PoissonSolver.CreateVectorFromTree<double, float>(source, index); Assert.Equal(new[] { 5.0, 7, 3 }, values.AsSpan().ToArray());
        var output = PoissonSolver.CreateTreeFromVector(values, index, -1f); Assert.Equal(3, output[Coord.Zero]); Assert.True(output.IsValueOn(Coord.Zero));
        var matrix = PoissonSolver.CreateISLaplacian(index, new VdbTree<bool>(), true);
        Assert.Equal(-6, matrix[0, 0]); Assert.Equal(1, matrix[0, 1]); Assert.Equal(1, matrix[0, 2]); Assert.Equal(0, matrix[1, 2]);
        Assert.Throws<ArithmeticException>(() => PoissonSolver.CreateTreeFromVector(new Pcg.Vector<double>(2), index, 0f));
    }
    [Theory]
    [InlineData(false, 24)]
    [InlineData(true, 6)]
    public void QuadraticFieldHasNativeIndexSpaceLaplacian(bool staggered, double expected)
    {
        var field = new VdbTree<double>();
        for (int x = -3; x <= 3; ++x) for (int y = -3; y <= 3; ++y) for (int z = -3; z <= 3; ++z) field[new(x, y, z)] = x * x + y * y + z * z;
        var index = PoissonSolver.CreateIndexTree(field); var interior = new VdbTree<bool>(); interior.TopologyUnion(index); Morphology.ErodeActiveValues(interior, 1, NearestNeighbors.Face, TilePolicy.IgnoreTiles);
        var matrix = PoissonSolver.CreateISLaplacian(index, interior, staggered); var values = PoissonSolver.CreateVectorFromTree<double, double>(field, index); var result = new Pcg.Vector<double>(values.Size); matrix.VectorMultiply(values, result);
        for (int x = -1; x <= 1; ++x) for (int y = -1; y <= 1; ++y) for (int z = -1; z <= 1; ++z) Assert.Equal(expected, result[index[new(x, y, z)]]);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundaryCallbackOrderAndReferenceUpdatesMatchNative(bool staggered)
    {
        var domain = new VdbTree<bool>(); domain[Coord.Zero] = true; var index = PoissonSolver.CreateIndexTree(domain); var source = new Pcg.Vector<double>(1, 0); var neighbors = new List<Coord>();
        var matrix = PoissonSolver.CreateISLaplacianWithBoundaryConditions(index, new VdbTree<bool>(), (Coord c, Coord n, ref double value, ref double diagonal) => { Assert.Equal(Coord.Zero, c); neighbors.Add(n); value = value * 10 + neighbors.Count; diagonal -= neighbors.Count; }, source, staggered);
        Coord[] expected = staggered ? [new(-1, 0, 0), new(0, -1, 0), new(0, 0, -1), new(0, 0, 1), new(0, 1, 0), new(1, 0, 0)] : [new(-2, 0, 0), new(2, 0, 0), new(0, -2, 0), new(0, 2, 0), new(0, 0, -2), new(0, 0, 2)];
        Assert.Equal(expected, neighbors); Assert.Equal(123456, source[0]); Assert.Equal(-21, matrix[0, 0]);
    }
    [Fact]
    public void InteriorMaskCannotCreateInvalidNeighborIndices()
    {
        var domain = new VdbTree<bool>(); domain[Coord.Zero] = true; var index = PoissonSolver.CreateIndexTree(domain);
        var interior = new VdbTree<bool>(); interior[Coord.Zero] = false; // Activity, not stored bool value, classifies interior.
        Assert.Throws<ArgumentException>(() => PoissonSolver.CreateISLaplacian(index, interior, true));
        Assert.Equal(-6, PoissonSolver.CreateISLaplacian(index, interior, false)[0, 0]);
    }
    [Theory]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, false)]
    [InlineData(int.MinValue, true)]
    [InlineData(int.MaxValue, true)]
    public void CoordinateOverflowIsExplicit(int coordinate, bool staggered)
    {
        var index = new VdbTree<int>(-1); index[new(coordinate, 0, 0)] = 0;
        Assert.Throws<OverflowException>(() => PoissonSolver.CreateISLaplacian(index, new VdbTree<bool>(), staggered));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenTankPressureMatchesAnalyticDepth(bool single)
    {
        if (single) Tank<float>(); else Tank<double>();
        static void Tank<T>() where T : unmanaged, IFloatingPointIeee754<T>
        {
            var (source, _) = PoissonTestSystems.Input<T>(6); var before = source.DeepCopy(); var state = new Pcg.State(false, 100, 1e-12, 1e-12);
            var output = PoissonSolver.SolveWithBoundaryConditions(source, PoissonTestSystems.Boundary(2), ref state, true);
            Assert.True(state.Success); Assert.InRange(state.Iterations, 1, 60); Assert.Equal(T.Zero, output.Background); Assert.True(source.HasSameTopology(before));
            foreach (var (coordinate, value) in output.EnumerateActiveVoxels()) { Assert.InRange(System.Math.Abs(double.CreateTruncating(value) + coordinate.Y), 0, typeof(T) == typeof(float) ? 1e-5 : 1e-10); Assert.Equal(T.Zero, source.GetValue(coordinate)); }
        }
    }
    [Fact]
    public void FailedIterationLimitReturnsApproximationAndPreservesInputs()
    {
        var (source, domain) = PoissonTestSystems.Input<float>(5); var sourceBefore = source.DeepCopy(); var domainBefore = domain.DeepCopy(); var state = new Pcg.State(false, 0, 0, 0);
        var output = PoissonSolver.SolveWithBoundaryConditions(source, domain, PoissonSolver.DirichletBoundary, ref state, true);
        Assert.False(state.Success); Assert.Equal(0, state.Iterations); Assert.Equal(domain.ActiveVoxelCount, output.ActiveVoxelCount); Assert.True(source.HasSameTopology(sourceBefore)); Assert.True(domain.HasSameTopology(domainBefore));
        foreach (var (coordinate, value) in output.EnumerateActiveVoxels()) { Assert.Equal(0, value); Assert.Equal(sourceBefore.GetValue(coordinate), source.GetValue(coordinate)); }
    }
    [Fact]
    public void InvalidCustomPreconditionerFallsBackToJacobi()
    {
        var (source, _) = PoissonTestSystems.Input<double>(3); var one = new Pcg.State(false, 40, 1e-9, 1e-11); var two = one;
        var expected = PoissonSolver.Solve(source, ref one, true, new() { PreconditionerFactory = a => new Pcg.JacobiPreconditioner<double>(a) });
        bool called = false; var actual = PoissonSolver.Solve(source, ref two, true, new() { PreconditionerFactory = a => { called = true; return new InvalidPreconditioner(a.Size); } });
        Assert.True(called); Assert.Equal(one, two); foreach (var (coordinate, value) in actual.EnumerateActiveVoxels()) Assert.Equal(expected.GetValue(coordinate), value);
    }
    private sealed class InvalidPreconditioner(int size) : Pcg.Preconditioner<double>(size)
    {
        public override bool IsValid => false;
        public override void Apply(Pcg.Vector<double> residual, Pcg.Vector<double> result) => throw new InvalidOperationException("The invalid preconditioner must be replaced.");
    }
    [Fact]
    public void CancellationAndCallbackFailurePreserveCallerStateAndTree()
    {
        var (source, _) = PoissonTestSystems.Input<double>(3); var original = new Pcg.State(true, 13, .1, .2); var state = original;
        Assert.Throws<OperationCanceledException>(() => PoissonSolver.Solve(source, ref state, options: new() { CancellationToken = new(true) })); Assert.Equal(original, state);
        ulong active = source.ActiveVoxelCount;
        Assert.Throws<InvalidOperationException>(() => PoissonSolver.SolveWithBoundaryConditions(source, (Coord c, Coord n, ref double value, ref double diagonal) => throw new InvalidOperationException("boundary failed"), ref state));
        Assert.Equal(original, state); Assert.Equal(active, source.ActiveVoxelCount);
    }
    [Fact]
    public void EmptyDomainReturnsZeroBackgroundAndFailedZeroIterationState()
    {
        var domain = new VdbTree<byte>(); domain.SetValueOff(new(4096, 1, 1), 77); var state = new Pcg.State(true, 99, .1, .2);
        var output = PoissonSolver.SolveWithBoundaryConditions(new VdbTree<float>(8), domain, PoissonSolver.DirichletBoundary, ref state);
        Assert.Equal(default, state); Assert.Equal(0, output.Background); Assert.Equal(1, output.LeafCount); Assert.Equal(0UL, output.ActiveVoxelCount);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void NumericLowLevelConversionsSupportStoredScalarTypes(int type)
    {
        switch (type) { case 0: RoundTrip<sbyte>(); break; case 1: RoundTrip<byte>(); break; case 2: RoundTrip<short>(); break; case 3: RoundTrip<ushort>(); break; case 4: RoundTrip<int>(); break; case 5: RoundTrip<uint>(); break; case 6: RoundTrip<long>(); break; default: RoundTrip<Half>(); break; }
        static void RoundTrip<T>() where T : unmanaged, INumberBase<T>
        {
            var source = new VdbTree<T>(T.CreateChecked(3)); source[Coord.Zero] = T.CreateChecked(17); source[new(0, 0, 1)] = T.CreateChecked(29);
            var index = PoissonSolver.CreateIndexTree(source); var vector = PoissonSolver.CreateVectorFromTree<double, T>(source, index); Assert.Equal(new[] { 17.0, 29 }, vector.AsSpan().ToArray());
            var output = PoissonSolver.CreateTreeFromVector(vector, index, T.CreateChecked(7)); Assert.Equal(T.CreateChecked(17), output[Coord.Zero]); Assert.Equal(T.CreateChecked(29), output[new(0, 0, 1)]); Assert.Equal(T.CreateChecked(7), output.Background);
        }
    }
    [Fact]
    public void BooleanConversionsUseNativeNonzeroSemantics()
    {
        var source = new VdbTree<bool>(true); source.SetValueOn(Coord.Zero, false); source.SetValueOn(new(0, 0, 1), true); source.SetValueOn(new(0, 0, 2), false);
        var index = PoissonSolver.CreateIndexTree(source); var vector = PoissonSolver.CreateVectorFromTree<double>(source, index); Assert.Equal(new[] { 0.0, 1, 0 }, vector.AsSpan().ToArray());
        vector[0] = double.NaN; vector[1] = -0.0; vector[2] = -2;
        var output = PoissonSolver.CreateTreeFromVector(vector, index, false); Assert.True(output[Coord.Zero]); Assert.False(output[new(0, 0, 1)]); Assert.True(output[new(0, 0, 2)]); Assert.True(output.IsValueOn(new(0, 0, 1)));
    }
}
