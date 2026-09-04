using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
namespace Tedd.OpenVDB.Tests;

public sealed class LevelSetFractureConformanceTests
{
    private static readonly Lazy<JsonElement> Fixture = new(() =>
    {
        using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-level-set-fracture.json.gz"));
        using var gzip = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress); using var doc = JsonDocument.Parse(gzip);
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", doc.RootElement.GetProperty("upstreamCommit").GetString()); return doc.RootElement.Clone();
    });
    public static IEnumerable<object[]> Cases() => Fixture.Value.GetProperty("cases").EnumerateArray().Select((x, i) => new object[] { i, x.GetProperty("type").GetString()!, x.GetProperty("scene").GetInt32(), x.GetProperty("profile").GetInt32() });
    public static IEnumerable<object[]> Quaternions() => Fixture.Value.GetProperty("quaternions").EnumerateArray().Select((x, i) => new object[] { i });
    [Theory, MemberData(nameof(Cases))]
    public void ResidualsFragmentsAndInstanceSamplingMatchNative(int index, string type, int scene, int profile)
    {
        JsonElement item = Fixture.Value.GetProperty("cases")[index]; if (type == "float") Compare<float>(item, scene, profile); else Compare<double>(item, scene, profile);
    }
    private static void Compare<T>(JsonElement item, int scene, int profile) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var input = Scene<T>(scene); var cutter = Sphere<T>(2.5f, new(2, .25f, 0)); List<Grid<T>> grids = [input]; var fracture = new LevelSetFracture<T>();
        Vec3f[]? points = null; Quatf[]? rotations = null;
        if (profile is 2 or 3 or 4 or 5)
        {
            points = [new(-.75f, 0, 0), new(.75f, 1.25f, 0)];
            if (profile == 3) rotations = [Quatf.Identity, Quatf.Identity];
            if (profile == 4) rotations = [Quatf.FromAxisAngle(Axis.Z, .37f), Quatf.FromAxisAngle(Axis.X, -.21f) * Quatf.FromAxisAngle(Axis.Y, .44f)];
            if (profile == 5) rotations = [Quatf.FromAxisAngle(Axis.Z, .37f)];
        }
        if (profile == 8) cutter = input; if (profile == 9) grids.Clear();
        fracture.Fracture(grids, cutter, profile == 1, points, rotations, profile != 7);
        if (profile is 6 or 7) fracture.Fracture(grids, Sphere<T>(2.25f, new(0, 2, 0)), true, cutterOverlap: profile != 7);
        VerifyGrids(item.GetProperty("residuals"), grids); VerifyGrids(item.GetProperty("fragments"), fracture.Fragments);
    }
    private static void VerifyGrids<T>(JsonElement items, IList<Grid<T>> grids) where T : unmanaged, IFloatingPointIeee754<T>
    {
        Assert.Equal(items.GetArrayLength(), grids.Count); for (int index = 0; index < grids.Count; ++index) LevelSetSolidsConformanceTests.Verify(items[index], grids[index]);
    }
    [Theory, MemberData(nameof(Quaternions))]
    public void SinglePrecisionQuaternionMatrixAndXyzAnglesMatchNative(int index)
    {
        JsonElement item = Fixture.Value.GetProperty("quaternions")[index], values = item.GetProperty("q");
        Quatf q = new(Number(values[0]), Number(values[1]), Number(values[2]), Number(values[3]));
        if (index < 42) { float[] a = [0, .3f, -.7f, 1.5707963267948966f, -1.5707963267948966f, 3.141592653589793f, 6.5f]; Quatf built = Quatf.FromAxisAngle((Axis)(index / 14), a[index % 14 / 2]); if (index % 2 != 0) built *= 3; Assert.Equal(q, built); }
        if (index >= 48) { Quatf built = index switch { 48 => Quatf.FromAxisAngle(Axis.Z, .37f), 49 => Quatf.FromAxisAngle(Axis.X, -.21f), 50 => Quatf.FromAxisAngle(Axis.Y, .44f), _ => Quatf.FromAxisAngle(Axis.X, -.21f) * Quatf.FromAxisAngle(Axis.Y, .44f) }; Assert.Equal(q, built); }
        Vec3f angles = q.EulerAnglesXyz(); Mat4f matrix = q.ToMat4();
        for (int axis = 0; axis < 3; ++axis) Equal(Number(item.GetProperty("angles")[axis]), angles[axis]);
        for (int i = 0; i < 16; ++i) Equal(Number(item.GetProperty("matrix")[i]), matrix[i / 4, i % 4]);
        static void Equal(float expected, float actual) => Assert.True(expected == actual || float.IsNaN(expected) && float.IsNaN(actual), $"Expected {expected:R}, got {actual:R}");
        static float Number(JsonElement value) => value.ValueKind == JsonValueKind.String ? float.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetSingle();
    }
    internal static Grid<T> Sphere<T>(float radius, Vec3f center = default) where T : unmanaged, IFloatingPointIeee754<T> => LevelSetSphere.Create<T>(T.CreateTruncating(radius), center.AsVec3d(), T.One, T.CreateTruncating(3));
    internal static Grid<T> Scene<T>(int mode) where T : unmanaged, IFloatingPointIeee754<T>
    {
        var grid = Sphere<T>(mode == 2 ? .8f : mode == 3 ? 1.5f : 3); grid.Name = "object";
        if (mode == 1) Composite.CsgUnion(grid, Sphere<T>(2, new(0, 5, 0)));
        if (mode == 4) grid.Tree.Clear();
        if (mode == 5) { grid.Tree.Clear(); grid.Tree.AddTile(1, Coord.Zero, -T.One, true); }
        if (mode == 6) { grid.Tree.Clear(); for (int i = 0; i < 9; ++i) grid.Tree.SetValueOn(new(i * 8, 0, 0), T.CreateTruncating(.5)); }
        if (mode == 7) { grid.Tree.Clear(); for (int x = 0; x < 3; ++x) for (int y = 0; y < 3; ++y) for (int z = 0; z < 3; ++z) grid.Tree.SetValueOn(new(x, y, z), T.CreateTruncating(x - 1)); }
        return grid;
    }
}
