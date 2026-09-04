// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.IO;
namespace Tedd.OpenVDB.Tests;

public class VectorTransformerNativeTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-vector-transform.json"))));
    public static IEnumerable<object[]> Cases()
    {
        int i = 0; foreach (var c in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray()) yield return [i++, $"{c.GetProperty("kind")}/{c.GetProperty("type")}/{c.GetProperty("mode")}/{c.GetProperty("world")}"];
    }
    [Theory, MemberData(nameof(Cases))]
    public void NativeVectorsMetadataActivityAndSingularFailuresMatch(int index, string label)
    {
        Assert.NotEmpty(label); Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        var c = Fixture.Value.RootElement.GetProperty("cases")[index];
        if (c.GetProperty("kind").GetString() == "scalar")
        {
            var grid = new Grid<float> { VectorType = (VectorType)c.GetProperty("mode").GetInt32(), IsInWorldSpace = c.GetProperty("world").GetInt32() != 0 };
            Assert.Equal(1, c.GetProperty("error").GetInt32()); Assert.Throws<InvalidCastException>(() => VectorTransformer.TransformVectors(grid, Mat4d.Identity)); return;
        }
        switch (c.GetProperty("type").GetString())
        {
            case "vec3d": Run(c, (x, y, z) => new Vec3d(x, y, z), v => v); break;
            case "vec3f": Run(c, (x, y, z) => new Vec3f((float)x, (float)y, (float)z), v => new Vec3d(v.X, v.Y, v.Z)); break;
            case "vec3i": Run(c, (x, y, z) => new Vec3i((int)x, (int)y, (int)z), v => new Vec3d(v.X, v.Y, v.Z)); break;
        }
    }
    private static void Run<T>(JsonElement c, Func<double, double, double, T> make, Func<T, Vec3d> vector) where T : unmanaged
    {
        var grid = new Grid<T>(make(.25, -.5, 1.25)) { VectorType = (VectorType)c.GetProperty("mode").GetInt32(), IsInWorldSpace = c.GetProperty("world").GetInt32() != 0 };
        var tree = grid.Tree; tree.SetValueOn(Coord.Zero, make(2, -3, 4)); tree.SetValueOn(new(1, 0, 0), make(0, 0, 0)); tree.SetValueOn(new(2, 0, 0), make(7, 8, -9));
        tree.SetValueOff(new(3, 0, 0), make(-2, 3, -4)); tree.AddTile(1, new(8, 0, 0), make(1, 2, 3), true); tree.AddTile(2, new(128, 0, 0), make(4, -5, 6), false);
        tree.AddTile(3, new(4096), make(1, -1, 2), true); tree.AddTile(3, new(-4096), make(-3, 2, 1), false);
        var matrix = Mat4d.FromSpan(c.GetProperty("matrix").EnumerateArray().Select(e => e.GetDouble()).ToArray());
        var exception = Record.Exception(() => VectorTransformer.TransformVectors(grid, matrix));
        if (c.GetProperty("error").GetString() == "ArithmeticError") Assert.IsType<ArithmeticException>(exception); else Assert.Null(exception);
        Equal<T>(ReadVector(c.GetProperty("background")), vector(grid.Background)); Assert.Equal(c.GetProperty("active").GetUInt64(), tree.ActiveVoxelCount); Assert.Equal(c.GetProperty("leaves").GetInt64(), tree.LeafCount);
        var groups = tree.EnumerateValues(ValueIterationMode.All).GroupBy(v => (Depth: 3 - v.Level, Active: v.IsActive, Value: vector(v.Value)))
            .Select(g => (g.Key.Depth, g.Key.Active, g.Key.Value, Count: g.LongCount())).OrderBy(g => g.Depth).ThenBy(g => g.Active).ThenBy(g => g.Value.X).ThenBy(g => g.Value.Y).ThenBy(g => g.Value.Z).ToArray();
        var expectedGroups = c.GetProperty("groups"); Assert.Equal(expectedGroups.GetArrayLength(), groups.Length);
        for (int i = 0; i < groups.Length; i++)
        {
            var e = expectedGroups[i]; var a = groups[i]; Assert.Equal(e[0].GetInt32(), a.Depth); Assert.Equal(e[1].GetInt32() != 0, a.Active); Equal<T>(ReadVector(e[2]), a.Value); Assert.Equal(e[3].GetInt64(), a.Count);
        }
        foreach (var p in c.GetProperty("probes").EnumerateArray())
        {
            Coord coordinate = new(p[0].GetInt32(), p[1].GetInt32(), p[2].GetInt32()); Equal<T>(ReadVector(p[3]), vector(tree.GetValue(coordinate)));
            Assert.Equal(p[4].GetInt32() != 0, tree.IsValueOn(coordinate)); Assert.Equal(p[5].GetInt32(), tree.GetValueDepth(coordinate));
        }
    }
    public static IEnumerable<object[]> MetadataCases() => Fixture.Value.RootElement.GetProperty("metadata").EnumerateArray().Select(e => new object[] { e.GetProperty("id").GetInt32(), e.GetProperty("text").GetString()!, e.GetProperty("examples").GetString()!, e.GetProperty("description").GetString()! });
    [Theory, MemberData(nameof(MetadataCases))]
    public void NativeVectorTypeStringsMatch(int id, string text, string examples, string description)
    {
        Assert.Equal(text, Grid<Vec3d>.VectorTypeToString((VectorType)id)); Assert.Equal(examples, Grid<Vec3d>.VectorTypeExamples((VectorType)id)); Assert.Equal(description, Grid<Vec3d>.VectorTypeDescription((VectorType)id));
        Assert.Equal(id is >= 0 and <= 4 ? (VectorType)id : VectorType.Invariant, Grid<Vec3d>.ParseVectorType(" " + text.ToUpperInvariant() + " "));
    }
    private static Vec3d ReadVector(JsonElement p) => new(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble());
    private static void Equal<T>(Vec3d expected, Vec3d actual) where T : unmanaged
    {
        double tolerance = typeof(T) == typeof(Vec3f) ? 2e-6 : typeof(T) == typeof(Vec3i) ? 0 : 2e-13;
        for (int i = 0; i < 3; i++) Assert.True(System.Math.Abs(expected[i] - actual[i]) <= tolerance * System.Math.Max(1, System.Math.Abs(expected[i])), $"Expected {expected}, actual {actual}.");
    }
}

public class VectorTransformerTests
{
    [Fact]
    public void RelativeAndAbsoluteVectorsTreatTranslationDifferently()
    {
        var relative = new Grid<Vec3d> { VectorType = VectorType.ContravariantRelative }; relative[Coord.Zero] = new(1, 2, 3);
        var absolute = relative.DeepCopy(); absolute.VectorType = VectorType.ContravariantAbsolute;
        var m = Mat4d.CreateScale(new Vec3d(2, 3, 4)) * Mat4d.CreateTranslation(new(5, 6, 7));
        VectorTransformer.TransformVectors(relative, m); VectorTransformer.TransformVectors(absolute, m);
        Assert.Equal(new Vec3d(2, 6, 12), relative[Coord.Zero]); Assert.Equal(new Vec3d(7, 12, 19), absolute[Coord.Zero]);
    }
    [Fact]
    public void CovectorsUseInverseTransposeAndUnitNormalsNormalize()
    {
        var g = new Grid<Vec3d> { VectorType = VectorType.Covariant }; g[Coord.Zero] = new(2, 3, 4);
        VectorTransformer.TransformVectors(g, Mat4d.CreateScale(new Vec3d(2, 3, 4))); Assert.Equal(Vec3d.One, g[Coord.Zero]);
        g.VectorType = VectorType.CovariantNormalize; VectorTransformer.TransformVectors(g, Mat4d.Identity); Assert.Equal(1, g[Coord.Zero].Length, 14);
    }
    [Fact]
    public void TinyNormalsRetainTheirUnnormalizedValues()
    {
        var op = new MatMulNormalize(Mat4d.Identity); Vec3d tiny = new(1e-8, 0, 0);
        Assert.Equal(tiny, op.Apply(tiny)); Assert.Equal(Vec3d.Zero, op.Apply(Vec3d.Zero)); Assert.Equal(Vec3d.UnitX, op.Apply(new Vec3d(2e-7, 0, 0)));
        var nan = op.Apply(new Vec3d(double.NaN, 1, 2)); Assert.True(double.IsNaN(nan.X)); Assert.True(double.IsNaN(nan.Y)); Assert.True(double.IsNaN(nan.Z));
    }
    [Fact]
    public void SingularCovectorMatrixFailsBeforeValueMutation()
    {
        var g = new Grid<Vec3d> { VectorType = VectorType.Covariant }; g[Coord.Zero] = new(2, 3, 4);
        Assert.Throws<ArithmeticException>(() => VectorTransformer.TransformVectors(g, Mat4d.Zero)); Assert.Equal(new Vec3d(2, 3, 4), g[Coord.Zero]);
        g.VectorType = VectorType.Invariant; VectorTransformer.TransformVectors(g, Mat4d.Zero);
        g.VectorType = VectorType.Covariant; g.IsInWorldSpace = false; VectorTransformer.TransformVectors(g, Mat4d.Zero); Assert.Equal(new Vec3d(2, 3, 4), g[Coord.Zero]);
    }
    [Fact]
    public void StoredInactiveValuesTransformWhileImplicitRootBackgroundDoesNot()
    {
        var g = new Grid<Vec3d>(Vec3d.One) { VectorType = VectorType.ContravariantAbsolute }; g.Tree.SetValueOff(Coord.Zero, new(2)); g.Tree.AddTile(3, new(4096), new(3), false);
        var transform = g.Transform; VectorTransformer.TransformVectors(g, Mat4d.CreateTranslation(new(10)));
        Assert.Equal(new Vec3d(12), g.Tree.GetValue(Coord.Zero)); Assert.False(g.Tree.IsValueOn(Coord.Zero)); Assert.Equal(new Vec3d(13), g.Tree.GetValue(new(4096)));
        Assert.Equal(new Vec3d(11), g.Tree.GetValue(new(128, 0, 0))); Assert.Equal(Vec3d.One, g.Background); Assert.Equal(Vec3d.One, g.Tree.GetValue(new(8192))); Assert.Same(transform, g.Transform);
    }
    [Fact]
    public void LocalSpaceMetadataUsesNativeTruthConversion()
    {
        var g = new Grid<Vec3d>(); Assert.True(g.IsInWorldSpace); g.SetMetadata(Grid<Vec3d>.LocalSpaceMetadataKey, "false"); Assert.False(g.IsInWorldSpace);
        g.IsInWorldSpace = true; Assert.False(g.GetMetadata<bool>(Grid<Vec3d>.LocalSpaceMetadataKey)); g.SetMetadata(Grid<Vec3d>.LocalSpaceMetadataKey, 0); Assert.True(g.IsInWorldSpace);
        g.IsInWorldSpace = false; Assert.True(g.GetMetadata<bool>(Grid<Vec3d>.LocalSpaceMetadataKey));
    }
    [Fact]
    public void VectorMetadataRejectsNativeTypeConflicts()
    {
        var g = new Grid<Vec3d>(); Assert.Equal(VectorType.Invariant, g.VectorType); g.SetMetadata(Grid<Vec3d>.VectorTypeMetadataKey, 17);
        Assert.Equal(VectorType.Invariant, g.VectorType); Assert.Throws<InvalidCastException>(() => g.VectorType = VectorType.Covariant); g.ClearVectorType(); g.VectorType = VectorType.Covariant; Assert.Equal(VectorType.Covariant, g.VectorType);
        g.VectorType = (VectorType)77; Assert.Equal(string.Empty, g.GetMetadata<string>(Grid<Vec3d>.VectorTypeMetadataKey)); Assert.Equal(VectorType.Invariant, g.VectorType);
    }
    [Fact]
    public void MetadataAndTransformedValuesSurviveArchiveRoundTrip()
    {
        var g = new Grid<Vec3f> { VectorType = VectorType.CovariantNormalize, IsInWorldSpace = true }; g[Coord.Zero] = new(2, 3, 4); VectorTransformer.TransformVectors(g, Mat4d.Identity);
        using var stream = new MemoryStream(); VdbFile.Write(stream, g); stream.Position = 0; var restored = VdbFile.Read<Vec3f>(stream);
        Assert.Equal(g.VectorType, restored.VectorType); Assert.Equal(g.IsInWorldSpace, restored.IsInWorldSpace); Assert.Equal(g[Coord.Zero], restored[Coord.Zero]);
    }
    [Fact]
    public void FunctorsOperateOnLiveHandlesWithoutChangingActivity()
    {
        var tree = new VdbTree<Vec3i>(); tree.SetValueOff(Coord.Zero, new(1, 2, 3)); var value = tree.EnumerateValueHandles(ValueIterationMode.All, 3, 3).First();
        new MatMul(Mat4d.CreateScale(2)).Apply(value); Assert.Equal(new Vec3i(2, 4, 6), tree.GetValue(Coord.Zero)); Assert.False(tree.IsValueOn(Coord.Zero));
        new HomogeneousMatMul(Mat4d.CreateTranslation(new(5))).Apply(value); Assert.Equal(new Vec3i(7, 9, 11), tree.GetValue(Coord.Zero));
        Assert.Equal(Vec3d.Zero, new HomogeneousMatMul(Mat4d.Zero).Apply(Vec3d.One)); Assert.Throws<InvalidCastException>(() => new MatMul(Mat4d.Identity).Apply(1f));
    }
    [Fact]
    public void UndefinedIntegerCastsAreRejected()
    {
        Assert.Throws<OverflowException>(() => new MatMul(Mat4d.CreateScale(1e100)).Apply(new Vec3i(1)));
        Assert.Throws<OverflowException>(() => new MatMul(Mat4d.Identity with { M00 = double.NaN }).Apply(new Vec3i(1)));
    }
    [Fact]
    public void PreCanceledTransformLeavesValuesUntouched()
    {
        var g = new Grid<Vec3d> { VectorType = VectorType.ContravariantAbsolute }; g[Coord.Zero] = Vec3d.One;
        Assert.Throws<OperationCanceledException>(() => VectorTransformer.TransformVectors(g, Mat4d.CreateScale(2), new CancellationToken(true))); Assert.Equal(Vec3d.One, g[Coord.Zero]);
    }
}