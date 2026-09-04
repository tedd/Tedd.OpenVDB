using System.Runtime.InteropServices;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathExpandedMatrixTests
{
    [Fact]
    public void ThreeByThreeKnownInverseAndAdjointMatchExactIntegerReference()
    {
        Mat3d matrix = new(1, 2, 3, 0, 1, 4, 5, 6, 0);
        Mat3d expected = new(-24, 18, 5, 20, -15, -4, -5, 4, 1);
        Assert.Equal(1, matrix.Determinant);
        Assert.Equal(expected, matrix.Inverse());
        Assert.Equal(expected, matrix.Adjoint());
        Assert.Equal(expected.Transpose(), matrix.Cofactor());
        Assert.Equal(Mat3d.Identity, matrix * expected);
        Assert.Equal(Mat3d.Identity, expected * matrix);
        Assert.Equal(2, matrix.Trace);
    }
    [Fact]
    public void MatrixLayoutsAndRowColumnConversionsPreserveNativeStorage()
    {
        Assert.Equal(72, Marshal.SizeOf<Mat3d>());
        Assert.Equal(128, Marshal.SizeOf<Mat4d>());
        var matrix = new Mat3d(1, 2, 3, 4, 5, 6, 7, 8, 9);
        Assert.Equal(new Vec3d(4, 5, 6), matrix.Row(1));
        Assert.Equal(new Vec3d(3, 6, 9), matrix.Column(2));
        Assert.Equal(matrix, Mat3d.FromRows(matrix.Row(0), matrix.Row(1), matrix.Row(2)));
        Assert.Equal(matrix, Mat3d.FromColumns(matrix.Column(0), matrix.Column(1), matrix.Column(2)));
        Assert.Equal(matrix, matrix.ToMat4().GetMat3());
        Assert.True(matrix.ToMat4().IsAffine);
        Assert.Equal(Mat4d.Identity.WithMat3(matrix), matrix.ToMat4());
        double[] buffer = new double[9]; matrix.CopyTo(buffer); Assert.Equal(matrix, Mat3d.FromSpan(buffer));
        Assert.Throws<ArgumentException>(() => matrix.CopyTo(new double[8]));
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix[3, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix[0, -1]);
    }
    [Fact]
    public void MatrixVectorProductsDistinguishRowAndColumnConventions()
    {
        Mat3d m = new(1, 2, 3, 4, 5, 6, 7, 8, 9);
        Vec3d v = new(2, 3, 5);
        Assert.Equal(new Vec3d(49, 59, 69), v * m);
        Assert.Equal(new Vec3d(23, 53, 83), m * v);
        Assert.Equal(m.Transform(v), v * m);
        Assert.Equal(m.PreTransform(v), m * v);
        Vec3d diagonal = new(2, 3, 5);
        Assert.Equal(m * Mat3d.CreateScale(diagonal), m.TimesDiagonal(diagonal));
        Assert.Equal(Mat3d.CreateScale(diagonal) * m, m.DiagonalTimes(diagonal));
        Assert.Equal(new Mat3d(2, 4, 6, 3, 6, 9, 5, 10, 15), Mat3d.OuterProduct(v, new(1, 2, 3)));
        Assert.Equal(new Vec3d(0, 1, 0), new Vec3d(1, 0, 0) * Mat3d.Skew(new(0, 0, 1)));
    }
    [Fact]
    public void SingularMatrixAndDeterminantToleranceHaveExplicitBehavior()
    {
        Assert.Throws<ArithmeticException>(() => Mat3d.Zero.Inverse());
        Assert.Throws<ArithmeticException>(() => new Mat3d(1, 2, 3, 2, 4, 6, 3, 6, 9).Inverse());
        Assert.False(Mat3d.Zero.TryInverse(out _));
        Assert.Throws<ArithmeticException>(() => Mat4d.Zero.Inverse());
        Assert.False(Mat4d.Zero.TryInverse(out _));
        var tiny = Mat3d.CreateScale(new Vec3d(1e-12, 1, 1));
        Assert.Equal(Mat3d.Identity, tiny * tiny.Inverse());
        Assert.Throws<ArithmeticException>(() => tiny.Inverse(1e-11));
        Assert.Throws<ArithmeticException>(() => tiny.ToMat4().Inverse(1e-11));
        Assert.True((tiny.ToMat4() * tiny.ToMat4().Inverse()).ApproxEquals(Mat4d.Identity));
        Assert.Throws<ArgumentOutOfRangeException>(() => Mat3d.Identity.Inverse(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Mat4d.Identity.Inverse(-1));
    }
    [Fact]
    public void FourByFourInverseFallbackHandlesSingularUpperSubmatrix()
    {
        Mat4d matrix = new(0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0);
        Assert.Equal(0, matrix.Determinant3x3);
        Assert.Equal(-1, matrix.Determinant);
        Assert.Equal(matrix, matrix.Inverse());
        Assert.Equal(Mat4d.Identity, matrix * matrix.Inverse());
        Assert.Equal(-matrix, matrix.Cofactor());
        Assert.Equal(-matrix, matrix.Adjoint());
        Assert.Equal(Mat4d.Identity, matrix * matrix.Adjoint() / matrix.Determinant);
    }
    [Fact]
    public void GeneralInverseUsesZeroToleranceRatherThanAffineMapThreshold()
    {
        var matrix = Mat4d.CreateScale(1e-6);
        Assert.Throws<ArithmeticException>(() => matrix.InverseAffine());
        Assert.True((matrix * matrix.Inverse()).ApproxEquals(Mat4d.Identity, 1e-12));
        Assert.Throws<ArithmeticException>(() => matrix.Inverse(1e-17));
    }
    [Fact]
    public void ProjectiveTransformsHonorHomogeneousDivisionAndZeroWConvention()
    {
        Mat4d matrix = new(2, 0, 0, 0, 0, 3, 0, 0, 0, 0, 4, 1, 5, 7, 11, 1);
        Vec3d point = new(1, 2, 3);
        Assert.Equal(new Vec3d(7, 13, 23), matrix.TransformPoint(point));
        Assert.Equal(new Vec3d(1.75, 3.25, 5.75), matrix.TransformH(point));
        Assert.Equal(new Vec4d(7, 13, 23, 4), new Vec4d(point) * matrix);
        Assert.Equal(matrix.Transform(new Vec4d(point)), new Vec4d(point) * matrix);
        Assert.Equal(matrix.PreTransform(new Vec4d(point)), matrix * new Vec4d(point));
        Assert.Equal(matrix.Transpose().TransformH(point), matrix.PreTransformH(point));
        Assert.Equal(Vec3d.Zero, matrix.TransformH(new Vec3d(1, 2, -1)));
        Assert.Equal(Vec3d.Zero, (Mat4d.Identity with { M33 = 0 }).TransformH(point));
        Assert.Equal(point, Mat4d.Identity.TransformH(point));
        Assert.True(matrix.Inverse().TransformH(matrix.TransformH(point)).ApproxEquals(point, 1e-12));
    }
    [Fact]
    public void DeterminantsMatchIndependentPermutationExpansion()
    {
        Random random = new(35789);
        for (int iteration = 0; iteration < 1000; iteration++)
        {
            double[] values = Enumerable.Range(0, 16).Select(_ => (double)random.Next(-3, 4)).ToArray();
            var matrix = Mat4d.FromSpan(values);
            double expected = PermutationDeterminant(values);
            Assert.Equal(expected, matrix.Determinant);
            Assert.Equal(expected, matrix.Transpose().Determinant);
        }
    }
    [Fact]
    public void RandomGeneralInverseRoundTripsAndAgreesWithAdjoint()
    {
        Random random = new(68217);
        for (int iteration = 0; iteration < 500; iteration++)
        {
            double[] values = Enumerable.Range(0, 16).Select(_ => random.NextDouble() * 2 - 1).ToArray();
            for (int i = 0; i < 4; i++) values[i * 4 + i] += 3;
            Mat4d matrix = Mat4d.FromSpan(values), inverse = matrix.Inverse();
            Assert.True((matrix * inverse).ApproxEquals(Mat4d.Identity, 1e-11));
            Assert.True((inverse * matrix).ApproxEquals(Mat4d.Identity, 1e-11));
            Assert.True(inverse.ApproxEquals(matrix.Adjoint() / matrix.Determinant, 1e-11));
            Vec4d point = new(random.NextDouble(), random.NextDouble(), random.NextDouble(), random.NextDouble());
            Assert.True(((point * matrix) * inverse).ApproxEquals(point, 1e-11));
            var m3 = matrix.GetMat3();
            Assert.True((m3 * m3.Inverse()).ApproxEquals(Mat3d.Identity, 1e-11));
            Assert.True((m3.Inverse() * m3).ApproxEquals(Mat3d.Identity, 1e-11));
            Assert.True(m3.Inverse().Transpose().ApproxEquals(m3.Transpose().Inverse(), 1e-11));
        }
    }
    [Fact]
    public void RotationMatrixChecksRejectShearAndSupportReflections()
    {
        Assert.True(Mat3d.Identity.IsUnitary);
        Assert.True(Mat3d.CreateScale(new Vec3d(-1, 1, 1)).IsUnitary);
        Assert.False(Mat3d.CreateShear(0.5, Axis.X, Axis.Y).IsUnitary);
        Assert.True(Mat3d.CreateScale(new Vec3d(2, 3, 4)).IsDiagonal);
        Assert.True(Mat3d.Identity.IsSymmetric);
        Assert.False(Mat3d.CreateShear(1, Axis.X, Axis.Y).IsSymmetric);
        Assert.Throws<ArithmeticException>(() => Mat3d.CreateRotation(Vec3d.Zero, 1));
        Assert.True(Mat3d.CreateRotation(new Vec3d(0, 0, 7), System.Math.PI / 2).ApproxEquals(Mat3d.CreateRotation(System.Math.PI / 2, Axis.Z), 1e-12));
    }
    [Fact]
    public void MatrixNaNBehaviorMatchesNativeArithmetic()
    {
        var nan3 = Mat3d.Identity with { M00 = double.NaN };
        var nan4 = Mat4d.Identity with { M00 = double.NaN };
        Assert.False(nan3.Equals(nan3));
        Assert.False(nan4.Equals(nan4));
        Assert.False(nan3.IsFinite);
        Assert.False(nan4.IsFinite);
        Assert.True(nan3.IsUnitary);
        Assert.Throws<ArithmeticException>(() => nan3.Inverse());
        Assert.Throws<ArithmeticException>(() => nan4.Inverse());
        Assert.True(double.IsNaN((Mat4d.Identity with { M33 = double.NaN }).TransformH(Vec3d.One).X));
    }
    private static double PermutationDeterminant(double[] matrix)
    {
        double sum = 0;
        for (int a = 0; a < 4; a++)
            for (int b = 0; b < 4; b++)
                for (int c = 0; c < 4; c++)
                    for (int d = 0; d < 4; d++)
                    {
                        int[] permutation = [a, b, c, d];
                        if (permutation.Distinct().Count() != 4) continue;
                        int inversions = 0;
                        for (int i = 0; i < 4; i++) for (int j = i + 1; j < 4; j++) if (permutation[i] > permutation[j]) inversions++;
                        double product = matrix[a] * matrix[4 + b] * matrix[8 + c] * matrix[12 + d];
                        sum += (inversions & 1) == 0 ? product : -product;
                    }
        return sum;
    }
}
