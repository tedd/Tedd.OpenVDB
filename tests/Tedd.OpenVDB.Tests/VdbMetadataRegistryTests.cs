// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbMetadataRegistryTests
{
    private readonly record struct Material(int Id, float Weight);
    private sealed record Observation(string Name, string Value);
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "registry");
    private static readonly Dictionary<string, string> Native = JsonSerializer.Deserialize<Observation[]>(File.ReadAllText(Path.Combine(Folder, "native-registry.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.ToDictionary(x => x.Name, x => x.Value);
    private static bool NativeBool(string name) => bool.Parse(Native[name]);
    private static byte[] Encode(Material value) { byte[] result = new byte[8]; BinaryPrimitives.WriteInt32LittleEndian(result, value.Id); BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(4), value.Weight); return result; }
    private static Material Decode(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt32LittleEndian(bytes), BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]));
    private static void Register(MetadataRegistry registry, string name = "voxel_material") => registry.RegisterType<Material>(name, Encode, Decode, value => value.Id != 0 || value.Weight != 0, value => value.Id.ToString(CultureInfo.InvariantCulture) + "@" + value.Weight.ToString("G6", CultureInfo.InvariantCulture), 8);
    private static MetadataRegistry CustomRegistry() { var registry = MetadataRegistry.CreateDefault(); Register(registry); Register(registry, "__voxel_temporary"); return registry; }
    private static MetadataValue MaterialValue(MetadataRegistry registry) => registry.CreateMetadata("voxel_material", new Material(17, 1.25f));
    private static MetaMap ReadMap(byte[] data, MetadataRegistry registry) { var map = new MetaMap(); using var stream = new MemoryStream(data); map.ReadMeta(stream, options: new() { MetadataRegistry = registry }); return map; }
    public static IEnumerable<object[]> BuiltinNames() => Native.Keys.Where(key => key.StartsWith("default-type-", StringComparison.Ordinal)).Select(key => new object[] { key[13..] });
    [Theory, MemberData(nameof(BuiltinNames))]
    public void NativeBuiltinFactoryDefaultsAndAliases(string name)
    {
        MetadataValue actual = MetadataRegistry.CreateDefault().CreateMetadata(name);
        Assert.Equal(Native["default-type-" + name], actual.TypeName); Assert.Equal(Native["default-bytes-" + name], Convert.ToHexStringLower(actual.ToByteArray()));
    }
    [Fact]
    public void BuiltinFactoriesCanBeRestoredAndConstructTypedValues()
    {
        var registry = new MetadataRegistry(); registry.RegisterBuiltinType("float"); registry.RegisterBuiltinType("ptidx32");
        Assert.Equal(2.5f, registry.CreateMetadata("float", 2.5f).GetValue<float>());
        Assert.Equal("int32", registry.CreateMetadata("ptidx32", 47).TypeName);
        Assert.Throws<InvalidCastException>(() => registry.CreateMetadata("float", 2d));
        Assert.Throws<ArgumentException>(() => registry.RegisterBuiltinType("float")); Assert.Throws<NotSupportedException>(() => registry.RegisterBuiltinType("missing"));
        Assert.Throws<NotSupportedException>(() => Register(registry, "__delayedload"));
    }
    [Fact]
    public void NativeCustomRegistrationDefaultsAndErrors()
    {
        var registry = CustomRegistry(); Assert.Equal(NativeBool("registered"), registry.IsRegisteredType("voxel_material"));
        Assert.Equal(NativeBool("duplicate-rejected"), Record.Exception(() => Register(registry)) is ArgumentException);
        Assert.Equal(NativeBool("missing-rejected"), Record.Exception(() => registry.CreateMetadata("missing_type")) is KeyNotFoundException);
        foreach (var pair in new[] { ("custom-default", registry.CreateMetadata("voxel_material")), ("custom", MaterialValue(registry)) })
        {
            Assert.Equal(Native[pair.Item1 + "-bytes"], Convert.ToHexStringLower(pair.Item2.ToByteArray()));
            Assert.Equal(Native[pair.Item1 + "-text"], Convert.ToHexStringLower(Encoding.UTF8.GetBytes(pair.Item2.ToString())));
            Assert.Equal(NativeBool(pair.Item1 + "-bool"), pair.Item2.AsBool());
        }
        Assert.Throws<InvalidCastException>(() => registry.CreateMetadata("voxel_material", 1));
        registry.UnregisterType("voxel_material"); Assert.Equal(NativeBool("unregistered"), !registry.IsRegisteredType("voxel_material"));
        Assert.False(registry.UnregisterType("missing_type")); Assert.True(NativeBool("unregister-missing"));
        registry.ClearRegistry(); Assert.Equal(NativeBool("clear-removes-builtin"), !registry.IsRegisteredType("int32")); Assert.Equal(NativeBool("clear-removes-custom"), !registry.IsRegisteredType("voxel_material"));
    }
    [Fact]
    public void NativeMapOrderingTextCopyReplacementAndPartialInsert()
    {
        var registry = CustomRegistry(); var map = new MetaMap(); MetadataValue material = MaterialValue(registry);
        map.InsertMeta("z", ""); map.InsertMeta("material", material); map.InsertMeta("a", 11); map.InsertMeta("\uE000", 2); map.InsertMeta("\U00010000", 3);
        using var stream = new MemoryStream(); map.WriteMeta(stream); Assert.Equal(Native["map-wire"], Convert.ToHexStringLower(stream.ToArray()));
        Assert.Equal(Native["map-text"], Convert.ToHexStringLower(Encoding.UTF8.GetBytes(map.ToString("  "))));
        Assert.Equal(NativeBool("missing-null"), map.GetMetadata("absent") is null);
        MetaMap shallow = map.CopyMeta(), deep = map.DeepCopyMeta();
        Assert.Equal(NativeBool("shallow-shared"), ReferenceEquals(shallow["a"], map["a"])); Assert.Equal(NativeBool("deep-distinct"), !ReferenceEquals(deep["a"], map["a"])); Assert.Equal(NativeBool("deep-equal"), deep.Equals(map));
        Assert.NotSame(material, map["material"]); Assert.Equal(new Material(17, 1.25f), map.MetaValue<Material>("material"));
        map.InsertMeta("a", 44); Assert.Equal(NativeBool("replacement-detaches"), !ReferenceEquals(shallow["a"], map["a"])); Assert.Equal(NativeBool("replacement-old-value"), shallow.MetaValue<int>("a") == 11);
        Assert.Equal(NativeBool("type-conflict"), Record.Exception(() => map.InsertMeta("a", 1f)) is InvalidCastException); Assert.Equal(NativeBool("empty-name"), Record.Exception(() => map.InsertMeta("", material)) is ArgumentException);
        var incoming = new MetaMap(); incoming.InsertMeta("b", 5); incoming.InsertMeta("z", 6);
        Assert.Equal(NativeBool("map-conflict"), Record.Exception(() => map.InsertMeta(incoming)) is InvalidCastException); Assert.Equal(NativeBool("partial-insert"), map.ContainsKey("b"));
        map.Assign(map); Assert.Equal(NativeBool("self-assign-retains"), map.Count == 6); map.RemoveMeta("absent"); Assert.Equal(NativeBool("remove-missing-retains"), map.Count == 6);
        Assert.True(map.TryGetMetadata("material", out Material typed)); Assert.Equal(new Material(17, 1.25f), typed); Assert.False(map.TryGetMetadata("material", out int _)); Assert.False(map.TryGetMetadata("absent", out int _));
        var parsed = ReadMap(stream.ToArray(), registry); Assert.Equal(deep, parsed); Assert.Equal(deep.GetHashCode(), parsed.GetHashCode());
        parsed.ClearMetadata(); Assert.Empty(parsed); parsed.Assign(deep); Assert.Equal(deep, parsed);
        Assert.Throws<KeyNotFoundException>(() => parsed.MetaValue<int>("absent")); Assert.Throws<InvalidCastException>(() => parsed.MetaValue<double>("a"));
    }
    [Theory, InlineData("float-signed-zero"), InlineData("float-nan-same"), InlineData("float-nan-different"), InlineData("double-signed-zero"), InlineData("half-signed-zero"), InlineData("vec-nan-same"), InlineData("vec-nan-different"), InlineData("custom-opaque-equal")]
    public void NativeEqualityComparesEncodedPayloads(string name)
    {
        float nanA = BitConverter.Int32BitsToSingle(0x7fc12345), nanB = BitConverter.Int32BitsToSingle(0x7fc12346);
        (MetadataValue a, MetadataValue b) pair = name switch
        {
            "float-signed-zero" => (new(0f), new(-0f)),
            "float-nan-same" => (new(nanA), new(nanA)),
            "float-nan-different" => (new(nanA), new(nanB)),
            "double-signed-zero" => (new(0d), new(-0d)),
            "half-signed-zero" => (new((Half)0f), new((Half)(-0f))),
            "vec-nan-same" => (new(new Vec3f(nanA)), new(new Vec3f(nanA))),
            "vec-nan-different" => (new(new Vec3f(nanA)), new(new Vec3f(nanB))),
            _ => (MaterialValue(CustomRegistry()), new(new OpaqueMetadata("voxel_material", Encode(new(17, 1.25f)))))
        };
        var (a, b) = pair;
        Assert.Equal(NativeBool(name), a == b); Assert.Equal(NativeBool(name), a.Equals(b));
        if (a.Equals(b)) Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
    [Fact]
    public void NativeUnknownMetadataAndPointAliases()
    {
        MetaMap map = ReadMap(Convert.FromHexString(Native["map-wire"]), MetadataRegistry.CreateDefault());
        Assert.Equal(NativeBool("unknown-preserved"), map["material"].Value is OpaqueMetadata); Assert.Equal(NativeBool("unknown-byte-equality"), map["material"].Equals(MaterialValue(CustomRegistry())));
        MetaMap aliases = ReadMap(Convert.FromHexString(Native["alias-wire"]), MetadataRegistry.CreateDefault());
        Assert.Equal(Native["alias32-type"], aliases["ptidx32"].TypeName); Assert.Equal(47, aliases.MetaValue<int>("ptidx32")); Assert.Equal(Native["alias64-type"], aliases["ptidx64"].TypeName); Assert.Equal(47L, aliases.MetaValue<long>("ptidx64"));
        var registry = MetadataRegistry.CreateDefault(); registry.UnregisterType("float");
        var unknown = ReadMap(Convert.FromHexString(Native["unregistered-float-wire"]), registry);
        Assert.Equal(NativeBool("unregistered-builtin-opaque"), unknown["float"].Value is OpaqueMetadata);
        using var output = new MemoryStream(); unknown.WriteMeta(output); Assert.Equal(Native["unregistered-float-wire"], Convert.ToHexStringLower(output.ToArray()));
    }
    [Theory, InlineData(0, true), InlineData(4, true), InlineData(6, true), InlineData(0, false), InlineData(4, false), InlineData(6, false)]
    public void NativeCustomArchiveRoundTrip(int compression, bool registered)
    {
        MetadataRegistry registry = registered ? CustomRegistry() : MetadataRegistry.CreateDefault(); using var source = File.OpenRead(Path.Combine(Folder, $"native-registry-{compression}.vdb"));
        var archive = VdbFile.ReadArchive(source, options: new() { MetadataRegistry = registry }); var grid = Assert.IsType<Grid<float>>(Assert.Single(archive.Grids));
        Assert.Equal(7.25f, grid.Tree.GetValue(new(-1, 2, 3))); Assert.Equal(1UL, grid.ActiveVoxelCount);
        Assert.Equal(registered, grid.Metadata.ContainsKey("temporary")); Assert.Equal(NativeBool($"unknown-temporary-dropped-{compression}"), registered || !grid.Metadata.ContainsKey("temporary"));
        if (registered) { Assert.Equal(new Material(17, 1.25f), grid.GetMetadata<Material>("material")); Assert.Equal(new Material(22, 4.5f), archive.Metadata["file_material"].GetValue<Material>()); }
        else Assert.IsType<OpaqueMetadata>(grid.Metadata["material"].Value);
        using var output = new MemoryStream(); VdbFile.WriteArchive(output, archive, options: new() { Compression = (VdbCompression)compression });
        string? folder = Environment.GetEnvironmentVariable("TEDD_OPENVDB_REGISTRY_OUTPUT_DIR");
        if (folder is not null) { Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, $"managed-{compression}-{(registered ? "registered" : "unknown")}.vdb"), output.ToArray()); }
        output.Position = 0; var reread = VdbFile.ReadArchive(output, options: new() { MetadataRegistry = registry });
        Assert.Equal(new MetaMap(archive.Metadata), new MetaMap(reread.Metadata)); Assert.Equal(new MetaMap(grid.Metadata), new MetaMap(reread.Grids[0].Metadata));
    }
    [Fact]
    public void ReadSnapshotSurvivesRegistrationChangesInsideCustomDecoder()
    {
        var registry = MetadataRegistry.CreateDefault(); int calls = 0;
        registry.RegisterType<Material>("voxel_material", Encode, data => { ++calls; registry.ClearRegistry(); return Decode(data); }, value => value.Id != 0, value => value.Id.ToString(CultureInfo.InvariantCulture), 8);
        var map = new MetaMap(); map.InsertMeta("a", MaterialValue(registry)); map.InsertMeta("b", MaterialValue(registry)); using var bytes = new MemoryStream(); map.WriteMeta(bytes); bytes.Position = 0;
        var decoded = new MetaMap(); decoded.ReadMeta(bytes, options: new() { MetadataRegistry = registry }); Assert.Equal(2, calls); Assert.IsType<Material>(decoded["a"].Value); Assert.IsType<Material>(decoded["b"].Value); Assert.False(registry.IsRegisteredType("voxel_material"));
    }
    [Fact]
    public void RegistrationAndPayloadsAreImmutableSnapshots()
    {
        var registry = MetadataRegistry.CreateDefault(); byte[] payload = Encode(new(17, 1.25f));
        registry.RegisterType<Material>("voxel_material", _ => payload, Decode, value => value.Id != 0, value => value.Id.ToString(CultureInfo.InvariantCulture), 8);
        var value = MaterialValue(registry); payload[0] = 99; byte[] exposed = value.ToByteArray(); exposed[0] = 88; Assert.Equal(17, value.ToByteArray()[0]);
        registry.UnregisterType("voxel_material"); Register(registry); Assert.Equal("17", value.ToString()); Assert.Equal("17@1.25", MaterialValue(registry).ToString()); Assert.Equal("17", value.DeepCopy().ToString());
    }
    [Fact]
    public void GridMetadataProvidesNativeMapOperations()
    {
        var grid = new Grid<int>(); grid.InsertMeta("coord", new Coord(1, 2, 3)); grid.InsertMeta("material", MaterialValue(CustomRegistry()));
        Assert.Equal(2, grid.MetaCount); Assert.Equal(new Vec3i(1, 2, 3), grid.MetaValue<Vec3i>("coord")); Assert.Null(grid.GetMetadata("missing"));
        Assert.Same(grid.GetMetadata("coord"), grid.CopyMeta()["coord"]); Assert.NotSame(grid.GetMetadata("coord"), grid.DeepCopyMeta()["coord"]); Assert.NotSame(grid.GetMetadata("coord"), grid.DeepCopy().GetMetadata("coord"));
        Assert.Throws<InvalidCastException>(() => grid.InsertMeta("coord", 2)); Assert.True(grid.RemoveMeta("coord")); grid.ClearMetadata(); Assert.Empty(grid.Metadata);
    }
    [Theory, InlineData(false), InlineData(true)]
    public void InvalidNamesAreRejectedBeforeMutation(bool surrogate)
    {
        string name = surrogate ? new string((char)0xd800, 1) : string.Empty;
        var map = new MetaMap(); Assert.ThrowsAny<ArgumentException>(() => map.InsertMeta(name, 1)); Assert.Empty(map);
        Assert.ThrowsAny<ArgumentException>(() => Register(new MetadataRegistry(), name));
    }
    [Theory, InlineData(-1), InlineData(0), InlineData(1), InlineData(7), InlineData(9)]
    public void CustomCodecRejectsInvalidFixedSizes(int length)
    {
        var registry = new MetadataRegistry();
        if (length < 0) { Assert.Throws<ArgumentOutOfRangeException>(() => registry.RegisterType<Material>("custom", Encode, Decode, _ => false, _ => "", length)); return; }
        registry.RegisterType<Material>("custom", Encode, Decode, _ => false, _ => "", length); Assert.Throws<InvalidDataException>(() => registry.CreateMetadata("custom", default(Material)));
    }
    private static byte[] SingleField(string type, byte[] payload)
    {
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data, Encoding.UTF8, true); writer.Write(1u);
        foreach (string text in new[] { "field", type }) { byte[] encoded = Encoding.UTF8.GetBytes(text); writer.Write((uint)encoded.Length); writer.Write(encoded); }
        writer.Write((uint)payload.Length); writer.Write(payload); return data.ToArray();
    }
    [Theory, InlineData(0), InlineData(1), InlineData(7), InlineData(9), InlineData(4096)]
    public void CustomMalformedLengthFailsBeforeDecoder(int length)
    {
        int calls = 0; var registry = new MetadataRegistry(); registry.RegisterType<Material>("voxel_material", Encode, data => { ++calls; return Decode(data); }, _ => false, _ => "", 8);
        Assert.Throws<InvalidDataException>(() => ReadMap(SingleField("voxel_material", new byte[length]), registry)); Assert.Equal(0, calls);
    }
    [Theory, InlineData("vec2i", 7), InlineData("vec2s", 9), InlineData("vec2d", 15), InlineData("vec4i", 17), InlineData("vec4s", 15), InlineData("vec4d", 31), InlineData("mat4s", 63), InlineData("half", 1), InlineData("bool", 2)]
    public void BuiltinMalformedLengthIsRejected(string name, int length) => Assert.Throws<InvalidDataException>(() => ReadMap(SingleField(name, new byte[length]), MetadataRegistry.CreateDefault()));
    [Fact]
    public void DecoderErrorsTruncationLimitsAndStreamOwnership()
    {
        byte[] data = SingleField("voxel_material", Encode(new(17, 1.25f))); var registry = CustomRegistry();
        for (int length = 0; length < data.Length; ++length)
        {
            Exception? failure = Record.Exception(() => ReadMap(data[..length], registry));
            Assert.True(failure is InvalidDataException or EndOfStreamException, failure?.GetType().FullName ?? "Truncated metadata was accepted.");
        }
        var map = new MetaMap(); map.InsertMeta("previous", 1); using var input = new MemoryStream(data);
        Assert.Throws<InvalidDataException>(() => map.ReadMeta(input, options: new() { MetadataRegistry = registry, MaxAllocatedBytes = 1 })); Assert.Empty(map); Assert.True(input.CanRead);
        input.Position = 0; Assert.Throws<InvalidDataException>(() => map.ReadMeta(input, leaveOpen: false, options: new() { MetadataRegistry = registry, MaxMetadataBytes = 7 })); Assert.False(input.CanRead);
        var throwing = new MetadataRegistry(); throwing.RegisterType<Material>("voxel_material", Encode, _ => throw new ArgumentException("invalid"), _ => false, _ => "", 8);
        Assert.Throws<InvalidDataException>(() => ReadMap(data, throwing));
        using var output = new MemoryStream(); map.WriteMeta(output, leaveOpen: false); Assert.False(output.CanWrite);
    }
    [Fact]
    public void AllocationBudgetAccountsForCustomPayloadSnapshot()
    {
        var registry = CustomRegistry(); byte[] data = SingleField("voxel_material", Encode(new(17, 1.25f))); long minimum = 0;
        for (int limit = 1; limit < 10000; ++limit)
        {
            using var input = new MemoryStream(data); var map = new MetaMap();
            try { map.ReadMeta(input, options: new() { MetadataRegistry = registry, MaxAllocatedBytes = limit }); minimum = limit; break; } catch (InvalidDataException) { }
        }
        Assert.InRange(minimum, 100, 9999); using var failed = new MemoryStream(data);
        Assert.Throws<InvalidDataException>(() => new MetaMap().ReadMeta(failed, options: new() { MetadataRegistry = registry, MaxAllocatedBytes = minimum - 1 }));
    }
}
