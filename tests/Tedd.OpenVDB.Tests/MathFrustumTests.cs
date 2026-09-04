using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathFrustumTests
{
    private static readonly BBoxd Bounds = new(new Vec3d(-2, -4, 10), new Vec3d(6, 8, 30));
    private static NonlinearFrustumMap Create(double taper = 0.25, double depth = 3) =>
        new(Bounds, taper, depth, Mat4d.CreateScale(new Vec3d(2, 3, 4)) * Mat4d.CreateRotation(0.37, Axis.Y) * Mat4d.CreateTranslation(new Vec3d(11, -7, 3)));
    private static void Near(Vec3d expected, Vec3d actual, double tolerance = 1e-10) =>
        Assert.True(expected.ApproxEquals(actual, tolerance), $"Expected {expected}, actual {actual}");
    private static void Near(Mat3d expected, Mat3d actual, double tolerance = 1e-10) =>
        Assert.True(expected.ApproxEquals(actual, tolerance), $"Expected {expected}, actual {actual}");
    [Fact]
    public void NearFarPlaneCornersUseNativeWidthAndTaperConvention()
    {
        var map = new NonlinearFrustumMap(Bounds, 0.25, 3);
        Assert.Equal(new Vec3d(-0.5, -0.75, 0), map.ApplyMap(Bounds.Min));
        Assert.Equal(new Vec3d(0.5, 0.75, 0), map.ApplyMap(new(6, 8, 10)));
        Assert.Equal(new Vec3d(-2, -3, 3), map.ApplyMap(new(-2, -4, 30)));
        Assert.Equal(new Vec3d(2, 3, 3), map.ApplyMap(Bounds.Max));
        Assert.Equal(Vec3d.Zero, map.ApplyMap(new(2, 2, 10)));
        Assert.Equal(new Vec3d(0, 0, 3), map.ApplyMap(new(2, 2, 30)));
        Assert.Equal(1, map.Gamma);
        Assert.False(map.IsLinear); Assert.False(map.HasUniformScale); Assert.True(map.IsValid);
    }
    [Fact]
    public void DefaultMapIsCenteredUnitCubeRatherThanIdentity()
    {
        var map = new NonlinearFrustumMap();
        Assert.Equal(new Vec3d(-0.5, -0.5, 0), map.ApplyMap(Vec3d.Zero));
        Assert.False(map.IsIdentity); Assert.True(map.HasSimpleAffine);
        var identity = new NonlinearFrustumMap(new BBoxd(new Vec3d(-0.5, -0.5, 0), new Vec3d(0.5, 0.5, 1)), 1, 1);
        Assert.True(identity.IsIdentity);
        Assert.False(identity.HasUniformScale);
    }
    [Theory]
    [InlineData(0.01, 0.1)]
    [InlineData(0.25, 3)]
    [InlineData(1, 1)]
    [InlineData(2, 7)]
    [InlineData(100, 0.5)]
    public void PointRoundTripsCoverBothTaperDirectionsAndTranslatedRotatedNonuniformMaps(double taper, double depth)
    {
        var map = Create(taper, depth); var random = new Random(1792);
        for (int i = 0; i < 300; ++i)
        {
            Vec3d index = Bounds.Min + Bounds.Extents * new Vec3d(random.NextDouble(), random.NextDouble(), random.NextDouble());
            Near(index, map.ApplyInverseMap(map.ApplyMap(index)), 2e-9);
        }
    }
    [Theory]
    [InlineData(0.25, 3)]
    [InlineData(0.5, 7)]
    [InlineData(1, 4)]
    [InlineData(2, 3)]
    public void JacobianAndInverseTransposeMatchFiniteDifferencesAndDuality(double taper, double depth)
    {
        var map = Create(taper, depth); var random = new Random(1974); const double h = 1e-4;
        for (int iteration = 0; iteration < 50; ++iteration)
        {
            Vec3d index = Bounds.Min + Bounds.Extents * new Vec3d(random.NextDouble(), random.NextDouble(), random.NextDouble());
            Vec3d v = new(random.NextDouble() - 0.5, random.NextDouble() - 0.5, random.NextDouble() - 0.5);
            Vec3d g = new(random.NextDouble(), random.NextDouble(), random.NextDouble());
            Vec3d world = map.ApplyMap(index);
            Vec3d numeric = (map.ApplyMap(index + h * v) - map.ApplyMap(index - h * v)) / (2 * h);
            Near(numeric, map.ApplyJacobian(v, index), 2e-9);
            Near(v, map.ApplyInverseJacobian(map.ApplyJacobian(v, index), index), 1e-10);
            Near(g, map.ApplyInverseJacobianTranspose(map.ApplyJacobianTranspose(g, index), index), 1e-10);
            Near(g * map.Jacobian(index).Inverse().Transpose(), map.ApplyInverseJacobianTranspose(g, index), 1e-10);
            Vec3d inverseNumeric = (map.ApplyInverseMap(world + h * v) - map.ApplyInverseMap(world - h * v)) / (2 * h);
            Near(inverseNumeric, map.ApplyInverseJacobian(v, index), 2e-7);
            Assert.InRange(System.Math.Abs(map.ApplyJacobian(v, index).Dot(g) - v.Dot(map.ApplyJacobianTranspose(g, index))), 0, 1e-12);
            for (int axis = 0; axis < 3; ++axis)
            {
                Vec3d unit = axis == 0 ? Vec3d.UnitX : axis == 1 ? Vec3d.UnitY : Vec3d.UnitZ;
                double numericGradient = (g.Dot(map.ApplyInverseMap(world + h * unit)) - g.Dot(map.ApplyInverseMap(world - h * unit))) / (2 * h);
                Assert.InRange(System.Math.Abs(numericGradient - map.ApplyInverseJacobianTranspose(g, index)[axis]), 0, 2e-7);
            }
        }
    }
    [Fact]
    public void NativeCurvatureMatchesNumericalHessianWhenGammaEqualsOne()
    {
        var map = Create(); Vec3d index = new(4, 5, 23); Vec3d world = map.ApplyMap(index);
        Mat3d hessian = new(4, 1, 2, 1, 6, -1, 2, -1, 10); Vec3d linear = new(1, -3, 2);
        Vec3d gradient = index * hessian + linear;
        Mat3d actual = map.ApplyInverseJacobianCurvature(hessian, gradient, index);
        const double h = 0.002;
        double Field(Vec3d w) { Vec3d p = map.ApplyInverseMap(w); return 0.5 * (p * hessian).Dot(p) + linear.Dot(p); }
        for (int row = 0; row < 3; ++row) for (int col = 0; col < 3; ++col)
        {
            Vec3d u = row == 0 ? Vec3d.UnitX : row == 1 ? Vec3d.UnitY : Vec3d.UnitZ;
            Vec3d v = col == 0 ? Vec3d.UnitX : col == 1 ? Vec3d.UnitY : Vec3d.UnitZ;
            double expected = (Field(world + h * u + h * v) - Field(world + h * u - h * v) - Field(world - h * u + h * v) + Field(world - h * u - h * v)) / (4 * h * h);
            Assert.InRange(System.Math.Abs(expected - actual[row, col]), 0, 0.001);
        }
        Near(actual, actual.Transpose());
    }
    [Fact]
    public void NativeCurvatureAndDeterminantRetainLiteralUpstreamExpressions()
    {
        var map = new NonlinearFrustumMap(new BBoxd(Vec3d.Zero, new Vec3d(4, 4, 4)), 0.2, 2);
        Assert.Equal(2, map.Gamma);
        Vec3d index = new(3, 2, 2);
        // Native pt3 has one gamma factor, including in the zz Hessian term.
        Mat3d curvature = map.ApplyInverseJacobianCurvature(Mat3d.Zero, Vec3d.UnitX, index);
        Assert.InRange(System.Math.Abs(curvature[0, 2] + 8.0 / 9), 0, 1e-12);
        Assert.InRange(System.Math.Abs(curvature[2, 2] - 4.0 / 9), 0, 1e-12);
        // Native determinant(loc) consumes loc.z directly, unlike Jacobian(index).
        Assert.Equal(25.0 / 32, map.DeterminantAt(index));
        Assert.Equal(9.0 / 32, map.Jacobian(index).Determinant);
        Assert.Equal(1, map.Determinant);
    }
    [Fact]
    public void PositionlessDifferentialsDescribeOnlySecondaryMap()
    {
        var map = Create(); Vec3d v = new(2, 3, 4);
        Near(map.SecondMap.TransformDirection(v), map.ApplyJacobian(v));
        Near(v, map.ApplyInverseJacobian(map.ApplyJacobian(v)));
        Near(v, map.ApplyInverseJacobianTranspose(map.ApplyJacobianTranspose(v)));
        var transform = new Transform(map);
        Near(map.ApplyJacobian(v), transform.ApplyJacobian(v));
        Near(map.ApplyInverseJacobian(v), transform.ApplyInverseJacobian(v));
        Near(map.ApplyJacobianTranspose(v), transform.ApplyJacobianTranspose(v));
        Near(map.ApplyInverseJacobianTranspose(v), transform.ApplyInverseJacobianTranspose(v));
    }
    [Fact]
    public void VoxelSizeUsesThreeMappedSegmentsAndVariesWithPosition()
    {
        var map = Create(); Vec3d index = new(4, 5, 23), world = map.ApplyMap(index);
        var expected = new Vec3d((map.ApplyMap(index + Vec3d.UnitX) - world).Length,
            (map.ApplyMap(index + Vec3d.UnitY) - world).Length, (map.ApplyMap(index + Vec3d.UnitZ) - world).Length);
        Near(expected, map.VoxelSizeAt(index));
        Near(new(0.25, 0.375, 0.6), map.VoxelSize);
        Assert.NotEqual(map.VoxelSize, map.VoxelSizeAt(index));
        var transform = new Transform(map);
        Near(map.VoxelSize, transform.VoxelSize); Near(expected, transform.VoxelSizeAt(index));
        Assert.Equal(map.Determinant, transform.VoxelVolume);
        Assert.Equal(map.DeterminantAt(index), transform.VoxelVolumeAt(index));
        Near(map.Jacobian(index), transform.Jacobian(index));
    }
    [Fact]
    public void FocalPointProducesNativeExceptionsAndIeeeInverseResults()
    {
        var map = new NonlinearFrustumMap(); map = map.WithTaper(0.5);
        Vec3d focalIndex = new(0.5, 0.5, -1);
        Assert.Equal(new Vec3d(0, 0, -1), map.ApplyMap(focalIndex));
        Assert.False(map.ApplyInverseMap(new Vec3d(0, 0, -1)).IsFinite);
        Assert.False(map.ApplyInverseJacobian(Vec3d.UnitX, focalIndex).IsFinite);
        Assert.Throws<ArithmeticException>(() => map.ApplyInverseJacobianTranspose(Vec3d.UnitX, focalIndex));
        Assert.Throws<ArithmeticException>(() => map.ApplyInverseJacobianCurvature(Mat3d.Identity, Vec3d.UnitX, focalIndex));
    }
    [Fact]
    public void TransformCopiesMutationsAndSecondaryCompositionKeepFrustumIndependent()
    {
        var map = Create(); var transform = new Transform(map); var copy = transform.Copy(); Vec3d index = new(4, 5, 23);
        Assert.NotSame(map, transform.FrustumMap); Assert.NotSame(copy.FrustumMap, transform.FrustumMap);
        Assert.False(transform.IsLinear); Assert.Equal("NonlinearFrustumMap", transform.MapType);
        Mat4d extra = Mat4d.CreateTranslation(new(1, 2, 3));
        transform.PostMult(extra);
        Near(extra.TransformPoint(map.ApplyMap(index)), transform.IndexToWorld(index));
        Near(map.ApplyMap(index), copy.IndexToWorld(index));
        transform.SetFrustumTaper(0.5); transform.SetFrustumDepth(2);
        Assert.Equal(0.5, transform.FrustumMap!.Taper); Assert.Equal(2, transform.FrustumMap.Depth);
        Assert.Equal(0.25, copy.FrustumMap!.Taper); Assert.Equal(3, copy.FrustumMap.Depth);
        Assert.True(copy.FrustumMap.ApproxEquals(map));
        var pre = new Transform(map); pre.PreMult(extra);
        Near(map.WithSecondMap(extra * map.SecondMap).ApplyMap(index), pre.IndexToWorld(index));
        Mat4d matrix = transform.Matrix;
        Assert.Throws<ArithmeticException>(() => transform.PostScale(0)); Assert.Equal(matrix, transform.Matrix);
        Assert.Throws<ArgumentOutOfRangeException>(() => transform.SetFrustumDepth(0));
        Assert.Equal(2, transform.FrustumMap.Depth);
        Assert.Throws<InvalidOperationException>(() => new Transform().SetFrustumTaper(0.5));
        Assert.Throws<NotSupportedException>(() => transform.Inverse());
    }
    [Fact]
    public void NonlinearSpanMappingSupportsBothPartialOverlapDirections()
    {
        var transform = new Transform(Create());
        Vec3d[] original = Enumerable.Range(0, 12).Select(i => new Vec3d(i * 0.5 - 1, i * 0.2, 11 + i)).ToArray();
        var mapped = original.Select(p => transform.IndexToWorld(p)).ToArray();
        foreach (int offset in new[] { -2, 0, 2 })
        {
            int input = offset < 0 ? 2 : 0, output = offset > 0 ? 2 : 0;
            var buffer = new Vec3d[14]; original.CopyTo(buffer, input);
            transform.IndexToWorld(buffer.AsSpan(input, 12), buffer.AsSpan(output, 12));
            for (int i = 0; i < 12; ++i) Near(mapped[i], buffer[output + i]);
            var inverseBuffer = new Vec3d[14]; mapped.CopyTo(inverseBuffer, input);
            transform.WorldToIndex(inverseBuffer.AsSpan(input, 12), inverseBuffer.AsSpan(output, 12));
            for (int i = 0; i < 12; ++i) Near(original[i], inverseBuffer[output + i]);
        }
        Assert.Throws<ArgumentException>(() => transform.IndexToWorld(original, new Vec3d[1]));
    }
    [Fact]
    public void BoundsTransformMapsAllCornersAndEmptyBoundsRemainEmpty()
    {
        var transform = new Transform(Create());
        BBoxd expected = BBoxd.Empty;
        for (int corner = 0; corner < 8; ++corner)
            expected = expected.Expand(transform.IndexToWorld(new Vec3d((corner & 1) == 0 ? Bounds.Min.X : Bounds.Max.X,
                (corner & 2) == 0 ? Bounds.Min.Y : Bounds.Max.Y, (corner & 4) == 0 ? Bounds.Min.Z : Bounds.Max.Z)));
        Assert.Equal(expected, transform.IndexToWorld(Bounds));
        Assert.Equal(BBoxd.Empty, transform.IndexToWorld(BBoxd.Empty));
        Assert.Equal(BBoxd.Empty, transform.WorldToIndex(BBoxd.Empty));
        var ray = new Rayd(Vec3d.Zero, Vec3d.UnitX);
        Assert.Throws<ArgumentException>(() => ray.IndexToWorld(transform));
        Assert.Throws<ArgumentException>(() => ray.WorldToIndex(transform));
    }
    [Fact]
    public void ConstructorRejectsDegenerateOrNonfiniteMappings()
    {
        Assert.Throws<ArgumentException>(() => new NonlinearFrustumMap(BBoxd.Empty, 1, 1));
        Assert.Throws<ArgumentException>(() => new NonlinearFrustumMap(new BBoxd(Vec3d.Zero, new Vec3d(1, 0, 1)), 1, 1));
        Assert.Throws<ArithmeticException>(() => new NonlinearFrustumMap(new BBoxd(Vec3d.Zero, new Vec3d(1, 1e-16, 1)), 1, 1));
        Assert.Throws<ArgumentException>(() => new NonlinearFrustumMap(new BBoxd(Vec3d.Zero, new Vec3d(double.PositiveInfinity)), 1, 1));
        foreach (double invalid in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NonlinearFrustumMap(Bounds, invalid, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NonlinearFrustumMap(Bounds, 1, invalid));
        }
        Assert.Throws<ArithmeticException>(() => new NonlinearFrustumMap(Bounds, 1, 1, Mat4d.CreateScale(0)));
        Assert.Throws<ArithmeticException>(() => new NonlinearFrustumMap(Bounds, double.Epsilon, 1));
        Assert.Throws<ArgumentException>(() => new NonlinearFrustumMap(Bounds, 1, 1, Mat4d.Identity with { M03 = 1 }));
    }
}
