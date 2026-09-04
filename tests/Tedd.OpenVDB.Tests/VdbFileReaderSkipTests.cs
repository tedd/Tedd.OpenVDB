// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbFileReaderSkipTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int compression in new[] { 0, 1, 2, 4, 5, 6 }) foreach (bool half in new[] { false, true }) foreach (bool vector in new[] { false, true }) for (int profile = 0; profile < 7; ++profile)
            yield return [compression, half, vector, profile];
    }
    [Theory, MemberData(nameof(Cases))]
    public void RegionSkippingHandlesEveryActiveMaskCodeCompressionAndHalfWidth(int compression, bool half, bool vector, int profile)
    {
        if (vector) Check<Vec3f>(compression, half, profile); else Check<float>(compression, half, profile);
    }
    private static T Value<T>(float number) where T : unmanaged
    {
        if (typeof(T) == typeof(float)) return Unsafe.As<float, T>(ref number);
        var vector = new Vec3f(number, number * .5f, -number); return Unsafe.As<Vec3f, T>(ref vector);
    }
    private static byte[] File<T>(int compression, bool half, int profile) where T : unmanaged
    {
        var grid = new Grid<T>(Value<T>(2)) { Name = "skip" }; var leaf = grid.Tree.TouchLeaf(new(-8, 0, 0));
        for (int i = 0; i < 512; ++i)
        {
            float value = profile switch { 0 => 2, 1 => -2, 2 => 3, 3 => (i & 1) == 0 ? -2 : 2, 4 => (i & 1) == 0 ? 3 : 2, 5 => (i & 1) == 0 ? 3 : 4, _ => 3 + i % 3 };
            leaf.SetValueOff(i, Value<T>(value));
        }
        grid.Tree.SetValueOn(new(1, 2, 3), Value<T>(1.25f)); using var stream = new MemoryStream();
        VdbFile.Write(stream, grid, options: new() { Compression = (VdbCompression)compression, SaveFloatAsHalf = half }); return stream.ToArray();
    }
    private static (long Mask, long Code, long Length, long Payload, long Size) Payload<T>(byte[] bytes, int compression) where T : unmanaged
    {
        using var stream = new MemoryStream(bytes); using var input = new BinaryReader(stream); stream.Position = 57;
        int metadata = input.ReadInt32(); for (int i = 0; i < metadata; ++i) { SkipString(input); SkipString(input); stream.Position += input.ReadInt32(); }
        Assert.Equal(1, input.ReadInt32()); SkipString(input); SkipString(input); SkipString(input); input.ReadInt64(); long block = input.ReadInt64(); input.ReadInt64();
        stream.Position = block + 64; long codeAt = stream.Position; byte code = input.ReadByte(); if (code is 2 or 4 or 5) stream.Position += Unsafe.SizeOf<T>(); if (code == 5) stream.Position += Unsafe.SizeOf<T>(); if (code is 3 or 4 or 5) stream.Position += 64;
        long lengthAt = -1, size = 0; if ((compression & 3) != 0) { lengthAt = stream.Position; long length = input.ReadInt64(); size = System.Math.Abs(length); }
        return (block, codeAt, lengthAt, stream.Position, size);
    }
    private static void SkipString(BinaryReader input) { uint size = input.ReadUInt32(); input.BaseStream.Position += size; }
    private static void Check<T>(int compression, bool half, int profile) where T : unmanaged
    {
        byte[] bytes = File<T>(compression, half, profile);
        using var reader = VdbFile.OpenRead(new MemoryStream(bytes)); Grid<T> full = reader.ReadGrid<T>("skip");
        Grid<T> region = reader.ReadGrid<T>("skip", new BBoxd(new Vec3d(1, 2, 3), new Vec3d(1, 2, 3)));
        Assert.Equal(1UL, region.ActiveVoxelCount); Assert.Equal(Value<T>(1.25f), region.Tree.GetValue(new(1, 2, 3))); Assert.Equal(Value<T>(2), region.Tree.GetValue(new(-8, 0, 0)));
        Assert.True(full.Tree.LeafCount > region.Tree.LeafCount);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SkippedChunksStillValidateFramingAndTopology(int corruption)
    {
        byte[] bytes = File<float>(6, false, 6); var payload = Payload<float>(bytes, 6);
        if (corruption == 0) BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan((int)payload.Length), long.MaxValue);
        if (corruption == 1) BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan((int)payload.Length), long.MinValue);
        if (corruption == 2) bytes[(int)payload.Code] = 255;
        if (corruption == 3) bytes[(int)payload.Mask] ^= 1;
        using var reader = VdbFile.OpenRead(new MemoryStream(bytes)); Assert.Throws<InvalidDataException>(() => reader.ReadGrid("skip", new BBoxd(new Vec3d(1, 2, 3), new Vec3d(1, 2, 3))));
    }
    [Fact]
    public void NativeCorruptBloscFixtureIsSkippedOnlyByAnExcludingRegion()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "file-partial");
        using var json = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(folder, "native-partial-skipped-buffer.json"))); JsonElement data = json.RootElement;
        var w = data.GetProperty("world"); Vec3d world = new(w[0].GetDouble(), w[1].GetDouble(), w[2].GetDouble());
        using var stream = new ObservedStream(System.IO.File.ReadAllBytes(Path.Combine(folder, "native-partial-skipped-buffer.vdb")));
        using var reader = VdbFile.OpenRead(stream);
        Grid<float> region = reader.ReadGrid<float>("skip", new BBoxd(world, world)); Assert.Equal(data.GetProperty("grid").GetProperty("active").GetUInt64(), region.ActiveVoxelCount);
        VdbFileReaderNativeTests.Check(data.GetProperty("grid"), region);
        long start = data.GetProperty("payload").GetInt64(), end = start + data.GetProperty("length").GetInt64();
        Assert.DoesNotContain(stream.ReadRanges, range => range.Start < end && range.End > start);
        Assert.Throws<NotSupportedException>(() => reader.ReadGrid("skip"));
    }
    private sealed class ObservedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public List<(long Start, long End)> ReadRanges { get; } = [];
        public override int Read(Span<byte> buffer) { long start = Position; int count = base.Read(buffer); ReadRanges.Add((start, start + count)); return count; }
        public override int Read(byte[] buffer, int offset, int count) { long start = Position; int read = base.Read(buffer, offset, count); ReadRanges.Add((start, start + read)); return read; }
    }
}