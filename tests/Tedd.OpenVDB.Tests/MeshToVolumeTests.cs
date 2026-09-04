// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Numerics;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class MeshToVolumeTests
{
    [Fact]
    public void UnsupportedDistanceTypeIsRejectedBeforeEmptyConvenienceReturn()
    {
        Assert.Throws<NotSupportedException>(() => MeshToVolume.ToLevelSet<Half>(new Transform(), ReadOnlyMemory<Vec3f>.Empty, ReadOnlyMemory<MeshTriangle>.Empty));
    }
    [Fact]
    public void NativeOuterBandDoesNotActivateEveryEuclideanCornerWithinWidth()
    {
        var grid = MeshToVolume.Convert<double>(new QuadAndTriangleDataAdapter<Vec3f>(CubePoints, default, CubeQuads), new Transform(),
            flags: MeshToVolumeFlags.DisableRenormalization);
        // Native expansion only propagates a seed when distance + voxelSize < width.
        Assert.False(grid.Tree.IsValueOn(new(-4, -4, -2))); Assert.Equal(3, grid.Tree.GetValue(new(-4, -4, -2)));
        Assert.True(grid.Tree.IsValueOn(new(-3, -3, -2))); Assert.Equal(System.Math.Sqrt(2), grid.Tree.GetValue(new(-3, -3, -2)));
    }
    internal static readonly Vec3f[] CubePoints = [new(-2, -2, -2), new(-2, -2, 2), new(-2, 2, -2), new(-2, 2, 2), new(2, -2, -2), new(2, -2, 2), new(2, 2, -2), new(2, 2, 2)];
    internal static readonly MeshQuad[] CubeQuads = [new(0, 1, 3, 2), new(4, 6, 7, 5), new(0, 4, 5, 1), new(2, 3, 7, 6), new(0, 2, 6, 4), new(1, 5, 7, 3)];
    [Theory, InlineData(false, false), InlineData(false, true), InlineData(true, false), InlineData(true, true)]
    public void ClosedCubeMatchesAnalyticValuesAndPreservesNativeBandLimits(bool single, bool unsigned)
    {
        if (single) CheckCube<float>(unsigned); else CheckCube<double>(unsigned);
    }
    private static void CheckCube<T>(bool unsigned) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var result = MeshToVolume.Convert<T>(new QuadAndTriangleDataAdapter<Vec3f>(CubePoints, default, CubeQuads), Transform.CreateLinearTransform(1), 3, 3,
            MeshToVolumeFlags.DisableRenormalization | (unsigned ? MeshToVolumeFlags.UnsignedDistanceField : 0));
        Assert.Equal(unsigned ? GridClass.Unknown : GridClass.LevelSet, result.GridClass);
        foreach (var c in new CoordBBox(new(-6), new(6)))
        {
            Vec3d q = c.AsVec3d().Abs() - 2;
            double exact = Vec3d.Max(q, Vec3d.Zero).Length + System.Math.Min(0, System.Math.Max(q.X, System.Math.Max(q.Y, q.Z)));
            if (unsigned) exact = System.Math.Abs(exact);
            bool active = result.Tree.IsValueOn(c);
            double expected = active ? exact : exact < 0 ? -3 : 3, actual = double.CreateTruncating(result.Tree.GetValue(c));
            Assert.True(System.Math.Abs(expected - actual) < 2e-6, $"{c}: expected {expected:R}, actual {actual:R}");
            if (System.Math.Abs(exact) < 2) Assert.True(active);
            if (System.Math.Abs(exact) >= 3) Assert.False(active);
        }
    }
    [Theory, InlineData(false), InlineData(true)]
    public void IndexGridPreservesBackgroundAndGetsIndependentTransform(bool unsigned)
    {
        var transform = Transform.CreateLinearTransform(0.5); var indices = new Grid<int>(42); indices.Tree.SetValueOn(new(100), 88);
        var oldTree = indices.Tree; var result = MeshToVolume.Convert<float>(new QuadAndTriangleDataAdapter<Vec3f>(CubePoints, default, CubeQuads), transform, 3, 3,
            unsigned ? MeshToVolumeFlags.UnsignedDistanceField : MeshToVolumeFlags.None, indices);
        Assert.NotSame(oldTree, indices.Tree); Assert.Equal(42, indices.Background); Assert.Equal(42, indices.Tree.GetValue(new(100)));
        Assert.True(indices.Transform.IsEquivalentTo(transform)); Assert.NotSame(transform, indices.Transform); Assert.NotSame(result.Transform, indices.Transform);
        Assert.All(indices.Tree.EnumerateLeaves().SelectMany(l => l.ActiveOffsets.Select(l.GetValue)), id => Assert.InRange(id, 0, 5));
    }
    [Fact]
    public void EmptyConvenienceAndAdapterFollowDifferentNativeConstructionPaths()
    {
        var transform = Transform.CreateLinearTransform(0.5);
        var convenience = MeshToVolume.ToLevelSet<float>(transform, ReadOnlyMemory<Vec3f>.Empty, ReadOnlyMemory<MeshTriangle>.Empty, halfWidth: 5);
        var adapter = MeshToVolume.Convert<float>(new QuadAndTriangleDataAdapter<Vec3f>(ReadOnlyMemory<Vec3f>.Empty, default), transform, 5, 5);
        Assert.Equal(5, convenience.Background); Assert.Equal(GridClass.Unknown, convenience.GridClass); Assert.True(convenience.Transform.IsEquivalentTo(Transform.CreateLinearTransform(1)));
        Assert.Equal(2.5f, adapter.Background); Assert.Equal(GridClass.LevelSet, adapter.GridClass); Assert.True(adapter.Transform.IsEquivalentTo(transform));
    }
    [Theory, InlineData(float.NaN, 3), InlineData(float.PositiveInfinity, 3), InlineData(float.NegativeInfinity, 3), InlineData(3, float.NaN)]
    public void InvalidWidthsReturnNativeEmptyMaxBackground(float exterior, float interior)
    {
        var result = MeshToVolume.Convert<float>(new QuadAndTriangleDataAdapter<Vec3f>(CubePoints, default, CubeQuads), Transform.CreateLinearTransform(1), exterior, interior);
        Assert.Equal(float.MaxValue, result.Background); Assert.True(result.Tree.IsEmpty); Assert.Equal(GridClass.Unknown, result.GridClass);
    }
    [Fact]
    public void InteriorCallbacksRunAndCanCancelTheOperation()
    {
        int calls = 0; using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => MeshToVolume.Convert<float>(new QuadAndTriangleDataAdapter<Vec3f>(CubePoints, default, CubeQuads), Transform.CreateLinearTransform(1),
            interiorTest: c => { if (++calls == 5) cancellation.Cancel(); return c.X == 0; }, options: new() { CancellationToken = cancellation.Token }));
        Assert.True(calls >= 5);
    }
    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void ConversionHonorsResourceBudgetsAndPreCancellation(int mode)
    {
        var options = mode switch { 0 => new MeshToVolumeOptions { MaximumLeaves = 0 }, 1 => new MeshToVolumeOptions { MaximumVoxelEvaluations = 0 }, _ => new MeshToVolumeOptions { CancellationToken = new(true) } };
        Assert.ThrowsAny<Exception>(() => MeshToVolume.Convert<float>(new QuadAndTriangleDataAdapter<Vec3f>(CubePoints, default, CubeQuads), Transform.CreateLinearTransform(1), options: options));
    }
    [Theory, InlineData(float.NaN), InlineData(float.PositiveInfinity), InlineData(float.NegativeInfinity)]
    public void NonfiniteGeometryIsRejected(float value)
    {
        var points = (Vec3f[])CubePoints.Clone(); points[0] = new(value, 0, 0);
        Assert.Throws<ArgumentException>(() => MeshToVolume.Convert<float>(new QuadAndTriangleDataAdapter<Vec3f>(points, default, CubeQuads), Transform.CreateLinearTransform(1)));
    }
    [Fact]
    public void AdapterSupportsTrianglesThenQuadsAndRejectsInvalidIndices()
    {
        var adapter = new QuadAndTriangleDataAdapter<Vec3f>(CubePoints, new MeshTriangle[] { new(0, 1, 2) }, CubeQuads);
        Assert.Equal(7, adapter.PolygonCount); Assert.Equal(8, adapter.PointCount); Assert.Equal(3, adapter.VertexCount(0)); Assert.Equal(4, adapter.VertexCount(1));
        Assert.Equal(new Vec3d(-2, 2, -2), adapter.GetIndexSpacePoint(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.VertexCount(-1)); Assert.Throws<IndexOutOfRangeException>(() => adapter.VertexCount(7));
        Assert.Throws<NotSupportedException>(() => new QuadAndTriangleDataAdapter<int>(new[] { 1 }, default));
    }
    [Fact]
    public void ExteriorTracingRetainsValuesOnBarrierAndAllActivity()
    {
        var tree = new VdbTree<float>(100);
        var leaf = tree.TouchLeaf(Coord.Zero);
        for (int n = 0; n < 512; n++)
        {
            Coord c = leaf.OffsetToGlobalCoord(n); bool barrier = c.X is 1 or 6 || c.Y is 1 or 6 || c.Z is 1 or 6;
            leaf.SetValueOnly(n, barrier ? 0.5f : 100); if ((n % 7) == 0) leaf.SetActiveState(n, true);
        }
        ulong active = tree.ActiveVoxelCount; MeshToVolume.TraceExteriorBoundaries(tree);
        Assert.Equal(active, tree.ActiveVoxelCount); Assert.Equal(-100, tree.GetValue(Coord.Zero)); Assert.Equal(0.5f, tree.GetValue(new(1, 3, 3)));
        Assert.Equal(100, tree.GetValue(new(3, 3, 3))); Assert.Equal(100, tree.GetValue(new(100)));
    }
}
