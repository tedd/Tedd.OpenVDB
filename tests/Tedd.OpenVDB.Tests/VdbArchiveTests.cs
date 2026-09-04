// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Text;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Xunit;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbArchiveTests
{
    public static IEnumerable<object[]> Profiles()
    {
        foreach (int flags in new[] { 0, 3 }) foreach (bool instances in new[] { false, true }) foreach (bool streaming in new[] { false, true })
            yield return [flags, instances, streaming];
    }
    private static string Key(int flags, bool instances, bool streaming) => $"archive-{flags}-{(instances ? "instances" : "copies")}-{(streaming ? "stream" : "seek")}";
    private static byte[] Fixture(int flags = 3, bool instances = true, bool streaming = false) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "archives", $"native-{Key(flags, instances, streaming)}.vdb"));

    [Theory, MemberData(nameof(Profiles))]
    public void NativeArchiveRoundTripsMetadataMapsAndInstances(int flags, bool instances, bool streaming)
    {
        using var input = new MemoryStream(Fixture(flags, instances, streaming));
        VdbArchive archive = VdbFile.ReadArchive(input);
        Assert.Equal(7, archive.Grids.Count);
        Assert.Equal("managed port reference", archive.Metadata["creator"].Value);
        Assert.Equal(225L, archive.Metadata["revision"].Value);
        var opaque = archive.Metadata["opaque"].GetValue<OpaqueMetadata>();
        Assert.Equal("vendor_test_v1", opaque.TypeName); Assert.Equal(new byte[] { 0, 1, 127, 128, 255, 42 }, opaque.Data.ToArray());
        Assert.Equal(new[] { "duplicate", "duplicate", "", "fog_instance", "duplicate", "", "unitary" }, archive.Grids.Select(grid => grid.Name));
        var blocks = Assert.IsType<Grid<int>>(archive.Grids[0]); var fog = Assert.IsType<Grid<float>>(archive.Grids[1]);
        Assert.Equal(42, blocks.Tree.GetValue(new(-1, 2, 3))); Assert.False(blocks.Tree.IsValueOn(new(8, 0, 0)));
        Assert.Equal(17, blocks.Tree.GetValue(new(8, 0, 0))); Assert.Equal(1.25f, fog.Tree.GetValue(new(-9, 0, 0)));
        Assert.Equal(-5.5f, fog.Tree.GetValue(new(2, 3, 4)));
        Assert.Equal(instances, ReferenceEquals(blocks.Tree, Assert.IsType<Grid<int>>(archive.Grids[4]).Tree));
        Assert.Equal(instances, ReferenceEquals(fog.Tree, Assert.IsType<Grid<float>>(archive.Grids[3]).Tree));
        Assert.False(fog.Transform.IsLinear);
        var frustum = Assert.IsType<NonlinearFrustumMap>(fog.Transform.FrustumMap);
        Assert.Equal(new BBoxd(new Vec3d(-10, -20, 0), new Vec3d(110, 60, 100)), frustum.BoundingBox);
        Assert.Equal(0.5, frustum.Taper); Assert.Equal(40, frustum.Depth);
        Assert.Equal(new Vec3d(2, 4, 6), blocks.Transform.IndexToWorld(new Vec3d(1, 2, 3)));
        Assert.Equal(new Vec3d(2, 6, 12), archive.Grids[2].Transform.IndexToWorld(new Vec3d(1, 2, 3)));
        Assert.Equal(new Vec3d(4, 6, 8), archive.Grids[3].Transform.IndexToWorld(new Vec3d(1, 2, 3)));
        Assert.Equal(new Vec3d(7, 12, 19), archive.Grids[4].Transform.IndexToWorld(new Vec3d(1, 2, 3)));
        Assert.Equal(new Vec3d(4, 8, 12), archive.Grids[5].Transform.IndexToWorld(new Vec3d(1, 2, 3)));
        using var bytes = new MemoryStream();
        using Stream output = streaming ? new Streaming(bytes) : bytes;
        VdbFile.WriteArchive(output, archive, options: new() { Compression = (VdbCompression)flags, EnableInstancing = instances });
        byte[] encoded = bytes.ToArray();
        string? directory = Environment.GetEnvironmentVariable("TEDD_OPENVDB_ARCHIVE_OUTPUT_DIR");
        if (directory is not null) { Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory, $"managed-{Key(flags, instances, streaming)}.vdb"), encoded); }
        using var decoded = new MemoryStream(encoded);
        VdbArchive actual = VdbFile.ReadArchive(decoded);
        Assert.Equal(archive.Metadata, actual.Metadata);
        for (int index = 0; index < archive.Grids.Count; ++index)
        {
            IGrid expectedGrid = archive.Grids[index], actualGrid = actual.Grids[index];
            Assert.Equal(expectedGrid.Metadata, actualGrid.Metadata);
            Assert.Equal(expectedGrid.Transform.IsLinear, actualGrid.Transform.IsLinear);
            foreach (var point in new[] { Vec3d.Zero, new Vec3d(1.25, -2.5, 3.75), new Vec3d(100, 30, 90) })
                Assert.True(expectedGrid.Transform.IndexToWorld(point).ApproxEquals(actualGrid.Transform.IndexToWorld(point), 1e-12));
            switch (expectedGrid)
            {
                case Grid<int> grid: EqualTree(grid.Tree, Assert.IsType<Grid<int>>(actualGrid).Tree); break;
                case Grid<float> grid: EqualTree(grid.Tree, Assert.IsType<Grid<float>>(actualGrid).Tree); break;
                case Grid<double> grid: EqualTree(grid.Tree, Assert.IsType<Grid<double>>(actualGrid).Tree); break;
            }
        }
        Assert.Equal(instances, ReferenceEquals(Assert.IsType<Grid<int>>(actual.Grids[0]).Tree, Assert.IsType<Grid<int>>(actual.Grids[4]).Tree));
        Assert.Equal(instances, ReferenceEquals(Assert.IsType<Grid<float>>(actual.Grids[1]).Tree, Assert.IsType<Grid<float>>(actual.Grids[3]).Tree));
    }
    private static void EqualTree<T>(VdbTree<T> expected, VdbTree<T> actual) where T : unmanaged
    {
        Assert.True(expected.HasSameTopology(actual)); Assert.Equal(expected.Background, actual.Background);
        Assert.Equal(expected.ActiveVoxelCount, actual.ActiveVoxelCount); Assert.Equal(expected.LeafCount, actual.LeafCount);
        foreach (var value in expected.EnumerateValues(ValueIterationMode.All)) { Assert.Equal(value.Value, actual.GetValue(value.Bounds.Min)); Assert.Equal(value.IsActive, actual.IsValueOn(value.Bounds.Min)); }
        foreach (var value in actual.EnumerateValues(ValueIterationMode.All)) Assert.Equal(value.Value, expected.GetValue(value.Bounds.Min));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void EmptyArchiveAndFileMetadataRoundTrip(bool streaming)
    {
        var metadata = new Dictionary<string, MetadataValue> { ["source"] = new("empty"), ["custom"] = new(new OpaqueMetadata("unknown_0", [])) };
        using var bytes = new MemoryStream(); using Stream output = streaming ? new Streaming(bytes) : bytes;
        VdbFile.WriteArchive(output, new VdbArchive([], metadata));
        using var input = new MemoryStream(bytes.ToArray()); VdbArchive decoded = VdbFile.ReadArchive(input);
        Assert.Empty(decoded.Grids); Assert.Equal(metadata, decoded.Metadata);
    }

    [Fact]
    public void UnknownMetadataIsImmutableAndRetainsRawPayload()
    {
        byte[] data = [0, 255, 17]; var opaque = new OpaqueMetadata("unknown", data); data[0] = 13;
        Assert.Equal((byte)0, opaque.Data[0]); Assert.Equal(opaque, new OpaqueMetadata("unknown", [0, 255, 17]));
        Assert.Throws<ArgumentException>(() => new OpaqueMetadata("int32", [1, 2, 3, 4]));
        var grid = new Grid<int>(); grid.SetMetadata("opaque", opaque);
        using var file = new MemoryStream(); VdbFile.Write(file, grid); file.Position = 0;
        Assert.Equal(opaque, VdbFile.Read<int>(file).GetMetadata<OpaqueMetadata>("opaque"));
    }

    [Fact]
    public void ArchivePreflightRejectsUnsupportedLastGridBeforeWriting()
    {
        using var file = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => VdbFile.WriteArchive(file, new VdbArchive([new Grid<int>(), new Grid<byte>()])));
        Assert.Equal(0, file.Length);
    }

    [Fact]
    public void InstanceMetadataAndTransformAreIndependentWhileTreeRemainsShared()
    {
        using var input = new MemoryStream(Fixture()); var archive = VdbFile.ReadArchive(input);
        var first = Assert.IsType<Grid<float>>(archive.Grids[1]); var instance = Assert.IsType<Grid<float>>(archive.Grids[3]);
        instance.Tree.SetValueOn(new(50, 60, 70), 18); Assert.Equal(18, first.Tree.GetValue(new(50, 60, 70)));
        instance.Name = "changed"; Assert.Equal("duplicate", first.Name); Assert.False(first.Transform.IsLinear); Assert.True(instance.Transform.IsLinear);
        Assert.Throws<NotSupportedException>(() => VdbFile.Read<float>(new MemoryStream(Fixture())));
    }

    [Fact]
    public void InstancingRequiresMatchingHalfFlags()
    {
        var first = new Grid<float>(1) { Name = "shared" }; first.Tree.SetValueOn(new(1, 2, 3), 1.0005f);
        var second = new Grid<float>(first.Tree) { Name = "shared" }; second.SetMetadata("is_saved_as_half_float", true);
        using var file = new MemoryStream(); VdbFile.WriteArchive(file, new VdbArchive([first, second])); file.Position = 0;
        var decoded = VdbFile.ReadArchive(file); var a = Assert.IsType<Grid<float>>(decoded.Grids[0]); var b = Assert.IsType<Grid<float>>(decoded.Grids[1]);
        Assert.NotSame(a.Tree, b.Tree); Assert.NotEqual(a.Tree.GetValue(new(1, 2, 3)), b.Tree.GetValue(new(1, 2, 3)));
    }

    [Theory, InlineData(0), InlineData(1), InlineData(6)]
    public void GridCountLimitsAreEnforced(int limit)
    {
        using var input = new MemoryStream(Fixture());
        if (limit == 0) Assert.Throws<ArgumentOutOfRangeException>(() => VdbFile.ReadArchive(input, options: new() { MaxGrids = limit }));
        else Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(input, options: new() { MaxGrids = limit }));
    }

    [Fact]
    public void TruncatedMultiGridArchivesAndOwnershipAreChecked()
    {
        byte[] source = Fixture();
        foreach (int length in new[] { 0, 57, 70, source.Length / 2, source.Length - 1 })
        {
            using var input = new MemoryStream(source[..length]); Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(input)); Assert.True(input.CanRead);
        }
        var closed = new MemoryStream(source[..^1]); Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(closed, leaveOpen: false)); Assert.False(closed.CanRead);
    }

    [Theory, InlineData("duplicate"), InlineData("instance-block"), InlineData("instance-type"), InlineData("overlap"), InlineData("missing-parent")]
    public void InvalidArchiveDescriptorsAreRejected(string mutation)
    {
        byte[] bytes = Fixture(); List<Layout> descriptors = Layouts(bytes);
        Layout first = descriptors[0], instance = descriptors[3];
        switch (mutation)
        {
            case "duplicate": bytes.AsSpan(first.Name, first.NameLength).CopyTo(bytes.AsSpan(descriptors[1].Name)); break;
            case "instance-block": BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(instance.Offsets + 8), 1); break;
            case "instance-type": bytes[instance.Parent + first.NameLength - 1] = (byte)'0'; break;
            case "overlap": BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(first.Offsets + 16), first.Offsets + 24); break;
            case "missing-parent": bytes[instance.Parent] = (byte)'X'; break;
        }
        using var input = new MemoryStream(bytes);
        if (mutation == "missing-parent") Assert.Throws<NotSupportedException>(() => VdbFile.ReadArchive(input));
        else Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(input));
    }

    [Theory, InlineData("taper-zero"), InlineData("taper-nan"), InlineData("depth-negative"), InlineData("bounds-empty"), InlineData("bounds-infinite"), InlineData("matrix-nan"), InlineData("matrix-singular")]
    public void InvalidFrustumPayloadsAreRejected(string mutation)
    {
        var grid = new Grid<float> { Transform = new Transform(new NonlinearFrustumMap(new BBoxd(Vec3d.Zero, Vec3d.One), 0.5, 2)) };
        using var file = new MemoryStream(); VdbFile.Write(file, grid); byte[] bytes = file.ToArray();
        int payload = bytes.AsSpan().IndexOf("NonlinearFrustumMap"u8) + "NonlinearFrustumMap".Length;
        int position = mutation switch
        {
            "taper-zero" or "taper-nan" => payload + 48,
            "depth-negative" => payload + 56,
            "bounds-empty" => payload + 24,
            "bounds-infinite" => payload,
            _ => payload + 64 + 4 + "AffineMap".Length
        };
        double value = mutation switch { "taper-nan" or "matrix-nan" => double.NaN, "bounds-infinite" => double.PositiveInfinity, "depth-negative" => -1, _ => 0 };
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(position), BitConverter.DoubleToInt64Bits(value));
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<float>(new MemoryStream(bytes)));
    }

    [Fact]
    public void NonlinearSecondaryMapIsRejectedWithoutRecursiveParsing()
    {
        var grid = new Grid<float> { Transform = new Transform(new NonlinearFrustumMap()) };
        using var file = new MemoryStream(); VdbFile.Write(file, grid); byte[] original = file.ToArray();
        int type = original.AsSpan().IndexOf("NonlinearFrustumMap"u8) + "NonlinearFrustumMap".Length + 64;
        int oldSize = 4 + "AffineMap".Length, newSize = 4 + "NonlinearFrustumMap".Length;
        byte[] bytes = new byte[original.Length + newSize - oldSize]; original.AsSpan(0, type).CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(type), "NonlinearFrustumMap".Length);
        "NonlinearFrustumMap"u8.CopyTo(bytes.AsSpan(type + 4)); original.AsSpan(type + oldSize).CopyTo(bytes.AsSpan(type + newSize));
        int offsets = Layouts(original)[0].Offsets;
        for (int index = 8; index <= 16; index += 8)
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offsets + index), BinaryPrimitives.ReadInt64LittleEndian(original.AsSpan(offsets + index)) + newSize - oldSize);
        Assert.Throws<InvalidDataException>(() => VdbFile.Read<float>(new MemoryStream(bytes)));
    }

    [Fact]
    public void UnsupportedGenericReadHonorsOwnership()
    {
        var input = new MemoryStream(); Assert.Throws<NotSupportedException>(() => VdbFile.Read<byte>(input, leaveOpen: false)); Assert.False(input.CanRead);
    }
    private sealed record Layout(int Name, int NameLength, int Type, int Parent, int Offsets);
    private static List<Layout> Layouts(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var reader = new BinaryReader(stream);
        stream.Position = 57; uint metadata = reader.ReadUInt32();
        for (int index = 0; index < metadata; ++index) { SkipString(reader); SkipString(reader); uint payload = reader.ReadUInt32(); stream.Position += payload; }
        int count = reader.ReadInt32(); var result = new List<Layout>();
        for (int index = 0; index < count; ++index)
        {
            int length = reader.ReadInt32(), name = (int)stream.Position; stream.Position += length;
            int type = (int)stream.Position + 4; SkipString(reader); int parent = (int)stream.Position + 4; SkipString(reader);
            int offsets = (int)stream.Position; reader.ReadInt64(); reader.ReadInt64(); long end = reader.ReadInt64();
            result.Add(new(name, length, type, parent, offsets)); stream.Position = end;
        }
        return result;
    }
    private static void SkipString(BinaryReader reader) { uint length = reader.ReadUInt32(); reader.BaseStream.Position += length; }
    private sealed class Streaming(Stream stream) : Stream
    {
        public override bool CanRead => stream.CanRead; public override bool CanSeek => false; public override bool CanWrite => stream.CanWrite;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => stream.Read(buffer);
        public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => stream.Write(buffer);
        public override void Flush() => stream.Flush(); public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
