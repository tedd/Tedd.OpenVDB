// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Xunit;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbGridTypeTests
{
    public static IEnumerable<object[]> Profiles()
    {
        foreach (string type in new[] { "int64", "bool", "mask", "vec3i", "vec3s", "vec3d" })
            foreach (bool half in new[] { false, true })
            {
                if (half && type is not ("vec3s" or "vec3d")) continue;
                for (int flags = 0; flags < 4; ++flags) yield return [type, flags, half];
            }
    }
    private static string Name(string type, int flags, bool half) => $"{type}-{flags}-{(half ? "half" : "full")}";
    private static string PathFor(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "gridtypes", file + ".vdb");
    private static IGrid Load(string file) { using var stream = File.OpenRead(PathFor(file)); return Assert.Single(VdbFile.ReadArchive(stream).Grids); }

    [Theory, MemberData(nameof(Profiles))]
    public void NativeExtendedGridProfilesReadAndWrite(string type, int flags, bool half)
    {
        IGrid native = Load("native-" + Name(type, flags, half));
        IGrid expected = Load($"expected-{type}-{(half ? "half" : "full")}");
        Compare(expected, native);
        Assert.Equal(new Coord(-1, 2, -3), native.Metadata["integer_vector"].Value);
        Assert.Equal(new Vec3i(-1, 2, -3), native.Metadata["integer_vector"].GetValue<Vec3i>());
        Assert.Equal(new Vec3f(1.25f, -2.5f, 3.75f), native.Metadata["float_vector"].Value);
        if (type == "int64")
        {
            var grid = Assert.IsType<Grid<long>>(native);
            Assert.Equal(long.MinValue, grid.Tree.GetValue(new(1, 2, 3))); Assert.Equal(long.MaxValue, grid.Tree.GetValue(new(3, 2, 1)));
            Assert.Equal(9007199254740994L, grid.Background);
        }
        IGrid source = Load("native-" + Name(type, 0, false));
        using var output = new MemoryStream();
        VdbFile.WriteArchive(output, new VdbArchive([source]), options: new() { Compression = (VdbCompression)flags, SaveFloatAsHalf = half });
        string? directory = Environment.GetEnvironmentVariable("TEDD_OPENVDB_GRIDTYPES_OUTPUT_DIR");
        if (directory is not null) { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "managed-" + Name(type, flags, half) + ".vdb"), output.ToArray()); }
        output.Position = 0; IGrid decoded = Assert.Single(VdbFile.ReadArchive(output).Grids); Compare(expected, decoded);
        Assert.Equal(false, source.Metadata["is_saved_as_half_float"].Value);
    }
    private static void Compare(IGrid expected, IGrid actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType()); Assert.Equal(expected.Name, actual.Name);
        switch (expected)
        {
            case Grid<long> grid: CompareTree(grid.Tree, Assert.IsType<Grid<long>>(actual).Tree); break;
            case Grid<bool> grid: CompareTree(grid.Tree, Assert.IsType<Grid<bool>>(actual).Tree); break;
            case Grid<Vec3i> grid: CompareTree(grid.Tree, Assert.IsType<Grid<Vec3i>>(actual).Tree); break;
            case Grid<Vec3f> grid: CompareTree(grid.Tree, Assert.IsType<Grid<Vec3f>>(actual).Tree); break;
            case Grid<Vec3d> grid: CompareTree(grid.Tree, Assert.IsType<Grid<Vec3d>>(actual).Tree); break;
            case MaskGrid grid: CompareTree(grid.ToBooleanGrid().Tree, Assert.IsType<MaskGrid>(actual).ToBooleanGrid().Tree); break;
            default: throw new InvalidOperationException();
        }
    }
    private static void CompareTree<T>(VdbTree<T> expected, VdbTree<T> actual) where T : unmanaged
    {
        Assert.True(expected.HasSameTopology(actual)); EqualValue(expected.Background, actual.Background);
        Assert.Equal(expected.ActiveVoxelCount, actual.ActiveVoxelCount); Assert.Equal(expected.LeafCount, actual.LeafCount);
        foreach (var value in expected.EnumerateValues(ValueIterationMode.All)) { EqualValue(value.Value, actual.GetValue(value.Bounds.Min)); Assert.Equal(value.IsActive, actual.IsValueOn(value.Bounds.Min)); }
        foreach (var value in actual.EnumerateValues(ValueIterationMode.All)) EqualValue(value.Value, expected.GetValue(value.Bounds.Min));
    }
    private static void EqualValue<T>(T expected, T actual)
    {
        switch (expected)
        {
            case Vec3f vector: var f = (Vec3f)(object)actual!; for (int index = 0; index < 3; ++index) EqualValue(vector[index], f[index]); break;
            case Vec3d vector: var d = (Vec3d)(object)actual!; for (int index = 0; index < 3; ++index) EqualValue(vector[index], d[index]); break;
            case float scalar when float.IsNaN(scalar): Assert.True(float.IsNaN((float)(object)actual!)); break;
            case double scalar when double.IsNaN(scalar): Assert.True(double.IsNaN((double)(object)actual!)); break;
            case float scalar when scalar == 0: Assert.Equal(BitConverter.SingleToInt32Bits(scalar), BitConverter.SingleToInt32Bits((float)(object)actual!)); break;
            case double scalar when scalar == 0: Assert.Equal(BitConverter.DoubleToInt64Bits(scalar), BitConverter.DoubleToInt64Bits((double)(object)actual!)); break;
            default: Assert.Equal(expected, actual); break;
        }
    }

    [Fact]
    public void BooleanValueAndActivityAreIndependentWhileMaskLeafBitsAreCoupled()
    {
        var boolean = new Grid<bool>(); boolean.Tree.SetValueOff(new(1, 2, 3), true); boolean.Tree.SetValueOn(new(3, 2, 1), false);
        using var output = new MemoryStream(); VdbFile.Write(output, boolean, options: new() { Compression = VdbCompression.Zlib | VdbCompression.ActiveMask });
        output.Position = 0; Grid<bool> actual = VdbFile.Read<bool>(output);
        Assert.True(actual.Tree.GetValue(new(1, 2, 3))); Assert.False(actual.Tree.IsValueOn(new(1, 2, 3)));
        Assert.False(actual.Tree.GetValue(new(3, 2, 1))); Assert.True(actual.Tree.IsValueOn(new(3, 2, 1)));
        var mask = new MaskGrid(); mask.SetValueOn(new(1, 2, 3)); mask.SetValueOff(new(3, 2, 1));
        Assert.True(mask.GetValue(new(1, 2, 3))); Assert.True(mask.IsValueOn(new(1, 2, 3)));
        Grid<bool> snapshot = mask.ToBooleanGrid(); snapshot.Tree.SetValueOff(new(1, 2, 3), false); Assert.True(mask.IsValueOn(new(1, 2, 3)));
    }

    [Fact]
    public void MaskGridMaintainsLeafInvariantAndSerializesSharedInstances()
    {
        var grid = new MaskGrid(true) { Name = "mask" }; grid.SetValueOff(new(-1, -2, -3));
        Assert.Equal(511UL, grid.ActiveVoxelCount); Assert.True(grid.IsValueOn(new(-2, -2, -3))); Assert.False(grid.GetValue(new(-1, -2, -3)));
        grid.SetValueOn(new(1, 2, 3)); grid.AddTile(1, new(128, 0, 0), true);
        using var output = new MemoryStream(); VdbFile.WriteArchive(output, new VdbArchive([grid, grid])); output.Position = 0;
        var archive = VdbFile.ReadArchive(output); var a = Assert.IsType<MaskGrid>(archive.Grids[0]); var b = Assert.IsType<MaskGrid>(archive.Grids[1]);
        a.SetValueOff(new(1, 2, 3)); Assert.False(b.IsValueOn(new(1, 2, 3)));
        MaskGrid copy = a.DeepCopy(); copy.SetValueOn(new(-1, -2, -3)); Assert.False(a.IsValueOn(new(-1, -2, -3)));
    }

    [Fact]
    public void Vec3iMetadataRetainsNativeTypeAndCoordinateCompatibility()
    {
        var grid = new Grid<Vec3i>(); grid.SetMetadata("vector", new Vec3i(1, -2, 3));
        Assert.Equal("vec3i", grid.Metadata["vector"].TypeName); Assert.Equal(new Coord(1, -2, 3), grid.GetMetadata<Coord>("vector"));
        Assert.True(grid.TryGetMetadata<Vec3i>("vector", out var vector)); Assert.Equal(new Vec3i(1, -2, 3), vector);
        using var output = new MemoryStream(); VdbFile.Write(output, grid); output.Position = 0;
        Assert.Equal(vector, VdbFile.Read<Vec3i>(output).GetMetadata<Vec3i>("vector"));
    }

    [Fact]
    public void MaskImportRejectsInconsistentLeafValuesWithoutMutatingTheSource()
    {
        var boolean = new Grid<bool>(); boolean.Tree.SetValueOff(new(1, 2, 3), true);
        Assert.Throws<InvalidDataException>(() => MaskGrid.FromBooleanGrid(boolean)); Assert.True(boolean.Tree.GetValue(new(1, 2, 3)));
        boolean.Tree.SetActiveState(new Coord(1, 2, 3), true); MaskGrid mask = MaskGrid.FromBooleanGrid(boolean);
        boolean.Tree.Clear(); Assert.True(mask.IsValueOn(new(1, 2, 3)));
        using var output = new MemoryStream(); VdbFile.Write(output, mask); output.Position = 0;
        Assert.True(VdbFile.ReadMask(output).IsValueOn(new(1, 2, 3)));
    }
    [Theory, InlineData("int64"), InlineData("bool"), InlineData("mask"), InlineData("vec3i")]
    public void HalfEncodingRejectsNonRealValueTypesBeforeWriting(string type)
    {
        IGrid grid = type switch { "int64" => new Grid<long>(), "bool" => new Grid<bool>(), "mask" => new MaskGrid(), _ => new Grid<Vec3i>() };
        using var output = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => VdbFile.WriteArchive(output, new VdbArchive([grid]), options: new() { SaveFloatAsHalf = true })); Assert.Equal(0, output.Length);
    }

    [Theory, InlineData("bool"), InlineData("mask")]
    public void BooleanAndMaskLeafOriginsAreValidated(string type)
    {
        byte[] bytes = File.ReadAllBytes(PathFor("native-" + Name(type, 0, false))); int offsets = DescriptorOffsets(bytes);
        long block = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offsets + 8)); bytes[(int)block + 64] ^= 1;
        Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(new MemoryStream(bytes)));
    }

    [Fact]
    public void InvalidBooleanScalarsAndLargeVectorAllocationsAreRejected()
    {
        byte[] bytes = File.ReadAllBytes(PathFor("native-bool-0-full")); int offsets = DescriptorOffsets(bytes);
        int grid = (int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offsets));
        using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream); stream.Position = grid + 4;
        uint count = reader.ReadUInt32(); for (int index = 0; index < count; ++index) { Skip(reader); Skip(reader); uint size = reader.ReadUInt32(); stream.Position += size; }
        Skip(reader); // native transform map name
        stream.Position += 15 * sizeof(double); // native default UniformScaleMap caches five Vec3d vectors
        stream.Position += 4; bytes[(int)stream.Position] = 2;
        Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(new MemoryStream(bytes)));
        using var vector = File.OpenRead(PathFor("native-vec3d-3-half"));
        Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(vector, options: new() { MaxAllocatedBytes = 4096 }));
    }
    private static int DescriptorOffsets(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream); stream.Position = 65;
        Skip(reader); Skip(reader); Skip(reader); return (int)stream.Position;
    }
    private static void Skip(BinaryReader reader) { uint size = reader.ReadUInt32(); reader.BaseStream.Position += size; }
}
