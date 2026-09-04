// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Text.Json;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbMapTests
{
    private sealed record Sample(double[] Point, double[] Mapped, double[] Inverse, double[] Jacobian, double[] InverseJacobian, double[] Jt, double[] Ijt);
    private sealed record Fixture(int Source, string Operation, bool Error, string? Type, string? Wire, bool Uniform, bool Identity, double Determinant, double[]? VoxelSize, double[]? Matrix, double[]? InverseMatrix, double[]? Ijc, Sample[]? Samples);
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "maps");
    private static readonly Fixture[] Fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(Folder, "native-maps.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    private static readonly Dictionary<int, Fixture> Bases = Fixtures.Where(x => x.Operation == "base").ToDictionary(x => x.Source);
    public static IEnumerable<object[]> Cases() => Fixtures.Select((x, index) => new object[] { index, x.Source, x.Operation });
    private static Vec3d Vector(double[] value) => new(value[0], value[1], value[2]);
    private static Mat3d Hessian => new(1.25, -.75, .625, .125, 2.5, -1.25, -.5, .375, 3.75);
    private static Mat4d ScaleTranslationMatrix => Mat4d.CreateScale(.375) * Mat4d.CreateTranslation(new(1.25, -2.5, .75));
    private static MapBase MakeBase(int index) => index switch
    {
        0 => new ScaleMap(),
        1 => new ScaleMap(new Vec3d(.375)),
        2 => new ScaleMap(new Vec3d(-.7, 1.125, 1.375)),
        3 => new UniformScaleMap(),
        4 => new UniformScaleMap(.375),
        5 => new TranslationMap(),
        6 => new TranslationMap(new(1.25, -2.5, .75)),
        7 => new ScaleTranslateMap(new Vec3d(.375), new(1.25, -2.5, .75)),
        8 => new UniformScaleTranslateMap(.375, new(1.25, -2.5, .75)),
        9 => new ScaleTranslateMap(new Vec3d(-.7, 1.125, 1.375), new(-.25, .75, 1.125)),
        10 => new UnitaryMap(),
        11 => new UnitaryMap(Axis.Z, .21),
        12 => new UnitaryMap(Mat3d.CreateScale(new Vec3d(-1, 1, 1))),
        13 => new AffineMap(),
        14 => new AffineMap(ScaleTranslationMatrix),
        15 => MakeGeneralAffine(),
        16 => new NonlinearFrustumMap(new BBoxd(new Vec3d(-8, -6, -4), new Vec3d(8, 6, 12)), .65, 9, Mat4d.CreateScale(.75)),
        17 => new ScaleMap(new Vec3d(1e-4, 1, 1)),
        18 => new AffineMap(Mat4d.CreateScale(new Vec3d(1e-9, 1, 1))),
        19 => new TranslationMap(new(1e17, -1e17, 3e16)),
        20 => new AffineMap(Mat4d.CreateScale(.375) * Mat4d.CreateTranslation(new(1e17, -1e17, 3e16))),
        21 => ReadWire(Bases[index].Wire!).BaseMap,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
    private static MapBase MakeGeneralAffine()
    {
        MapBase map = new AffineMap(Mat4d.CreateScale(new Vec3d(.7, 1.125, 1.375)));
        return map.PostRotate(.21, Axis.Z).PostShear(.13, Axis.X, Axis.Y).PostTranslate(new(-.25, .75, 1.125));
    }
    private static Transform ReadWire(string wire)
    {
        using var input = new MemoryStream(Convert.FromHexString(wire)); var transform = new Transform(); transform.Read(input); Assert.Equal(input.Length, input.Position); return transform;
    }
    private static MapBase Operate(int source, string operation)
    {
        if (source < 0) return operation switch { "default" => new Transform().BaseMap, "factory-scale" => Transform.CreateLinearTransform(.375).BaseMap, _ => Transform.CreateLinearTransform(ScaleTranslationMatrix).BaseMap };
        MapBase map = MakeBase(source);
        return operation switch
        {
            "base" => map,
            "map-copy" => map.Copy(),
            "transform" => new Transform(map).BaseMap,
            "transform-copy" => Transform.FromMap(map).Copy().BaseMap,
            "inverse" => map.InverseMap(),
            "pre-translate" => map.PreTranslate(new(.125, -.75, 2.25)),
            "post-translate" => map.PostTranslate(new(.125, -.75, 2.25)),
            "pre-uniform" => map.PreScale(2),
            "post-uniform" => map.PostScale(2),
            "pre-scale" => map.PreScale(new Vec3d(.75, 1.5, -.5)),
            "post-scale" => map.PostScale(new Vec3d(.75, 1.5, -.5)),
            "pre-mult" => Multiply(map, true),
            "post-mult" => Multiply(map, false),
            _ => AxisOperation(map, operation)
        };
    }
    private static MapBase AxisOperation(MapBase map, string operation)
    {
        Axis axis = (Axis)(operation[^1] - '0'), other = (Axis)(((int)axis + 1) % 3); bool pre = operation.StartsWith("pre", StringComparison.Ordinal);
        return operation.Contains("rotate", StringComparison.Ordinal) ? pre ? map.PreRotate(.21, axis) : map.PostRotate(.21, axis) : pre ? map.PreShear(.13, axis, other) : map.PostShear(.13, axis, other);
    }
    private static MapBase Multiply(MapBase map, bool pre)
    {
        var transform = Transform.FromMap(map); Mat4d matrix = Mat4d.CreateScale(new Vec3d(.875, 1.25, 1.5)) * Mat4d.CreateTranslation(new(.25, -.5, 1));
        if (pre) transform.PreMult(matrix); else transform.PostMult(matrix); return transform.BaseMap;
    }
    private static void Near(double expected, double actual)
    {
        if (expected == actual) return;
        Assert.True(double.IsFinite(actual) && System.Math.Abs(expected - actual) <= 2e-12 * System.Math.Max(1, System.Math.Abs(expected)), $"Expected {expected:R}; got {actual:R}.");
    }
    private static void VectorNear(double[] expected, Vec3d actual, bool exact = false)
    {
        for (int axis = 0; axis < 3; ++axis) { if (exact) Assert.Equal(expected[axis], actual[axis]); else Near(expected[axis], actual[axis]); }
    }
    [Theory, MemberData(nameof(Cases))]
    public void NativeMapOperationsRetainTypesArithmeticAndWire(int index, int source, string operation)
    {
        Fixture expected = Fixtures[index]; Assert.Equal(source, expected.Source); Assert.Equal(operation, expected.Operation);
        if (expected.Error) { Assert.Throws<ArithmeticException>(() => Operate(source, operation)); return; }
        MapBase actual = Operate(source, operation); Assert.Equal(expected.Type, actual.MapType); Assert.Equal(expected.Uniform, actual.HasUniformScale); Assert.Equal(expected.Identity, actual.IsIdentity);
        Near(expected.Determinant, actual.Determinant); VectorNear(expected.VoxelSize!, actual.VoxelSize);
        for (int row = 0; row < 4; ++row) for (int column = 0; column < 4; ++column) { Near(expected.Matrix![row * 4 + column], actual.Matrix[row, column]); Near(expected.InverseMatrix![row * 4 + column], actual.InverseMatrix[row, column]); }
        Mat3d ijc = actual.ApplyIJC(Hessian); for (int row = 0; row < 3; ++row) for (int column = 0; column < 3; ++column) Near(expected.Ijc![row * 3 + column], ijc[row, column]);
        bool specialized = actual is ScaleMap or ScaleTranslateMap or TranslationMap;
        foreach (var sample in expected.Samples!)
        {
            Vec3d point = Vector(sample.Point); VectorNear(sample.Mapped, actual.ApplyMap(point), specialized); VectorNear(sample.Inverse, actual.ApplyInverseMap(point), specialized);
            VectorNear(sample.Jacobian, actual.ApplyJacobian(point), specialized); VectorNear(sample.InverseJacobian, actual.ApplyInverseJacobian(point), specialized); VectorNear(sample.Jt, actual.ApplyJT(point), specialized); VectorNear(sample.Ijt, actual.ApplyIJT(point), specialized);
        }
        var transform = Transform.FromMap(actual); using var bytes = new MemoryStream(); transform.Write(bytes);
        Assert.Equal(expected.Wire, Convert.ToHexStringLower(bytes.ToArray()));
        var read = ReadWire(expected.Wire!); Assert.Equal(expected.Type, read.MapType); using var roundTrip = new MemoryStream(); read.Write(roundTrip); Assert.Equal(bytes.ToArray(), roundTrip.ToArray());
    }
    [Theory, InlineData(0), InlineData(4), InlineData(6)]
    public void NativeMapArchivesRemainNativeCompatible(int compression)
    {
        using var source = File.OpenRead(Path.Combine(Folder, $"native-maps-{compression}.vdb")); VdbArchive archive = VdbFile.ReadArchive(source); Assert.Equal(20, archive.Grids.Count);
        for (int i = 0; i < archive.Grids.Count; ++i) { var grid = Assert.IsType<Grid<float>>(archive.Grids[i]); int sourceIndex = int.Parse(grid.Name[4..], System.Globalization.CultureInfo.InvariantCulture); Assert.Equal(sourceIndex + .25f, grid.Tree.GetValue(new(-1, 2, 3))); }
        using var bytes = new MemoryStream(); VdbFile.WriteArchive(bytes, archive, options: new() { Compression = (VdbCompression)compression });
        string? exported = Environment.GetEnvironmentVariable("TEDD_OPENVDB_MAP_OUTPUT_DIR"); if (exported is not null) { Directory.CreateDirectory(exported); File.WriteAllBytes(Path.Combine(exported, $"managed-maps-{compression}.vdb"), bytes.ToArray()); }
        bytes.Position = 0; VdbArchive read = VdbFile.ReadArchive(bytes); for (int i = 0; i < read.Grids.Count; ++i) Assert.Equal(archive.Grids[i].Transform.MapType, read.Grids[i].Transform.MapType);
    }
    [Fact]
    public void SpecializedInversePreservesNativeBoundaryAndBatchOverlap()
    {
        Transform transform = Transform.CreateLinearTransform(.375); transform.PostTranslate(new(1.25, -2.5, .75)); Assert.Equal("UniformScaleTranslateMap", transform.MapType);
        var points = new[] { new Vec3d(1.25, -2.5, .75), new Vec3d(1.0625, -2.3125, .1875), new Vec3d(1.249999999625, -2.499999999625, 1.124999999625) };
        Vec3d[] expected = points.Select(transform.WorldToIndex).ToArray(); Vec3d[] overlap = [.. points, default]; transform.WorldToIndex(overlap.AsSpan(0, 3), overlap.AsSpan(1)); Assert.Equal(expected, overlap[1..]);
        var affine = new Transform(transform.Matrix); Assert.Equal(8, transform.WorldToIndexNodeCentered(new Vec3d(-.5, .5, -1.5)).Y);
        Assert.Equal(7, affine.WorldToIndexNodeCentered(new Vec3d(-.5, .5, -1.5)).Y);
    }
    [Fact]
    public void ComposedUnitaryCopiesAndReadbackPreserveNativeAccumulatedRounding()
    {
        MapBase map = new UnitaryMap(); for (int i = 0; i < 1000; ++i) map = map.PostRotate(.21, (Axis)(i % 3));
        MapBase copy = map.Copy(); Assert.Equal(map.Matrix, copy.Matrix);
        using var bytes = new MemoryStream(); Transform.FromMap(map).Write(bytes); bytes.Position = 0; var loaded = new Transform(); loaded.Read(bytes);
        Assert.Equal(nameof(UnitaryMap), loaded.MapType); Assert.Equal(map.Matrix, loaded.Matrix); Assert.Equal(map.ApplyMap(Vec3d.One), loaded.IndexToWorld(Vec3d.One));
    }
    [Theory, InlineData(0), InlineData(24), InlineData(48), InlineData(72), InlineData(96)]
    public void NonfiniteScaleCacheIsRejected(int cacheOffset)
    {
        byte[] wire = Convert.FromHexString(Bases[1].Wire!); int prefix = 4 + nameof(ScaleMap).Length;
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(wire.AsSpan(prefix + cacheOffset), double.NaN);
        using var input = new MemoryStream(wire); var transform = new Transform(); Assert.Throws<InvalidDataException>(() => transform.Read(input)); Assert.Equal(nameof(ScaleMap), transform.MapType); Assert.True(transform.IsIdentity);
    }
    [Fact]
    public void UniformScalePrimaryValuesAndNestedFrustumsAreValidated()
    {
        byte[] wire = Convert.FromHexString(Bases[4].Wire!); int prefix = 4 + nameof(UniformScaleMap).Length;
        System.Buffers.Binary.BinaryPrimitives.WriteDoubleLittleEndian(wire.AsSpan(prefix + 8), 2);
        using var input = new MemoryStream(wire); Assert.Throws<InvalidDataException>(() => new Transform().Read(input));
        using var nested = new MemoryStream(); using (var output = new BinaryWriter(nested, System.Text.Encoding.UTF8, true))
        {
            void Name(string value) { byte[] name = System.Text.Encoding.UTF8.GetBytes(value); output.Write(name.Length); output.Write(name); }
            Name(nameof(NonlinearFrustumMap)); foreach (double value in new[] { 0d, 0d, 0d, 1d, 1d, 1d, 1d, 1d }) output.Write(value); Name(nameof(NonlinearFrustumMap));
        }
        nested.Position = 0; Assert.Throws<InvalidDataException>(() => new Transform().Read(nested));
    }
    [Fact]
    public void InvalidMapParametersAndCorruptInputAreBounded()
    {
        Assert.Throws<ArithmeticException>(() => new ScaleMap(Vec3d.Zero)); Assert.Throws<ArgumentException>(() => new TranslationMap(new Vec3d(double.NaN)));
        Assert.Throws<ArithmeticException>(() => new UnitaryMap(Mat4d.CreateScale(2))); Assert.Throws<ArithmeticException>(() => new UnitaryMap(Mat4d.CreateTranslation(Vec3d.One)));
        Assert.Throws<NotSupportedException>(() => new NonlinearFrustumMap().InverseMap());
        byte[] wire = Convert.FromHexString(Bases[8].Wire!);
        for (int i = 0; i < wire.Length; ++i)
        {
            using var truncated = new MemoryStream(wire[..i]); Exception? failure = Record.Exception(() => new Transform().Read(truncated)); Assert.True(failure is InvalidDataException or EndOfStreamException, failure?.GetType().Name ?? "Accepted truncated map");
        }
        using var limited = new MemoryStream(wire); Assert.Throws<InvalidDataException>(() => new Transform().Read(limited, options: new() { MaxAllocatedBytes = 1 }));
        using var disposed = new MemoryStream(wire); new Transform().Read(disposed, leaveOpen: false); Assert.False(disposed.CanRead);
        using var written = new MemoryStream(); new Transform().Write(written, leaveOpen: false); Assert.False(written.CanWrite);
    }
    [Fact]
    public void ExpandedAffineInverseDeterminantPreservesNativeHalfVoxelBoundary()
    {
        // Native particles-oracle.cpp transform(1), partitionPoints()[97].
        var transform = Transform.CreateLinearTransform(.75);
        transform.PostTranslate(new(.25, -.375, .125)); transform.PostRotate(.2, Axis.Z);
        Assert.Equal("AffineMap", transform.MapType);
        Assert.Equal(1, transform.WorldToIndexCellCentered(new Vec3d(.5)).Z);
    }
}
