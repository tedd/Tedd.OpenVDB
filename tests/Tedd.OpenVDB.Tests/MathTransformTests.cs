using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.Tests;

public class MathTransformTests
{
    [Fact]
    public void VectorAlgebraMatchesHandComputedResults()
    {
        var a = new Vec3d(1, 2, 3);
        var b = new Vec3d(-3, 2, 1);
        Assert.Equal(new Vec3d(-2, 4, 4), a + b);
        Assert.Equal(new Vec3d(4, 0, 2), a - b);
        Assert.Equal(new Vec3d(-3, 4, 3), a * b);
        Assert.Equal(4, a.Dot(b));
        Assert.Equal(new Vec3d(-4, -10, 8), a.Cross(b));
        Assert.Equal(14, a.LengthSquared);
        Assert.Equal(6, a.Sum);
        Assert.Equal(6, a.Product);
        Assert.Equal(new Vec3d(1, 3, 8), new Vec3d(8, 1, 3).Sorted());
        Assert.Equal(new Vec3d(3, 2, 1), a.Reversed());
        Assert.Equal(new Vec3d(-1, 2, 2), Vec3d.Lerp(a, b, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => a[3]);
    }
    [Fact]
    public void NormalizationAndProjectionFollowOpenVdbDegenerateConventions()
    {
        Assert.Throws<ArithmeticException>(() => Vec3d.Zero.Unit());
        Assert.False(Vec3d.Zero.TryNormalize(out Vec3d zero));
        Assert.Equal(Vec3d.Zero, zero);
        Assert.Equal(Vec3d.UnitX, Vec3d.Zero.UnitSafe());
        Assert.Equal(Vec3d.Zero, new Vec3d(4, 5, 6).Projection(Vec3d.Zero));
        Assert.Equal(0, new Vec3d(4, 5, 6).Component(Vec3d.Zero));
        Assert.Equal(new Vec3d(4, 0, 0), new Vec3d(4, 5, 6).Projection(new Vec3d(2, 0, 0)));
        var unit = new Vec3d(3, 0, 4).Unit(out double length);
        Assert.Equal(5, length);
        AssertNear(new Vec3d(0.6, 0, 0.8), unit);
        Assert.True(new Vec3d(3, 0, 4).TryNormalize(out var normalized));
        AssertNear(unit, normalized);
    }
    [Fact]
    public void RandomVectorCrossProductsAndPerpendicularsAreOrthogonal()
    {
        Random random = new(97341);
        for (int i = 0; i < 1000; ++i)
        {
            Vec3d a = RandomVector(random), b = RandomVector(random);
            Vec3d cross = a.Cross(b);
            Assert.InRange(System.Math.Abs(a.Dot(cross)), 0, 1e-11);
            Assert.InRange(System.Math.Abs(b.Dot(cross)), 0, 1e-11);
            Vec3d perpendicular = a.ArbitraryPerpendicular();
            Assert.InRange(System.Math.Abs(a.Dot(perpendicular)), 0, 1e-12);
            Assert.InRange(System.Math.Abs(perpendicular.Length - 1), 0, 1e-12);
        }
    }
    [Fact]
    public void VectorAndMatrixSpanConversionsCheckCapacity()
    {
        double[] values = new double[18];
        var vector = new Vec3d(2, 3, 5);
        vector.CopyTo(values);
        Assert.Equal(vector, Vec3d.FromSpan(values));
        Assert.Throws<ArgumentException>(() => vector.CopyTo(new double[2]));
        Assert.Throws<ArgumentException>(() => Vec3d.FromSpan(new double[2]));
        var matrix = Mat4d.CreateTranslation(vector);
        matrix.CopyTo(values);
        Assert.Equal(matrix, Mat4d.FromSpan(values));
        Assert.Throws<ArgumentException>(() => matrix.CopyTo(new double[15]));
        Assert.Throws<ArgumentException>(() => Mat4d.FromSpan(new double[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix[-1, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => matrix[0, 4]);
        Assert.Equal(matrix, matrix.Transpose().Transpose());
    }
    [Fact]
    public void IdentityAndSignedScalePreserveOpenVdbVoxelVolume()
    {
        var identity = new Transform();
        Assert.True(identity.IsIdentity);
        Assert.True(identity.IsLinear);
        Assert.Equal(Vec3d.One, identity.VoxelSize);
        Assert.Equal(1, identity.VoxelVolume);
        Assert.True(identity.HasUniformScale);
        var scale = Transform.CreateLinearTransform(-2);
        Assert.Equal(new Vec3d(2), scale.VoxelSize);
        Assert.Equal(-8, scale.VoxelVolume);
        Assert.True(scale.HasUniformScale);
        Assert.Equal(new Vec3d(-2, -4, -6), scale.IndexToWorld(new Coord(1, 2, 3)));
    }
    [Theory]
    [InlineData(Axis.X, 0, 1, 0, 0, 0, 1)]
    [InlineData(Axis.Y, 0, 0, 1, 1, 0, 0)]
    [InlineData(Axis.Z, 1, 0, 0, 0, 1, 0)]
    public void PositiveQuarterTurnMatchesUpstreamRowVectorRotation(Axis axis, double x, double y, double z, double expectedX, double expectedY, double expectedZ)
    {
        var transform = new Transform();
        transform.PostRotate(System.Math.PI / 2, axis);
        AssertNear(new Vec3d(expectedX, expectedY, expectedZ), transform.IndexToWorld(new Vec3d(x, y, z)));
        Assert.True(transform.HasUniformScale);
        AssertNear(Vec3d.One, transform.VoxelSize);
    }
    [Fact]
    public void PreAndPostOperationsActOnCorrectSideOfExistingTransform()
    {
        var pre = Transform.CreateLinearTransform(2);
        var post = pre.Copy();
        pre.PreTranslate(new Vec3d(1, 2, 3));
        post.PostTranslate(new Vec3d(1, 2, 3));
        Assert.Equal(new Vec3d(2, 4, 6), pre.IndexToWorld(Vec3d.Zero));
        Assert.Equal(new Vec3d(1, 2, 3), post.IndexToWorld(Vec3d.Zero));
        pre = new Transform(Mat4d.CreateTranslation(new Vec3d(1, 0, 0)));
        post = pre.Copy();
        pre.PreRotate(System.Math.PI / 2, Axis.Z);
        post.PostRotate(System.Math.PI / 2, Axis.Z);
        AssertNear(new Vec3d(1, 0, 0), pre.IndexToWorld(Vec3d.Zero));
        AssertNear(new Vec3d(0, 1, 0), post.IndexToWorld(Vec3d.Zero));
    }
    [Fact]
    public void ShearChangesVolumeShapeButNotDeterminantAndExcludesUniformScale()
    {
        var transform = new Transform();
        transform.PostShear(2, Axis.X, Axis.Y);
        Assert.Equal(new Vec3d(7, 3, 5), transform.IndexToWorld(new Vec3d(1, 3, 5)));
        Assert.Equal(1, transform.VoxelVolume);
        Assert.False(transform.HasUniformScale);
        Assert.Equal(new Vec3d(1, 3, 5), transform.WorldToIndex(new Vec3d(7, 3, 5)));
        Assert.Throws<ArgumentException>(() => Mat4d.CreateShear(1, Axis.X, Axis.X));
        Assert.Throws<ArgumentOutOfRangeException>(() => Mat4d.CreateRotation(1, (Axis)3));
    }
    [Fact]
    public void UniformEdgeLengthsAloneDoNotImplyUniformScale()
    {
        var matrix = Mat4d.Identity with { M10 = 0.6, M11 = 0.8 };
        var transform = new Transform(matrix);
        AssertNear(Vec3d.One, transform.VoxelSize);
        Assert.False(transform.HasUniformScale);
    }
    [Fact]
    public void InvalidMutationsAreAtomicAndLeaveInverseConsistent()
    {
        var transform = Transform.CreateLinearTransform(2);
        transform.PostTranslate(new Vec3d(3));
        var before = transform.Matrix;
        var inverse = transform.InverseMatrix;
        Assert.Throws<ArithmeticException>(() => transform.PreScale(new Vec3d(0, 1, 1)));
        Assert.Equal(before, transform.Matrix);
        Assert.Equal(inverse, transform.InverseMatrix);
        Assert.Throws<ArgumentException>(() => transform.PostTranslate(new Vec3d(double.NaN)));
        Assert.Equal(before, transform.Matrix);
        Assert.Throws<ArgumentException>(() => new Transform(Mat4d.Identity with { M03 = 1 }));
        Assert.Throws<ArithmeticException>(() => Transform.CreateLinearTransform(1e-6));
    }
    [Fact]
    public void IndexRoundingUsesCellAndNodeCenteredSemantics()
    {
        var transform = Transform.CreateLinearTransform(2);
        transform.PostTranslate(new Vec3d(1));
        Assert.Equal(new Coord(0, 1, -1), transform.WorldToIndexCellCentered(new Vec3d(0, 2, -2)));
        Assert.Equal(new Coord(-1, 0, -2), transform.WorldToIndexNodeCentered(new Vec3d(0, 2, -2)));
    }
    [Fact]
    public void RandomAffineInverseAndCompositionRoundTrip()
    {
        Random random = new(328473);
        for (int i = 0; i < 500; ++i)
        {
            var transform = new Transform();
            Vec3d scale = new(random.NextDouble() * 3 + 0.1, -(random.NextDouble() * 3 + 0.1), random.NextDouble() * 3 + 0.1);
            transform.PreScale(scale);
            transform.PostRotate(random.NextDouble() * 6, Axis.X);
            transform.PreRotate(random.NextDouble() * 6, Axis.Y);
            transform.PostRotate(random.NextDouble() * 6, Axis.Z);
            transform.PostShear(random.NextDouble() - 0.5, Axis.Z, Axis.X);
            transform.PostTranslate(RandomVector(random));
            var point = RandomVector(random);
            AssertNear(point, transform.WorldToIndex(transform.IndexToWorld(point)), 1e-10);
            AssertNear(point, transform.IndexToWorld(transform.WorldToIndex(point)), 1e-10);
            Assert.True((transform.Matrix * transform.InverseMatrix).ApproxEquals(Mat4d.Identity, 1e-10));
            Assert.True((transform.InverseMatrix * transform.Matrix).ApproxEquals(Mat4d.Identity, 1e-10));
            AssertNear(transform.WorldToIndex(point), transform.Inverse().IndexToWorld(point), 1e-10);
            Assert.InRange(System.Math.Abs(transform.VoxelVolume - scale.Product), 0, 1e-10);
        }
    }
    [Fact]
    public void InverseTransposeKeepsNormalsPerpendicularToTransformedTangents()
    {
        var transform = new Transform();
        transform.PostScale(new Vec3d(2, 3, 5));
        transform.PostShear(0.8, Axis.X, Axis.Y);
        transform.PostRotate(0.73, Axis.Z);
        var normal = new Vec3d(2, -1, 0);
        var tangent = new Vec3d(1, 2, 3);
        var worldNormal = transform.ApplyInverseJacobianTranspose(normal);
        var worldTangent = transform.ApplyJacobian(tangent);
        Assert.InRange(System.Math.Abs(worldNormal.Dot(worldTangent)), 0, 1e-12);
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(0, 4)]
    public void BatchTransformsSupportInPlaceAndOverlappingSpans(int sourceOffset, int destinationOffset)
    {
        var transform = Transform.CreateLinearTransform(2);
        transform.PostTranslate(new Vec3d(7));
        Vec3d[] buffer = Enumerable.Range(0, 8).Select(i => new Vec3d(i, -i, i + 1)).ToArray();
        Vec3d[] original = buffer.AsSpan(sourceOffset, 4).ToArray();
        Vec3d[] expected = original.Select(transform.IndexToWorld).ToArray();
        transform.IndexToWorld(buffer.AsSpan(sourceOffset, 4), buffer.AsSpan(destinationOffset, 4));
        Assert.Equal(expected, buffer.AsSpan(destinationOffset, 4).ToArray());
        transform.WorldToIndex(buffer.AsSpan(destinationOffset, 4), buffer.AsSpan(destinationOffset, 4));
        Assert.Equal(original, buffer.AsSpan(destinationOffset, 4).ToArray());
    }
    [Fact]
    public void BatchTransformsValidateCapacityAndAcceptEmptyBuffers()
    {
        var transform = new Transform();
        transform.IndexToWorld([], []);
        Assert.Throws<ArgumentException>(() => transform.IndexToWorld(new Vec3d[2], new Vec3d[1]));
        var copy = transform.Copy();
        copy.PostScale(4);
        Assert.True(transform.IsIdentity);
        Assert.False(copy.IsIdentity);
    }
    private static Vec3d RandomVector(Random random) => new(random.NextDouble() * 20 - 10, random.NextDouble() * 20 - 10, random.NextDouble() * 20 - 10);
    private static void AssertNear(Vec3d expected, Vec3d actual, double tolerance = 1e-12) => Assert.True(expected.ApproxEquals(actual, tolerance), $"Expected {expected}, actual {actual}.");
}
