using System.Runtime.InteropServices;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathQuaternionTests
{
    [Theory]
    [InlineData(Axis.X, 0, 1, 0, 0, 0, 1)]
    [InlineData(Axis.Y, 0, 0, 1, 1, 0, 0)]
    [InlineData(Axis.Z, 1, 0, 0, 0, 1, 0)]
    public void AxisAngleQuaternionMatchesNativeRowRotation(Axis axis, double x, double y, double z, double ex, double ey, double ez)
    {
        Quatd quaternion = Quatd.FromAxisAngle(axis, System.Math.PI / 2);
        Assert.True(quaternion.RotateVector(new(x, y, z)).ApproxEquals(new(ex, ey, ez), 1e-12));
        Assert.True(quaternion.ToMat3().ApproxEquals(Mat3d.CreateRotation(System.Math.PI / 2, axis), 1e-12));
        Assert.InRange(System.Math.Abs(quaternion.Angle - System.Math.PI / 2), 0, 1e-12);
        Assert.InRange(System.Math.Abs(quaternion.Axis.Length - 1), 0, 1e-12);
    }
    [Fact]
    public void NonunitQuaternionRotationNormalizesAndDegenerateRotationIsIdentity()
    {
        Quatd q = Quatd.FromAxisAngle(Axis.Z, 0.81);
        Assert.True(q.ToMat3().ApproxEquals((q * 17).ToMat3(), 1e-12));
        Assert.Equal(Mat3d.Identity, Quatd.Zero.ToMat3());
        Assert.Equal(Mat3d.Identity, new Quatd(1e-6, 0, 0, 0).ToMat3());
        Assert.Equal(new Vec3d(1, 2, 3), Quatd.Zero.RotateVector(new(1, 2, 3)));
        Assert.Equal(0, Quatd.Identity.Angle);
        Assert.Equal(Vec3d.UnitX, Quatd.Identity.Axis);
        Assert.Throws<ArithmeticException>(() => Quatd.Zero.Unit());
        Assert.Throws<ArithmeticException>(() => Quatd.Zero.Inverse());
        Assert.False(Quatd.Zero.TryNormalize(out var normalized));
        Assert.Equal(Quatd.Zero, normalized);
        Assert.Throws<ArgumentException>(() => Quatd.FromAxisAngle(new Vec3d(0, 0, 2), 1));
        Assert.Throws<ArgumentException>(() => Quatd.FromAxisAngle(Vec3d.Zero, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Quatd.FromAxisAngle((Axis)3, 1));
    }
    [Fact]
    public void QuaternionProductsAndInverseMatchKnownHamiltonAlgebra()
    {
        Quatd i = new(1, 0, 0, 0), j = new(0, 1, 0, 0), k = new(0, 0, 1, 0);
        Assert.Equal(k, i * j);
        Assert.Equal(-k, j * i);
        Assert.Equal(-Quatd.Identity, i * i);
        Assert.Equal(-Quatd.Identity, j * j);
        Assert.Equal(-Quatd.Identity, k * k);
        Quatd value = new(1, 2, 3, 4);
        Assert.True((value * value.Inverse()).ApproxEquals(Quatd.Identity, 1e-12));
        Assert.True((value.Inverse() * value).ApproxEquals(Quatd.Identity, 1e-12));
        Assert.Equal(new Quatd(-1, -2, -3, 4), value.Conjugate());
        Assert.Equal(value, value.Conjugate().Conjugate());
        Assert.True(value.Unit().Conjugate().ApproxEquals(value.Unit().Inverse(), 1e-12));
        Assert.Equal(new Quatd(2, 3, 4, 0), Quatd.Identity.Derivative(new(2, 3, 4)));
    }
    [Fact]
    public void QuaternionMultiplicationReversesRowMatrixCompositionOrder()
    {
        Quatd a = Quatd.FromAxisAngle(Axis.X, 0.7), b = Quatd.FromAxisAngle(Axis.Z, -0.8);
        Vec3d point = new(2, 3, 5);
        Assert.True((a * b).ToMat3().ApproxEquals(b.ToMat3() * a.ToMat3(), 1e-12));
        Assert.True((a * b).RotateVector(point).ApproxEquals(a.RotateVector(b.RotateVector(point)), 1e-12));
    }
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    [InlineData(1.0)]
    public void QuaternionSlerpMatchesConstantAngularVelocity(double weight)
    {
        Quatd a = Quatd.Identity, b = Quatd.FromAxisAngle(Axis.Z, System.Math.PI / 2);
        Quatd result = Quatd.Slerp(a, b, weight);
        Assert.True(result.ApproxEquals(Quatd.FromAxisAngle(Axis.Z, weight * System.Math.PI / 2), 1e-12));
        Assert.InRange(System.Math.Abs(result.Length - 1), 0, 1e-12);
        Assert.True(Mat3d.Slerp(a.ToMat3(), b.ToMat3(), weight).ApproxEquals(result.ToMat3(), 1e-12));
    }
    [Fact]
    public void SlerpPreservesSourceLongArcAndAntipodalTieRules()
    {
        Quatd a = Quatd.Identity, b = -Quatd.FromAxisAngle(Axis.Z, System.Math.PI / 2);
        var midpoint = Quatd.Slerp(a, b, 0.5);
        double root = System.Math.Sqrt(0.5);
        Assert.True(midpoint.RotateVector(Vec3d.UnitX).ApproxEquals(new(-root, -root, 0), 1e-12));
        Assert.Equal(a, Quatd.Slerp(a, -a, 0.499));
        Assert.Equal(-a, Quatd.Slerp(a, -a, 0.5));
        Assert.Equal(-a, Quatd.Slerp(a, -a, 0.501));
        Assert.True(Quatd.Slerp(a, a, 0.43).ApproxEquals(a, 1e-12));
        Assert.True(Quatd.Slerp(a * 3, a * 5, 0.43).ApproxEquals(a, 1e-12));
    }
    [Fact]
    public void RotationMatrixConversionUsesEveryTraceBranch()
    {
        foreach (Axis axis in Enum.GetValues<Axis>())
            foreach (double angle in new[] { 0.0, 0.3, System.Math.PI, 3.7, 6.0 })
            {
                var q = Quatd.FromAxisAngle(axis, angle);
                var restored = Quatd.FromRotationMatrix(q.ToMat3());
                Assert.True(restored.ToMat3().ApproxEquals(q.ToMat3(), 1e-12));
                Assert.InRange(System.Math.Abs(System.Math.Abs(restored.Dot(q)) - 1), 0, 1e-12);
            }
        Assert.Throws<ArithmeticException>(() => Quatd.FromRotationMatrix(Mat3d.CreateScale(2)));
        Assert.Throws<ArithmeticException>(() => Quatd.FromRotationMatrix(Mat3d.CreateScale(new Vec3d(-1, 1, 1))));
        Assert.Throws<ArithmeticException>(() => Quatd.FromRotationMatrix(Mat3d.CreateShear(0.1, Axis.X, Axis.Y)));
    }
    [Fact]
    public void RandomQuaternionRotationPreservesLengthDotProductsAndInverse()
    {
        Random random = new(46857);
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            Quatd quaternion = new(random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1, random.NextDouble() * 2 - 1);
            Vec3d a = new(random.NextDouble() * 10, random.NextDouble() * 10, random.NextDouble() * 10);
            Vec3d b = new(random.NextDouble() * 10, random.NextDouble() * 10, random.NextDouble() * 10);
            var rotatedA = quaternion.RotateVector(a); var rotatedB = quaternion.RotateVector(b);
            Assert.InRange(System.Math.Abs(a.LengthSquared - rotatedA.LengthSquared), 0, 1e-10);
            Assert.InRange(System.Math.Abs(a.Dot(b) - rotatedA.Dot(rotatedB)), 0, 1e-10);
            Assert.True(quaternion.Inverse().RotateVector(rotatedA).ApproxEquals(a, 1e-11));
            Assert.True(Quatd.FromRotationMatrixUnchecked(quaternion.ToMat3()).ToMat3().ApproxEquals(quaternion.ToMat3(), 1e-12));
        }
    }
    [Fact]
    public void BezierRotationInterpolationPreservesEndpoints()
    {
        Mat3d a = Mat3d.CreateRotation(0.1, Axis.X), b = Mat3d.CreateRotation(0.4, Axis.Y), c = Mat3d.CreateRotation(0.7, Axis.Z), d = Mat3d.CreateRotation(1.2, Axis.X);
        Assert.True(Mat3d.BezierLerp(a, b, c, d, 0).ApproxEquals(a, 1e-12));
        Assert.True(Mat3d.BezierLerp(a, b, c, d, 1).ApproxEquals(d, 1e-12));
    }
    [Fact]
    public void QuaternionNaNAndSpanSemanticsAreExplicit()
    {
        Assert.Equal(32, Marshal.SizeOf<Quatd>());
        var nan = new Quatd(double.NaN, 0, 0, 1);
        Assert.False(nan.Equals(nan));
        Assert.False(nan.IsFinite);
        Assert.Throws<ArithmeticException>(() => nan.Inverse());
        Assert.True(double.IsNaN(nan.Unit().X));
        Assert.True(double.IsNaN(Quatd.Slerp(nan, Quatd.Identity, 0.5).X));
        double[] values = new double[4];
        var q = new Quatd(1, 2, 3, 4); q.CopyTo(values);
        Assert.Equal(q, Quatd.FromSpan(values));
        Assert.Throws<ArgumentException>(() => q.CopyTo(new double[3]));
        Assert.Throws<ArgumentException>(() => Quatd.FromSpan(new double[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => q[4]);
    }
}
