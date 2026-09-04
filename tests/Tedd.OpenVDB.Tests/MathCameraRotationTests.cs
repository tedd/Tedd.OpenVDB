using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathCameraRotationTests
{
    private static void Near(Vec3d expected, Vec3d actual, double tolerance = 1e-10) =>
        Assert.True(expected.ApproxEquals(actual, tolerance), $"Expected {expected}, actual {actual}");
    [Fact]
    public void FromToRotationsCoverAllPrincipalAxesAndOppositeDirections()
    {
        Vec3d[] directions = [Vec3d.UnitX, Vec3d.UnitY, Vec3d.UnitZ, -Vec3d.UnitX, -Vec3d.UnitY, -Vec3d.UnitZ];
        foreach (Vec3d from in directions) foreach (Vec3d to in directions)
        {
            Mat3d rotation = Mat3d.CreateRotation(from, to);
            Near(to, from * rotation);
            Assert.InRange(System.Math.Abs(rotation.Determinant - 1), 0, 1e-14);
            Assert.True((rotation * rotation.Transpose()).ApproxEquals(Mat3d.Identity, 1e-14));
            Assert.Equal(rotation.ToMat4(), Mat4d.CreateRotation(from, to));
        }
    }
    [Fact]
    public void FromToRotationUsesReflectionBranchForNearlyParallelDirections()
    {
        Vec3d from = new Vec3d(2, 3, 4).Unit();
        foreach (double sign in new[] { -1d, 1d })
        {
            Vec3d to = (sign * from + new Vec3d(1e-10, -1e-10, 0)).Unit();
            Mat3d rotation = Mat3d.CreateRotation(from, to);
            Near(to, from * rotation, 1e-14);
            Assert.True((rotation * rotation.Transpose()).ApproxEquals(Mat3d.Identity, 1e-14));
        }
    }
    [Fact]
    public void RandomScaledDirectionPairsProduceProperMinimalRotations()
    {
        var random = new Random(7758);
        for (int iteration = 0; iteration < 500; ++iteration)
        {
            Vec3d from = new(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5);
            Vec3d to = new(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5);
            Mat3d rotation = Mat3d.CreateRotation(from, to);
            Near(to.Unit(), from.Unit() * rotation);
            Assert.InRange(System.Math.Abs(rotation.Determinant - 1), 0, 1e-12);
            Near(from.Cross(to).Unit(), from.Cross(to).Unit() * rotation, 1e-12);
        }
    }
    [Fact]
    public void FromToDegenerateInputsRetainNativeIeeeAndFailedNormalizationBehavior()
    {
        Assert.Equal(Mat3d.Identity, Mat3d.CreateRotation(Vec3d.Zero, Vec3d.Zero));
        Assert.False(Mat3d.CreateRotation(new Vec3d(double.NaN, 0, 0), Vec3d.UnitX).IsFinite);
        Assert.Throws<ArgumentOutOfRangeException>(() => Mat3d.CreateRotation(Vec3d.UnitX, Vec3d.UnitY, -1));
    }
    [Theory]
    [InlineData(0, 0, 1, 0, 1, 0)]
    [InlineData(0, 0, -1, 0, 1, 0)]
    [InlineData(1, 0, 0, 0, 1, 0)]
    [InlineData(0, 1, 0, 0, 0, 1)]
    [InlineData(1, 0, 1, 0, 1, 0)]
    public void CameraFrustumAlignsNearFarPlanesAndUpVector(double dx, double dy, double dz, double ux, double uy, double uz)
    {
        Vec3d position = new(3, -5, 7), direction = new(dx, dy, dz), up = new(ux, uy, uz);
        var map = NonlinearFrustumMap.CreateFromCamera(position, direction, up, 2, 3, 12, 80, 120);
        Assert.Equal(new BBoxd(Vec3d.Zero, new Vec3d(80, 40, 120)), map.BoundingBox);
        Vec3d center = new(40, 20, 0), forward = direction.Unit();
        Near(position + forward * 3, map.ApplyMap(center));
        Near(position + forward * 15, map.ApplyMap(center + new Vec3d(0, 0, 120)));
        Near(up, map.ApplyMap(center + new Vec3d(0, 20, 0)) - map.ApplyMap(center));
        Near(up * 5, map.ApplyMap(center + new Vec3d(0, 20, 120)) - map.ApplyMap(center + new Vec3d(0, 0, 120)));
        Near(position, map.ApplyMap(new Vec3d(40, 20, -30)));
        Assert.InRange(System.Math.Abs(map.Taper - 0.2), 0, 1e-15);
        Assert.InRange(System.Math.Abs(map.Depth - 3), 0, 1e-15);
        Near(new(0.05, 0.05, 0.1), map.VoxelSize);
    }
    [Fact]
    public void CameraIndexHeightUsesNativeRoundHalfUp()
    {
        var map = NonlinearFrustumMap.CreateFromCamera(Vec3d.Zero, Vec3d.UnitZ, Vec3d.UnitY, 2, 1, 4, 5, 10);
        Assert.Equal(new Vec3d(5, 3, 10), map.BoundingBox.Max);
    }
    [Fact]
    public void CameraRejectsInvalidOrientationAndDistances()
    {
        Assert.Throws<ArgumentException>(() => NonlinearFrustumMap.CreateFromCamera(Vec3d.Zero, Vec3d.Zero, Vec3d.UnitY, 1, 1, 1, 8, 8));
        Assert.Throws<ArgumentException>(() => NonlinearFrustumMap.CreateFromCamera(Vec3d.Zero, Vec3d.UnitZ, Vec3d.UnitZ, 1, 1, 1, 8, 8));
        Assert.Throws<ArgumentException>(() => NonlinearFrustumMap.CreateFromCamera(Vec3d.Zero, Vec3d.UnitZ, Vec3d.Zero, 1, 1, 1, 8, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => NonlinearFrustumMap.CreateFromCamera(Vec3d.Zero, Vec3d.UnitZ, Vec3d.UnitY, 0, 1, 1, 8, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => NonlinearFrustumMap.CreateFromCamera(Vec3d.Zero, Vec3d.UnitZ, Vec3d.UnitY, 1, 0, 1, 8, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => NonlinearFrustumMap.CreateFromCamera(Vec3d.Zero, Vec3d.UnitZ, Vec3d.UnitY, 1, 1, 0, 8, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => NonlinearFrustumMap.CreateFromCamera(Vec3d.Zero, Vec3d.UnitZ, Vec3d.UnitY, 1, 1, 1, 0, 8));
        Assert.Throws<OverflowException>(() => NonlinearFrustumMap.CreateFromCamera(Vec3d.Zero, Vec3d.UnitZ, Vec3d.UnitY, 1e-12, 1, 1, 8, 8));
    }
    [Fact]
    public void CurvatureOverloadsRetainAffineAndFrustumMapSemantics()
    {
        Mat4d matrix = Mat4d.CreateScale(new Vec3d(2, 3, 4)) * Mat4d.CreateRotation(0.31, Axis.Y);
        var affine = new Transform(matrix);
        Mat3d hessian = new(2, 1, 2, 1, 4, 3, 2, 3, 8);
        Mat3d inverse = matrix.InverseAffine().GetMat3(), expected = inverse * hessian * inverse.Transpose();
        Assert.Equal(expected, affine.ApplyInverseJacobianCurvature(hessian));
        Assert.Equal(expected, affine.ApplyInverseJacobianCurvature(hessian, new(1, 2, 3), new(4, 5, 6)));
        var map = new NonlinearFrustumMap(new BBoxd(Vec3d.Zero, new Vec3d(10)), 0.5, 2, matrix);
        var transform = new Transform(map); Vec3d gradient = new(2, 3, 4), index = new(3, 4, 5);
        Assert.Equal(expected, map.ApplyInverseJacobianCurvature(hessian));
        Assert.Equal(expected, transform.ApplyInverseJacobianCurvature(hessian));
        Assert.Equal(map.ApplyInverseJacobianCurvature(hessian, gradient, index), transform.ApplyInverseJacobianCurvature(hessian, gradient, index));
    }
}
