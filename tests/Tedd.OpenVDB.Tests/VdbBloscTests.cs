// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Xunit;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbBloscTests
{
    public static IEnumerable<object[]> Profiles()
    {
        foreach (string type in new[] { "int32", "float", "double", "int64", "bool", "mask", "vec3i", "vec3s", "vec3d" })
            foreach (bool half in new[] { false, true })
            {
                if (half && type is not ("float" or "double" or "vec3s" or "vec3d")) continue;
                for (int flags = 4; flags < 8; ++flags) yield return [type, flags, half];
            }
    }
    private static string Key(string type, int flags, bool half) => $"{type}-{flags}-{(half ? "half" : "full")}";
    private static string PathFor(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "blosc", name + ".vdb");
    private static IGrid Load(string path) { using var stream = File.OpenRead(path); return Assert.Single(VdbFile.ReadArchive(stream).Grids); }
    private static string Existing(string type, string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", type is "int32" or "float" or "double" ? "compression" : "gridtypes", name + ".vdb");

    [Theory, MemberData(nameof(Profiles))]
    public void NativeBloscValueTypesDecodeAndManagedFilesRoundTrip(string type, int flags, bool half)
    {
        IGrid expected = Load(Existing(type, $"expected-{type}-{(half ? "half" : "full")}"));
        Compare(expected, Load(PathFor("native-" + Key(type, flags, half))));
        IGrid source = Load(Existing(type, "native-" + Key(type, 0, false)));
        using var output = new MemoryStream();
        VdbFile.WriteArchive(output, new VdbArchive([source]), options: new() { Compression = (VdbCompression)flags, SaveFloatAsHalf = half });
        string? directory = Environment.GetEnvironmentVariable("TEDD_OPENVDB_BLOSC_OUTPUT_DIR");
        if (directory is not null) { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, "managed-" + Key(type, flags, half) + ".vdb"), output.ToArray()); }
        output.Position = 0; Compare(expected, Assert.Single(VdbFile.ReadArchive(output).Grids));
    }
    private static void Compare(IGrid expected, IGrid actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType()); Assert.Equal(expected.Name, actual.Name);
        foreach (var pair in expected.Metadata)
            if (pair.Key != "is_saved_as_half_float") Assert.Equal(pair.Value, actual.Metadata[pair.Key]);
        switch (expected)
        {
            case Grid<int> grid: Tree(grid.Tree, Assert.IsType<Grid<int>>(actual).Tree); break;
            case Grid<float> grid: Tree(grid.Tree, Assert.IsType<Grid<float>>(actual).Tree); break;
            case Grid<double> grid: Tree(grid.Tree, Assert.IsType<Grid<double>>(actual).Tree); break;
            case Grid<long> grid: Tree(grid.Tree, Assert.IsType<Grid<long>>(actual).Tree); break;
            case Grid<bool> grid: Tree(grid.Tree, Assert.IsType<Grid<bool>>(actual).Tree); break;
            case Grid<Vec3i> grid: Tree(grid.Tree, Assert.IsType<Grid<Vec3i>>(actual).Tree); break;
            case Grid<Vec3f> grid: Tree(grid.Tree, Assert.IsType<Grid<Vec3f>>(actual).Tree); break;
            case Grid<Vec3d> grid: Tree(grid.Tree, Assert.IsType<Grid<Vec3d>>(actual).Tree); break;
            case MaskGrid grid: Tree(grid.ToBooleanGrid().Tree, Assert.IsType<MaskGrid>(actual).ToBooleanGrid().Tree); break;
        }
    }
    private static void Tree<T>(VdbTree<T> expected, VdbTree<T> actual) where T : unmanaged
    {
        Assert.True(expected.HasSameTopology(actual)); Equal(expected.Background, actual.Background);
        Assert.Equal(expected.ActiveVoxelCount, actual.ActiveVoxelCount); Assert.Equal(expected.LeafCount, actual.LeafCount);
        foreach (var item in expected.EnumerateValues(ValueIterationMode.All)) { Equal(item.Value, actual.GetValue(item.Bounds.Min)); Assert.Equal(item.IsActive, actual.IsValueOn(item.Bounds.Min)); }
        foreach (var item in actual.EnumerateValues(ValueIterationMode.All)) Equal(item.Value, expected.GetValue(item.Bounds.Min));
    }
    private static void Equal<T>(T expected, T actual)
    {
        switch (expected)
        {
            case Vec3f v: var f = (Vec3f)(object)actual!; for (int i = 0; i < 3; ++i) Equal(v[i], f[i]); break;
            case Vec3d v: var d = (Vec3d)(object)actual!; for (int i = 0; i < 3; ++i) Equal(v[i], d[i]); break;
            case float v when float.IsNaN(v): Assert.True(float.IsNaN((float)(object)actual!)); break;
            case double v when double.IsNaN(v): Assert.True(double.IsNaN((double)(object)actual!)); break;
            case float v when v == 0: Assert.Equal(BitConverter.SingleToInt32Bits(v), BitConverter.SingleToInt32Bits((float)(object)actual!)); break;
            case double v when v == 0: Assert.Equal(BitConverter.DoubleToInt64Bits(v), BitConverter.DoubleToInt64Bits((double)(object)actual!)); break;
            default: Assert.Equal(expected, actual); break;
        }
    }

    [Theory]
    [InlineData("lz4-split")]
    [InlineData("lz4-unsplit")]
    [InlineData("lz4-multiblock")]
    [InlineData("lz4-leftover")]
    [InlineData("lz4-plain")]
    [InlineData("memcpy")]
    [InlineData("noise")]
    [InlineData("zlib")]
    public void NativeBloscFrameVariantsDecode(string name) => Compare(Load(PathFor("expected-frame-" + name)), Load(PathFor("frame-" + name)));

    [Theory, InlineData("bitshuffle"), InlineData("blosclz"), InlineData("zstd")]
    public void UnsupportedNativeBloscCodecsOrFiltersAreExplicit(string name)
    {
        using var input = File.OpenRead(PathFor("frame-" + name)); Assert.Throws<NotSupportedException>(() => VdbFile.Read<float>(input));
    }

    [Fact]
    public void WriterActuallyCompressesRepetitiveValuesAndPreservesRawNoise()
    {
        var grid = new Grid<float>(1); grid.Tree.Fill(new CoordBBox(Coord.Zero, new Coord(7, 7, 7)), 7, true);
        grid.Tree.TouchLeaf(Coord.Zero);
        using var raw = new MemoryStream(); VdbFile.Write(raw, grid);
        using var packed = new MemoryStream(); VdbFile.Write(packed, grid, options: new() { Compression = VdbCompression.Blosc });
        Assert.True(packed.Length < raw.Length / 10);
        packed.Position = 0; Tree(grid.Tree, VdbFile.Read<float>(packed).Tree);
        IGrid noise = Load(PathFor("expected-frame-noise"));
        using var noiseFile = new MemoryStream(); VdbFile.WriteArchive(noiseFile, new VdbArchive([noise]), options: new() { Compression = VdbCompression.Blosc | VdbCompression.ActiveMask });
        noiseFile.Position = 0; Compare(noise, Assert.Single(VdbFile.ReadArchive(noiseFile).Grids));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void EmptyCompressedBuffersHonorNativeHalfFraming(bool half)
    {
        var grid = new Grid<float>(10); grid.Tree.TouchLeaf(Coord.Zero);
        using var file = new MemoryStream(); VdbFile.Write(file, grid, options: new() { Compression = VdbCompression.Blosc | VdbCompression.ActiveMask, SaveFloatAsHalf = half });
        file.Position = 0; var decoded = VdbFile.Read<float>(file); Assert.Equal(1, decoded.Tree.LeafCount); Assert.Equal(0UL, decoded.ActiveVoxelCount);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("type-zero")]
    [InlineData("block-zero")]
    [InlineData("block-large")]
    [InlineData("raw-size")]
    [InlineData("compressed-size")]
    [InlineData("reserved")]
    [InlineData("offset-negative")]
    [InlineData("offset-before-table")]
    [InlineData("split-negative")]
    [InlineData("split-zero")]
    [InlineData("split-overrun")]
    [InlineData("trailing")]
    [InlineData("lz4-offset-zero")]
    [InlineData("lz4-literal-overflow")]
    public void CorruptBloscFramesFailWithinTheirOutputBudget(string mutation)
    {
        byte[] file = File.ReadAllBytes(PathFor("frame-lz4-split")); var layout = Layout(file); byte[] frame = file.AsSpan(layout.Frame).ToArray();
        int start = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16));
        switch (mutation)
        {
            case "short": frame = frame[..13]; break;
            case "type-zero": frame[3] = 0; break;
            case "block-zero": Put(frame, 8, 0); break;
            case "block-large": Put(frame, 8, uint.MaxValue); break;
            case "raw-size": Put(frame, 4, uint.MaxValue); break;
            case "compressed-size": Put(frame, 12, uint.MaxValue); break;
            case "reserved": frame[2] |= 8; break;
            case "offset-negative": Put(frame, 16, uint.MaxValue); break;
            case "offset-before-table": Put(frame, 16, 0); break;
            case "split-negative": Put(frame, start, uint.MaxValue); break;
            case "split-zero": Put(frame, start, 0); break;
            case "split-overrun": Put(frame, start, (uint)frame.Length); break;
            case "trailing": Array.Resize(ref frame, frame.Length + 1); Put(frame, 12, (uint)frame.Length); break;
            case "lz4-offset-zero":
                int cursor = start + 4; int literals = frame[cursor++] >> 4;
                if (literals == 15) { byte next; do { next = frame[cursor++]; literals += next; } while (next == 255); }
                cursor += literals; frame[cursor] = frame[cursor + 1] = 0; break;
            case "lz4-literal-overflow": frame[start + 4] = 0xf0; frame[start + 5] = frame[start + 6] = 255; break;
        }
        byte[] corrupted = ReplaceFrame(file, layout, frame);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<float>(new MemoryStream(corrupted)));
    }

    [Theory, InlineData("duplicate"), InlineData("overlap")]
    public void BloscBlocksCannotAliasOrOverlap(string mutation)
    {
        byte[] file = File.ReadAllBytes(PathFor("frame-lz4-multiblock")); var layout = Layout(file); byte[] frame = file.AsSpan(layout.Frame).ToArray();
        uint first = BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(16)); Put(frame, 20, mutation == "duplicate" ? first : first + 1);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<float>(new MemoryStream(ReplaceFrame(file, layout, frame))));
    }

    [Theory, InlineData("frame-version"), InlineData("codec-version")]
    public void UnknownBloscVersionsAreExplicit(string mutation)
    {
        byte[] file = File.ReadAllBytes(PathFor("frame-lz4-split")); var layout = Layout(file); file[layout.Frame + (mutation == "frame-version" ? 0 : 1)] = 3;
        Assert.Throws<NotSupportedException>(() => VdbFile.Read<float>(new MemoryStream(file)));
    }

    [Fact]
    public void BloscAllocationLimitsAndTruncationRemainEnforced()
    {
        byte[] file = File.ReadAllBytes(PathFor("frame-lz4-split"));
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<float>(new MemoryStream(file), options: new() { MaxAllocatedBytes = 4096 }));
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<float>(new MemoryStream(file[..^1])));
        var layout = Layout(file); BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(layout.Frame - 8), long.MaxValue);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<float>(new MemoryStream(file)));
    }
    private readonly record struct FileLayout(int Offsets, int Frame);
    private static FileLayout Layout(byte[] file)
    {
        using var stream = new MemoryStream(file); using var reader = new BinaryReader(stream); stream.Position = 65;
        for (int index = 0; index < 3; ++index) { uint length = reader.ReadUInt32(); stream.Position += length; }
        int offsets = (int)stream.Position; reader.ReadInt64(); int block = checked((int)reader.ReadInt64());
        return new(offsets, block + 65 + 8);
    }
    private static byte[] ReplaceFrame(byte[] file, FileLayout layout, byte[] frame)
    {
        byte[] output = new byte[layout.Frame + frame.Length]; file.AsSpan(0, layout.Frame).CopyTo(output); frame.CopyTo(output.AsSpan(layout.Frame));
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(layout.Frame - 8), frame.Length);
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(layout.Offsets + 16), output.Length); return output;
    }
    private static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
}
