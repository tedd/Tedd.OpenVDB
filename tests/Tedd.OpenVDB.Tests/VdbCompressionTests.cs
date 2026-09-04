using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class VdbCompressionTests
{
    public static IEnumerable<object[]> Profiles()
    {
        foreach (string scalar in new[] { "int32", "float", "double" })
            foreach (bool half in new[] { false, true })
                foreach (uint flags in new uint[] { 0, 1, 2, 3 })
                    if (!half || scalar != "int32") yield return new object[] { scalar, flags, half };
    }
    private static string Name(string scalar, uint flags, bool half) => $"{scalar}-{flags}-{(half ? "half" : "full")}";
    private static byte[] Native(string scalar, uint flags, bool half) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "compression", $"native-{Name(scalar, flags, half)}.vdb"));
    private static byte[] Expected(string scalar, bool half) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "compression", $"expected-{scalar}-{(half ? "half" : "full")}.vdb"));

    [Theory]
    [MemberData(nameof(Profiles))]
    public void NativeCompressedAndHalfFilesMatchNativeCanonicalValues(string scalar, uint flags, bool half)
    {
        switch (scalar)
        {
            case "int32": Check<int>(scalar, flags, half); break;
            case "float": Check<float>(scalar, flags, half); break;
            case "double": Check<double>(scalar, flags, half); break;
        }
    }
    private static void Check<T>(string scalar, uint flags, bool half) where T : unmanaged
    {
        using var expectation = new MemoryStream(Expected(scalar, half));
        Grid<T> expected = VdbFile.Read<T>(expectation);
        using var native = new MemoryStream(Native(scalar, flags, half));
        Grid<T> actual = VdbFile.Read<T>(native);
        AssertGrid(expected, actual);
        Assert.Equal(half, actual.GetMetadata<bool>("is_saved_as_half_float"));
        using var original = new MemoryStream(Native(scalar, 0, false));
        Grid<T> source = VdbFile.Read<T>(original);
        T background = source.Background;
        using var encoded = new MemoryStream();
        VdbFile.Write(encoded, source, options: new() { Compression = (VdbCompression)flags, SaveFloatAsHalf = half });
        Assert.False(source.GetMetadata<bool>("is_saved_as_half_float"));
        Assert.Equal(background, source.Background);
        encoded.Position = 0;
        actual = VdbFile.Read<T>(encoded);
        AssertGrid(expected, actual);
        Assert.Equal(half, actual.GetMetadata<bool>("is_saved_as_half_float"));
        string? export = Environment.GetEnvironmentVariable("TEDD_OPENVDB_COMPRESSION_OUTPUT_DIR");
        if (!string.IsNullOrEmpty(export))
        {
            Directory.CreateDirectory(export);
            File.WriteAllBytes(Path.Combine(export, $"managed-{Name(scalar, flags, half)}.vdb"), encoded.ToArray());
        }
    }
    private static void AssertGrid<T>(Grid<T> expected, Grid<T> actual) where T : unmanaged
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Transform.Matrix, actual.Transform.Matrix);
        Assert.Equal(expected.Tree.LeafCount, actual.Tree.LeafCount);
        Assert.Equal(expected.ActiveVoxelCount, actual.ActiveVoxelCount);
        Assert.True(expected.Tree.HasSameTopology(actual.Tree));
        EqualScalar(expected.Background, actual.Background);
        foreach (var item in expected.Tree.EnumerateValues(ValueIterationMode.All))
        {
            EqualScalar(item.Value, actual.Tree.GetValue(item.Coordinate));
            Assert.Equal(item.IsActive, actual.Tree.IsValueOn(item.Coordinate));
            Assert.Equal(expected.Tree.GetValueDepth(item.Coordinate), actual.Tree.GetValueDepth(item.Coordinate));
        }
    }
    private static void EqualScalar<T>(T expected, T actual) where T : unmanaged
    {
        if (expected is float f && float.IsNaN(f)) { Assert.True(actual is float v && float.IsNaN(v)); return; }
        if (expected is double d && double.IsNaN(d)) { Assert.True(actual is double v && double.IsNaN(v)); return; }
        Assert.Equal(expected, actual);
        if (expected.Equals(default(T)))
            Assert.True(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref expected, 1)).SequenceEqual(
                MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref actual, 1))), "Signed zero differs.");
    }

    [Fact]
    public void NativeCorpusExercisesAllSevenMaskMetadataCases()
    {
        byte[] bytes = Native("int32", 2, false); var layout = LayoutOf(bytes);
        using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream);
        stream.Position = layout.Block;
        var codes = new List<byte>();
        for (int leaf = 0; leaf < 10; ++leaf)
        {
            int active = 0;
            for (int word = 0; word < 8; ++word) active += BitOperations.PopCount(reader.ReadUInt64());
            byte code = reader.ReadByte(); codes.Add(code);
            if (code is 2 or 4 or 5) stream.Position += 4;
            if (code == 5) stream.Position += 4;
            if (code is 3 or 4 or 5) stream.Position += 64;
            stream.Position += (code == 6 ? 512 : active) * 4;
        }
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6 }, codes.Take(7));
    }

    [Fact]
    public void HalfMetadataCanEnableEncodingAndOptionsCanOverrideIt()
    {
        var grid = new Grid<double>(10.123); grid.SetMetadata("is_saved_as_half_float", true);
        grid[new Coord(1, 2, 3)] = 1.000488281250001;
        using var half = new MemoryStream(); VdbFile.Write(half, grid); half.Position = 0;
        var decoded = VdbFile.Read<double>(half);
        Assert.Equal((double)(float)(Half)(float)10.123, decoded.Background);
        Assert.Equal((double)(float)(Half)(float)1.000488281250001, decoded[new Coord(1, 2, 3)]);
        using var full = new MemoryStream(); VdbFile.Write(full, grid, options: new() { SaveFloatAsHalf = false }); full.Position = 0;
        decoded = VdbFile.Read<double>(full); Assert.Equal(10.123, decoded.Background);
        Assert.False(decoded.GetMetadata<bool>("is_saved_as_half_float"));
        Assert.True(grid.GetMetadata<bool>("is_saved_as_half_float"));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(3u)]
    public void EmptyActiveBuffersRespectHalfAndZlibFraming(uint flags)
    {
        var grid = new Grid<float>(10); grid.Tree.TouchLeaf(new Coord(0, 0, 0));
        using var file = new MemoryStream();
        VdbFile.Write(file, grid, options: new() { Compression = (VdbCompression)flags, SaveFloatAsHalf = true });
        file.Position = 0; var result = VdbFile.Read<float>(file);
        Assert.Equal(1, result.Tree.LeafCount); Assert.Equal(0UL, result.ActiveVoxelCount); Assert.Equal(10, result[new Coord(0, 0, 0)]);
    }

    [Theory]
    [InlineData(16u)]
    [InlineData(8u)]
    [InlineData(uint.MaxValue)]
    public void RejectsUnsupportedCompressionBeforeWriting(uint flags)
    {
        using var file = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => VdbFile.Write(file, new Grid<int>(), options: new() { Compression = (VdbCompression)flags }));
        Assert.Empty(file.ToArray());
    }
    [Fact]
    public void RejectsHalfForIntegerGridsBeforeWriting()
    {
        using var file = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => VdbFile.Write(file, new Grid<int>(), options: new() { SaveFloatAsHalf = true }));
        Assert.Empty(file.ToArray());
    }
    private static byte[] ZippedLeaf()
    {
        var grid = new Grid<int>(10); grid.Tree.TouchLeaf(new Coord(0, 0, 0));
        using var file = new MemoryStream(); VdbFile.Write(file, grid, options: new() { Compression = VdbCompression.Zlib });
        return file.ToArray();
    }

    [Theory]
    [InlineData("negative-size")]
    [InlineData("minimum-size")]
    [InlineData("huge-size")]
    [InlineData("checksum")]
    [InlineData("header")]
    [InlineData("mask-code")]
    [InlineData("expanded-size")]
    [InlineData("missing-trailer")]
    public void RejectsMalformedCompressedBuffers(string corruption)
    {
        byte[] file = ZippedLeaf(); var layout = LayoutOf(file);
        int marker = layout.Block + 64, lengthOffset = marker + 1;
        long length = BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(lengthOffset));
        Assert.True(length > 0);
        switch (corruption)
        {
            case "negative-size": BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(lengthOffset), -2047); break;
            case "minimum-size": BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(lengthOffset), long.MinValue); break;
            case "huge-size": BinaryPrimitives.WriteInt64LittleEndian(file.AsSpan(lengthOffset), int.MaxValue); break;
            case "checksum": file[^1] ^= 0x80; break;
            case "header": file[lengthOffset + 8] = 0; break;
            case "mask-code": file[marker] = 7; break;
            case "expanded-size":
                using (var zipped = new MemoryStream())
                {
                    using (var writer = new ZLibStream(zipped, CompressionLevel.Optimal, true)) writer.Write(new byte[4096]);
                    file = ReplaceChunk(file, layout, lengthOffset, zipped.ToArray());
                }
                break;
            case "missing-trailer":
                file = ReplaceChunk(file, layout, lengthOffset, file.AsSpan(lengthOffset + 8, (int)length - 4).ToArray());
                break;
        }
        using var input = new MemoryStream(file);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(input));
    }
    [Fact]
    public void CompressedInputHonorsAllocationBudgetBeforeChunkAllocation()
    {
        using var input = new MemoryStream(ZippedLeaf());
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(input, options: new() { MaxAllocatedBytes = 10_000 }));
    }
    private static byte[] ReplaceChunk(byte[] file, FileLayout layout, int lengthOffset, byte[] payload)
    {
        var result = new byte[lengthOffset + 8 + payload.Length];
        file.AsSpan(0, lengthOffset).CopyTo(result);
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(lengthOffset), payload.Length);
        payload.CopyTo(result, lengthOffset + 8);
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(layout.Offsets + 16), result.Length);
        return result;
    }
    private readonly record struct FileLayout(int Offsets, int Block);
    private static FileLayout LayoutOf(byte[] file)
    {
        using var stream = new MemoryStream(file); using var reader = new BinaryReader(stream);
        stream.Position = 65;
        for (int text = 0; text < 3; ++text) { uint length = reader.ReadUInt32(); stream.Position += length; }
        int offsets = (int)stream.Position; reader.ReadInt64(); int block = (int)reader.ReadInt64();
        return new(offsets, block);
    }
}
