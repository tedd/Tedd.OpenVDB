// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Points;
namespace Tedd.OpenVDB.Tests;

public class AttributeStreamNativeTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-attribute-stream.json.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    });
    public static IEnumerable<object[]> Cases()
    {
        foreach (var c in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
            foreach (bool compress in new[] { false, true })
                yield return [c.GetProperty("id").GetInt32(), c.GetProperty("type").GetString() + "/" + c.GetProperty("codec").GetString() + "/" + c.GetProperty("profile").GetInt32(), compress];
    }
    [Theory, MemberData(nameof(Cases))]
    public void NativeStreamsAndManagedExportsPreserveEncodedStorage(int index, string label, bool compress)
    {
        Assert.NotEmpty(label);
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        var c = Fixture.Value.RootElement.GetProperty("cases")[index];
        switch (c.GetProperty("type").GetString())
        {
            case "int32": Dispatch(c, e => e.GetInt32(), compress); break;
            case "float": Dispatch(c, e => e.GetSingle(), compress); break;
            case "double": Dispatch(c, e => e.GetDouble(), compress); break;
            case "vec3s": Dispatch(c, e => new Vec3f(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle()), compress); break;
            case "vec3d": Dispatch(c, e => new Vec3d(e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble()), compress); break;
            default: throw new InvalidDataException(label);
        }
    }
    private static void Dispatch<T>(JsonElement c, Func<JsonElement, T> read, bool compress) where T : unmanaged
    {
        switch (c.GetProperty("codec").GetString())
        {
            case "null": Run<T, NullCodec<T>>(c, read, compress); break;
            case "trnc": Run<T, TruncateCodec<T>>(c, read, compress); break;
            case "fxpt8": Run<T, FixedPoint8Codec<T, PositionRange>>(c, read, compress); break;
            case "fxpt16": Run<T, FixedPoint16Codec<T, PositionRange>>(c, read, compress); break;
            case "ufxpt8": Run<T, FixedPoint8Codec<T, UnitRange>>(c, read, compress); break;
            case "ufxpt16": Run<T, FixedPoint16Codec<T, UnitRange>>(c, read, compress); break;
            case "uvec": Run<T, UnitVecCodec<T>>(c, read, compress); break;
            default: throw new InvalidDataException();
        }
    }
    private static void Run<T, TCodec>(JsonElement c, Func<JsonElement, T> read, bool compress)
        where T : unmanaged where TCodec : unmanaged, IAttributeCodec<T>
    {
        var source = new TypedAttributeArray<T, TCodec>(c.GetProperty("n").GetInt32(), c.GetProperty("s").GetInt32(), c.GetProperty("constant").GetInt32() != 0, read(c.GetProperty("initial")));
        if (c.GetProperty("expanded").GetInt32() != 0)
        {
            source.Expand(); int i = 0;
            foreach (var input in c.GetProperty("inputs").EnumerateArray()) source.Set(i++, read(input));
        }
        var flags = (AttributeFlags)c.GetProperty("flags").GetInt32();
        source.IsHidden = flags.HasFlag(AttributeFlags.Hidden); source.IsTransient = flags.HasFlag(AttributeFlags.Transient); source.IsStreaming = flags.HasFlag(AttributeFlags.Streaming);
        Snapshot(c, source, read);
        string encoding = compress ? "blosc" : "raw";
        byte[] native = Convert.FromHexString(c.GetProperty(encoding).GetString()!);
        using var written = new MemoryStream();
        AttributeStreams.Write(written, source, compress, c.GetProperty("outputTransient").GetInt32() != 0);
        byte[] managed = written.ToArray();
        if (!compress || c.GetProperty("omitted").GetInt32() != 0) Assert.Equal(native, managed);
        string? export = Environment.GetEnvironmentVariable("VDB_ATTRIBUTE_STREAM_EXPORT");
        if (!string.IsNullOrEmpty(export))
        {
            Directory.CreateDirectory(export);
            string target = Path.Combine(export, c.GetProperty("id").GetInt32() + "-" + encoding + ".bin");
            File.WriteAllBytes(target + ".tmp", managed); File.Move(target + ".tmp", target, true);
        }
        if (c.GetProperty("omitted").GetInt32() != 0) { Assert.Empty(native); return; }
        using var inputStream = new MemoryStream([.. native, 0xA7]);
        var decoded = AttributeStreams.Read<T, TCodec>(inputStream); Snapshot(c, decoded, read);
        Assert.Equal(native.Length, inputStream.Position); Assert.Equal(0xA7, inputStream.ReadByte());
        inputStream.Position = 0;
        var metadata = AttributeStreams.ReadMetadata(inputStream);
        int headerSize = (native[9] & 1) != 0 ? 18 : 14;
        Assert.Equal(headerSize, inputStream.Position);
        Assert.Equal(flags, metadata.Flags); Assert.Equal(source.Size, metadata.Size);
        Assert.Equal(source.HasConstantStride ? source.Stride : source.DataSize, metadata.StrideOrTotalSize);
        Assert.Equal(BinaryPrimitives.ReadUInt64LittleEndian(native) - 6, (ulong)metadata.EncodedBytes);
        Snapshot(c, AttributeStreams.ReadBuffers<T, TCodec>(inputStream, metadata), read);
        Assert.Equal(native.Length, inputStream.Position);
        written.Position = 0; Snapshot(c, AttributeStreams.Read<T, TCodec>(written), read);
        Assert.Equal(written.Length, written.Position);
    }
    private static void Snapshot<T>(JsonElement expected, AttributeArray<T> array, Func<JsonElement, T> read) where T : unmanaged
    {
        Assert.Equal(expected.GetProperty("size").GetInt32(), array.Size); Assert.Equal(expected.GetProperty("stride").GetInt32(), array.Stride);
        Assert.Equal(expected.GetProperty("dataSize").GetInt32(), array.DataSize); Assert.Equal(expected.GetProperty("flags").GetInt32(), (int)array.Flags);
        Assert.Equal(expected.GetProperty("uniform").GetInt32() != 0, array.IsUniform);
        Assert.Equal(expected.GetProperty("storageSize").GetInt32(), array.StorageTypeSize);
        Assert.Equal(expected.GetProperty("storage").GetString(), Convert.ToHexStringLower(array.GetStorageBytes()));
        var values = expected.GetProperty("values"); Assert.Equal(values.GetArrayLength(), array.DataSize);
        for (int i = 0; i < values.GetArrayLength(); i++)
        {
            T a = read(values[i]), b = array.Get(i);
            if (typeof(T) == typeof(Vec3f))
            {
                var x = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateReadOnlySpan(ref a, 1)); var y = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateReadOnlySpan(ref b, 1));
                for (int j = 0; j < 3; j++) Assert.Equal(x[j], y[j]);
            }
            else if (typeof(T) == typeof(Vec3d))
            {
                var x = MemoryMarshal.Cast<T, double>(MemoryMarshal.CreateReadOnlySpan(ref a, 1)); var y = MemoryMarshal.Cast<T, double>(MemoryMarshal.CreateReadOnlySpan(ref b, 1));
                for (int j = 0; j < 3; j++) Assert.Equal(x[j], y[j]);
            }
            else Assert.Equal(a, b);
        }
    }
    [Fact]
    public void NativeFixtureContainsCompressionPaddingFallbackAndTransientOmissions()
    {
        int compressed = 0, padded = 0, fallback = 0, skipped = 0;
        foreach (var c in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
        {
            byte[] bytes = Convert.FromHexString(c.GetProperty("blosc").GetString()!);
            if (bytes.Length == 0) { skipped++; continue; }
            if ((bytes[9] & 2) != 0) continue;
            int marker = (bytes[9] & 1) != 0 ? 18 : 14;
            if (bytes[marker] == 0) { fallback++; continue; }
            compressed++;
            uint decodedBytes = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(marker + 5));
            if (decodedBytes == 128 && c.GetProperty("dataSize").GetInt32() * c.GetProperty("storageSize").GetInt32() < 128) padded++;
        }
        Assert.True(compressed > 100); Assert.True(padded > 25); Assert.True(fallback > 25); Assert.Equal(50, skipped);
    }
}
