using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;

namespace Tedd.OpenVDB.Tests;

public sealed class RayTracerNativeTests
{
    private const string Commit = "f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf";
    private static readonly JsonDocument Fixture = LoadFixture();
    private static IEnumerable<JsonElement> Cases(string kind) => Fixture.RootElement.GetProperty("cases").EnumerateArray().Where(item => item.GetProperty("kind").GetString() == kind);

    [Fact]
    public void FilmAndRgbaMatchNativeBitForBit()
    {
        JsonElement rgbaCase = Assert.Single(Cases("rgba"));
        Film.Rgba[] values =
        [
            new(), new(0.375f),
            new Film.Rgba(0.2f, 0.3f, 0.4f, 0.25f) + new Film.Rgba(0.5f, 0.6f, 0.7f, 0.75f),
            new Film.Rgba(0.2f, 0.3f, 0.4f, 0.25f) * new Film.Rgba(0.5f, 0.6f, 0.7f, 0.75f),
            new Film.Rgba(0.2f, 0.3f, 0.4f, 0.25f) * 2f,
            Accumulated(), Over()
        ];
        ComparePixels(rgbaCase.GetProperty("values"), values, 0);

        JsonElement filmCase = Assert.Single(Cases("film"));
        var film = new Film(7, 5);
        ComparePixels(filmCase.GetProperty("default"), film.ReadOnlyPixels, 0);
        film.Checkerboard(new Film.Rgba(0.2f, 0.3f, 0.4f, 0.5f), new Film.Rgba(0.8f, 0.7f, 0.6f, 0.1f), 2);
        ComparePixels(filmCase.GetProperty("checker"), film.ReadOnlyPixels, 0);
        Assert.Equal(filmCase.GetProperty("bytes").EnumerateArray().Select(item => item.GetByte()), film.ConvertToByteBuffer());
    }

    [Fact]
    public void PerspectiveAndOrthographicCameraRaysMatchNative()
    {
        CameraConfiguration[] configurations =
        [
            new(1,1,new(0),new(0),new(0),new(0,1,0),50,41.2136,1,0.001,100,false),
            new(4,3,new(10,-20,30),new(1.25,-2.5,8),new(0),new(0,1,0),35,24.576,7.5,0.125,80,false),
            new(7,5,new(-17.5,42.25,91),new(-3.5,2.25,11.75),new(0.25,-0.5,1),new(0,1,0),80,41.2136,12,0.01,250,true),
            new(9,6,new(0),new(4,-3,9),new(-1,2,-0.5),new(0.2,1,0.1),52.5,36,4.25,0.5,40,true)
        ];
        Assert.Equal(8, Cases("camera").Count());
        foreach (JsonElement item in Cases("camera"))
        {
            int kind = item.GetProperty("camera").GetInt32();
            CameraConfiguration c = configurations[item.GetProperty("configuration").GetInt32()];
            var film = new Film(c.Width, c.Height);
            BaseCamera camera = kind == 0
                ? new PerspectiveCamera(film, c.Rotation, c.Translation, c.Lens, c.Aperture, c.Near, c.Far)
                : new OrthographicCamera(film, c.Rotation, c.Translation, c.FrameWidth, c.Near, c.Far);
            if (c.LookAt) camera.LookAt(c.Target, c.Up);
            foreach (JsonElement expected in item.GetProperty("rays").EnumerateArray())
            {
                Rayd actual = camera.GetRay(expected.GetProperty("x").GetInt32(), expected.GetProperty("y").GetInt32(), expected.GetProperty("ox").GetDouble(), expected.GetProperty("oy").GetDouble());
                CompareVector(expected.GetProperty("eye"), actual.Eye, 2e-13);
                CompareVector(expected.GetProperty("direction"), actual.Direction, 2e-13);
                JsonElement times = expected.GetProperty("times");
                Near(times[0].GetDouble(), actual.MinTime, 2e-13);
                Near(times[1].GetDouble(), actual.MaxTime, 2e-13);
            }
        }
    }

    [Fact]
    public void ConstantAndSampledShadersMatchNative()
    {
        Film.Rgba[] colors = [new(1f), new(0.2f, 0.4f, 0.8f, 0.1f), new(-0.25f, 1.25f, 0.5f, 0.75f)];
        Vec3d[] positions = [new(0), new(2, 3, 4), new(-1.5, 7, 0.125)];
        Vec3d[] normals = [new(0, 0, 1), new(-0.5, 0.25, 1), new(0.2, -0.9, 0.3)];
        Vec3d[] directions = [new(0, 0, -1), new(0, 0.6, -0.8), new(-0.75, 0.5, 0.125)];
        BBoxd bounds = new(new Vec3d(-2, -3, -4), new Vec3d(6, 9, 12));
        foreach (JsonElement item in Cases("constantShader"))
        {
            int color = item.GetProperty("color").GetInt32(), sample = item.GetProperty("sample").GetInt32();
            BaseShader[] shaders = [new MatteShader(colors[color]), new NormalShader(colors[color]), new PositionShader(bounds, colors[color]), new DiffuseShader(colors[color])];
            Film.Rgba[] actual = shaders.Select(shader => shader.Shade(positions[sample], normals[sample], directions[sample])).ToArray();
            ComparePixels(item.GetProperty("outputs"), actual, 0);
        }
        foreach (JsonElement item in Cases("sampledShader"))
        {
            if (item.GetProperty("precision").GetString() == "float") CompareSampled<Vec3f>(item);
            else CompareSampled<Vec3d>(item);
        }
    }

    [Fact]
    public void LevelSetImagesMatchNativeReferenceTracer()
    {
        Assert.Equal(16, Cases("levelSet").Count());
        Grid<float>? floatGrid = null; Grid<double>? doubleGrid = null;
        foreach (JsonElement item in Cases("levelSet"))
        {
            string precision = item.GetProperty("precision").GetString()!;
            if (precision == "float")
            {
                floatGrid ??= LevelSetSphere.Create<float>(2.75f, new(0.25, -0.5, 0.125), 0.5f, 3f);
                CompareLevelSet(item, floatGrid);
            }
            else
            {
                doubleGrid ??= LevelSetSphere.Create<double>(2.75, new(0.25, -0.5, 0.125), 0.5, 3);
                CompareLevelSet(item, doubleGrid);
            }
        }
    }

    [Fact]
    public void VolumeImagesMatchNativeReferenceRenderer()
    {
        Assert.Equal(8, Cases("volume").Count());
        Grid<float>? floatGrid = null; Grid<double>? doubleGrid = null;
        foreach (JsonElement item in Cases("volume"))
        {
            string precision = item.GetProperty("precision").GetString()!;
            if (precision == "float")
            {
                if (floatGrid is null) { floatGrid = LevelSetSphere.Create<float>(2.75f, new(0.25, -0.5, 0.125), 0.5f, 3f); LevelSetUtil.SdfToFogVolume(floatGrid); }
                CompareVolume(item, floatGrid);
            }
            else
            {
                if (doubleGrid is null) { doubleGrid = LevelSetSphere.Create<double>(2.75, new(0.25, -0.5, 0.125), 0.5, 3); LevelSetUtil.SdfToFogVolume(doubleGrid); }
                CompareVolume(item, doubleGrid);
            }
        }
    }

    private static void CompareLevelSet<T>(JsonElement item, Grid<T> grid) where T : unmanaged, System.Numerics.IFloatingPointIeee754<T>
    {
        var film = new Film(9, 7, new Film.Rgba(0.02f, 0.04f, 0.08f, 0.3f));
        int cameraKind = item.GetProperty("camera").GetInt32(), shaderMode = item.GetProperty("shader").GetInt32();
        BaseCamera camera = cameraKind == 0 ? new PerspectiveCamera(film, translation: new Vec3d(0, 0, 9), focalLength: 45, aperture: 36, nearPlane: 0.05, farPlane: 30)
            : new OrthographicCamera(film, translation: new Vec3d(0, 0, 9), frameWidth: 8, nearPlane: 0.05, farPlane: 30);
        camera.LookAt(new Vec3d(0.25, -0.5, 0.125));
        var color = new Film.Rgba(0.9f, 0.4f, 0.2f, 0.35f);
        BaseShader shader = shaderMode switch { 0 => new MatteShader(color), 1 => new NormalShader(color), 2 => new PositionShader(new BBoxd(new Vec3d(-4), new Vec3d(4)), color), _ => new DiffuseShader(color) };
        new LevelSetRayTracer<T>(grid, shader, camera, item.GetProperty("samples").GetInt32(), 7703).Render(false);
        ComparePixels(item.GetProperty("pixels"), film.ReadOnlyPixels, 3e-6f);
    }

    private static void CompareVolume<T>(JsonElement item, Grid<T> grid) where T : unmanaged, System.Numerics.IFloatingPointIeee754<T>
    {
        var film = new Film(9, 7, new Film.Rgba(0.4f));
        int cameraKind = item.GetProperty("camera").GetInt32(), mode = item.GetProperty("mode").GetInt32();
        BaseCamera camera = cameraKind == 0 ? new PerspectiveCamera(film, translation: new Vec3d(0, 0, 9), focalLength: 45, aperture: 36, nearPlane: 0.05, farPlane: 30)
            : new OrthographicCamera(film, translation: new Vec3d(0, 0, 9), frameWidth: 8, nearPlane: 0.05, farPlane: 30);
        camera.LookAt(new Vec3d(0.25, -0.5, 0.125));
        var renderer = new VolumeRender<T>(new VolumeRayIntersector<T>(grid), camera);
        if (mode != 0)
        {
            renderer.PrimaryStep = 0.625; renderer.ShadowStep = 1.375; renderer.SetLightDirection(new(-0.2, 0.7, 0.4));
            renderer.LightColor = new(0.5, 0.8, 0.3); renderer.Absorption = new(0.2, 0.05, 0.125); renderer.Scattering = new(1.1, 1.7, 0.9);
            renderer.LightGain = 0.35; renderer.Cutoff = 0.0025;
        }
        renderer.Render(false);
        ComparePixels(item.GetProperty("pixels"), film.ReadOnlyPixels, 3e-6f);
    }

    private static void CompareSampled<TVector>(JsonElement item) where TVector : unmanaged
    {
        var grid = new Grid<TVector>(MakeVector<TVector>(-0.1, 0.2, 0.3));
        grid.Transform = Transform.CreateLinearTransform(0.75); grid.Transform.PostTranslate(new(1.25, -2.5, 0.5));
        for (int x = -2; x <= 2; ++x) for (int y = -2; y <= 2; ++y) for (int z = -2; z <= 2; ++z)
            grid[new Coord(x, y, z)] = MakeVector<TVector>(0.1 * x + 0.03 * y, -0.05 * y + 0.02 * z + 0.4, 0.07 * z - 0.025 * x + 0.6);
        BBoxd bounds = new(new Vec3d(-3, -4, -5), new Vec3d(7, 8, 9));
        Vec3d[] normals = [new(0, 0, 1), new(-0.5, 0.25, 1), new(1, 0, 0), new(0.2, -0.9, 0.3), new(-1, 0.5, 0)];
        Vec3d[] directions = [new(0, 0, -1), new(0, 0.6, -0.8), new(-1, 0, 0), new(-0.75, 0.5, 0.125), new(0.5, -0.5, 0.5)];
        int index = 0;
        foreach (JsonElement sample in item.GetProperty("samples").EnumerateArray())
        {
            Vec3d world = grid.IndexToWorld(ReadVector(sample.GetProperty("index")));
            BaseShader[] shaders = [new MatteGridShader<TVector>(grid), new NormalGridShader<TVector>(grid), new PositionGridShader<TVector>(bounds, grid), new DiffuseGridShader<TVector>(grid)];
            Film.Rgba[] actual = shaders.Select(shader => shader.Shade(world, normals[index], directions[index])).ToArray();
            ComparePixels(sample.GetProperty("outputs"), actual, 1e-7f); ++index;
        }
    }

    private static TVector MakeVector<TVector>(double x, double y, double z) where TVector : unmanaged
    {
        if (typeof(TVector) == typeof(Vec3f)) return (TVector)(object)new Vec3f((float)x, (float)y, (float)z);
        return (TVector)(object)new Vec3d(x, y, z);
    }

    private static Film.Rgba Accumulated() { var a = new Film.Rgba(0.2f, 0.3f, 0.4f, 0.25f); a.Add(new(0.5f, 0.6f, 0.7f, 0.75f)); return a; }
    private static Film.Rgba Over() { var a = new Film.Rgba(0.2f, 0.3f, 0.4f, 0.25f); a.Over(new(0.5f, 0.6f, 0.7f, 0.75f)); return a; }
    private static Vec3d ReadVector(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble(), value[2].GetDouble());
    private static void CompareVector(JsonElement expected, Vec3d actual, double tolerance) { Near(expected[0].GetDouble(), actual.X, tolerance); Near(expected[1].GetDouble(), actual.Y, tolerance); Near(expected[2].GetDouble(), actual.Z, tolerance); }
    private static void ComparePixels(JsonElement expected, ReadOnlySpan<Film.Rgba> actual, float tolerance)
    {
        Assert.Equal(expected.GetArrayLength(), actual.Length); int index = 0;
        foreach (JsonElement pixel in expected.EnumerateArray())
        {
            Film.Rgba value = actual[index++];
            Near(pixel[0].GetSingle(), value.R, tolerance); Near(pixel[1].GetSingle(), value.G, tolerance); Near(pixel[2].GetSingle(), value.B, tolerance); Near(pixel[3].GetSingle(), value.A, tolerance);
        }
    }
    private static void Near(float expected, float actual, float tolerance) => Assert.InRange(System.Math.Abs(actual - expected), 0, tolerance);
    private static void Near(double expected, double actual, double tolerance)
    {
        double scale = System.Math.Max(1, System.Math.Abs(expected));
        Assert.True(System.Math.Abs(actual - expected) <= tolerance * scale, $"Expected {expected:R}, actual {actual:R}.");
    }
    private static JsonDocument LoadFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-ray-tracer.json");
        JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal(Commit, document.RootElement.GetProperty("upstreamCommit").GetString());
        return document;
    }
    private sealed record CameraConfiguration(int Width, int Height, Vec3d Rotation, Vec3d Translation, Vec3d Target, Vec3d Up, double Lens, double Aperture, double FrameWidth, double Near, double Far, bool LookAt);
}
