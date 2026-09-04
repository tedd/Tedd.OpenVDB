// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Text;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbFileReaderTests
{
    private static byte[] Archive(bool instances = false, bool streaming = false)
    {
        var populated = new Grid<int>(9) { Name = "z_populated" }; populated.Tree.SetValueOn(new(-1, 2, 3), 42);
        var empty = instances ? new Grid<int>(populated.Tree) { Name = "a_empty" } : new Grid<int>(17) { Name = "a_empty" };
        empty.Transform = Transform.CreateLinearTransform(2);
        using var stream = new TrackedStream([], !streaming); VdbFile.WriteArchive(stream, new VdbArchive([populated, empty], new MetaMap { }), options: new() { Compression = VdbCompression.None }); return stream.Bytes;
    }
    private readonly record struct Descriptor(string Name, int NamePosition, int TypePosition, int OffsetPosition, long Grid, long Blocks, long End);
    private static Descriptor[] Descriptors(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream, Encoding.UTF8); stream.Position = 57;
        int fields = reader.ReadInt32(); for (int i = 0; i < fields; ++i) { String(reader); String(reader); stream.Position += reader.ReadInt32(); }
        int count = reader.ReadInt32(); var result = new Descriptor[count];
        for (int i = 0; i < count; ++i)
        {
            int nameAt = (int)stream.Position + 4; string name = String(reader); int typeAt = (int)stream.Position + 4; String(reader); String(reader);
            int offsets = (int)stream.Position; long grid = reader.ReadInt64(), block = reader.ReadInt64(), end = reader.ReadInt64(); result[i] = new(name, nameAt, typeAt, offsets, grid, block, end); stream.Position = end;
        }
        return result;
    }
    private static string String(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
    [Fact]
    public void IndexAndMetadataSkipTopologyAndCorruptUnselectedLeafBuffers()
    {
        byte[] bytes = Archive(); var first = Descriptors(bytes)[0]; bytes[(int)first.Blocks] ^= 1;
        using var stream = new TrackedStream(bytes); using var reader = VdbFile.OpenRead(stream, options: new() { MaxNodes = 1, MaxAllocatedBytes = 64 * 1024 });
        Assert.True(reader.IsRandomAccess); Assert.Equal(new[] { "a_empty", "z_populated" }, reader.GridNames);
        Assert.True(reader.HasGrid("z_populated")); Assert.Equal(0UL, reader.ReadGridMetadata("z_populated").ActiveVoxelCount);
        Assert.Equal(0UL, reader.ReadGrid<int>("a_empty").ActiveVoxelCount);
        Assert.DoesNotContain(stream.ReadRanges, range => range.Start < first.End && range.End > first.Blocks);
        Assert.Throws<InvalidDataException>(() => reader.ReadGrid("z_populated"));
    }
    [Fact]
    public void IndexingDoesNotDecodeCompressionOrTransformsInUnselectedGrids()
    {
        byte[] bytes = Archive(); var first = Descriptors(bytes)[0]; BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)first.Grid), uint.MaxValue);
        using var reader = VdbFile.OpenRead(new MemoryStream(bytes)); Assert.Equal(17, reader.ReadGrid<int>("a_empty").Background);
        Assert.Throws<NotSupportedException>(() => reader.ReadGridMetadata("z_populated"));
    }
    [Fact]
    public void MetadataBackgroundMatchesNativeRandomAccessVersusStreaming()
    {
        foreach (bool streaming in new[] { false, true })
        {
            using var reader = VdbFile.OpenRead(new MemoryStream(Archive(streaming: streaming)));
            Assert.Equal(streaming ? 9 : 0, ((Grid<int>)reader.ReadGridMetadata("z_populated")).Background);
            Assert.All(reader.ReadAllGridMetadata(), grid => Assert.Equal(0UL, grid.ActiveVoxelCount));
        }
    }
    [Fact]
    public void SelectedReadsHaveNativeRandomAccessAndCachedIdentity()
    {
        foreach (bool streaming in new[] { false, true })
        {
            using var reader = VdbFile.OpenRead(new MemoryStream(Archive(streaming: streaming)));
            var a = reader.ReadGrid<int>("z_populated"); var b = reader.ReadGrid<int>("z_populated");
            Assert.Equal(streaming, ReferenceEquals(a, b)); Assert.Equal(streaming, ReferenceEquals(a.Tree, b.Tree));
            a.Tree.SetValueOn(Coord.Zero, 99); Assert.Equal(streaming ? 99 : 9, b.Tree.GetValue(Coord.Zero));
        }
    }
    [Fact]
    public void GetGridsPreservesNativeNameOrderAndSharedTrees()
    {
        foreach (bool streaming in new[] { false, true })
        {
            using var reader = VdbFile.OpenRead(new MemoryStream(Archive(instances: true, streaming: streaming)));
            var grids = reader.GetGrids(); Assert.Equal(streaming ? "z_populated" : "a_empty", grids[0].Name);
            Assert.Same(((Grid<int>)grids[0]).Tree, ((Grid<int>)grids[1]).Tree);
            var selected = reader.ReadGrid<int>("a_empty"); Assert.Equal(42, selected.Tree.GetValue(new(-1, 2, 3))); Assert.Equal(2, selected.Transform.VoxelSize.X);
        }
    }
    [Fact]
    public void RegionalInstanceReadsUseInstanceTransformAndDoNotChangeCachedParent()
    {
        foreach (bool streaming in new[] { false, true })
        {
            using var reader = VdbFile.OpenRead(new MemoryStream(Archive(instances: true, streaming: streaming)));
            var grid = reader.ReadGrid<int>("a_empty", new BBoxd(new Vec3d(-2, 4, 6), new Vec3d(-2, 4, 6)));
            Assert.Equal(1UL, grid.ActiveVoxelCount); Assert.Equal(42, grid.Tree.GetValue(new(-1, 2, 3)));
            Assert.Equal(1UL, reader.ReadGrid<int>("z_populated").ActiveVoxelCount);
        }
    }
    [Fact]
    public void NonseekableOffsetFilesUseExplicitEagerFallback()
    {
        using var stream = new TrackedStream(Archive(), seekable: false); using var reader = VdbFile.OpenRead(stream);
        Assert.False(reader.IsRandomAccess); Assert.Null(reader.Size); Assert.Equal(42, reader.ReadGrid<int>("z_populated").Tree.GetValue(new(-1, 2, 3)));
        Assert.Throws<InvalidDataException>(() => VdbFile.OpenRead(new TrackedStream(Archive(), false), options: new() { MaxNodes = 1 }));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LeaveOpenAndDisposalAreConsistent(bool leaveOpen)
    {
        using var stream = new TrackedStream(Archive()); var reader = VdbFile.OpenRead(stream, leaveOpen); Assert.True(reader.IsOpen);
        reader.Dispose(); reader.Dispose(); Assert.False(reader.IsOpen); Assert.Equal(!leaveOpen, stream.WasDisposed);
        Assert.Throws<ObjectDisposedException>(() => reader.HasGrid("a_empty")); Assert.Throws<ObjectDisposedException>(() => reader.GetMetadata()); Assert.Throws<ObjectDisposedException>(() => reader.GridNames);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FailedOpeningHonorsOwnership(bool leaveOpen)
    {
        using var stream = new TrackedStream([0, 1, 2]); Assert.Throws<InvalidDataException>(() => VdbFile.OpenRead(stream, leaveOpen)); Assert.Equal(!leaveOpen, stream.WasDisposed);
    }
    [Fact]
    public void MissingAndTypedLookupFailuresDoNotPoisonTheReader()
    {
        using var reader = VdbFile.OpenRead(new MemoryStream(Archive())); Assert.False(reader.HasGrid("missing"));
        Assert.Throws<KeyNotFoundException>(() => reader.ReadGrid("missing")); Assert.Throws<KeyNotFoundException>(() => reader.ReadGridMetadata("missing")); Assert.Throws<InvalidCastException>(() => reader.ReadGrid<float>("a_empty"));
        Assert.Equal(17, reader.ReadGrid<int>("a_empty").Background);
    }
    [Fact]
    public void BudgetFailuresArePerOperationAndRetainAUsableIndex()
    {
        using var reader = VdbFile.OpenRead(new MemoryStream(Archive()), options: new() { MaxAllocatedBytes = 64 * 1024, MaxNodes = 1 });
        for (int i = 0; i < 12; ++i)
        {
            Assert.Throws<InvalidDataException>(() => reader.ReadGrid("z_populated")); Assert.Equal(0UL, reader.ReadGridMetadata("z_populated").ActiveVoxelCount); Assert.Equal(17, reader.ReadGrid<int>("a_empty").Background);
        }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void MalformedOffsetsAndDescriptorsAreRejectedBeforePayloadReads(int corruption)
    {
        byte[] bytes = Archive(); Descriptor first = Descriptors(bytes)[0];
        switch (corruption)
        {
            case 0: BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(first.OffsetPosition), -1); break;
            case 1: BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(first.OffsetPosition + 8), first.Grid - 1); break;
            case 2: BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(first.OffsetPosition + 16), bytes.Length + 1L); break;
            case 3: BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(first.OffsetPosition + 16), first.Grid - 1); break;
            case 4: BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(first.OffsetPosition), first.Grid + 1); break;
            case 5: BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(first.NamePosition - 4), uint.MaxValue); break;
            case 6: bytes[first.NamePosition] = 255; break;
            case 7: bytes = [.. bytes, 0]; break;
        }
        Assert.Throws<InvalidDataException>(() => VdbFile.OpenRead(new MemoryStream(bytes)));
    }
    [Fact]
    public void UnknownGridTypeIsRejectedDuringIndexingEvenIfUnselected()
    {
        byte[] bytes = Archive(); Descriptor first = Descriptors(bytes)[0]; bytes[first.TypePosition] = (byte)'X';
        Assert.Throws<NotSupportedException>(() => VdbFile.OpenRead(new MemoryStream(bytes)));
    }
    [Fact]
    public void EveryTruncatedEmptyFilePrefixIsRejected()
    {
        using var source = new MemoryStream(); VdbFile.Write(source, new Grid<int>()); byte[] bytes = source.ToArray();
        for (int length = 0; length < bytes.Length; ++length) Assert.Throws<InvalidDataException>(() => VdbFile.OpenRead(new MemoryStream(bytes, 0, length, false)));
    }
    [Fact]
    public void FiniteRegionValidationAndUnsortedNativeSentinelAreExplicit()
    {
        using var reader = VdbFile.OpenRead(new MemoryStream(Archive())); Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadGrid("z_populated", new BBoxd(new Vec3d(0), new Vec3d(double.PositiveInfinity))));
        Assert.Equal(1UL, reader.ReadGrid("z_populated", BBoxd.Empty).ActiveVoxelCount);
        Assert.Equal(1UL, reader.ReadGrid("z_populated", new BBoxd(new Vec3d(1), new Vec3d(-1))).ActiveVoxelCount);
    }
    [Fact]
    public void ChangedLengthIsDetectedAndExternalPositionChangesAreResetBySeeking()
    {
        using var stream = new TrackedStream(Archive()); using var reader = VdbFile.OpenRead(stream); stream.Position = 0; Assert.Equal(17, reader.ReadGrid<int>("a_empty").Background);
        stream.SetLength(stream.Length + 1); Assert.Throws<InvalidDataException>(() => reader.ReadGrid("a_empty"));
    }
    [Fact]
    public async Task ConcurrentReadCallsSerializeStreamAccessAndReturnIndependentTrees()
    {
        using var reader = VdbFile.OpenRead(new MemoryStream(Archive())); Grid<int>[] grids = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => reader.ReadGrid<int>("z_populated"))));
        Assert.All(grids, grid => Assert.Equal(42, grid.Tree.GetValue(new(-1, 2, 3)))); Assert.Equal(12, grids.Select(grid => grid.Tree).Distinct(ReferenceEqualityComparer.Instance).Count());
    }
    [Theory]
    [InlineData("cycle", false)]
    [InlineData("cycle", true)]
    [InlineData("self", false)]
    [InlineData("self", true)]
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    [InlineData("duplicate", false)]
    [InlineData("duplicate", true)]
    [InlineData("type-mismatch", false)]
    [InlineData("type-mismatch", true)]
    public void InvalidNativeInstanceGraphsAreRejected(string scenario, bool seekable)
    {
        string file = Path.Combine(AppContext.BaseDirectory, "Fixtures", "instances", $"native-{scenario}-{(seekable ? "seek" : "stream")}.vdb");
        Assert.Throws<InvalidDataException>(() => VdbFile.OpenRead(file));
    }
    [Theory]
    [InlineData("half-child", false)]
    [InlineData("half-child", true)]
    [InlineData("half-parent", false)]
    [InlineData("half-parent", true)]
    public void NativeInstanceHalfFlagsMayDifferFromParent(string scenario, bool seekable)
    {
        using var reader = VdbFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "instances", $"native-{scenario}-{(seekable ? "seek" : "stream")}.vdb"));
        Assert.Equal(42, reader.ReadGrid<float>("a").Tree.GetValue(new(1, 2, 3)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeDuplicateNamesUseUniqueSuffixesAndStreamingLookupIsExact(bool seekable)
    {
        using var reader = VdbFile.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "instances", $"native-unique-suffix-{(seekable ? "seek" : "stream")}.vdb"));
        Assert.Equal(new[] { "repeat[0]", "repeat[1]" }, reader.GridNames); Assert.True(reader.HasGrid("repeat")); Assert.True(reader.HasGrid("repeat[1]")); Assert.False(reader.HasGrid("repeat[2]"));
        Assert.Equal(42, reader.ReadGrid<float>("repeat[0]").Tree.GetValue(new(1, 2, 3)));
        if (seekable) Assert.Equal(42, reader.ReadGrid<float>("repeat").Tree.GetValue(new(1, 2, 3)));
        else Assert.Throws<KeyNotFoundException>(() => reader.ReadGrid("repeat"));
    }
    [Fact]
    public void MetadataFactorySnapshotPersistsAcrossSelectedReadsAndRejectsReentrancy()
    {
        var registry = MetadataRegistry.CreateDefault(); VdbFileReader? reader = null; bool reenter = false;
        registry.RegisterType<int>("reader-test", BitConverter.GetBytes, bytes =>
        {
            if (reenter) { Assert.Throws<InvalidOperationException>(() => reader!.HasGrid("test")); Assert.Throws<InvalidOperationException>(() => reader!.Dispose()); }
            return BinaryPrimitives.ReadInt32LittleEndian(bytes);
        }, value => value != 0, value => value.ToString(), 4);
        var grid = new Grid<int>() { Name = "test" }; grid.SetMetadata("custom", registry.CreateMetadata("reader-test", 42));
        using var stream = new MemoryStream(); VdbFile.Write(stream, grid); stream.Position = 0;
        using (reader = VdbFile.OpenRead(stream, options: new() { MetadataRegistry = registry }))
        {
            registry.UnregisterType("reader-test"); reenter = true;
            Assert.Equal(42, ((Grid<int>)reader.ReadGridMetadata("test")).GetMetadata<int>("custom"));
            Assert.Equal(42, reader.ReadGrid<int>("test").GetMetadata<int>("custom"));
        }
    }
    [Fact]
    public void FileMetadataCopiesAreIndependentAndReadArchiveIncludesOneBoundedOperation()
    {
        using var stream = new MemoryStream(); var metadata = new MetaMap(); metadata.InsertMeta("answer", 42);
        VdbFile.WriteArchive(stream, new VdbArchive([new Grid<int>() { Name = "one" }], metadata)); stream.Position = 0;
        using var reader = VdbFile.OpenRead(stream); MetaMap first = reader.GetMetadata(); first.InsertMeta("answer", 43);
        Assert.Equal(42, reader.GetMetadata().MetaValue<int>("answer")); VdbArchive archive = reader.ReadArchive(); Assert.Single(archive.Grids); Assert.Equal(42, archive.Metadata["answer"].GetValue<int>());
    }
    [Fact]
    public void DisposedReaderReleasesEagerArchiveAndInputWhileItsHandleRemainsAlive()
    {
        var (reader, grid, stream) = DisposedReferences();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.False(grid.IsAlive); Assert.False(stream.IsAlive); GC.KeepAlive(reader);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (VdbFileReader Reader, WeakReference Grid, WeakReference Stream) DisposedReferences()
    {
        var stream = new MemoryStream(Archive(streaming: true)); var reader = VdbFile.OpenRead(stream, leaveOpen: false);
        var grid = reader.ReadGrid("z_populated"); var references = (reader, new WeakReference(grid), new WeakReference(stream)); reader.Dispose(); return references;
    }
    [Fact]
    public void ReturnedGridRemainsUsableAfterItsReaderCloses()
    {
        var reader = VdbFile.OpenRead(new MemoryStream(Archive(streaming: true)), leaveOpen: false); var grid = reader.ReadGrid<int>("z_populated"); reader.Dispose();
        Assert.Equal(42, grid.Tree.GetValue(new(-1, 2, 3))); grid.Tree.SetValueOn(Coord.Zero, 99); Assert.Equal(99, grid.Tree.GetValue(Coord.Zero));
    }
    private sealed class TrackedStream : Stream
    {
        private readonly MemoryStream _inner = new(); private readonly bool _seekable;
        public TrackedStream(byte[] bytes, bool seekable = true) { _inner.Write(bytes); _inner.Position = 0; _seekable = seekable; }
        public List<(long Start, long End)> ReadRanges { get; } = [];
        public bool WasDisposed { get; private set; }
        public byte[] Bytes => _inner.ToArray();
        public override bool CanRead => !WasDisposed; public override bool CanSeek => _seekable && !WasDisposed; public override bool CanWrite => !WasDisposed;
        public override long Length => _inner.Length; public override long Position { get => _inner.Position; set { if (!_seekable) throw new NotSupportedException(); _inner.Position = value; } }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) { long start = _inner.Position; int count = _inner.Read(buffer); ReadRanges.Add((start, start + count)); return count; }
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
        public override void Flush() => _inner.Flush(); public override long Seek(long offset, SeekOrigin origin) => _seekable ? _inner.Seek(offset, origin) : throw new NotSupportedException(); public override void SetLength(long value) => _inner.SetLength(value);
        protected override void Dispose(bool disposing) { WasDisposed = true; if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}