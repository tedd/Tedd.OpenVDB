using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetRebuildConformanceTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() =>
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-level-set-rebuild.json.gz"));
        using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", document.RootElement.GetProperty("upstreamCommit").GetString());
        return document.RootElement.Clone();
    });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((item, i) => new object[] { i, item.GetProperty("type").GetString()!, item.GetProperty("scene").GetInt32(), item.GetProperty("transform").GetInt32(), item.GetProperty("operation").GetInt32(), item.GetProperty("kernel").GetInt32() });
    [Theory, MemberData(nameof(Cases))]
    public void ValuesAndTopologyMatchNative(int index, string type, int scene, int transform, int operation, int kernel)
    {
        if (type == "float") Run<float>(index, scene, transform, operation, kernel);
        else Run<double>(index, scene, transform, operation, kernel);
    }
    private static void Run<T>(int index, int scene, int mode, int operation, int kernel) where T : unmanaged, IFloatingPointIeee754<T>
    {
        JsonElement item = Fixture.Value.GetProperty("cases")[index];
        Grid<T> input = Shape<T>(scene), output;
        Transform transform = CreateTransform(mode);
        if (operation == 0) output = LevelSetRebuild.Rebuild(input, item.GetProperty("iso").GetSingle(), item.GetProperty("exterior").GetSingle(), item.GetProperty("interior").GetSingle(), mode < 0 ? null : transform);
        else
        {
            output = new(T.CreateTruncating(mode == 3 ? .1875 : 3.125)) { Transform = transform, GridClass = item.GetProperty("outputClass").GetInt32() == 1 ? GridClass.LevelSet : GridClass.FogVolume, Name = "retained" };
            output.SetMetadata("custom", "output metadata");
            output.Tree.SetValueOn(new(30, 2, -1), T.CreateTruncating(81));
            Resampling.ResampleToMatch(input, output, (SamplingOrder)kernel);
            Assert.Same(transform, output.Transform);
            Assert.Equal("output metadata", output.Metadata["custom"].GetValue<string>());
        }
        Assert.Equal((GridClass)item.GetProperty("class").GetInt32(), output.GridClass);
        Assert.Equal(item.GetProperty("name").GetString(), output.Name);
        if (operation == 0) { Assert.False(output.Metadata.ContainsKey("custom")); Assert.NotSame(input.Transform, output.Transform); }
        AssertValue(item.GetProperty("background"), output.Background, "background");
        Assert.Equal(item.GetProperty("active").GetUInt64(), output.ActiveVoxelCount);
        Assert.Equal(item.GetProperty("tiles").GetUInt64(), output.Tree.ActiveTileCount);
        Assert.Equal(item.GetProperty("roots").GetInt32(), output.Tree.RootEntryCount);
        JsonElement expectedLeaves = item.GetProperty("leaves");
        Assert.Equal(expectedLeaves.GetArrayLength(), output.Tree.LeafCount);
        foreach (JsonElement expected in expectedLeaves.EnumerateArray())
        {
            JsonElement origin = expected.GetProperty("origin"); Coord coordinate = new(origin[0].GetInt32(), origin[1].GetInt32(), origin[2].GetInt32());
            var leaf = output.Tree.ProbeLeaf(coordinate); Assert.NotNull(leaf);
            int offset = 0;
            foreach (JsonElement run in expected.GetProperty("runs").EnumerateArray())
            {
                int end = checked(offset + run[0].GetInt32()); Assert.InRange(end, offset + 1, 512);
                for (; offset < end; ++offset)
                {
                    Assert.Equal(run[2].GetInt32() != 0, leaf.IsValueOn(offset));
                    AssertValue(run[1], leaf.GetValue(offset), $"{coordinate}, offset {offset}");
                }
            }
            Assert.Equal(512, offset);
        }
        foreach (JsonElement probe in item.GetProperty("probes").EnumerateArray())
        {
            Coord coordinate = new(probe[0].GetInt32(), probe[1].GetInt32(), probe[2].GetInt32());
            AssertValue(probe[3], output.Tree.GetValue(coordinate), coordinate.ToString());
            Assert.Equal(probe[4].GetInt32() != 0, output.Tree.IsValueOn(coordinate));
            Assert.Equal(probe[5].GetInt32(), output.Tree.GetValueDepth(coordinate));
        }
    }
    private static void AssertValue<T>(JsonElement expected, T actual, string location) where T : unmanaged, IFloatingPointIeee754<T>
    {
        double wanted = expected.ValueKind == JsonValueKind.String ? double.Parse(expected.GetString()!, CultureInfo.InvariantCulture) : expected.GetDouble();
        double observed = double.CreateTruncating(actual), tolerance = typeof(T) == typeof(float) ? 3e-6 : 3e-12;
        Assert.True(wanted == observed || double.IsNaN(wanted) && double.IsNaN(observed) || System.Math.Abs(wanted - observed) <= tolerance * System.Math.Max(1, System.Math.Abs(wanted)), $"{location}: expected {wanted:R}, got {observed:R}");
    }
    internal static Grid<T> Shape<T>(int scene) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var grid = new Grid<T>(T.CreateTruncating(2.5)) { GridClass = GridClass.LevelSet };
        if (scene is 0 or 1 or 5) grid = LevelSetSphere.Create<T>(T.CreateTruncating(2.5), new(.25, .5, -.25), T.One, T.CreateTruncating(2));
        if (scene == 1) grid.GridClass = GridClass.FogVolume;
        if (scene == 2) grid.Tree.AddTile(1, Coord.Zero, -T.One, true);
        if (scene == 3) grid.Tree.SetValueOff(Coord.Zero, -T.One);
        if (scene == 4) grid.Tree.SetValueOn(Coord.Zero, -T.One);
        if (scene == 5) grid.Transform = CreateTransform(3);
        grid.Name = "source"; grid.SetMetadata("custom", "source metadata"); return grid;
    }
    internal static Transform CreateTransform(int mode)
    {
        if (mode == 1) return Transform.CreateLinearTransform(1.25);
        if (mode == 3) return Transform.CreateFrustumTransform(new BBoxd(new Vec3d(-8), new Vec3d(8)), .75, 12, 1);
        if (mode != 2) return new();
        var transform = new Transform(); transform.PreScale(new Vec3d(.75, 1.25, 1)); transform.PostRotate(.2, Axis.Z); transform.PostTranslate(new Vec3d(.375, -.25, .125)); return transform;
    }
}
