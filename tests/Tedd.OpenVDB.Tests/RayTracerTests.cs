using System.Text;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;

namespace Tedd.OpenVDB.Tests;

public sealed class RayTracerTests
{
    [Fact]
    public void FilmDefaultStorageAndFillRetainNativeAlphaSemantics()
    {
        var film = new Film(5, 3);
        Assert.Equal(15, film.PixelCount);
        Assert.All(film.ReadOnlyPixels.ToArray(), pixel => AssertRgba(new(0f, 0f, 0f, 1f), pixel));
        film.Fill(new Film.Rgba(0f, 0f, 0f, 0f));
        Assert.All(film.ReadOnlyPixels.ToArray(), pixel => Assert.Equal(0f, pixel.A));
        film.Fill();
        Assert.All(film.ReadOnlyPixels.ToArray(), pixel => Assert.Equal(1f, pixel.A));
        film[2, 1] = new Film.Rgba(0.25f, 0.5f, 0.75f, 0.125f);
        AssertRgba(new(0.25f, 0.5f, 0.75f, 0.125f), film[2, 1]);
    }

    [Fact]
    public void RgbaArithmeticMatchesNativeColorAndAccumulationContracts()
    {
        var left = new Film.Rgba(0.2f, 0.3f, 0.4f, 0.25f);
        var right = new Film.Rgba(0.5f, 0.6f, 0.7f, 0.75f);
        AssertRgba(new(0.7f, 0.9f, 1.1f, 1f), left + right, 1e-7f);
        AssertRgba(new(0.1f, 0.18f, 0.28f, 1f), left * right, 1e-7f);
        AssertRgba(new(0.4f, 0.6f, 0.8f, 1f), left * 2f);
        left.Add(right);
        AssertRgba(new(0.7f, 0.9f, 1.1f, 1f), left, 1e-7f);

        var foreground = new Film.Rgba(0.2f, 0.4f, 0.6f, 0.25f);
        foreground.Over(new Film.Rgba(0.8f, 0.5f, 0.1f, 0.5f));
        AssertRgba(new(0.35f, 0.2875f, 0.1875f, 0.625f), foreground, 1e-7f);
    }

    [Fact]
    public void CheckerboardUsesNativeBitMaskPatternIncludingZeroSize()
    {
        var film = new Film(6, 5);
        Film.Rgba first = new(0.2f), second = new(0.8f);
        film.Checkerboard(first, second, 2);
        for (int y = 0; y < film.Height; ++y)
            for (int x = 0; x < film.Width; ++x)
                Assert.Equal(((x & 2) ^ (y & 2)) != 0 ? first : second, film[x, y]);
        film.Checkerboard(first, second, 0);
        Assert.All(film.ReadOnlyPixels.ToArray(), pixel => Assert.Equal(second, pixel));
    }

    [Fact]
    public void ByteBufferAndPpmAreNativeP6Layout()
    {
        var film = new Film(2, 1);
        film[0, 0] = new Film.Rgba(0f, 0.5f, 1f, 0.25f);
        film[1, 0] = new Film.Rgba(1f / 255f, 254f / 255f, 0.1f, 1f);
        Assert.Equal(new byte[] { 0, 127, 255, 63, 1, 254, 25, 255 }, film.ConvertToByteBuffer());
        using var stream = new MemoryStream();
        film.WritePpm(stream);
        byte[] header = Encoding.ASCII.GetBytes("P6\n2 1\n255\n");
        Assert.Equal(header.Concat(new byte[] { 0, 127, 255, 1, 254, 25 }), stream.ToArray());
        film[0, 0] = new Film.Rgba(float.NaN, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => film.ConvertToByteBuffer());
        film[0, 0] = new Film.Rgba(1.01f, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => film.ConvertToByteBuffer());
    }

    [Fact]
    public void SavePpmRetainsNativeWholePathExtensionRule()
    {
        var film = new Film(1, 1, new Film.Rgba(0.25f, 0.5f, 0.75f));
        string withoutDot = "raytracerppm" + Guid.NewGuid().ToString("N"), expected = withoutDot + ".ppm";
        string withDot = withoutDot + ".data";
        try
        {
            Assert.Equal(expected, film.SavePpm(withoutDot));
            Assert.True(File.Exists(expected));
            Assert.Equal(withDot, film.SavePpm(withDot));
            Assert.True(File.Exists(withDot));
        }
        finally
        {
            File.Delete(expected);
            File.Delete(withDot);
        }
    }

    [Fact]
    public void FilmRejectsInvalidDimensionsAndCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Film(-1, 1));
        Assert.Throws<OverflowException>(() => new Film(int.MaxValue, 2));
        var film = new Film(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = film[-1, 0]; });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = film[2, 0]; });
        Assert.Throws<ArgumentOutOfRangeException>(() => film.Checkerboard(size: -1));
    }

    [Fact]
    public void CamerasProduceNormalizedHoudiniRaysAndPreserveLookAtOnFailure()
    {
        var film = new Film(3, 3);
        var perspective = new PerspectiveCamera(film, translation: new Vec3d(1, 2, 10), nearPlane: 0.25, farPlane: 80);
        perspective.LookAt(new Vec3d(1, 2, 0));
        Rayd center = perspective.GetRay(1, 1);
        AssertVec(new(1, 2, 10), center.Eye);
        AssertVec(-Vec3d.UnitZ, center.Direction);
        Assert.Equal(1, center.Direction.Length, 13);
        Assert.Equal(0.25, center.MinTime, 13);
        Mat4d before = perspective.ScreenToWorld;
        perspective.LookAt(center.Eye);
        Assert.Equal(before, perspective.ScreenToWorld);
        perspective.LookAt(Vec3d.Zero, Vec3d.Zero);
        Assert.Equal(before, perspective.ScreenToWorld);

        Rayd corner = perspective.GetRay(0, 0, 0, 0);
        Assert.Equal(1, corner.Direction.Length, 13);
        Assert.True(corner.MaxTime >= center.MaxTime || double.IsPositiveInfinity(corner.MaxTime));
    }

    [Fact]
    public void OrthographicRaysShareDirectionAndClippingTimes()
    {
        var film = new Film(4, 2);
        var camera = new OrthographicCamera(film, new Vec3d(10, -20, 30), new Vec3d(3, -4, 5), 8, 0.5, 50);
        Rayd a = camera.GetRay(0, 0, 0, 0), b = camera.GetRay(3, 1, 1, 1);
        AssertVec(a.Direction, b.Direction);
        Assert.Equal(a.MinTime, b.MinTime);
        Assert.Equal(a.MaxTime, b.MaxTime);
        Assert.NotEqual(a.Eye, b.Eye);
        Assert.Equal(1, a.Direction.Length, 13);
    }

    [Fact]
    public void CameraConversionsAndPreconditionsAreExplicit()
    {
        foreach (double length in new[] { 12.5, 35.0, 50.0, 120.0 })
        {
            double field = PerspectiveCamera.FocalLengthToFieldOfView(length, 41.2136);
            Assert.Equal(length, PerspectiveCamera.FieldOfViewToFocalLength(field, 41.2136), 12);
        }
        Assert.Throws<ArgumentException>(() => new PerspectiveCamera(new Film(0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerspectiveCamera(new Film(1, 1), focalLength: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerspectiveCamera(new Film(1, 1), nearPlane: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerspectiveCamera(new Film(1, 1), nearPlane: 2, farPlane: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrthographicCamera(new Film(1, 1), frameWidth: 0));
    }

    [Fact]
    public void ConstantShadersPreserveNativeAlphaResetAndModulation()
    {
        var material = new Film.Rgba(0.2f, 0.4f, 0.8f, 0.1f);
        Vec3d position = new(2, 3, 4), normal = new(-0.5, 0.25, 1), direction = new(0, 0.6, -0.8);
        AssertRgba(material, new MatteShader(material).Shade(position, normal, direction));
        AssertRgba(new(0.05f, 0.25f, 0.8f, 1), new NormalShader(material).Shade(position, normal, direction));
        AssertRgba(new(0.1f, 0.2f, 0.4f, 1), new PositionShader(new BBoxd(Vec3d.Zero, new Vec3d(4, 6, 8)), material).Shade(position, normal, direction));
        AssertRgba(new(0.13f, 0.26f, 0.52f, 1), new DiffuseShader(material).Shade(position, normal, direction), 1e-7f);
    }

    [Fact]
    public void GridShadersSampleWorldSpaceAndSupportNativeVectorTypes()
    {
        var grid = new Grid<Vec3f>();
        grid.Transform = Transform.CreateLinearTransform(0.5);
        grid[new Coord(2, 4, 6)] = new Vec3f(0.2f, 0.4f, 0.6f);
        Vec3d world = grid.IndexToWorld(new Coord(2, 4, 6));
        Film.Rgba matte = new MatteGridShader<Vec3f>(grid).Shade(world, Vec3d.UnitZ, -Vec3d.UnitZ);
        AssertRgba(new(0.2f, 0.4f, 0.6f, 1), matte);
        AssertRgba(new(0.2f, 0.4f, 1.2f, 1), new NormalGridShader<Vec3f>(grid).Shade(world, Vec3d.UnitZ, -Vec3d.UnitZ));
        AssertRgba(new(0.2f, 0.4f, 0.6f, 1), new DiffuseGridShader<Vec3f>(grid).Shade(world, Vec3d.UnitZ, -Vec3d.UnitZ));
        Assert.Throws<NotSupportedException>(() => new MatteGridShader<Vec4d>(new Grid<Vec4d>()));
    }

    [Fact]
    public void LevelSetRayTracerRendersHitsBackgroundAndDeterministicSupersampling()
    {
        Grid<float> sphere = LevelSetSphere.Create(3.5f, Vec3f.Zero, 0.5f, 3f);
        Film serialFilm = RenderLevelSet(sphere, threaded: false, samples: 5);
        Film parallelFilm = RenderLevelSet(sphere, threaded: true, samples: 5);
        Assert.Equal(serialFilm.ReadOnlyPixels.ToArray(), parallelFilm.ReadOnlyPixels.ToArray());
        Film.Rgba center = serialFilm[6, 6], corner = serialFilm[0, 0];
        Assert.True(center.R > 0.7f && center.G > 0.2f);
        AssertRgba(new(0.05f, 0.1f, 0.2f, 1), corner);
        Assert.All(serialFilm.ReadOnlyPixels.ToArray(), pixel => Assert.Equal(1f, pixel.A));
    }

    [Fact]
    public void RayTracerCopiesShaderAndHonorsCancellationAndSampleBounds()
    {
        Grid<double> sphere = LevelSetSphere.Create(2.0, Vec3d.Zero, 1.0);
        var film = new Film(3, 3, new Film.Rgba(0f));
        var camera = new PerspectiveCamera(film, translation: new Vec3d(0, 0, 8), farPlane: 20);
        camera.LookAt(Vec3d.Zero);
        var source = new MutableShader(new Film.Rgba(0.8f, 0.2f, 0.1f));
        var tracer = new LevelSetRayTracer<double>(sphere, source, camera);
        source.Color = new Film.Rgba(0f);
        tracer.Render(false);
        Assert.True(film[1, 1].R > 0.7f);
        Assert.Throws<ArgumentOutOfRangeException>(() => tracer.SetPixelSamples(0));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => tracer.Render(cancellationToken: cancellation.Token));
    }

    [Fact]
    public void VolumeRendererProducesDeterministicFogAndValidatesNonterminatingSteps()
    {
        Grid<float> fog = LevelSetSphere.Create(3.5f, Vec3f.Zero, 0.5f, 3f);
        LevelSetUtil.SdfToFogVolume(fog);
        Film serial = RenderVolume(fog, threaded: false), parallel = RenderVolume(fog, threaded: true);
        Assert.Equal(serial.ReadOnlyPixels.ToArray(), parallel.ReadOnlyPixels.ToArray());
        Assert.True(serial[5, 5].A > 0);
        Assert.Contains(serial.ReadOnlyPixels.ToArray(), pixel => pixel.R > 0 || pixel.G > 0 || pixel.B > 0);

        var camera = new PerspectiveCamera(new Film(1, 1), translation: new Vec3d(0, 0, 10), farPlane: 30);
        camera.LookAt(Vec3d.Zero);
        var renderer = new VolumeRenderer<float>(new VolumeRayIntersector<float>(fog), camera);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.PrimaryStep = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => renderer.ShadowStep = double.NaN);
        Assert.Throws<ArithmeticException>(() => renderer.SetLightDirection(Vec3d.Zero));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => renderer.Render(cancellationToken: cancellation.Token));
    }

    private static Film RenderLevelSet(Grid<float> sphere, bool threaded, int samples)
    {
        var film = new Film(13, 13, new Film.Rgba(0.05f, 0.1f, 0.2f));
        var camera = new PerspectiveCamera(film, translation: new Vec3d(0, 0, 12), farPlane: 40);
        camera.LookAt(Vec3d.Zero);
        new LevelSetRayTracer<float>(sphere, new DiffuseShader(new Film.Rgba(0.9f, 0.4f, 0.2f)), camera, samples, 7703).Render(threaded);
        return film;
    }

    private static Film RenderVolume(Grid<float> fog, bool threaded)
    {
        var film = new Film(11, 11, new Film.Rgba(0.4f));
        var camera = new PerspectiveCamera(film, translation: new Vec3d(0, 0, 12), farPlane: 40);
        camera.LookAt(Vec3d.Zero);
        var renderer = new VolumeRenderer<float>(new VolumeRayIntersector<float>(fog), camera) { PrimaryStep = 0.75, ShadowStep = 1.5 };
        renderer.Render(threaded);
        return film;
    }

    private static void AssertRgba(Film.Rgba expected, Film.Rgba actual, float tolerance = 0)
    {
        Assert.InRange(System.Math.Abs(actual.R - expected.R), 0, tolerance);
        Assert.InRange(System.Math.Abs(actual.G - expected.G), 0, tolerance);
        Assert.InRange(System.Math.Abs(actual.B - expected.B), 0, tolerance);
        Assert.InRange(System.Math.Abs(actual.A - expected.A), 0, tolerance);
    }

    private static void AssertVec(Vec3d expected, Vec3d actual, double tolerance = 1e-12) =>
        Assert.True(expected.ApproxEquals(actual, tolerance), $"Expected {expected}, actual {actual}.");

    private sealed class MutableShader(Film.Rgba color) : BaseShader
    {
        public Film.Rgba Color { get; set; } = color;
        public override Film.Rgba Shade(Vec3d position, Vec3d normal, Vec3d rayDirection) => Color;
        public override BaseShader Copy() => new MutableShader(Color);
    }
}
