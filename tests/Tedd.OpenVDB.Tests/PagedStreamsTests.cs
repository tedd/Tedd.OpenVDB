// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using Tedd.OpenVDB.Points;

namespace Tedd.OpenVDB.Tests;

public sealed class PagedStreamsTests
{
    [Fact]
    public void SmallRawPageHasNegativeSizeHeaderAndExactData()
    {
        byte[] value = [1, 2, 3, 4];
        var encoded = Encode([value]);
        Assert.Equal(4, encoded.Header.Length);
        Assert.Equal(-4, BinaryPrimitives.ReadInt32LittleEndian(encoded.Header));
        Assert.Equal(value, encoded.Data);
        Assert.Equal([value], Decode(encoded, [value.Length]));
    }

    [Fact]
    public void CompressiblePageUsesTwoIntegerHeaderAndSharedDecodedPage()
    {
        byte[] first = new byte[3000], second = new byte[4000], third = new byte[5000];
        first.AsSpan().Fill(7); second.AsSpan().Fill(7); third.AsSpan().Fill(7);
        var encoded = Encode([first, second, third]);
        Assert.Equal(8, encoded.Header.Length);
        Assert.True(BinaryPrimitives.ReadInt32LittleEndian(encoded.Header) > 0);
        Assert.Equal(12_000, BinaryPrimitives.ReadInt32LittleEndian(encoded.Header.AsSpan(4)));
        var decoded = DecodeWithHandles(encoded, [3000, 4000, 5000]);
        Assert.Same(decoded.Handles[0].Page, decoded.Handles[1].Page);
        Assert.Same(decoded.Handles[1].Page, decoded.Handles[2].Page);
        Assert.Equal(first, decoded.Values[0]); Assert.Equal(second, decoded.Values[1]); Assert.Equal(third, decoded.Values[2]);
    }

    [Fact]
    public void BlocksFlushOnlyWhenTheyWouldExceedOneMebibyte()
    {
        using var stream = new MemoryStream();
        using var writer = new PagedOutputStream(stream, sizeOnly: true);
        writer.Write(new byte[PagedOutputStream.PageSize]);
        Assert.Empty(stream.ToArray());
        writer.Write([1]);
        Assert.NotEmpty(stream.ToArray());
        long afterFirstPage = stream.Length;
        writer.Flush(); Assert.True(stream.Length > afterFirstPage);
    }

    [Fact]
    public void OversizedBlocksAreImmediateIndependentPages()
    {
        byte[] large = Enumerable.Repeat((byte)19, PagedOutputStream.PageSize + 1).ToArray();
        byte[] tail = [8, 9, 10];
        var encoded = Encode([large, large, tail]);
        var decoded = DecodeWithHandles(encoded, [large.Length, large.Length, tail.Length]);
        Assert.NotSame(decoded.Handles[0].Page, decoded.Handles[1].Page);
        Assert.NotSame(decoded.Handles[1].Page, decoded.Handles[2].Page);
        Assert.Equal(large, decoded.Values[0]); Assert.Equal(large, decoded.Values[1]); Assert.Equal(tail, decoded.Values[2]);
    }

    [Fact]
    public void OneThirdPageBlocksProduceNativePageGrouping()
    {
        int size = PagedOutputStream.PageSize / 3;
        byte[][] blocks = Enumerable.Range(0, 10).Select(index => Enumerable.Repeat((byte)index, size).ToArray()).ToArray();
        var encoded = Encode(blocks);
        var decoded = DecodeWithHandles(encoded, blocks.Select(static block => block.Length).ToArray());
        Assert.Equal(4, decoded.Handles.Select(static handle => handle.Page).Distinct().Count());
        for (int i = 0; i < blocks.Length; i++) Assert.Equal(blocks[i], decoded.Values[i]);
    }

    [Fact]
    public void IncompressibleShortPagesRemainRaw()
    {
        byte[] values = new byte[4096]; uint state = 0x12345678;
        for (int i = 0; i < values.Length; i++) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; values[i] = (byte)state; }
        var encoded = Encode([values]);
        Assert.Equal(-values.Length, BinaryPrimitives.ReadInt32LittleEndian(encoded.Header));
        Assert.Equal(values, encoded.Data);
        Assert.Equal(values, Decode(encoded, [values.Length])[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(47)]
    [InlineData(48)]
    [InlineData(60)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(1024)]
    public void ThresholdSizesRoundTrip(int size)
    {
        byte[] value = Enumerable.Repeat((byte)0xA5, size).ToArray();
        Assert.Equal(value, Decode(Encode([value]), [size])[0]);
    }

    [Fact]
    public void EmptyWritesAreIgnoredAndDisposeDoesNotImplicitlyFlush()
    {
        using var stream = new MemoryStream();
        var writer = new PagedOutputStream(stream, sizeOnly: true);
        writer.Write([]); writer.Write([1, 2, 3]);
        Assert.Equal(0, stream.Length);
        writer.Dispose();
        Assert.Equal(0, stream.Length);
        Assert.Throws<ObjectDisposedException>(() => writer.Write([4]));
        Assert.Throws<ObjectDisposedException>(() => writer.Flush());
        stream.WriteByte(9); Assert.Equal(1, stream.Length);
    }

    [Fact]
    public void HandlesCopyRangesAndSupportAllocationFreeReads()
    {
        byte[] value = Enumerable.Range(0, 200).Select(static i => (byte)i).ToArray();
        var encoded = Encode([value]);
        using var stream = new MemoryStream([.. encoded.Header, .. encoded.Data]);
        var sizes = new PagedInputStream(stream);
        PageHandle handle = sizes.CreateHandle(value.Length);
        var data = new PagedInputStream(stream); data.Read(handle, value.Length);
        PageHandle copy = handle.Copy(); Assert.Same(handle.Page, copy.Page);
        Span<byte> destination = stackalloc byte[200]; copy.Read(destination);
        Assert.True(value.AsSpan().SequenceEqual(destination)); Assert.Equal(value, copy.Read());
        Assert.Throws<ArgumentException>(() => copy.Read(new byte[199]));
    }

    [Fact]
    public void LoadedPageSupportsConcurrentIndependentHandleReads()
    {
        byte[] value = Enumerable.Range(0, 100_000).Select(static i => (byte)(i * 31)).ToArray();
        var encoded = Encode([value]);
        var decoded = DecodeWithHandles(encoded, [value.Length]);
        PageHandle handle = decoded.Handles[0];
        Parallel.For(0, 64, _ => Assert.Equal(value, handle.Read()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(int.MinValue)]
    public void InvalidZeroAndMinimumHeadersAreRejected(int header)
    {
        byte[] bytes = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(bytes, header);
        Assert.Throws<InvalidDataException>(() => new PagedInputStream(new MemoryStream(bytes)).CreateHandle(1));
    }

    [Fact]
    public void HeaderAndHandleBoundsAreValidated()
    {
        byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, -10);
        var input = new PagedInputStream(new MemoryStream(header));
        Assert.Throws<InvalidDataException>(() => input.CreateHandle(11));
        Assert.Throws<ArgumentOutOfRangeException>(() => input.CreateHandle(0));
        Assert.Throws<InvalidDataException>(() => new PagedInputStream(new MemoryStream(header), new() { MaximumPageBytes = 9 }).CreateHandle(1));
    }

    [Fact]
    public void CompressibleFortyEightBytePageRemainsRaw()
    {
        byte[] value = new byte[48];
        var encoded = Encode([value]);
        Assert.Equal(-48, BinaryPrimitives.ReadInt32LittleEndian(encoded.Header));
        Assert.Equal(value, encoded.Data);
    }

    [Fact]
    public void FailedPageLoadRemainsFailedAndCannotAdvanceReplayState()
    {
        byte[] value = new byte[64];
        var encoded = Encode([value]);
        using var stream = new MemoryStream(encoded.Header);
        PageHandle handle = new PagedInputStream(stream).CreateHandle(value.Length);
        var reader = new PagedInputStream(Stream.Null);
        Assert.Throws<EndOfStreamException>(() => reader.Read(handle, value.Length));
        Assert.False(handle.Page.IsLoaded);
        Assert.Throws<EndOfStreamException>(() => reader.Read(handle, value.Length));
        Assert.True(reader.AtPageBoundary);
        Assert.Throws<EndOfStreamException>(handle.Read);
    }
    [Fact]
    public void TruncatedRawAndCompressedPagesFailWithoutPartialResults()
    {
        byte[] value = Enumerable.Repeat((byte)3, 10_000).ToArray();
        var encoded = Encode([value]);
        using var rawHeader = new MemoryStream(); Span<byte> header = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, -20); rawHeader.Write(header); rawHeader.Position = 0;
        PageHandle raw = new PagedInputStream(rawHeader).CreateHandle(20);
        Assert.Throws<EndOfStreamException>(() => new PagedInputStream(Stream.Null).Read(raw, 20));
        using var truncated = new MemoryStream([.. encoded.Header, .. encoded.Data.AsSpan(0, encoded.Data.Length - 1)]);
        PageHandle compressed = new PagedInputStream(truncated).CreateHandle(value.Length);
        Assert.Throws<EndOfStreamException>(() => new PagedInputStream(truncated).Read(compressed, value.Length));
    }

    [Fact]
    public void CorruptCompressedPageAndAllocationBudgetAreRejected()
    {
        byte[] value = Enumerable.Repeat((byte)5, 20_000).ToArray();
        var encoded = Encode([value]);
        byte[] corrupt = (byte[])encoded.Data.Clone(); corrupt[0] = 99;
        using var stream = new MemoryStream([.. encoded.Header, .. corrupt]);
        PageHandle handle = new PagedInputStream(stream).CreateHandle(value.Length);
        Assert.Throws<NotSupportedException>(() => new PagedInputStream(stream).Read(handle, value.Length));
        using var limited = new MemoryStream([.. encoded.Header, .. encoded.Data]);
        var options = new PagedReadOptions { MaximumTotalAllocationBytes = encoded.Data.Length - 1 };
        PageHandle limitedHandle = new PagedInputStream(limited, options).CreateHandle(value.Length);
        Assert.Throws<InvalidDataException>(() => new PagedInputStream(limited, options).Read(limitedHandle, value.Length));
    }

    [Fact]
    public void LoadedPagesConsumeTheirPayloadWhenReplayedThroughAFreshReader()
    {
        byte[] headers = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(headers, -4);
        BinaryPrimitives.WriteInt32LittleEndian(headers.AsSpan(4), -4);
        using var headerStream = new MemoryStream(headers);
        var sizeReader = new PagedInputStream(headerStream);
        PageHandle first = sizeReader.CreateHandle(4);
        PageHandle second = sizeReader.CreateHandle(4);
        using (var initial = new MemoryStream([1, 1, 1, 1]))
            new PagedInputStream(initial).Read(first, 4);
        using var replay = new MemoryStream([1, 1, 1, 1, 2, 2, 2, 2]);
        var reader = new PagedInputStream(replay);
        reader.Read(first, 4);
        reader.Read(second, 4);
        Assert.Equal(8, replay.Position);
        Assert.Equal(new byte[] { 2, 2, 2, 2 }, second.Read());
    }

    [Fact]
    public void MalformedFrameAndUnloadedHandleFailBeforeLargeDecodedAllocation()
    {
        byte[] frame = new byte[16];
        frame[0] = 99;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), 10_000_000);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(8), 10_000_000);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(12), frame.Length);
        byte[] header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header, frame.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 10_000_000);
        using var stream = new MemoryStream([.. header, .. frame]);
        PageHandle malformed = new PagedInputStream(stream).CreateHandle(10_000_000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<NotSupportedException>(() => new PagedInputStream(stream).Read(malformed, 10_000_000));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1_000_000);

        byte[] rawHeader = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(rawHeader, -10_000_000);
        PageHandle unloaded = new PagedInputStream(new MemoryStream(rawHeader)).CreateHandle(10_000_000);
        before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidOperationException>(unloaded.Read);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1_000_000);
    }
    [Fact]
    public void HandlesMustBeReplayedInOriginalOrderAndWithOriginalSizes()
    {
        byte[] first = new byte[100], second = new byte[100], third = new byte[PagedOutputStream.PageSize];
        var encoded = Encode([first, second, third]);
        using var stream = new MemoryStream([.. encoded.Header, .. encoded.Data]);
        var sizeReader = new PagedInputStream(stream);
        PageHandle a = sizeReader.CreateHandle(100);
        PageHandle b = sizeReader.CreateHandle(100);
        PageHandle c = sizeReader.CreateHandle(PagedOutputStream.PageSize);
        var dataReader = new PagedInputStream(stream);
        Assert.Throws<ArgumentException>(() => dataReader.Read(a, 99));
        Assert.Throws<InvalidDataException>(() => dataReader.Read(b, 100));
        dataReader.Read(a, 100);
        Assert.Throws<InvalidDataException>(() => dataReader.Read(c, PagedOutputStream.PageSize));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FullPageEncodingReusesWorkspaceWithinAllocationBudget(bool incompressible, bool sizeOnly)
    {
        byte[] page = new byte[PagedOutputStream.PageSize];
        if (incompressible)
        {
            uint state = 0x12345678;
            for (int index = 0; index < page.Length; ++index)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                page[index] = (byte)state;
            }
        }

        using var sink = new CountingWriteStream();
        using var writer = new PagedOutputStream(sink, sizeOnly);
        for (int iteration = 0; iteration < 2; ++iteration)
        {
            writer.Write(page);
            writer.Flush();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 4; ++iteration)
        {
            writer.Write(page);
            writer.Flush();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= 32_768, $"Four steady-state pages allocated {allocated:N0} bytes.");
        Assert.True(sink.BytesWritten > 0);
    }
    [Fact]
    public void CancellationAndStreamCapabilitiesAreChecked()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new PagedInputStream(new MemoryStream(), new() { CancellationToken = cancellation.Token }));
        Assert.Throws<ArgumentException>(() => new PagedInputStream(new WriteOnlyStream()));
        Assert.Throws<ArgumentException>(() => new PagedOutputStream(new ReadOnlyStream()));
    }

    private static (byte[] Header, byte[] Data) Encode(IReadOnlyList<byte[]> blocks)
    {
        using var header = new MemoryStream();
        using (var writer = new PagedOutputStream(header, sizeOnly: true))
        {
            foreach (byte[] block in blocks) writer.Write(block);
            writer.Flush();
        }
        using var data = new MemoryStream();
        using (var writer = new PagedOutputStream(data))
        {
            foreach (byte[] block in blocks) writer.Write(block);
            writer.Flush();
        }
        return (header.ToArray(), data.ToArray());
    }

    private static byte[][] Decode((byte[] Header, byte[] Data) encoded, IReadOnlyList<int> sizes) =>
        DecodeWithHandles(encoded, sizes).Values;

    private static (PageHandle[] Handles, byte[][] Values) DecodeWithHandles((byte[] Header, byte[] Data) encoded, IReadOnlyList<int> sizes)
    {
        using var stream = new MemoryStream([.. encoded.Header, .. encoded.Data, 0xB7]);
        var sizeReader = new PagedInputStream(stream);
        PageHandle[] handles = sizes.Select(sizeReader.CreateHandle).ToArray();
        Assert.Equal(encoded.Header.Length, stream.Position);
        var dataReader = new PagedInputStream(stream);
        for (int i = 0; i < handles.Length; i++) dataReader.Read(handles[i], sizes[i]);
        Assert.Equal(encoded.Header.Length + encoded.Data.Length, stream.Position);
        Assert.Equal(0xB7, stream.ReadByte());
        return (handles, handles.Select(static handle => handle.Read()).ToArray());
    }

    private sealed class CountingWriteStream : Stream
    {
        public long BytesWritten { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => BytesWritten;
        public override long Position { get => BytesWritten; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => BytesWritten += count;
        public override void Write(ReadOnlySpan<byte> buffer) => BytesWritten += buffer.Length;
    }
    private sealed class WriteOnlyStream : MemoryStream { public override bool CanRead => false; }
    private sealed class ReadOnlyStream : MemoryStream { public override bool CanWrite => false; }
}
