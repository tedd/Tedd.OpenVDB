using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetSolidsConformanceTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() =>
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-level-set-solids.json.gz"));
        using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", document.RootElement.GetProperty("upstreamCommit").GetString()); return document.RootElement.Clone();
    });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((item, index) => new object[] { index, item.GetProperty("operation").GetInt32(), item.GetProperty("type").GetString()!, item.TryGetProperty("faces", out var faces) ? faces.GetInt32() : item.GetProperty("scene").GetInt32(), item.GetProperty("profile").GetInt32() });
    [Theory, MemberData(nameof(Cases))]
    public void ValuesTopologyAndExceptionalInputsMatchNative(int index, int operation, string type, int shape, int profile)
    {
        JsonElement item = Fixture.Value.GetProperty("cases")[index]; Assert.Equal(profile, item.GetProperty("profile").GetInt32());
        if (operation == 0)
        {
            if (type == "float") Platonic<float>(item, shape); else Platonic<double>(item, shape);
        }
        else
        {
            if (type == "bool") Topology<bool>(item, shape); else Topology<int>(item, shape);
        }
    }
    private static void Platonic<T>(JsonElement item, int faces) where T : unmanaged, IFloatingPointIeee754<T>
    {
        JsonElement c = item.GetProperty("center"); Vec3f center = new(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle());
        Grid<T> Create() => LevelSetPlatonic.Create<T>(faces, item.GetProperty("scale").GetSingle(), center, item.GetProperty("voxel").GetSingle(), item.GetProperty("width").GetSingle());
        if (item.TryGetProperty("error", out var error))
        {
            Assert.Contains("inside value must be negative", error.GetString()); Assert.Throws<ArgumentOutOfRangeException>(Create); return;
        }
        Verify(item, Create());
    }
    private static void Topology<T>(JsonElement item, int scene) where T : unmanaged
    {
        Grid<T> input = Shape<T>(scene); var transform = Transform.CreateLinearTransform(item.GetProperty("voxel").GetDouble()); transform.PostRotate(.21, Axis.Z); transform.PostTranslate(new Vec3d(.3, -.2, .1)); input.Transform = transform;
        Grid<float> output = TopologyToLevelSet.Create(input, item.GetProperty("width").GetInt32(), item.GetProperty("closing").GetInt32(), item.GetProperty("dilation").GetInt32(), item.GetProperty("smoothing").GetInt32());
        Verify(item, output); Assert.NotSame(transform, output.Transform); Assert.True(transform.IsEquivalentTo(output.Transform));
    }
    internal static void Verify<T>(JsonElement item, Grid<T> output) where T : unmanaged, IFloatingPointIeee754<T>
    {
        Assert.Equal((GridClass)item.GetProperty("class").GetInt32(), output.GridClass); Assert.Equal(item.GetProperty("name").GetString(), output.Name);
        AssertValue(item.GetProperty("background"), output.Background, "background");
        Assert.Equal(item.GetProperty("active").GetUInt64(), output.ActiveVoxelCount); Assert.Equal(item.GetProperty("tiles").GetUInt64(), output.Tree.ActiveTileCount); Assert.Equal(item.GetProperty("roots").GetInt32(), output.Tree.RootEntryCount);
        for (int axis = 0; axis < 3; ++axis)
        {
            Assert.InRange(System.Math.Abs(item.GetProperty("voxelSize")[axis].GetDouble() - output.Transform.VoxelSize[axis]), 0, 1e-12);
            Assert.InRange(System.Math.Abs(item.GetProperty("worldZero")[axis].GetDouble() - output.Transform.IndexToWorld(Vec3d.Zero)[axis]), 0, 1e-12);
        }
        JsonElement leaves = item.GetProperty("leaves"); Assert.Equal(leaves.GetArrayLength(), output.Tree.LeafCount);
        foreach (JsonElement expected in leaves.EnumerateArray())
        {
            JsonElement origin = expected.GetProperty("origin"); Coord coordinate = new(origin[0].GetInt32(), origin[1].GetInt32(), origin[2].GetInt32()); var leaf = output.Tree.ProbeLeaf(coordinate); Assert.NotNull(leaf);
            int position = 0;
            foreach (JsonElement run in expected.GetProperty("runs").EnumerateArray())
            {
                int end = checked(position + run[0].GetInt32()); Assert.InRange(end, position + 1, 512);
                for (; position < end; ++position) { Assert.Equal(run[2].GetInt32() != 0, leaf.IsValueOn(position)); AssertValue(run[1], leaf.GetValue(position), $"{coordinate}, offset {position}"); }
            }
            Assert.Equal(512, position);
        }
        foreach (JsonElement probe in item.GetProperty("probes").EnumerateArray())
        {
            Coord coordinate = new(probe[0].GetInt32(), probe[1].GetInt32(), probe[2].GetInt32()); AssertValue(probe[3], output.Tree.GetValue(coordinate), coordinate.ToString());
            Assert.Equal(probe[4].GetInt32() != 0, output.Tree.IsValueOn(coordinate)); Assert.Equal(probe[5].GetInt32(), output.Tree.GetValueDepth(coordinate));
        }
    }
    private static void AssertValue<T>(JsonElement expected, T actual, string location) where T : unmanaged, IFloatingPointIeee754<T>
    {
        double wanted = expected.ValueKind == JsonValueKind.String ? double.Parse(expected.GetString()!, CultureInfo.InvariantCulture) : expected.GetDouble();
        double observed = double.CreateTruncating(actual), tolerance = typeof(T) == typeof(float) ? 3e-6 : 3e-12;
        Assert.True(wanted == observed || double.IsNaN(wanted) && double.IsNaN(observed) || System.Math.Abs(wanted - observed) <= tolerance * System.Math.Max(1, System.Math.Abs(wanted)), $"{location}: expected {wanted:R}, got {observed:R}");
    }
    internal static Grid<T> Shape<T>(int scene) where T : unmanaged
    {
        static T Value(int value) => typeof(T) == typeof(bool) ? (T)(object)(value != 0) : (T)(object)value;
        var grid = new Grid<T>(Value(19)) { Name = "input" }; var tree = grid.Tree;
        if (scene == 0) tree.SetValueOn(Coord.Zero, Value(0));
        if (scene == 1) tree.Fill(new(new(-3, -2, -1), new(3, 2, 1)), Value(0), true);
        if (scene == 2) { tree.Fill(new(new(-4, -2, -2), new(-1, 2, 2)), Value(1), true); tree.Fill(new(new(1, -2, -2), new(4, 2, 2)), Value(-1), true); }
        if (scene == 3) { tree.AddTile(1, Coord.Zero, Value(0), true); tree.AddTile(1, new(-8, 0, 0), Value(-7), false); tree.Fill(new(new(8, 2, 2), new(10, 4, 4)), Value(7), true); }
        if (scene == 4) { tree.SetValueOff(Coord.Zero, Value(-1)); tree.AddTile(1, new(8), Value(123), false); }
        if (scene == 5) tree.TopologyUnion(LevelSetSphere.Create<float>(2.5f, new(.25, .5, -.25), 1, 1.5f).Tree);
        if (scene == 6) tree.AddTile(2, Coord.Zero, Value(0), true);
        return grid;
    }
}
