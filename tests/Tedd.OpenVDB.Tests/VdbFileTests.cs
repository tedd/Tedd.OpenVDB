using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB.Tests;

public sealed class VdbFileTests
{
    private static readonly Mat4d Matrix = new(2, .5, 0, 0, 0, 3, .25, 0, 0, 0, 4, 0, 10, -20, 30, 1);
    private static byte[] Fixture(string scalar = "int32", string scenario = "mixed", bool streaming = false) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"native-{scalar}-{scenario}{(streaming ? "-stream" : "")}.vdb"));

    [Theory]
    [InlineData("int32", "empty")]
    [InlineData("float", "empty")]
    [InlineData("double", "empty")]
    [InlineData("int32", "mixed")]
    [InlineData("float", "mixed")]
    [InlineData("double", "mixed")]
    public void ReaderMatchesActualNativeWriterAndManagedWriterPreservesGrid(string scalar, string scenario)
    {
        switch (scalar)
        {
            case "int32": CheckInterchange<int>(scalar, scenario); break;
            case "float": CheckInterchange<float>(scalar, scenario); break;
            case "double": CheckInterchange<double>(scalar, scenario); break;
        }
    }

    private static void CheckInterchange<T>(string scalar, string scenario) where T : unmanaged
    {
        Grid<T> expected = MakeGrid<T>(scenario);
        using var native = new MemoryStream(Fixture(scalar, scenario));
        AssertGrid(expected, VdbFile.Read<T>(native));
        using var managed = new MemoryStream();
        VdbFile.Write(managed, expected);
        managed.Position = 0;
        AssertGrid(expected, VdbFile.Read<T>(managed));
        // Build-IoReference.ps1 enables this export and then independently reads each
        // managed file with the real C++ OpenVDB verifier. Ordinary tests need no native runtime.
        string? export = Environment.GetEnvironmentVariable("TEDD_OPENVDB_NATIVE_OUTPUT_DIR");
        if (!string.IsNullOrEmpty(export))
        {
            Directory.CreateDirectory(export);
            File.WriteAllBytes(Path.Combine(export, $"managed-{scalar}-{scenario}.vdb"), managed.ToArray());
        }
    }

    [Fact]
    public void ReadsNativeStreamingArchiveThroughNonseekableInput()
    {
        using var input = new NonseekableStream(new MemoryStream(Fixture(streaming: true)));
        AssertGrid(MakeGrid<int>("mixed"), VdbFile.Read<int>(input));
    }

    [Fact]
    public void ReadsNativeOffsetArchiveThroughNonseekableInput()
    {
        using var input = new NonseekableStream(new MemoryStream(Fixture()));
        AssertGrid(MakeGrid<int>("mixed"), VdbFile.Read<int>(input));
    }

    [Fact]
    public void WritesNativeStreamingArchiveToNonseekableOutput()
    {
        using var bytes = new MemoryStream();
        using var output = new NonseekableStream(bytes);
        VdbFile.Write(output, MakeGrid<int>("mixed"));
        byte[] file = bytes.ToArray();
        Assert.Equal(0, file[20]);
        using var input = new MemoryStream(file);
        AssertGrid(MakeGrid<int>("mixed"), VdbFile.Read<int>(input));
        string? export = Environment.GetEnvironmentVariable("TEDD_OPENVDB_NATIVE_OUTPUT_DIR");
        if (!string.IsNullOrEmpty(export))
        {
            Directory.CreateDirectory(export);
            File.WriteAllBytes(Path.Combine(export, "managed-int32-mixed-stream.vdb"), file);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadAndWriteHonorStreamOwnership(bool leaveOpen)
    {
        var input = new MemoryStream(Fixture(scenario: "empty"));
        VdbFile.Read<int>(input, leaveOpen);
        Assert.Equal(leaveOpen, input.CanRead);
        var output = new MemoryStream();
        VdbFile.Write(output, MakeGrid<int>("empty"), leaveOpen);
        Assert.Equal(leaveOpen, output.CanWrite);
        input.Dispose(); output.Dispose();
    }

    [Fact]
    public void FailureAlsoHonorsStreamOwnership()
    {
        var input = new MemoryStream(new byte[8]);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(input, false));
        Assert.False(input.CanRead);
        var output = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => VdbFile.Write(output, new Grid<short>(), false));
        Assert.False(output.CanWrite);
    }

    [Fact]
    public void UnsupportedScalarOrMetadataCannotEmitPartialHeader()
    {
        using var output = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => VdbFile.Write(output, new Grid<short>()));
        Assert.Empty(output.ToArray());
        var grid = new Grid<int>(); grid.SetMetadata("name", 123);
        Assert.Throws<NotSupportedException>(() => VdbFile.Write(output, grid));
        Assert.Empty(output.ToArray());
    }

    [Fact]
    public void WriterTruncatesPreviousFileContents()
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[4096]); stream.Position = 0;
        var grid = MakeGrid<int>("empty");
        VdbFile.Write(stream, grid);
        Assert.True(stream.Length < 4096);
        stream.Position = 0; AssertGrid(grid, VdbFile.Read<int>(stream));
    }

    [Fact]
    public void RejectsNonzeroInitialStreamPosition()
    {
        using var input = new MemoryStream(Fixture()); input.Position = 1;
        Assert.Throws<ArgumentException>(() => VdbFile.Read<int>(input));
        using var output = new MemoryStream(); output.WriteByte(1);
        Assert.Throws<ArgumentException>(() => VdbFile.Write(output, new Grid<int>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(20)]
    [InlineData(56)]
    [InlineData(64)]
    [InlineData(120)]
    [InlineData(400)]
    [InlineData(822)]
    public void RejectsTruncatedNativeArchive(int length)
    {
        using var input = new MemoryStream(Fixture(scenario: "empty")[..length]);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(input));
    }

    [Theory]
    [InlineData("magic")]
    [InlineData("boolean")]
    [InlineData("uuid")]
    [InlineData("negative-grid-count")]
    [InlineData("grid-offset")]
    [InlineData("block-offset")]
    [InlineData("end-offset")]
    [InlineData("negative-root-count")]
    [InlineData("unaligned-root")]
    [InlineData("overlapping-masks")]
    [InlineData("raw-value-marker")]
    [InlineData("duplicate-root")]
    [InlineData("leaf-mask-mismatch")]
    public void RejectsCorruptNativeArchive(string corruption)
    {
        byte[] bytes = Fixture(); Layout layout = ParseLayout(bytes);
        switch (corruption)
        {
            case "magic": bytes[0] ^= 0x80; break;
            case "boolean": bytes[20] = 2; break;
            case "uuid": bytes[21] = (byte)'z'; break;
            case "negative-grid-count": Put32(bytes, 61, -1); break;
            case "grid-offset": Put64(bytes, layout.Offsets, layout.Grid + 1); break;
            case "block-offset": Put64(bytes, layout.Offsets + 8, layout.Block + 1); break;
            case "end-offset": Put64(bytes, layout.Offsets + 16, bytes.Length - 1); break;
            case "negative-root-count": Put32(bytes, layout.RootCounts, -1); break;
            case "unaligned-root": Put32(bytes, layout.RootCounts + 8, -4095); break;
            case "overlapping-masks":
                for (int index = 0; index < 4096; ++index) bytes[layout.FirstChildMask + 4096 + index] |= bytes[layout.FirstChildMask + index];
                break;
            case "raw-value-marker": bytes[layout.FirstChildMask + 8192] = 0; break;
            case "duplicate-root": bytes.AsSpan(layout.RootCounts + 8, 12).CopyTo(bytes.AsSpan(layout.RootCounts + 8 + 17)); break;
            case "leaf-mask-mismatch": bytes[layout.Block] ^= 1; break;
        }
        using var input = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(input));
    }

    [Theory]
    [InlineData("format")]
    [InlineData("multiple-grids")]
    [InlineData("file-metadata")]
    [InlineData("compression")]
    [InlineData("mask-compression")]
    [InlineData("dimensions")]
    [InlineData("map")]

    public void RejectsUnsupportedLayoutsExplicitly(string unsupported)
    {
        byte[] bytes = Fixture(); Layout layout = ParseLayout(bytes);
        switch (unsupported)
        {
            case "format": Put32(bytes, 8, 224); break;
            case "multiple-grids": Put32(bytes, 61, 2); break;
            case "file-metadata": Put32(bytes, 57, 1); break;
            case "compression": Put32(bytes, layout.Grid, 16); break;
            case "mask-compression": Put32(bytes, layout.Grid, 8); break;
            case "dimensions": bytes[layout.TypeStart + "Tree_int32_5_4_".Length] = (byte)'2'; break;
            case "map": bytes[layout.MapStart] = (byte)'X'; break;
            case "metadata-type": bytes[layout.FirstMetadataType] = (byte)'X'; break;
        }
        using var input = new MemoryStream(bytes);
        Assert.Throws<NotSupportedException>(() => VdbFile.Read<int>(input));
    }

    [Fact]
    public void RequestedScalarMustMatchNativeDescriptor()
    {
        using var input = new MemoryStream(Fixture("float"));
        Assert.Throws<NotSupportedException>(() => VdbFile.Read<double>(input));
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("allocation")]
    [InlineData("nodes")]
    [InlineData("roots")]
    [InlineData("metadata-items")]
    [InlineData("metadata-bytes")]
    [InlineData("strings")]
    public void ResourceLimitsRejectBeforeUnboundedAllocation(string limit)
    {
        VdbReadOptions options = limit switch
        {
            "bytes" => new() { MaxBytes = 1000 },
            "allocation" => new() { MaxAllocatedBytes = 4000 },
            "nodes" => new() { MaxNodes = 1 },
            "roots" => new() { MaxRootEntries = 1 },
            "metadata-items" => new() { MaxMetadataItems = 1 },
            "metadata-bytes" => new() { MaxMetadataBytes = 1 },
            _ => new() { MaxStringBytes = 2 }
        };
        using var input = new MemoryStream(Fixture());
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(input, options: options));
    }

    [Fact]
    public void ValidatesReadOptions()
    {
        using var input = new MemoryStream(Fixture());
        Assert.Throws<ArgumentOutOfRangeException>(() => VdbFile.Read<int>(input, options: new() { MaxNodes = 0 }));
    }

    private static Grid<T> MakeGrid<T>(string scenario) where T : unmanaged
    {
        var grid = new Grid<T>(Value<T>(-7)) { Name = "oracle_" + scenario, GridClass = GridClass.FogVolume, Transform = new(Matrix) };
        grid.SetMetadata("answer", 42); grid.SetMetadata("enabled", true); grid.SetMetadata("epoch", 1234567890123L);
        grid.SetMetadata("float", 1.25f); grid.SetMetadata("double", 2.5); grid.SetMetadata("caption", "native UTF-8: æøå");
        grid.SetMetadata("origin", new Coord(-1, 2, -3)); grid.SetMetadata("direction", new Vec3d(1.25, -2.5, 3.75)); grid.SetMetadata("matrix", Matrix);
        if (scenario == "empty") return grid;
        var tree = grid.Tree;
        tree.AddTile(3, new(-4096, -4096, -4096), Value<T>(3), true);
        tree.AddTile(3, new(4096, 0, 0), Value<T>(13), false);
        tree.AddTile(2, new(0, 0, 0), Value<T>(5), false);
        tree.AddTile(1, new(128, 0, 0), Value<T>(7), true);
        tree.SetValueOn(new(-1, 0, 1), Value<T>(11)); tree.SetValueOff(new(7, 8, 9), Value<T>(17));
        tree.SetValueOn(new(int.MaxValue, int.MaxValue, int.MaxValue), Value<T>(23));
        tree.SetValueOff(new(int.MinValue, int.MinValue, int.MinValue), Value<T>(29));
        if (typeof(T) != typeof(int))
        {
            tree.SetValueOn(new(1, 2, 3), Value<T>(1.25)); tree.SetValueOff(new(2, 3, 4), Value<T>(-0.0));
            tree.SetValueOn(new(3, 4, 5), Value<T>(double.PositiveInfinity)); tree.SetValueOff(new(4, 5, 6), Value<T>(double.NegativeInfinity));
            tree.SetValueOn(new(5, 6, 7), Value<T>(double.NaN));
        }
        return grid;
    }
    private static T Value<T>(double value) where T : unmanaged => typeof(T) == typeof(int) ? (T)(object)(int)value :
        typeof(T) == typeof(float) ? (T)(object)(float)value : (T)(object)value;
    private static void AssertValue<T>(T expected, T actual) where T : unmanaged
    {
        if (expected is float f && float.IsNaN(f)) { Assert.True(actual is float af && float.IsNaN(af)); return; }
        if (expected is double d && double.IsNaN(d)) { Assert.True(actual is double ad && double.IsNaN(ad)); return; }
        Assert.Equal(expected, actual);
        if (expected.Equals(default(T)))
            Assert.True(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref expected, 1)).SequenceEqual(
                MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref actual, 1))), "Signed zero was not preserved.");
    }
    private static void AssertGrid<T>(Grid<T> expected, Grid<T> actual) where T : unmanaged
    {
        Assert.Equal(expected.Name, actual.Name); Assert.Equal(expected.GridClass, actual.GridClass);
        Assert.Equal(expected.Transform.Matrix, actual.Transform.Matrix);
        Assert.Equal(expected.Metadata.Count, actual.Metadata.Count);
        foreach (var pair in expected.Metadata) Assert.Equal(pair.Value, actual.Metadata[pair.Key]);
        AssertValue(expected.Background, actual.Background);
        Assert.Equal(expected.Tree.LeafCount, actual.Tree.LeafCount);
        Assert.Equal(expected.ActiveVoxelCount, actual.ActiveVoxelCount);
        Assert.True(expected.Tree.HasSameTopology(actual.Tree));
        foreach (VoxelValue<T> entry in expected.Tree.EnumerateValues(ValueIterationMode.All))
        {
            AssertValue(entry.Value, actual.Tree.GetValue(entry.Coordinate));
            Assert.Equal(entry.IsActive, actual.Tree.IsValueOn(entry.Coordinate));
            Assert.Equal(expected.Tree.GetValueDepth(entry.Coordinate), actual.Tree.GetValueDepth(entry.Coordinate));
        }
    }

    private readonly record struct Layout(int Offsets, int Grid, int Block, int TypeStart, int MapStart, int RootCounts, int FirstChildMask, int FirstMetadataType);
    private static Layout ParseLayout(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream);
        static string ReadText(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes((int)reader.ReadUInt32()));
        stream.Position = 65; ReadText(reader); int typeStart = (int)stream.Position + 4; ReadText(reader); ReadText(reader);
        int offsets = (int)stream.Position; int grid = (int)reader.ReadInt64(); int block = (int)reader.ReadInt64(); reader.ReadInt64();
        stream.Position = grid + 4; uint metadata = reader.ReadUInt32(); int firstMetadataType = 0;
        for (int index = 0; index < metadata; ++index)
        {
            ReadText(reader); if (index == 0) firstMetadataType = (int)stream.Position + 4; ReadText(reader);
            uint size = reader.ReadUInt32(); stream.Position += size;
        }
        int mapStart = (int)stream.Position + 4; ReadText(reader); stream.Position += 128 + 4 + 4;
        int rootCounts = (int)stream.Position; uint tiles = reader.ReadUInt32(); reader.ReadUInt32();
        int firstChildMask = checked((int)(stream.Position + tiles * 17 + 12));
        return new(offsets, grid, block, typeStart, mapStart, rootCounts, firstChildMask, firstMetadataType);
    }
    private static void Put32(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    private static void Put64(byte[] bytes, int offset, long value) => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset, 8), value);

    private sealed class NonseekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
