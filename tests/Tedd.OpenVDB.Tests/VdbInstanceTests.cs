// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Text;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
namespace Tedd.OpenVDB.Tests;

public class VdbInstanceTests
{
    private static readonly string[] Keys = ["forward", "forward-chain-unresolved", "forward-chain-resolved", "chain-name-order", "half-child", "half-parent", "unique-suffix", "forward-int32", "forward-int64", "forward-float", "forward-double", "forward-bool", "forward-mask", "forward-vec3i", "forward-vec3s", "forward-vec3d"];
    private static string PathFor(string file) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "instances", file + ".vdb");
    private static byte[] Bytes(string file) => File.ReadAllBytes(PathFor(file));
    public static IEnumerable<object[]> Profiles()
    {
        foreach (string key in Keys) foreach (string form in new[] { "seek", "stream" })
            foreach (bool resolved in new[] { false, true }) foreach (bool streaming in new[] { false, true }) yield return [key + "-" + form, resolved, streaming];
    }
    [Theory, MemberData(nameof(Profiles))]
    public void NativeProfilesMatchStreamOrderOrExplicitResolution(string profile, bool resolved, bool streaming)
    {
        using Stream input = streaming ? new StreamingInput(Bytes("native-" + profile)) : new MemoryStream(Bytes("native-" + profile));
        var actual = VdbFile.ReadArchive(input, options: new() { InstanceResolution = resolved ? VdbInstanceResolution.ResolveDependencies : VdbInstanceResolution.NativeOrder });
        using var canonical = File.OpenRead(PathFor($"expected-{(resolved ? "resolved" : "native")}-{profile}"));
        var expected = VdbFile.ReadArchive(canonical);
        Assert.Equal(expected.Metadata.OrderBy(p => p.Key), actual.Metadata.OrderBy(p => p.Key)); Assert.Equal(expected.Grids.Count, actual.Grids.Count);
        for (int index = 0; index < actual.Grids.Count; ++index)
        {
            Compare(expected.Grids[index], actual.Grids[index]);
            for (int previous = 0; previous < index; ++previous)
            {
                if (actual.Grids[index] is MaskGrid || Half(actual.Grids[index]) != Half(actual.Grids[previous])) continue;
                Assert.Equal(ReferenceEquals(Tree(expected.Grids[index]), Tree(expected.Grids[previous])), ReferenceEquals(Tree(actual.Grids[index]), Tree(actual.Grids[previous])));
            }
        }
        string? directory = Environment.GetEnvironmentVariable("TEDD_OPENVDB_INSTANCE_OUTPUT_DIR");
        if (!streaming && directory is not null)
        {
            Directory.CreateDirectory(directory); using var output = new MemoryStream();
            VdbFile.WriteArchive(output, actual, options: new() { Compression = VdbCompression.ActiveMask | VdbCompression.Zlib });
            File.WriteAllBytes(Path.Combine(directory, $"managed-{(resolved ? "resolved" : "native")}-{profile}.vdb"), output.ToArray());
        }
        if (profile.StartsWith("half-", StringComparison.Ordinal)) Assert.Same(Tree(actual.Grids[0]), Tree(actual.Grids[1]));
        if (actual.Grids[0] is MaskGrid mask)
        {
            mask.SetValueOn(new(1000, 1000, 1000));
            Assert.All(actual.Grids, grid => Assert.True(Assert.IsType<MaskGrid>(grid).IsValueOn(new(1000, 1000, 1000))));
        }
    }
    private static bool Half(IGrid grid) => grid.Metadata.TryGetValue("is_saved_as_half_float", out var value) && (bool)value.Value;
    private static object Tree(IGrid grid) => grid switch
    {
        Grid<int> g => g.Tree,
        Grid<long> g => g.Tree,
        Grid<float> g => g.Tree,
        Grid<double> g => g.Tree,
        Grid<bool> g => g.Tree,
        Grid<Vec3i> g => g.Tree,
        Grid<Vec3f> g => g.Tree,
        Grid<Vec3d> g => g.Tree,
        _ => throw new InvalidOperationException()
    };
    private static void Compare(IGrid expected, IGrid actual)
    {
        Assert.Equal(expected.GetType(), actual.GetType()); Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Metadata.OrderBy(p => p.Key), actual.Metadata.OrderBy(p => p.Key));
        Assert.Equal(expected.Transform.IndexToWorld(new Vec3d(1.25, -2, 5)), actual.Transform.IndexToWorld(new Vec3d(1.25, -2, 5)));
        switch (expected)
        {
            case Grid<int> g: CompareTree(g.Tree, Assert.IsType<Grid<int>>(actual).Tree); break;
            case Grid<long> g: CompareTree(g.Tree, Assert.IsType<Grid<long>>(actual).Tree); break;
            case Grid<float> g: CompareTree(g.Tree, Assert.IsType<Grid<float>>(actual).Tree); break;
            case Grid<double> g: CompareTree(g.Tree, Assert.IsType<Grid<double>>(actual).Tree); break;
            case Grid<bool> g: CompareTree(g.Tree, Assert.IsType<Grid<bool>>(actual).Tree); break;
            case Grid<Vec3i> g: CompareTree(g.Tree, Assert.IsType<Grid<Vec3i>>(actual).Tree); break;
            case Grid<Vec3f> g: CompareTree(g.Tree, Assert.IsType<Grid<Vec3f>>(actual).Tree); break;
            case Grid<Vec3d> g: CompareTree(g.Tree, Assert.IsType<Grid<Vec3d>>(actual).Tree); break;
            case MaskGrid g: CompareTree(g.ToBooleanGrid().Tree, Assert.IsType<MaskGrid>(actual).ToBooleanGrid().Tree); break;
        }
    }
    private static void CompareTree<T>(VdbTree<T> expected, VdbTree<T> actual) where T : unmanaged
    {
        Assert.Equal(expected.Background, actual.Background); Assert.Equal(expected.ActiveVoxelCount, actual.ActiveVoxelCount); Assert.Equal(expected.LeafCount, actual.LeafCount);
        Assert.True(expected.HasSameTopology(actual));
        foreach (var value in expected.EnumerateValues(ValueIterationMode.All))
        { Assert.Equal(value.Value, actual.GetValue(value.Coordinate)); Assert.Equal(value.IsActive, actual.IsValueOn(value.Coordinate)); }
    }
    public static IEnumerable<object[]> InvalidProfiles()
    {
        foreach (string key in new[] { "cycle", "self", "missing", "type-mismatch", "duplicate" }) foreach (string form in new[] { "seek", "stream" })
            foreach (bool resolved in new[] { false, true }) foreach (bool streaming in new[] { false, true }) yield return [key + "-" + form, resolved, streaming];
    }
    [Theory, MemberData(nameof(InvalidProfiles))]
    public void InvalidGraphsAreRejectedBeforeReturn(string profile, bool resolved, bool streaming)
    {
        using Stream input = streaming ? new StreamingInput(Bytes("native-" + profile)) : new MemoryStream(Bytes("native-" + profile));
        Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(input, options: new() { InstanceResolution = resolved ? VdbInstanceResolution.ResolveDependencies : VdbInstanceResolution.NativeOrder }));
        Assert.True(input.CanRead);
    }
    [Theory, InlineData(false), InlineData(true)]
    public void MaskAndBooleanInstanceTypesCannotAlias(bool mask)
    {
        byte[] bytes = Bytes("native-forward-" + (mask ? "mask" : "bool") + "-seek");
        byte[] from = Encoding.UTF8.GetBytes(mask ? "Tree_mask_5_4_3" : "Tree_bool_5_4_3");
        Encoding.UTF8.GetBytes(mask ? "Tree_bool_5_4_3" : "Tree_mask_5_4_3").CopyTo(bytes, bytes.AsSpan().IndexOf(from));
        Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(new MemoryStream(bytes)));
    }
    [Fact]
    public void DefaultNativeChainBehaviorIsDeliberateAndResolutionIsOptIn()
    {
        using var source = new MemoryStream(Bytes("native-forward-chain-unresolved-seek"));
        var native = VdbFile.ReadArchive(source); var a = Assert.IsType<Grid<float>>(native.Grids[0]); var b = Assert.IsType<Grid<float>>(native.Grids[1]);
        Assert.Equal(0, a.Background); Assert.Equal(0, a.Tree.LeafCount); Assert.Equal(2, b.Tree.LeafCount); Assert.NotSame(a.Tree, b.Tree);
        source.Position = 0; var resolved = VdbFile.ReadArchive(source, options: new() { InstanceResolution = VdbInstanceResolution.ResolveDependencies });
        Assert.All(resolved.Grids, grid => Assert.Same(Tree(resolved.Grids[0]), Tree(grid)));
        var first = Assert.IsType<Grid<float>>(resolved.Grids[0]); var second = Assert.IsType<Grid<float>>(resolved.Grids[1]);
        first.SetMetadata("independent", 123); first.Transform = Transform.CreateLinearTransform(10);
        Assert.Equal(1, second.Metadata["independent"].Value); Assert.NotEqual(first.Transform.Matrix, second.Transform.Matrix);
    }
    [Theory, InlineData(false), InlineData(true)]
    public void HalfFlagsAreIndependentOfTheSharedTree(bool parentHalf)
    {
        using var input = new MemoryStream(Bytes("native-half-" + (parentHalf ? "parent" : "child") + "-seek"));
        var archive = VdbFile.ReadArchive(input); Assert.Same(Tree(archive.Grids[0]), Tree(archive.Grids[1]));
        Assert.Equal(!parentHalf, Half(archive.Grids[0])); Assert.Equal(parentHalf, Half(archive.Grids[1]));
    }
    [Theory, InlineData(false), InlineData(true)]
    public void LongChainsUseBoundedIterationAndChargeStoredNodesOnce(bool cycle)
    {
        byte[] bytes = Chain(4096, cycle); using var source = new StreamingInput(bytes);
        var options = new VdbReadOptions { InstanceResolution = VdbInstanceResolution.ResolveDependencies, MaxNodes = 6, MaxAllocatedBytes = 16 * 1024 * 1024 };
        if (cycle) Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(source, options: options));
        else
        {
            var archive = VdbFile.ReadArchive(source, options: options); Assert.Equal(4096, archive.Grids.Count);
            Assert.All(archive.Grids, grid => Assert.Same(Tree(archive.Grids[0]), Tree(grid)));
            Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(new MemoryStream(bytes), options: options with { MaxNodes = 5 }));
        }
    }
    [Theory, InlineData("grids"), InlineData("allocation"), InlineData("bytes")]
    public void InstanceGraphsRespectResourceBudgets(string budget)
    {
        byte[] bytes = Chain(64, false);
        VdbReadOptions options = budget switch { "grids" => new() { MaxGrids = 63 }, "allocation" => new() { MaxAllocatedBytes = 4096 }, _ => new() { MaxBytes = bytes.Length - 1 } };
        Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(new MemoryStream(bytes), options: options));
    }
    [Fact]
    public void InvalidOptionsAndDeferredResolutionFailuresHonorOwnership()
    {
        var empty = new MemoryStream(); Assert.Throws<ArgumentOutOfRangeException>(() => VdbFile.ReadArchive(empty, leaveOpen: false, options: new() { InstanceResolution = (VdbInstanceResolution)42 })); Assert.False(empty.CanRead);
        var missing = new MemoryStream(Bytes("native-missing-seek")); Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(missing, leaveOpen: false)); Assert.False(missing.CanRead);
        byte[] forward = Bytes("native-forward-seek");
        using var truncated = new StreamingInput(forward[..^1]); Assert.Throws<InvalidDataException>(() => VdbFile.ReadArchive(truncated)); Assert.True(truncated.CanRead);
    }
    // Reuse actual native metadata/transform/tree payloads and emit standard streaming
    // descriptors with unique names, avoiding large committed fixtures for depth tests.
    private static byte[] Chain(int count, bool cycle)
    {
        byte[] native = Bytes("native-unique-suffix-seek"); using var source = new MemoryStream(native); using var reader = new BinaryReader(source, Encoding.UTF8);
        source.Position = 57; uint metadata = reader.ReadUInt32();
        for (uint index = 0; index < metadata; ++index) { ReadString(reader); ReadString(reader); uint size = reader.ReadUInt32(); source.Position += size; }
        int countPosition = (int)source.Position; reader.ReadInt32(); int first = (int)source.Position;
        ReadString(reader); string type = ReadString(reader); ReadString(reader); long payload0 = reader.ReadInt64(); reader.ReadInt64(); long end0 = reader.ReadInt64();
        source.Position = end0; ReadString(reader); ReadString(reader); ReadString(reader); long payload1 = reader.ReadInt64(); reader.ReadInt64(); long end1 = reader.ReadInt64();
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        byte[] header = native[..first]; header[20] = 0; BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(countPosition), count); writer.Write(header);
        for (int index = 0; index < count; ++index)
        {
            bool instance = index != count - 1 || cycle;
            WriteString(writer, "repeat\x1e" + index); WriteString(writer, type); WriteString(writer, instance ? "repeat\x1e" + ((index + 1) % count) : "");
            writer.Write(0L); writer.Write(0L); writer.Write(0L);
            int start = checked((int)(instance ? payload0 : payload1)), length = checked((int)(instance ? end0 - payload0 : end1 - payload1));
            writer.Write(native.AsSpan(start, length));
        }
        return output.ToArray();
    }
    private static string ReadString(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
    private static void WriteString(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
    private sealed class StreamingInput(byte[] bytes) : Stream
    {
        private readonly MemoryStream _input = new(bytes);
        public override bool CanRead => _input.CanRead; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _input.Read(buffer);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _input.Dispose(); base.Dispose(disposing); }
    }
}
