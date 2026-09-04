// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Tedd.OpenVDB.IO;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbCodecTests
{
    private delegate void RawDecoder(ReadOnlySpan<byte> input, Span<byte> output);
    private delegate void FilterDecoder(ReadOnlySpan<byte> input, Span<byte> output, int typeSize);
    private delegate void FrameDecoder(byte[] input, Span<byte> output, Action<long> reserve);
    private static T Method<T>(string type, string method) where T : Delegate => typeof(VdbFile).Assembly.GetType("Tedd.OpenVDB.IO." + type)!.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate<T>();
    private static readonly RawDecoder BloscLz = Method<RawDecoder>("BloscLzCodec", "Decode");
    private static readonly RawDecoder Snappy = Method<RawDecoder>("SnappyBlockCodec", "Decode");
    private static readonly FilterDecoder BitShuffle = Method<FilterDecoder>("BitShuffleCodec", "Unshuffle");
    private static readonly FrameDecoder Frame = Method<FrameDecoder>("BloscCodec", "Decode");
    private sealed record Fixture(string Name, string Kind, int TypeSize, int Length, string Sha256, string Input);
    private static readonly Dictionary<string, Fixture> Fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "codecs", "native-codecs.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.ToDictionary(x => x.Name);
    public static IEnumerable<object[]> Cases() => Fixtures.Keys.Select(name => new object[] { name });
    [Theory, MemberData(nameof(Cases))]
    public void NativeCodecOutputMatchesEveryByte(string name)
    {
        var fixture = Fixtures[name]; byte[] source = Convert.FromHexString(fixture.Input);
        byte[] guarded = new byte[fixture.Length + 32]; Array.Fill(guarded, (byte)0xa5); Span<byte> output = guarded.AsSpan(16, fixture.Length);
        switch (fixture.Kind)
        {
            case "blosclz": BloscLz(source, output); break;
            case "snappy": Snappy(source, output); break;
            case "bitshuffle": BitShuffle(source, output, fixture.TypeSize); break;
            case "frame": Frame(source, output, _ => { }); break;
            default: throw new InvalidDataException(fixture.Kind);
        }
        Assert.Equal(fixture.Sha256, Convert.ToHexStringLower(SHA256.HashData(output)));
        Assert.All(guarded[..16], value => Assert.Equal(0xa5, value)); Assert.All(guarded[(16 + fixture.Length)..], value => Assert.Equal(0xa5, value));
    }
    public static IEnumerable<object[]> GridCases()
    {
        foreach (string codec in new[] { "blosclz", "snappy" })
            for (int filter = 0; filter < 3; ++filter)
                for (int profile = 0; profile < 4; ++profile) yield return new object[] { $"{codec}-{filter}-{profile}" };
    }
    [Theory, MemberData(nameof(GridCases))]
    public void NativeCompressedGridFilesDecodeAndRemainNativeWritable(string name)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "codecs");
        using var expectedFile = File.OpenRead(Path.Combine(folder, $"expected-frame-{name}.vdb"));
        using var source = File.OpenRead(Path.Combine(folder, $"frame-{name}.vdb"));
        var expected = VdbFile.Read<float>(expectedFile); var actual = VdbFile.Read<float>(source);
        Assert.Equal(expected.Name, actual.Name); Assert.Equal(expected.ActiveVoxelCount, actual.ActiveVoxelCount);
        Assert.True(expected.Tree.HasSameTopology(actual.Tree));
        for (int x = 0; x < 8; ++x) for (int y = 0; y < 8; ++y) for (int z = 0; z < 8; ++z)
        {
            var coordinate = new Tedd.OpenVDB.Math.Coord(x, y, z);
            Assert.Equal(BitConverter.SingleToInt32Bits(expected.Tree.GetValue(coordinate)), BitConverter.SingleToInt32Bits(actual.Tree.GetValue(coordinate)));
        }
        string? output = Environment.GetEnvironmentVariable("TEDD_OPENVDB_CODEC_OUTPUT_DIR");
        if (output is not null)
        {
            Directory.CreateDirectory(output); using var file = File.Create(Path.Combine(output, $"managed-{name}.vdb"));
            VdbFile.Write(file, actual, options: new() { Compression = VdbCompression.Blosc | VdbCompression.ActiveMask });
        }
    }
    [Theory]
    [InlineData("", 1)]
    [InlineData("80", 1)]
    [InlineData("8080808080", 1)]
    [InlineData("ffffffff1f", 1)]
    [InlineData("ffffffff0f", 1)]
    [InlineData("0000", 0)]
    [InlineData("0100", 1)]
    [InlineData("01fcffffffff", 1)]
    [InlineData("01f0", 1)]
    [InlineData("01f400", 1)]
    [InlineData("01f80000", 1)]
    [InlineData("01fc000000", 1)]
    [InlineData("040100", 4)]
    [InlineData("040101", 4)]
    [InlineData("0402", 4)]
    [InlineData("040200", 4)]
    [InlineData("04020000", 4)]
    [InlineData("0403", 4)]
    [InlineData("0403000000", 4)]
    [InlineData("0403ffffffff", 4)]
    [InlineData("040300000000", 4)]
    [InlineData("0400410101", 4)]
    [InlineData("0200410001ff", 2)]
    public void CorruptSnappyTokensRejectWithoutWritingOutsideDestination(string hex, int length) => Reject(Snappy, Convert.FromHexString(hex), length);
    [Theory]
    [InlineData("", 1)]
    [InlineData("00", 1)]
    [InlineData("1f00", 32)]
    [InlineData("0041", 2)]
    [InlineData("014142", 1)]
    [InlineData("004120", 4)]
    [InlineData("00412000", 4)]
    [InlineData("004120010042", 5)]
    [InlineData("0041e0", 10)]
    [InlineData("0041e0ff", 1000)]
    [InlineData("0041e0ff00", 1000)]
    [InlineData("0041e0ff00000042", 10)]
    [InlineData("00413fff", 5)]
    [InlineData("00413fff00", 5)]
    [InlineData("00413fff00000042", 5)]
    public void CorruptBloscLzTokensRejectWithoutWritingOutsideDestination(string hex, int length) => Reject(BloscLz, Convert.FromHexString(hex), length);
    private static void Reject(RawDecoder decoder, byte[] input, int length)
    {
        byte[] guarded = Enumerable.Repeat((byte)0xa5, length + 32).ToArray();
        Assert.Throws<InvalidDataException>(() => decoder(input, guarded.AsSpan(16, length)));
        Assert.All(guarded[..16], value => Assert.Equal(0xa5, value)); Assert.All(guarded[(length + 16)..], value => Assert.Equal(0xa5, value));
    }
    [Theory, InlineData("snappy-pattern-0-1024"), InlineData("blosclz-pattern-0-1024-9"), InlineData("blosclz-copy-8192-264")]
    public void EveryTruncatedRawPrefixRejects(string name)
    {
        var fixture = Fixtures[name]; byte[] input = Convert.FromHexString(fixture.Input); RawDecoder decoder = fixture.Kind == "snappy" ? Snappy : BloscLz;
        for (int length = 0; length < input.Length; ++length) Reject(decoder, input[..length], fixture.Length);
        Reject(decoder, input, fixture.Length - 1); Reject(decoder, input, fixture.Length + 1);
    }
    [Theory, InlineData(0), InlineData(-1)]
    public void InvalidBitshuffleDimensionsReject(int typeSize) => Assert.Throws<InvalidDataException>(() => BitShuffle(new byte[8], new byte[8], typeSize));
    [Fact]
    public void BitshuffleMismatchedOutputLengthRejects() => Assert.Throws<InvalidDataException>(() => BitShuffle(new byte[8], new byte[7], 1));
    [Fact]
    public void BitshuffleScratchAllocationIsChargedBeforeDecoding()
    {
        var fixture = Fixtures.Values.First(x => x.Kind == "frame" && x.Name.StartsWith("snappy-frame-2-4-8192-"));
        byte[] input = Convert.FromHexString(fixture.Input), output = Enumerable.Repeat((byte)0xa5, fixture.Length).ToArray();
        int blockSize = BinaryPrimitives.ReadInt32LittleEndian(input.AsSpan(8)); long charged = 0;
        Assert.Throws<InvalidDataException>(() => Frame(input, output, bytes => { charged += bytes; if (bytes == blockSize) throw new InvalidDataException("budget"); }));
        Assert.True(charged > blockSize); Assert.All(output, value => Assert.Equal(0xa5, value));
    }
}
