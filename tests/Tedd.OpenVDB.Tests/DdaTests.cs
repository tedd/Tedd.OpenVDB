// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0

using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.Tests;

public sealed class DdaTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PositiveAxisTraversalHasInclusiveEndTime(int axis)
    {
        var direction = axis switch { 0 => Vec3d.UnitX, 1 => Vec3d.UnitY, _ => Vec3d.UnitZ };
        var dda = new Dda(new Rayd(new Vec3d(0.25), direction, 0, 2.75));
        Assert.Equal(new Coord(0), dda.Voxel);
        Assert.Equal(0, dda.Time);
        Assert.Equal(0.75, dda.Next);
        for (var step = 1; step <= 3; ++step)
        {
            Assert.True(dda.Step());
            Assert.Equal(step, dda.Voxel[axis]);
            Assert.Equal(step - 0.25, dda.Time);
        }
        Assert.Equal(dda.MaxTime, dda.Next);
        Assert.False(dda.Step());
        Assert.Equal(4, dda.Voxel[axis]);
        Assert.Equal(3.75, dda.Time);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void NegativeAxisTraversalStartsAtFloorAndCrossesAtCorrectTimes(int axis)
    {
        var direction = axis switch { 0 => -Vec3d.UnitX, 1 => -Vec3d.UnitY, _ => -Vec3d.UnitZ };
        var dda = new Dda(new Rayd(new Vec3d(-0.25), direction, 0, 2.75));
        Assert.Equal(new Coord(-1), dda.Voxel);
        for (var step = 1; step <= 3; ++step)
        {
            Assert.True(dda.Step());
            Assert.Equal(-1 - step, dda.Voxel[axis]);
            Assert.Equal(step - 0.25, dda.Time);
        }
        Assert.False(dda.Step());
    }

    [Fact]
    public void NegativeRayOnVoxelBoundaryStepsImmediately()
    {
        var dda = new Dda(new Rayd(new Vec3d(0, 0.5, 0.5), -Vec3d.UnitX, 0, 2));
        Assert.Equal(new Coord(0), dda.Voxel);
        Assert.Equal(0, dda.Next);
        Assert.True(dda.Step()); Assert.Equal(new Coord(-1, 0, 0), dda.Voxel); Assert.Equal(0, dda.Time);
        Assert.True(dda.Step()); Assert.Equal(new Coord(-2, 0, 0), dda.Voxel); Assert.Equal(1, dda.Time);
        Assert.True(dda.Step()); Assert.Equal(new Coord(-3, 0, 0), dda.Voxel); Assert.Equal(2, dda.Time);
        Assert.False(dda.Step());
    }

    [Fact]
    public void CornerTiesAdvanceZThenYThenXAtTheSameTime()
    {
        var dda = new Dda(new Rayd(new Vec3d(0.5), Vec3d.One, 0, 0.5));
        Assert.True(dda.Step()); Assert.Equal(new Coord(0, 0, 1), dda.Voxel); Assert.Equal(0.5, dda.Time);
        Assert.True(dda.Step()); Assert.Equal(new Coord(0, 1, 1), dda.Voxel); Assert.Equal(0.5, dda.Time);
        Assert.True(dda.Step()); Assert.Equal(new Coord(1, 1, 1), dda.Voxel); Assert.Equal(0.5, dda.Time);
        Assert.False(dda.Step()); Assert.Equal(new Coord(1, 1, 2), dda.Voxel);
    }

    [Fact]
    public void EdgeTiesAdvanceHighestAxisFirst()
    {
        var dda = new Dda(new Rayd(new Vec3d(0.5), new Vec3d(1, 1, 0), 0, 0.5));
        Assert.True(dda.Step()); Assert.Equal(new Coord(0, 1, 0), dda.Voxel);
        Assert.True(dda.Step()); Assert.Equal(new Coord(1, 1, 0), dda.Voxel);
        Assert.False(dda.Step());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(12)]
    public void NodeTraversalAlignsNegativeOriginsAndUsesNodeDimensions(int log2Dim)
    {
        var dimension = 1 << log2Dim;
        var dda = new Dda(new Rayd(new Vec3d(-0.25), Vec3d.UnitX, 0, dimension + 0.25), log2Dim);
        Assert.Equal(log2Dim, dda.Log2Dim);
        Assert.Equal(dimension, dda.Dim);
        Assert.Equal(new Coord(-dimension), dda.Voxel);
        Assert.Equal(0.25, dda.Next);
        Assert.True(dda.Step()); Assert.Equal(new Coord(0, -dimension, -dimension), dda.Voxel);
        Assert.True(dda.Step()); Assert.Equal(new Coord(dimension, -dimension, -dimension), dda.Voxel);
        Assert.False(dda.Step());
    }

    [Fact]
    public void ExplicitTimeWindowUsesRayPositionAtStartAndCanBeReinitialized()
    {
        var ray = new Rayd(new Vec3d(0.25), new Vec3d(2, 0, 0), 0, 100);
        var dda = new Dda(ray, 2, 3);
        Assert.Equal(new Coord(4, 0, 0), dda.Voxel);
        Assert.Equal(2, dda.Time);
        Assert.Equal(2.375, dda.Next);
        Assert.True(dda.Step()); Assert.Equal(5, dda.Voxel.X);
        Assert.True(dda.Step()); Assert.Equal(6, dda.Voxel.X);
        Assert.False(dda.Step());
        dda.Initialize(new Rayd(new Vec3d(4.5), -Vec3d.UnitY, 0, 1));
        Assert.Equal(new Coord(4), dda.Voxel);
        Assert.True(dda.Step()); Assert.Equal(new Coord(4, 3, 4), dda.Voxel);
    }

    [Fact]
    public void PositiveAndNegativeZeroDirectionsDoNotMoveOtherAxes()
    {
        var dda = new Dda(new Rayd(new Vec3d(0.5), new Vec3d(1, 0, -0d), 0, 10));
        var count = 0;
        while (dda.Step())
        {
            Assert.Equal(0, dda.Voxel.Y);
            Assert.Equal(0, dda.Voxel.Z);
            Assert.True(double.IsFinite(dda.Time));
            ++count;
        }
        Assert.Equal(10, count);
    }

    [Fact]
    public void SubnormalDirectionsDoNotProduceNaNOrPhantomStationarySteps()
    {
        var positive = new Dda(new Rayd(new Vec3d(0.5), new Vec3d(double.Epsilon, 0, 0), 0, double.MaxValue));
        Assert.Equal(double.MaxValue, positive.Next);
        Assert.False(positive.Step());
        Assert.Equal(new Coord(0), positive.Voxel);
        var negative = new Dda(new Rayd(new Vec3d(0, 0.5, 0.5), new Vec3d(-double.Epsilon, 0, 0), 0, double.MaxValue));
        Assert.True(negative.Step());
        Assert.Equal(new Coord(-1, 0, 0), negative.Voxel);
        Assert.Equal(0, negative.Time);
        Assert.False(negative.Step());
    }

    [Fact]
    public void ZeroLengthTimeWindowStillIncludesImmediateBoundaryHits()
    {
        var positive = new Dda(new Rayd(new Vec3d(0.5), Vec3d.UnitX, 0, 0));
        Assert.Equal(0, positive.Next);
        Assert.False(positive.Step());
        var negative = new Dda(new Rayd(new Vec3d(0), -Vec3d.UnitX, 0, 0));
        Assert.True(negative.Step());
        Assert.Equal(0, negative.Time);
        Assert.False(negative.Step());
    }

    [Fact]
    public void TraversalMatchesIndependentRayCellIntersections()
    {
        var random = new Random(715);
        for (var trial = 0; trial < 200; ++trial)
        {
            var eye = new Vec3d(random.NextDouble() * 6 - 3, random.NextDouble() * 6 - 3, random.NextDouble() * 6 - 3);
            var direction = new Vec3d(random.NextDouble() * 4 - 2, random.NextDouble() * 4 - 2, random.NextDouble() * 4 - 2);
            var ray = new Rayd(eye, direction, 0, 3);
            var dda = new Dda(ray);
            var steps = 0;
            do
            {
                var entry = dda.Time;
                var exit = dda.Next;
                Assert.True(entry <= exit);
                var midpoint = ray.At((entry + exit) * 0.5);
                Assert.Equal(dda.Voxel, Coord.Floor(midpoint));
                var box = new BBoxd(dda.Voxel.AsVec3d(), dda.Voxel.AsVec3d() + Vec3d.One);
                Assert.True(ray.Intersects(box, out var expectedEntry, out var expectedExit));
                Assert.InRange(System.Math.Abs(entry - expectedEntry), 0, 1e-12);
                Assert.InRange(System.Math.Abs(exit - expectedExit), 0, 1e-12);
                Assert.True(++steps < 100);
            } while (dda.Step());
        }
    }

    [Fact]
    public void CoordinateDomainCrossingsThrowInsteadOfWrapping()
    {
        var right = new Dda(new Rayd(new Vec3d(int.MaxValue, 0.5, 0.5), Vec3d.UnitX, 0, 2));
        Assert.Throws<OverflowException>(() => right.Step());
        Assert.Equal(int.MaxValue, right.Voxel.X);
        var left = new Dda(new Rayd(new Vec3d(int.MinValue, 0.5, 0.5), -Vec3d.UnitX, 0, 2));
        Assert.Throws<OverflowException>(() => left.Step());
        Assert.Equal(int.MinValue, left.Voxel.X);
        var upperNode = new Dda(new Rayd(new Vec3d(int.MaxValue, 0.5, 0.5), Vec3d.UnitX, 0, 2), 3);
        Assert.Throws<OverflowException>(() => upperNode.Step());
    }

    [Fact]
    public void InvalidInputsAndUninitializedUseAreRejected()
    {
        var ray = new Rayd(default, Vec3d.UnitX, 0, 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Dda(ray, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Dda(ray, 13));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Dda(ray, 2, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Dda(ray, double.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Dda(ray, 0, double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new Dda(default));
        var dda = default(Dda);
        Assert.Throws<InvalidOperationException>(() => dda.Step());
        Assert.Throws<InvalidOperationException>(() => dda.Next);
        dda.Initialize(ray);
        Assert.True(dda.Step());
    }
}
