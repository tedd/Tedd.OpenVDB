using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class VolumeToMeshTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(.1)]
    [InlineData(.5)]
    [InlineData(1)]
    public void ClosedSphereHasFinitePointsAndPairedOrientedEdges(double adaptivity)
    {
        var input = VolumeToMeshConformanceTests.Shape<float>(1); var before = input.DeepCopy(); var mesher = new VolumeToMesh(adaptivity: adaptivity); mesher.Extract(input);
        Assert.NotEmpty(mesher.Points.ToArray()); foreach (var point in mesher.Points.Span) Assert.True(point.IsFinite);
        var edges = new Dictionary<(int, int), (int Count, int Direction)>();
        foreach (var pool in mesher.PolygonPools)
        {
            foreach (var q in pool.Quads.Span) { Edge(q.A, q.B); Edge(q.B, q.C); Edge(q.C, q.D); Edge(q.D, q.A); }
            foreach (var t in pool.Triangles.Span) { Edge(t.A, t.B); Edge(t.B, t.C); Edge(t.C, t.A); }
        }
        foreach (var entry in edges.Values) { Assert.Equal(2, entry.Count); Assert.Equal(0, entry.Direction); }
        GridTransformerTests.EqualTrees(before.Tree, input.Tree);
        void Edge(int a, int b) { Assert.InRange(a, 0, mesher.Points.Length - 1); Assert.InRange(b, 0, mesher.Points.Length - 1); Assert.NotEqual(a, b); var key = (System.Math.Min(a, b), System.Math.Max(a, b)); edges.TryGetValue(key, out var old); edges[key] = (old.Count + 1, old.Direction + (a < b ? 1 : -1)); }
    }
    [Fact]
    public void MesherReuseResetsResultsAndPreservesPriorMemory()
    {
        var mesher = new VolumeToMesh(); mesher.Extract(VolumeToMeshConformanceTests.Shape<float>(1)); var memory = mesher.Points; var copy = memory.ToArray(); mesher.Extract(new Grid<float>());
        Assert.Empty(mesher.Points.ToArray()); Assert.Empty(mesher.PolygonPools); Assert.Empty(mesher.PointFlags.ToArray()); Assert.Equal(copy, memory.ToArray());
    }
    [Fact]
    public void AllInactiveCrossingsDoNotCreateSurface()
    {
        var grid = new Grid<float>(1); grid.Tree.SetValueOff(Coord.Zero, -1); var mesher = new VolumeToMesh(); mesher.Extract(grid); Assert.Empty(mesher.Points.ToArray());
    }
    [Fact]
    public void BoolInsideClassificationUsesStoredValue()
    {
        var input = new Grid<bool>(); input.Tree.SetValueOn(Coord.Zero, true); var mesher = new VolumeToMesh(.5); mesher.Extract(input); Assert.Equal(8, mesher.Points.Length); Assert.Equal(6, mesher.PolygonPools.Sum(pool => pool.NumQuads));
        input.Tree.SetValueOnly(Coord.Zero, false); mesher.Extract(input); Assert.Empty(mesher.Points.ToArray()); Assert.Equal(1ul, input.ActiveVoxelCount);
    }
    [Fact]
    public void SparseRootTileBudgetRejectsSurfaceExpansion()
    {
        var input = new Grid<float>(3); input.Tree.AddTile(3, Coord.Zero, -1, true); var mesher = new VolumeToMesh { MaximumCells = 10000 }; Assert.Throws<InvalidOperationException>(() => mesher.Extract(input)); Assert.Equal(1ul, input.Tree.ActiveTileCount); Assert.Equal(0, input.Tree.LeafCount);
    }
    [Fact]
    public void PrecancellationRetainsPreviouslyCompletedOutput()
    {
        var mesher = new VolumeToMesh(); mesher.Extract(VolumeToMeshConformanceTests.Shape<float>(1)); var old = mesher.Points.ToArray(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); Assert.Throws<OperationCanceledException>(() => mesher.Extract(new Grid<float>(), cancellation.Token)); Assert.Equal(old, mesher.Points.ToArray());
    }
    [Fact]
    public void NonfiniteCrossingFailsWithoutMutatingInput()
    {
        var grid = new Grid<float>(1); grid.Tree.SetValueOn(Coord.Zero, -1); grid.Tree.SetValueOn(new(1, 0, 0), float.NaN); var mesher = new VolumeToMesh(); Assert.Throws<ArgumentException>(() => mesher.Extract(grid)); Assert.True(float.IsNaN(grid.Tree.GetValue(new(1, 0, 0)))); Assert.Equal(2ul, grid.ActiveVoxelCount); Assert.Empty(mesher.Points.ToArray()); Assert.Empty(mesher.PolygonPools); Assert.Empty(mesher.PointFlags.ToArray());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SurfaceMaskUsesTopologyAndRetainsOpenBoundary(bool inverted)
    {
        var input = VolumeToMeshConformanceTests.Shape<float>(1); var mask = VolumeToMeshConformanceTests.Mask(1); var mesher = new VolumeToMesh(); mesher.SetSurfaceMask(mask, inverted); mesher.Extract(input); Assert.True(mesher.Points.Length > 0);
        var full = new VolumeToMesh(); full.Extract(input); Assert.True(mesher.PolygonPools.Sum(p => p.NumQuads) < full.PolygonPools.Sum(p => p.NumQuads));
        foreach (var leaf in mask.Tree.EnumerateLeaves()) foreach (int offset in leaf.ActiveOffsets) leaf.SetValueOnly(offset, true); var expected = mesher.Points.ToArray(); mesher.Extract(input); Assert.Equal(expected, mesher.Points.ToArray());
    }
    [Theory]
    [InlineData(0)]
    [InlineData(.25)]
    [InlineData(1)]
    public void ReferenceMeshFlagsCoincidentExteriorPolygons(double adaptivity)
    {
        var reference = VolumeToMeshConformanceTests.Shape<float>(1); var mesher = new VolumeToMesh(adaptivity: adaptivity); mesher.SetReferenceGrid(reference, .5); mesher.Extract(reference);
        Assert.Equal(mesher.Points.Length, mesher.PointFlags.Length);
        foreach (var pool in mesher.PolygonPools) { foreach (byte flag in pool.QuadFlags.Span) Assert.Equal((byte)PolygonFlags.Exterior, flag); foreach (byte flag in pool.TriangleFlags.Span) Assert.Equal((byte)PolygonFlags.Exterior, flag); }
    }
    [Fact]
    public void ReferenceResetClearsCachedFragmentWeights()
    {
        var reference = VolumeToMeshConformanceTests.Shape<float>(1); var fragment = VolumeToMeshConformanceTests.Shape<float>(7); var mesher = new VolumeToMesh(); mesher.SetReferenceGrid(reference); mesher.Extract(VolumeToMeshConformanceTests.Shape<float>(9)); mesher.Extract(fragment);
        mesher.SetReferenceGrid(reference); mesher.Extract(fragment); var fresh = new VolumeToMesh(); fresh.SetReferenceGrid(reference); fresh.Extract(fragment); Assert.Equal(fresh.Points.ToArray(), mesher.Points.ToArray()); Assert.Equal(fresh.PointFlags.ToArray(), mesher.PointFlags.ToArray());
    }
    [Fact]
    public void IntegerLevelSetUsesGenericScalarPath()
    {
        var input = new Grid<int>(3) { GridClass = GridClass.LevelSet }; input.Tree.SetValueOn(Coord.Zero, -1); var mesher = new VolumeToMesh(); mesher.Extract(input); Assert.Equal(8, mesher.Points.Length); Assert.Equal(6, mesher.PolygonPools.Sum(p => p.NumQuads));
    }
    [Fact]
    public void FeaturePointIntersectsOrthogonalTangentPlanes()
    {
        Vec3d[] points = [new(2, 5, 7), new(8, 3, 9), new(5, 6, 4)], normals = [Vec3d.UnitX, Vec3d.UnitY, Vec3d.UnitZ]; Assert.Equal(new Vec3d(2, 3, 4), VolumeToMesh.FindFeaturePoint(points, normals));
    }
    [Fact]
    public void FeaturePointClampsIllConditionedDirectionsAtNativeThreshold()
    {
        Vec3d[] points = [new(2, 0, 0), new(0, 3, 0), new(0, 0, 4)], normals = [Vec3d.UnitX, Vec3d.UnitY, Vec3d.UnitZ * .01]; Vec3d result = VolumeToMesh.FindFeaturePoint(points, normals); Assert.Equal(2, result.X, 12); Assert.Equal(3, result.Y, 12); Assert.Equal(4.0 / 3, result.Z, 12);
    }
    [Fact]
    public void FeaturePointReproducesSeededPlaneIntersections()
    {
        var random = new Random(150901); for (int run = 0; run < 32; ++run)
        {
            Vec3d target = new(random.NextDouble() * 10, random.NextDouble() * 10, random.NextDouble() * 10); var points = new Vec3d[16]; var normals = new Vec3d[16];
            for (int n = 0; n < points.Length; ++n) { normals[n] = new Vec3d(random.NextDouble() - .5, random.NextDouble() - .5, random.NextDouble() - .5).Unit(); Vec3d tangent = normals[n].Cross(Vec3d.UnitX); points[n] = target + tangent * (random.NextDouble() * 4); }
            Vec3d actual = VolumeToMesh.FindFeaturePoint(points, normals); Assert.True((actual - target).Length < 1e-10);
        }
    }
    [Fact]
    public void FeaturePointRetainsNativeDegenerateBehavior()
    {
        Assert.Equal(Vec3d.Zero, VolumeToMesh.FindFeaturePoint([], [])); Assert.False(VolumeToMesh.FindFeaturePoint([Vec3d.One], [Vec3d.Zero]).IsFinite); Assert.Throws<ArgumentException>(() => VolumeToMesh.FindFeaturePoint([Vec3d.One], []));
    }
    [Fact]
    public void PolygonPoolsCopyTrimAndRetainIndependentFlags()
    {
        var pool = new PolygonPool(2, 1); pool.SetQuad(0, new(1, 2, 3, 4), PolygonFlags.Exterior); pool.SetTriangle(0, new(5, 6, 7), PolygonFlags.FractureSeam); var copy = pool.DeepCopy(); pool.SetQuad(0, new(9, 8, 7, 6)); Assert.Equal(new MeshQuad(1, 2, 3, 4), copy.GetQuad(0)); Assert.Equal((byte)PolygonFlags.Exterior, copy.QuadFlags.Span[0]); Assert.Equal((byte)PolygonFlags.FractureSeam, copy.TriangleFlags.Span[0]); Assert.True(copy.TrimQuads(1)); Assert.False(copy.TrimQuads(1)); copy.ClearTriangles(); Assert.Equal(0, copy.NumTriangles); Assert.Equal(3, new MeshQuad(1, 2, 3, -1).VertexCount); Assert.Throws<ArgumentOutOfRangeException>(() => new MeshTriangle(1, 2, 3)[3]);
    }
}
