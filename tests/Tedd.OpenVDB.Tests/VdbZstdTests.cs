// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Tedd.OpenVDB.IO;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbZstdTests
{
    private delegate void FrameDecoder(byte[] input, Span<byte> output, Action<long> reserve);
    private static readonly FrameDecoder DecodeFrame = typeof(VdbFile).Assembly.GetType("Tedd.OpenVDB.IO.BloscCodec")!.GetMethod("Decode", BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate<FrameDecoder>();
    private delegate void Decoder(ReadOnlySpan<byte> input, Span<byte> output, Action<long> reserve);
    private static readonly Decoder Decode = typeof(VdbFile).Assembly.GetType("Tedd.OpenVDB.IO.ZstdCodec")!.GetMethod("Decode", BindingFlags.NonPublic | BindingFlags.Static)!.CreateDelegate<Decoder>();
    private sealed record Fixture(string Name, string Kind, int Length, string Sha256, string Input);
    private static readonly Dictionary<string, Fixture> Fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "zstd", "native-zstd.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.ToDictionary(x => x.Name);
    public static IEnumerable<object[]> Cases() => Fixtures.Values.Select(x => new object[] { x.Name });
    [Theory, MemberData(nameof(Cases))]
    public void NativeDecodedBytesMatch(string name)
    {
        var fixture = Fixtures[name]; byte[] input = Convert.FromHexString(fixture.Input); byte[] guarded = Enumerable.Repeat((byte)0xa5, fixture.Length + 32).ToArray();
        if (fixture.Kind == "frame") DecodeFrame(input, guarded.AsSpan(16, fixture.Length), _ => { });
        else Decode(input, guarded.AsSpan(16, fixture.Length), _ => { });
        Assert.Equal(fixture.Sha256, Convert.ToHexStringLower(SHA256.HashData(guarded.AsSpan(16, fixture.Length))));
        Assert.All(guarded[..16], value => Assert.Equal(0xa5, value)); Assert.All(guarded[(fixture.Length + 16)..], value => Assert.Equal(0xa5, value));
    }
    [Theory]
    [InlineData("", 1)]
    [InlineData("28b52ffd", 1)]
    [InlineData("28b52ffd28", 1)]
    [InlineData("28b52ffde0ffffffffffffffff", 1)]
    [InlineData("28b52ffd2001ffffff", 1)]
    [InlineData("28b52ffd20010b0000", 1)]
    [InlineData("28b52ffd20011100004142", 1)]
    [InlineData("28b52ffd20010f000041", 1)]
    [InlineData("502a4d18ffffffff", 0)]
    [InlineData("28b52ffd20000d000000", 0)]
    public void InvalidFramesReject(string hex, int length) => Reject(Convert.FromHexString(hex), length);
    [Theory]
    [InlineData("28b52ffd21ff01")]
    [InlineData("28b52ffd22ffff01")]
    [InlineData("28b52ffd23ffffffff01")]
    [InlineData("28b52ffd00ff")]
    [InlineData("27b52ffd2000")]
    public void DictionariesExcessiveWindowsAndLegacyFormatsAreExplicit(string hex) => Assert.Throws<NotSupportedException>(() => Decode(Convert.FromHexString(hex), new byte[1], _ => { }));
    [Theory]
    [InlineData("pattern-3-4096-1-1")]
    [InlineData("literal-rle-all-sequence-rle")]
    [InlineData("repeat-offset-4-1")]
    public void EveryTruncatedPrefixAndWrongOutputSizeReject(string name)
    {
        var fixture = Fixtures[name]; byte[] input = Convert.FromHexString(fixture.Input);
        for (int length = 0; length < input.Length; ++length) Reject(input[..length], fixture.Length);
        Reject(input, fixture.Length - 1); Reject(input, fixture.Length + 1);
    }
    [Fact]
    public void ChecksumCorruptionIsRejected()
    {
        var fixture = Fixtures["pattern-3-4096-1-1"]; byte[] input = Convert.FromHexString(fixture.Input);
        input[^1] ^= 1; Reject(input, fixture.Length);
    }
    [Fact]
    public void RepeatedEntropyTableRequiresAnEarlierTable()
    {
        byte[] input = Convert.FromHexString(Fixtures["literal-rle-all-sequence-rle"].Input);
        input[12] = 0xfc; Reject(input, 16);
        input = Convert.FromHexString(Fixtures["literal-rle-all-sequence-rle"].Input); input[9] = 3; Reject(input, 16);
    }
    [Fact]
    public void ZeroRepeatDistanceAndOutOfWindowMatchReject()
    {
        byte[] input = Convert.FromHexString(Fixtures["repeat-offset-2-1"].Input);
        input[50] = 4; Reject(input, 42); // First offset becomes one; repeat-minus-one is then zero.
        input = Convert.FromHexString(Fixtures["repeat-offset-2-1"].Input); input[48] = 31; Reject(input, 42);
    }
    [Fact]
    public void AllocationBudgetIsCheckedBeforeEntropyBuffersAreCreated()
    {
        var fixture = Fixtures["pattern-3-4096-1-1"]; byte[] input = Convert.FromHexString(fixture.Input), output = Enumerable.Repeat((byte)0xa5, fixture.Length).ToArray();
        long requested = 0;
        Assert.Throws<InvalidDataException>(() => Decode(input, output, bytes => { requested = bytes; throw new InvalidDataException("budget"); }));
        Assert.True(requested > fixture.Length); Assert.All(output, value => Assert.Equal(0xa5, value));
    }
    [Theory, InlineData("pattern-3-4096-1-1"), InlineData("pattern-5-65536-19-0")]
    public void DeterministicMalformedEntropyMutationsStayWithinBuffers(string name)
    {
        var fixture = Fixtures[name]; byte[] original = Convert.FromHexString(fixture.Input); uint state = 0xabc123;
        for (int iteration = 0; iteration < 512; ++iteration)
        {
            byte[] input = (byte[])original.Clone(); state = unchecked(state * 1664525 + 1013904223); int index = (int)(state % (uint)input.Length);
            state = unchecked(state * 1664525 + 1013904223); input[index] ^= (byte)(1 + state % 255);
            byte[] guarded = Enumerable.Repeat((byte)0xa5, fixture.Length + 32).ToArray();
            Exception? error = Record.Exception(() => Decode(input, guarded.AsSpan(16, fixture.Length), bytes => { if (bytes > 200_000) throw new InvalidDataException("budget"); }));
            Assert.True(error is null or InvalidDataException or NotSupportedException, $"Mutation {iteration}, byte {index}: {error}");
            Assert.All(guarded[..16], value => Assert.Equal(0xa5, value)); Assert.All(guarded[(fixture.Length + 16)..], value => Assert.Equal(0xa5, value));
        }
    }
    private static void Reject(byte[] input, int length)
    {
        byte[] guarded = Enumerable.Repeat((byte)0xa5, length + 32).ToArray();
        Exception? error = Record.Exception(() => Decode(input, guarded.AsSpan(16, length), _ => { }));
        Assert.True(error is InvalidDataException or NotSupportedException, $"Expected bounded rejection, received {error}");
        Assert.All(guarded[..16], value => Assert.Equal(0xa5, value)); Assert.All(guarded[(length + 16)..], value => Assert.Equal(0xa5, value));
    }
    public static IEnumerable<object[]> GridCases() { for (int filter = 0; filter < 3; ++filter) for (int profile = 0; profile < 4; ++profile) yield return new object[] { $"zstd-{filter}-{profile}" }; }
    [Theory, MemberData(nameof(GridCases))]
    public void NativeGridInterchange(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "zstd");
        using var input = File.OpenRead(Path.Combine(directory, $"frame-{name}.vdb")); using var expectedFile = File.OpenRead(Path.Combine(directory, $"expected-frame-{name}.vdb"));
        var actual = VdbFile.Read<float>(input); var expected = VdbFile.Read<float>(expectedFile);
        Assert.Equal(expected.Name, actual.Name); Assert.True(expected.Tree.HasSameTopology(actual.Tree)); Assert.Equal(expected.ActiveVoxelCount, actual.ActiveVoxelCount);
        foreach (var item in expected.Tree.EnumerateValues(Tedd.OpenVDB.Tree.ValueIterationMode.All)) Assert.Equal(BitConverter.SingleToInt32Bits(item.Value), BitConverter.SingleToInt32Bits(actual.Tree.GetValue(item.Bounds.Min)));
        string? exported = Environment.GetEnvironmentVariable("TEDD_OPENVDB_ZSTD_OUTPUT_DIR");
        if (exported is not null) { Directory.CreateDirectory(exported); using var output = File.Create(Path.Combine(exported, $"managed-{name}.vdb")); VdbFile.Write(output, actual, options: new() { Compression = VdbCompression.Blosc | VdbCompression.ActiveMask }); }
    }
}
