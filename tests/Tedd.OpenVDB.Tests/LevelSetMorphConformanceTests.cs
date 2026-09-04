using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetMorphConformanceTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() =>
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-level-set-morph.json.gz"));
        using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", document.RootElement.GetProperty("upstreamCommit").GetString()); return document.RootElement.Clone();
    });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((item, index) => new object[] { index, item.GetProperty("type").GetString()!, item.GetProperty("spatial").GetInt32(), item.GetProperty("temporal").GetInt32(), item.GetProperty("targetShape").GetInt32(), item.GetProperty("sourceMap").GetInt32(), item.GetProperty("targetMap").GetInt32(), item.GetProperty("mask").GetInt32() });
    [Theory, MemberData(nameof(Cases))]
    public void ScalarSpeedCflTopologyAndTransformDispatchMatchNative(int index, string type, int spatial, int temporal, int targetShape, int sourceMap, int targetMap, int mask)
    {
        JsonElement item = Fixture.Value.GetProperty("cases")[index];
        if (type == "float") Compare<float>(item, spatial, temporal, targetShape, sourceMap, targetMap, mask); else Compare<double>(item, spatial, temporal, targetShape, sourceMap, targetMap, mask);
    }
    private static void Compare<T>(JsonElement item, int spatial, int temporal, int targetShape, int sourceMap, int targetMap, int mask) where T : unmanaged, IFloatingPointIeee754<T>
    {
        Grid<T> source = Source<T>(sourceMap), target = targetShape == 7 ? source : Target(source, targetShape, targetMap), alpha = mask == 6 ? source : Mask(source, mask);
        Assert.Equal(item.GetProperty("sourceMapType").GetString(), source.Transform.MapType);
        var morph = new LevelSetMorphing<T>(source, target) { SpatialScheme = (BiasedGradientScheme)spatial, TemporalScheme = (TemporalIntegrationScheme)temporal, GrainSize = 0 };
        if (mask != 0)
        {
            morph.SetAlphaMask(alpha); if (mask is >= 2 and <= 4) morph.SetMaskRange(T.CreateTruncating(.2), T.CreateTruncating(.8));
            if (mask == 6) morph.SetMaskRange(-T.One, T.One); morph.IsMaskInverted = mask == 3;
        }
        if (item.GetProperty("tracker").GetInt32() != 0) { morph.TrackerSpatialScheme = BiasedGradientScheme.First; morph.TrackerTemporalScheme = TemporalIntegrationScheme.TvdRungeKutta3; morph.NormalizationCount = 2; }
        ulong Advance() => morph.Advect(T.CreateTruncating(item.GetProperty("time0").GetDouble()), T.CreateTruncating(item.GetProperty("time1").GetDouble()));
        if (item.TryGetProperty("error", out _)) { Assert.ThrowsAny<ArgumentException>(() => { Advance(); }); return; }
        Assert.Equal(item.GetProperty("steps").GetUInt64(), Advance()); LevelSetSolidsConformanceTests.Verify(item, source);
    }
    internal static Grid<T> Source<T>(int map = 0) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var grid = LevelSetSphere.Create<T>(T.CreateTruncating(2.5f), new(.25, .5, -.25), T.CreateTruncating(map >= 2 ? 1 : .75f), T.CreateTruncating(3)); grid.Name = "source";
        if (map == 1) grid.Transform.PostTranslate(new(.375, -.25, .125));
        if (map == 2) grid.Transform.PostRotate(.25, Axis.Z);
        if (map == 3) grid.Transform = new Transform(new TranslationMap(new(.25, -.5, .125)));
        if (map == 4) grid.Transform = new Transform();
        if (map == 5) { grid.Transform = Transform.CreateLinearTransform(.75); grid.Transform.PostRotate(.2, Axis.Z); }
        if (map == 6) { grid.Transform.PostRotate(.2, Axis.Z); grid.Transform.PostTranslate(new(.25, .5, .125)); }
        if (map == 7) { grid.Tree.Clear(); grid.Tree.AddTile(1, new(64), -T.One, true); }
        if (map == 8) grid.Tree.AddTile(1, new(64), -T.One, true);
        if (map == 9) { grid.Tree.Clear(); grid.Tree.SetValueOff(Coord.Zero, -T.One); }
        if (map == 10) grid.Tree.Clear();
        if (map == 11) grid.Transform = Transform.FromMap(new UnitaryMap(Axis.Z, .25), simplify: false);
        if (map == 12) grid.Transform = Transform.FromMap(new TranslationMap(new(.25, -.5, .125)), simplify: false);
        return grid;
    }
    internal static Grid<T> Target<T>(Grid<T> source, int shape = 0, int map = 0) where T : unmanaged, IFloatingPointIeee754<T>
    {
        Grid<T> grid = LevelSetSphere.Create<T>(T.CreateTruncating(shape == 2 ? 1.75f : 3.25f), new(.25, .5, -.25), T.CreateTruncating((float)source.Transform.VoxelSize.X), T.CreateTruncating(3));
        if (shape is 1 or 4) grid = source.DeepCopy();
        if (shape == 3) grid = new(T.CreateTruncating(.2));
        if (shape == 5) grid = new(T.NaN);
        if (shape == 6) grid = new(T.PositiveInfinity);
        grid.Transform = source.Transform.Copy();
        if (map == 1) grid.Transform.PostTranslate(new(.375, -.25, .125));
        if (map == 2) grid.Transform = Transform.CreateFrustumTransform(new(new Vec3d(-4), new Vec3d(4)), .75, 8, 1);
        if (shape == 4) foreach (var value in grid.Tree.EnumerateValueHandles(ValueIterationMode.All)) value.SetValue(value.Value + T.CreateTruncating(5e-7));
        return grid;
    }
    internal static Grid<T> Mask<T>(Grid<T> source, int mode) where T : unmanaged, IFloatingPointIeee754<T>
    {
        Grid<T> grid = new(mode == 5 ? T.NaN : T.CreateTruncating(.5)) { Transform = source.Transform.Copy() };
        if (mode == 4) grid.Transform = Transform.CreateLinearTransform(.5);
        if (mode is 2 or 3 or 4) for (int x = -8; x <= 8; ++x) for (int y = -8; y <= 8; ++y) for (int z = -8; z <= 8; ++z)
        {
            Coord c = new(x, y, z); T value = T.CreateTruncating((x + 5) * .1); if ((x + y + z) % 2 == 0) grid.Tree.SetValueOn(c, value); else grid.Tree.SetValueOff(c, value);
        }
        return grid;
    }
}
