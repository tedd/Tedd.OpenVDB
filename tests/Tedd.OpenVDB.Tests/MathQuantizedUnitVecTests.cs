using System.Runtime.InteropServices;
using System.Text.Json;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public class MathQuantizedUnitVecTests
{
    private static readonly JsonDocument Fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-quantized-unit-vector.json")));
    public static IEnumerable<object[]> NativeCases()
    {
        int index = 0; foreach (var test in Fixture.RootElement.GetProperty("cases").EnumerateArray()) yield return [index++, test.GetProperty("kind").GetString()!, test.GetProperty("precision").GetString()!];
    }
    [Fact]
    public void FixtureExhaustivelyCoversAllWordsWithPinnedProvenance()
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.RootElement.GetProperty("upstreamCommit").GetString());
        Assert.Equal(384, Fixture.RootElement.GetProperty("cases").GetArrayLength());
        var cases = Fixture.RootElement.GetProperty("cases");
        for (int block = 0; block < 256; ++block) { Assert.Equal("decode", cases[block].GetProperty("kind").GetString()); Assert.Equal(block * 256, cases[block].GetProperty("start").GetInt32()); Assert.Equal(256, cases[block].GetProperty("values").GetArrayLength()); }
    }
    private static void Bits(JsonElement expected, Vec3f actual)
    {
        Assert.Equal(expected[0].GetUInt32(), BitConverter.SingleToUInt32Bits(actual.X));
        Assert.Equal(expected[1].GetUInt32(), BitConverter.SingleToUInt32Bits(actual.Y));
        Assert.Equal(expected[2].GetUInt32(), BitConverter.SingleToUInt32Bits(actual.Z));
    }
    [Theory]
    [MemberData(nameof(NativeCases))]
    public void EveryNativeDecodeAndPackBlockMatchesExactly(int index, string kind, string precision)
    {
        var test = Fixture.RootElement.GetProperty("cases")[index]; var values = test.GetProperty("values");
        if (kind == "decode")
        {
            int start = test.GetProperty("start").GetInt32(); var codes = new ushort[256]; var unpacked = new Vec3f[256];
            for (int i = 0; i < 256; ++i)
            {
                ushort code = (ushort)(start + i); codes[i] = code; Bits(values[i], QuantizedUnitVec.Unpack(code));
                Assert.Equal(values[i][3].GetUInt16(), QuantizedUnitVec.FlipSignBits(code)); QuantizedUnitVec.FlipSignBits(ref code); Assert.Equal(values[i][3].GetUInt16(), code);
            }
            QuantizedUnitVec.Unpack(codes, unpacked); for (int i = 0; i < 256; ++i) Bits(values[i], unpacked[i]);
            QuantizedUnitVec.FlipSignBits(codes.AsSpan()); for (int i = 0; i < 256; ++i) Assert.Equal(values[i][3].GetUInt16(), codes[i]); return;
        }
        var encoded = new ushort[64];
        if (precision == "float")
        {
            var vectors = new Vec3f[64];
            for (int i = 0; i < 64; ++i) { vectors[i] = new(values[i][0].GetSingle(), values[i][1].GetSingle(), values[i][2].GetSingle()); Assert.Equal(values[i][3].GetUInt16(), QuantizedUnitVec.Pack(vectors[i])); }
            QuantizedUnitVec.Pack(vectors, encoded);
        }
        else
        {
            var vectors = new Vec3d[64];
            for (int i = 0; i < 64; ++i) { vectors[i] = new(values[i][0].GetDouble(), values[i][1].GetDouble(), values[i][2].GetDouble()); Assert.Equal(values[i][3].GetUInt16(), QuantizedUnitVec.Pack(vectors[i])); }
            QuantizedUnitVec.Pack(vectors, encoded);
        }
        for (int i = 0; i < 64; ++i) Assert.Equal(values[i][3].GetUInt16(), encoded[i]);
    }
    [Fact]
    public void ZeroInputAndSignBitSemanticsMatchNative()
    {
        Assert.Equal((ushort)0, QuantizedUnitVec.Pack(Vec3f.Zero)); Assert.Equal((ushort)0, QuantizedUnitVec.Pack(new Vec3d(-0.0)));
        Assert.Equal(Vec3f.UnitZ, QuantizedUnitVec.Unpack(0));
        var minusZ = QuantizedUnitVec.Unpack(QuantizedUnitVec.FlipSignBits(0));
        Assert.Equal(BitConverter.SingleToUInt32Bits(-0f), BitConverter.SingleToUInt32Bits(minusZ.X));
        Assert.Equal(BitConverter.SingleToUInt32Bits(-0f), BitConverter.SingleToUInt32Bits(minusZ.Y)); Assert.Equal(-1, minusZ.Z);
        foreach (var axis in new[] { Vec3f.UnitX, Vec3f.UnitY, Vec3f.UnitZ, -Vec3f.UnitX, -Vec3f.UnitY, -Vec3f.UnitZ }) Assert.Equal(axis, QuantizedUnitVec.Unpack(QuantizedUnitVec.Pack(axis)));
    }
    [Fact]
    public void InvalidPackInputsThrowWhereNativeIntegerConversionIsUndefined()
    {
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => QuantizedUnitVec.Pack(new Vec3d(invalid, 1, 2)));
            Assert.Throws<ArgumentOutOfRangeException>(() => QuantizedUnitVec.Pack(new Vec3f(1, (float)invalid, 2)));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantizedUnitVec.Pack(new Vec3f(float.Epsilon, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => QuantizedUnitVec.Pack(new Vec3d(double.Epsilon, 0, 0)));
    }
    [Fact]
    public void BatchBoundsAndAliasingAreCheckedBeforeWrites()
    {
        var input = new[] { Vec3f.UnitX, Vec3f.UnitY, Vec3f.UnitZ }; ushort[] shortOutput = [77, 88];
        Assert.Throws<ArgumentException>(() => QuantizedUnitVec.Pack(input, shortOutput)); Assert.Equal(new ushort[] { 77, 88 }, shortOutput);
        ushort[] output = [0, 0, 0, 1234]; QuantizedUnitVec.Pack(input, output); Assert.Equal((ushort)1234, output[3]);
        var decoded = new Vec3f[4]; decoded[3] = new(77); QuantizedUnitVec.Unpack(output.AsSpan(0, 3), decoded); Assert.Equal(new Vec3f(77), decoded[3]);
        Assert.Throws<ArgumentException>(() => QuantizedUnitVec.Unpack(output, new Vec3f[3]));
        Assert.Throws<ArgumentException>(() => QuantizedUnitVec.Pack(input, MemoryMarshal.Cast<Vec3f, ushort>(input.AsSpan())));
        byte[] overlapping = new byte[48]; Assert.Throws<ArgumentException>(() => QuantizedUnitVec.Unpack(MemoryMarshal.Cast<byte, ushort>(overlapping).Slice(0, 3), MemoryMarshal.Cast<byte, Vec3f>(overlapping).Slice(0, 3)));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(65)]
    public void SignFlipBatchHandlesSimdTailsAndIsInvolutive(int count)
    {
        ushort[] codes = Enumerable.Range(0, count).Select(i => (ushort)(i * 997)).ToArray(); ushort[] original = (ushort[])codes.Clone();
        QuantizedUnitVec.FlipSignBits(codes); for (int i = 0; i < count; ++i) Assert.Equal((ushort)(original[i] ^ 0xE000), codes[i]);
        QuantizedUnitVec.FlipSignBits(codes); Assert.Equal(original, codes);
    }
    [Fact]
    public void SeededUnitSphereNormalsRespectNativeErrorBounds()
    {
        var random = new Random(617391);
        for (int i = 0; i < 40000; ++i)
        {
            var vector = new Vec3f((float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1), (float)(random.NextDouble() * 2 - 1)).Unit();
            var decoded = QuantizedUnitVec.Unpack(QuantizedUnitVec.Pack(vector)); Assert.InRange(System.MathF.Abs(decoded.Length - 1), 0, 1e-6f);
            float error = 0; for (int axis = 0; axis < 3; ++axis) { float delta = System.MathF.Abs(decoded[axis] - vector[axis]); Assert.InRange(delta, 0, .05f); error += delta; }
            Assert.True(error < .1f);
        }
    }
    [Fact]
    public void ScalarAndBatchCallsAllocateNoManagedStorage()
    {
        var vectors = Enumerable.Repeat(new Vec3f(.3f, -.4f, .5f), 65).ToArray(); var codes = new ushort[65]; var unpacked = new Vec3f[65];
        for (int i = 0; i < 10; ++i) { QuantizedUnitVec.Pack(vectors, codes); QuantizedUnitVec.Unpack(codes, unpacked); QuantizedUnitVec.FlipSignBits(codes); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; ++i) QuantizedUnitVec.Pack(vectors, codes);
        long pack = GC.GetAllocatedBytesForCurrentThread() - before; before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; ++i) QuantizedUnitVec.Unpack(codes, unpacked);
        long unpack = GC.GetAllocatedBytesForCurrentThread() - before; before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; ++i) QuantizedUnitVec.FlipSignBits(codes);
        long flip = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(pack == 0 && unpack == 0 && flip == 0, $"Allocated bytes: pack {pack}, unpack {unpack}, flip {flip}.");
    }
}
