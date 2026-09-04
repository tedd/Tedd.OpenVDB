// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Points;
namespace Tedd.OpenVDB.Tests;

public class AttributeStreamsTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void RoundTripPreservesStorageAndStrideWithoutTakingStreamOwnership(bool compress, bool uniform, bool variable)
    {
        var array = new TypedAttributeArray<Vec3f, TruncateCodec<Vec3f>>(17, variable ? 53 : 3, !variable, new(.125f, -.25f, 1.25f)) { IsHidden = true, IsStreaming = true };
        if (!uniform) array.Expand();
        using var stream = new MemoryStream(); AttributeStreams.Write(stream, array, compress);
        stream.Position = 0; var result = AttributeStreams.Read<Vec3f, TruncateCodec<Vec3f>>(stream);
        Assert.True(array.StorageEquals(result)); Assert.Equal(stream.Length, stream.Position); Assert.True(stream.CanRead); Assert.True(stream.CanWrite);
    }
    [Fact]
    public void RawLengthExcludesStrideAndCompressionMarker()
    {
        var array = new TypedAttributeArray<int>(3, 2, uniformValue: 17); array.Expand();
        using var stream = new MemoryStream(); AttributeStreams.Write(stream, array); byte[] bytes = stream.ToArray();
        Assert.Equal(30UL, BinaryPrimitives.ReadUInt64LittleEndian(bytes)); Assert.Equal(43, bytes.Length);
        Assert.Equal(8, bytes[8]); Assert.Equal(1, bytes[9]); Assert.Equal(3U, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(10)));
        Assert.Equal(2U, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(14))); Assert.Equal(0, bytes[18]);
        for (int i = 19; i < bytes.Length; i += 4) Assert.Equal(17, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransientAttributesRequireExplicitOutput(bool output)
    {
        var array = new TypedAttributeArray<int>(3, uniformValue: 17) { IsTransient = true };
        using var stream = new MemoryStream(); AttributeStreams.Write(stream, array, compress: true, outputTransient: output);
        Assert.Equal(output ? 18 : 0, stream.Length);
        if (output) { stream.Position = 0; var result = AttributeStreams.Read<int>(stream); Assert.True(result.IsTransient); Assert.Equal(17, result.Get(2)); }
    }
    [Theory]
    [InlineData(12, false)]
    [InlineData(13, true)]
    [InlineData(31, true)]
    [InlineData(32, true)]
    public void SmallBloscBuffersRespectThresholdAndPadding(int count, bool compressed)
    {
        var array = new TypedAttributeArray<int>(count, uniformValue: 17); array.Expand(); using var stream = new MemoryStream();
        AttributeStreams.Write(stream, array, compress: true); byte[] bytes = stream.ToArray(); Assert.Equal(compressed ? 1 : 0, bytes[14]);
        if (compressed) Assert.Equal(128U, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(19)));
        stream.Position = 0; Assert.True(array.StorageEquals(AttributeStreams.Read<int>(stream)));
    }
    [Fact]
    public void MetadataAndBuffersCanBeReadSeparatelyFromOneByteNonseekableStream()
    {
        var array = new TypedAttributeArray<float, FixedPoint8Codec<float, PositionRange>>(3, 7, false, .25f);
        array.Expand(); array.Set(5, -.25f); using var encoded = new MemoryStream(); AttributeStreams.Write(encoded, array, compress: true);
        using var stream = new OneByteStream(encoded.ToArray()); var metadata = AttributeStreams.ReadMetadata(stream);
        Assert.Equal(3, metadata.Size); Assert.Equal(7, metadata.StrideOrTotalSize); Assert.False(metadata.HasConstantStride);
        var result = AttributeStreams.ReadBuffers<float, FixedPoint8Codec<float, PositionRange>>(stream, metadata);
        Assert.True(array.StorageEquals(result)); Assert.True(stream.CanRead);
    }
    [Fact]
    public void ConcatenatedAttributesConsumeOnlyTheirOwnFrames()
    {
        using var stream = new MemoryStream();
        AttributeStreams.Write(stream, new TypedAttributeArray<int>(5, uniformValue: 9));
        AttributeStreams.Write(stream, new TypedAttributeArray<double>(7, uniformValue: .25));
        stream.Position = 0; Assert.Equal(9, AttributeStreams.Read<int>(stream).Get(4)); Assert.Equal(.25, AttributeStreams.Read<double>(stream).Get(6)); Assert.Equal(stream.Length, stream.Position);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryTruncatedPrefixIsRejected(bool compress)
    {
        byte[] original = Encoded(compress);
        for (int i = 0; i < original.Length; i++)
        {
            using var stream = new MemoryStream(original, 0, i);
            Assert.Throws<EndOfStreamException>(() => AttributeStreams.Read<int>(stream));
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
    public void InvalidFramingAndImpossibleShapesFailBeforeDataIsPublished(int kind)
    {
        byte[] bytes = Encoded(false);
        switch (kind)
        {
            case 0: BinaryPrimitives.WriteUInt64LittleEndian(bytes, 5); break;
            case 1: BinaryPrimitives.WriteUInt64LittleEndian(bytes, ulong.MaxValue); break;
            case 2: BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), 0); break;
            case 3: BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), uint.MaxValue); break;
            case 4: BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(10), int.MaxValue); break;
            case 5: bytes[14] = 2; break;
            case 6: BinaryPrimitives.WriteUInt64LittleEndian(bytes, BinaryPrimitives.ReadUInt64LittleEndian(bytes) + 1); break;
            case 7: bytes[8] = 0; break; // Variable total size defaults to one, below element count.
        }
        Assert.Throws<InvalidDataException>(() => AttributeStreams.Read<int>(new MemoryStream(bytes)));
    }
    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(128)]
    public void PagedAndUnknownSerializationFlagsFailExplicitly(byte flag)
    {
        byte[] bytes = Encoded(false); bytes[9] |= flag;
        Assert.Throws<NotSupportedException>(() => AttributeStreams.Read<int>(new MemoryStream(bytes)));
    }
    [Fact]
    public void DeprecatedMemoryCompressionFlagDoesNotChangeTheBufferLayout()
    {
        byte[] bytes = Encoded(false); bytes[9] |= 4;
        var result = AttributeStreams.Read<int>(new MemoryStream(bytes)); Assert.Equal(17, result.Get(0));
    }
    [Fact]
    public void CompletedReadsClearPartialReadButRetainUnknownAttributeFlags()
    {
        byte[] bytes = Encoded(false); bytes[8] |= 32 | 64;
        var result = AttributeStreams.Read<int>(new MemoryStream(bytes)); Assert.Equal(0, (int)(result.Flags & AttributeFlags.PartialRead)); Assert.Equal(64, (int)result.Flags & 64);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ReadBudgetsBoundEncodedDecodedTemporaryAndUniformStorage(int kind)
    {
        var options = kind switch
        {
            0 => new AttributeReadOptions { MaximumEncodedBytes = 0 },
            1 => new AttributeReadOptions { MaximumStorageBytes = 3 },
            2 => new AttributeReadOptions { MaximumTotalAllocationBytes = 10 },
            _ => new AttributeReadOptions { MaximumStorageBytes = 63 }
        };
        byte[] bytes = kind == 3 ? Encoded(true, 13) : Encoded(true);
        Assert.Throws<InvalidDataException>(() => AttributeStreams.Read<int>(new MemoryStream(bytes), options));
    }
    [Fact]
    public void CorruptBloscDecodedLengthIsRejectedBeforeLargeAllocation()
    {
        byte[] bytes = Encoded(true); Assert.Equal(1, bytes[14]);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(19), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => AttributeStreams.Read<int>(new MemoryStream(bytes)));
    }
    [Fact]
    public void UniformLargeShapesDoNotAllocateExpandedStorageOnRead()
    {
        using var stream = new MemoryStream(); var source = new TypedAttributeArray<int>(1_000_000, uniformValue: 19);
        AttributeStreams.Write(stream, source); stream.Position = 0;
        var result = AttributeStreams.Read<int>(stream, new() { MaximumStorageBytes = 16, MaximumTotalAllocationBytes = 16 });
        Assert.True(result.IsUniform); Assert.Equal(4, result.StorageByteCount); Assert.Equal(19, result.Get(999999));
        Assert.Throws<InvalidOperationException>(() => result.Expand());
    }
    [Fact]
    public void InvalidOptionsAndCancelledReadsDoNotConsumeTheStream()
    {
        using var stream = new MemoryStream(Encoded(false));
        Assert.Throws<ArgumentOutOfRangeException>(() => AttributeStreams.Read<int>(stream, new() { MaximumEncodedBytes = -1 }));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => AttributeStreams.Read<int>(stream, new() { CancellationToken = cancellation.Token }));
        Assert.Equal(0, stream.Position);
    }
    [Fact]
    public void ExternallyConstructedMetadataIsValidatedBeforeReading()
    {
        using var stream = new MemoryStream([0, 0, 0, 0]);
        Assert.Throws<InvalidDataException>(() => AttributeStreams.ReadBuffers<int, NullCodec<int>>(stream, new(AttributeFlags.ConstantStride, 3, 2, 0, 24)));
        Assert.Throws<InvalidDataException>(() => AttributeStreams.ReadBuffers<int, NullCodec<int>>(stream, new(AttributeFlags.None, 3, 2, 1, 24)));
        Assert.Equal(0, stream.Position);
    }
    private static byte[] Encoded(bool compress, int count = 512)
    {
        var array = new TypedAttributeArray<int>(count, uniformValue: 17); array.Expand(); using var stream = new MemoryStream();
        AttributeStreams.Write(stream, array, compress); return stream.ToArray();
    }
    private sealed class OneByteStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, System.Math.Min(1, count));
        public override int Read(Span<byte> buffer) => _inner.Read(buffer[..System.Math.Min(1, buffer.Length)]);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
