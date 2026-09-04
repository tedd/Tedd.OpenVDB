using System.Buffers.Binary;
using System.Text;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;

namespace Tedd.OpenVDB.Tests;

public class VdbFileReviewTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(1)]
    [InlineData("true")]
    public void WriterRejectsHalfFloatMetadataBeforeEmittingAnyBytes(object flag)
    {
        var grid = new Grid<float>();
        grid.SetMetadata("is_saved_as_half_float", flag);
        grid[new Coord(1, 2, 3)] = 123.5f;
        using var stream = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => VdbFile.Write(stream, grid));
        Assert.Equal(0, stream.Length);
    }
    [Fact]
    public void ReaderRejectsHalfFloatMetadataEvenWithoutDescriptorSuffix()
    {
        var grid = new Grid<float>();
        grid.SetMetadata("is_saved_as_half_float", false);
        byte[] bytes = Serialize(grid);
        Locations locations = Locate(bytes);
        int payload = locations.Metadata["is_saved_as_half_float"].Payload;
        bytes[payload] = 1;
        Assert.Throws<NotSupportedException>(() => VdbFile.Read<float>(new MemoryStream(bytes)));
    }
    [Fact]
    public void EveryTruncatedPrefixOfAnEmptyGridFails()
    {
        byte[] complete = Serialize(new Grid<int> { Name = "test" });
        for (int length = 0; length < complete.Length; ++length)
        {
            using var stream = new MemoryStream(complete, 0, length, writable: false);
            Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(stream));
        }
    }
    [Fact]
    public void StreamingInputRejectsTruncationWithoutRelyingOnFileOffsets()
    {
        byte[] complete = Serialize(new Grid<int> { Name = "stream" });
        Locations locations = Locate(complete);
        complete[20] = 0;
        complete.AsSpan(locations.OffsetTable, 24).Clear();
        for (int length = 0; length < complete.Length; ++length)
        {
            using var stream = new ForwardOnlyStream(complete.AsSpan(0, length).ToArray());
            Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(stream));
        }
        using var valid = new ForwardOnlyStream(complete);
        Assert.Equal("stream", VdbFile.Read<int>(valid).Name);
    }
    [Fact]
    public void ForgedLengthsCountsAndOffsetsAreRejectedBeforeAllocation()
    {
        byte[] complete = Serialize(new Grid<int>());
        Locations locations = Locate(complete);
        AssertInvalidMutation(complete, 65, uint.MaxValue); // Descriptor name byte length.
        AssertInvalidMutation(complete, locations.MetadataCount, uint.MaxValue);
        AssertInvalidMutation(complete, locations.RootTileCount, uint.MaxValue);
        AssertInvalidMutation(complete, locations.RootTileCount + 4, uint.MaxValue);
        byte[] negativeOffset = (byte[])complete.Clone();
        BinaryPrimitives.WriteInt64LittleEndian(negativeOffset.AsSpan(locations.OffsetTable), -1);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(new MemoryStream(negativeOffset)));
        byte[] overlappingOffset = (byte[])complete.Clone();
        BinaryPrimitives.WriteInt64LittleEndian(overlappingOffset.AsSpan(locations.OffsetTable + 8), 1);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(new MemoryStream(overlappingOffset)));
    }
    [Fact]
    public void MetadataTypeSizesUtf8AndDescriptorNameMustAgree()
    {
        var grid = new Grid<int> { Name = "grid" };
        grid.SetMetadata("flag", true);
        byte[] complete = Serialize(grid);
        Locations locations = Locate(complete);
        AssertInvalidMutation(complete, locations.Metadata["flag"].Size, 2);
        byte[] invalidBoolean = (byte[])complete.Clone();
        invalidBoolean[locations.Metadata["flag"].Payload] = 2;
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(new MemoryStream(invalidBoolean)));
        byte[] mismatchedName = (byte[])complete.Clone();
        mismatchedName[locations.Metadata["name"].Payload] = (byte)'x';
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(new MemoryStream(mismatchedName)));
        byte[] invalidUtf8 = (byte[])complete.Clone();
        invalidUtf8[locations.Metadata["name"].Payload] = 0xff;
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(new MemoryStream(invalidUtf8)));
    }
    [Fact]
    public void RootOriginsMasksAndLeafMaskCopiesAreValidated()
    {
        var tiled = new Grid<int>();
        tiled.Tree.AddTile(3, Coord.Zero, 17, true);
        byte[] tileBytes = Serialize(tiled);
        Locations tileLocations = Locate(tileBytes);
        AssertInvalidMutation(tileBytes, tileLocations.RootTileCount + 8, 1); // Root origin X must align to 4096.

        var leaf = new Grid<int>();
        leaf[Coord.Zero] = 42;
        byte[] complete = Serialize(leaf);
        Locations locations = Locate(complete);
        int upperMask = locations.RootTileCount + 8 + 12;
        byte[] overlap = (byte[])complete.Clone();
        overlap[upperMask + 4096] |= 1; // Upper active mask overlaps its first child bit.
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(new MemoryStream(overlap)));
        byte[] differentLeafMask = (byte[])complete.Clone();
        long blockOffset = BinaryPrimitives.ReadInt64LittleEndian(complete.AsSpan(locations.OffsetTable + 8));
        differentLeafMask[checked((int)blockOffset)] ^= 1;
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(new MemoryStream(differentLeafMask)));
    }
    [Fact]
    public void ResourceLimitsAndStreamOwnershipApplyOnFailure()
    {
        var grid = new Grid<int>();
        grid[Coord.Zero] = 42;
        byte[] bytes = Serialize(grid);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(new MemoryStream(bytes), options: new() { MaxAllocatedBytes = 64 }));
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(new MemoryStream(bytes), options: new() { MaxNodes = 1 }));
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(new MemoryStream(bytes), options: new() { MaxBytes = 64 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => VdbFile.Read<int>(new MemoryStream(bytes), options: new() { MaxMetadataItems = 0 }));
        var owned = new MemoryStream(new byte[2]);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(owned, leaveOpen: false));
        Assert.False(owned.CanRead);
        var retained = new MemoryStream(new byte[2]);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(retained));
        Assert.True(retained.CanRead);
        retained.Dispose();
    }
    [Fact]
    public void WriterTruncatesReusedSeekableStreamsAndLeavesRequestedOwnership()
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[8192]);
        stream.Position = 0;
        var grid = new Grid<double> { Name = "small" };
        VdbFile.Write(stream, grid);
        Assert.InRange(stream.Length, 1, 8191);
        Assert.Equal(stream.Length, stream.Position);
        stream.Position = 0;
        Assert.Equal("small", VdbFile.Read<double>(stream).Name);
        Assert.True(stream.CanRead);
    }
    private static byte[] Serialize<T>(Grid<T> grid) where T : unmanaged
    {
        using var stream = new MemoryStream();
        VdbFile.Write(stream, grid);
        return stream.ToArray();
    }
    private static void AssertInvalidMutation(byte[] source, int offset, uint value)
    {
        byte[] bytes = (byte[])source.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<int>(new MemoryStream(bytes)));
    }
    private sealed record Locations(int OffsetTable, int MetadataCount, int RootTileCount, Dictionary<string, (int Size, int Payload)> Metadata);
    private static Locations Locate(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        reader.BaseStream.Position = 65;
        ReadString(reader); ReadString(reader); ReadString(reader);
        int offsets = (int)reader.BaseStream.Position;
        reader.BaseStream.Position += 24 + 4; // Descriptor offsets and compression flags.
        int metadataCount = (int)reader.BaseStream.Position;
        uint count = reader.ReadUInt32();
        var metadata = new Dictionary<string, (int Size, int Payload)>();
        for (uint i = 0; i < count; ++i)
        {
            string name = ReadString(reader);
            ReadString(reader);
            int sizePosition = (int)reader.BaseStream.Position;
            uint size = reader.ReadUInt32();
            metadata.Add(name, (sizePosition, (int)reader.BaseStream.Position));
            reader.BaseStream.Position += size;
        }
        Assert.Equal("AffineMap", ReadString(reader));
        reader.BaseStream.Position += 128 + 4 + 4; // Matrix, one buffer, scalar background (int/float fixtures).
        return new(offsets, metadataCount, (int)reader.BaseStream.Position, metadata);
    }
    private static string ReadString(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(checked((int)reader.ReadUInt32())));
    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
