using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;

namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetSphereTests
{
    [Fact]
    public void SphereHasWorldDistanceValuesUniformTransformAndLevelSetClass()
    {
        var grid = LevelSetSphere.Create(5d, new Vec3d(1, -2, 3), 0.5d);
        Assert.Equal(GridClass.LevelSet, grid.GridClass);
        Assert.Equal(new Vec3d(0.5), grid.Transform.VoxelSize);
        Assert.Equal(1.5, grid.Background);
        Assert.Equal(-1.5, grid.Tree.GetValue(new Coord(2, -4, 6)));
        Assert.Equal(0, grid.Tree.GetValue(new Coord(12, -4, 6)));
        Assert.Equal(0.5, grid.Tree.GetValue(new Coord(13, -4, 6)));
        Assert.Equal(-0.5, grid.Tree.GetValue(new Coord(11, -4, 6)));
        Assert.True(grid.Tree.IsValueOn(new Coord(12, -4, 6)));
        Assert.False(grid.Tree.IsValueOn(new Coord(2, -4, 6)));
        Assert.False(grid.Tree.IsValueOn(new Coord(15, -4, 6)));
        Assert.Equal(1.5, grid.Tree.GetValue(new Coord(15, -4, 6)));
    }

    [Theory]
    [InlineData(1.49, true)]
    [InlineData(1.5, false)]
    [InlineData(1.51, false)]
    public void NyquistThresholdIsExactlyOneAndAHalfVoxels(double radius, bool empty)
    {
        var grid = LevelSetSphere.Create(radius, default, 1d);
        Assert.Equal(empty, grid.IsEmpty);
        Assert.Equal(GridClass.LevelSet, grid.GridClass);
        Assert.Equal(3, grid.Background);
    }

    [Fact]
    public void ActiveSamplesMatchIndependentAnalyticDistanceAndStrictBand()
    {
        var center = new Vec3d(-1.25, 2.5, 0.75);
        const double radius = 4.25, voxel = 0.75, width = 2.5;
        var grid = LevelSetSphere.Create(radius, center, voxel, width);
        foreach (var point in new CoordBBox(new Coord(-12), new Coord(12)))
        {
            var world = new Vec3d(point.X * voxel, point.Y * voxel, point.Z * voxel);
            var distance = (world - center).Length - radius;
            var expectedActive = System.Math.Abs(distance) < voxel * width;
            Assert.Equal(expectedActive, grid.Tree.IsValueOn(point));
            var expectedValue = System.Math.Clamp(distance, -voxel * width, voxel * width);
            Assert.InRange(System.Math.Abs(expectedValue - grid.Tree.GetValue(point)), 0, 2e-14);
        }
    }

    [Fact]
    public void SinglePrecisionFactoryUsesFloatArithmeticAndSupportsFractionalCenters()
    {
        var grid = LevelSetSphere.Create(5f, new Vec3f(-0.25f, 0.5f, 1.75f), 0.5f, 2f);
        Assert.Equal(1f, grid.Background);
        Assert.True(grid.ActiveVoxelCount > 0);
        Assert.Equal(GridClass.LevelSet, grid.GridClass);
        Assert.Equal(new Vec3d(0.5), grid.Transform.VoxelSize);
        foreach (var (coordinate, value) in grid.Tree.EnumerateActiveVoxels())
        {
            var x = (float)coordinate.X + 0.5f;
            var y = (float)coordinate.Y - 1f;
            var z = (float)coordinate.Z - 3.5f;
            var expected = 0.5f * (MathF.Sqrt(y * y + x * x + z * z) - 10f);
            Assert.Equal(expected, value);
        }
    }

    [Theory]
    [InlineData(0, 1, 3)]
    [InlineData(-1, 1, 3)]
    [InlineData(2, 0, 3)]
    [InlineData(2, -1, 3)]
    [InlineData(2, 1, 1)]
    [InlineData(2, 1, 0)]
    [InlineData(double.NaN, 1, 3)]
    [InlineData(double.PositiveInfinity, 1, 3)]
    public void InvalidParametersAreRejected(double radius, double voxelSize, double width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelSetSphere.Create(radius, default, voxelSize, width));

    [Fact]
    public void CancellationAndUnrepresentableBoundsFailBeforeReturningPartialGrids()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => LevelSetSphere.Create(10d, default, 1d, cancellationToken: cancellation.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelSetSphere.Create(10d, new Vec3d(double.NaN), 1d));
        Assert.Throws<ArgumentOutOfRangeException>(() => LevelSetSphere.Create(10d, new Vec3d(int.MaxValue), 1d));
    }
}

public sealed class LevelSetSphereNativeTests
{
    private static readonly Lazy<Dictionary<string, System.Text.Json.JsonElement>> Fixtures = new(() =>
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-levelset-sphere.json")));
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", document.RootElement.GetProperty("upstreamCommit").GetString());
        return document.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(static entry => entry.GetProperty("name").GetString()!, static entry => entry.Clone());
    });

    public static IEnumerable<object[]> NativeCases() => Fixtures.Value.Keys.Select(static name => new object[] { name });

    [Theory]
    [MemberData(nameof(NativeCases))]
    public void SphereMatchesNativeVoxelValuesAndSparseTopology(string name)
    {
        var fixture = Fixtures.Value[name];
        if (fixture.GetProperty("type").GetString() == "float") Verify<float>(fixture);
        else Verify<double>(fixture);
    }

    private static void Verify<T>(System.Text.Json.JsonElement fixture) where T : unmanaged, System.Numerics.IFloatingPointIeee754<T>
    {
        var center = fixture.GetProperty("center");
        var grid = LevelSetSphere.Create(T.CreateChecked(fixture.GetProperty("radius").GetDouble()),
            new Vec3d(center[0].GetDouble(), center[1].GetDouble(), center[2].GetDouble()),
            T.CreateChecked(fixture.GetProperty("voxel").GetDouble()), T.CreateChecked(fixture.GetProperty("width").GetDouble()));
        Assert.Equal(GridClass.LevelSet, grid.GridClass);
        Assert.Equal(T.CreateChecked(fixture.GetProperty("background").GetDouble()), grid.Background);
        Assert.Equal(fixture.GetProperty("activeCount").GetUInt64(), grid.ActiveVoxelCount);
        Assert.Equal(fixture.GetProperty("leafCount").GetInt64(), grid.Tree.LeafCount);
        foreach (var sample in fixture.GetProperty("samples").EnumerateArray())
        {
            var coordinate = new Coord(sample[0].GetInt32(), sample[1].GetInt32(), sample[2].GetInt32());
            var active = grid.Tree.ProbeValue(coordinate, out var value);
            var expected = T.CreateChecked(sample[3].GetDouble());
            Assert.True(value == expected, $"{fixture.GetProperty("name").GetString()} at {coordinate}: expected {expected}, got {value}.");
            Assert.Equal(sample[4].GetInt32() != 0, active);
            Assert.Equal(sample[5].GetInt32(), grid.Tree.GetValueDepth(coordinate));
        }
    }
}
