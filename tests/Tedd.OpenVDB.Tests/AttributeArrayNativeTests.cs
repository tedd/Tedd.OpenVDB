// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Points;
namespace Tedd.OpenVDB.Tests;

public class AttributeArrayNativeTests
{
    private static readonly Lazy<JsonDocument> Fixture = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-attributes.json.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress); return JsonDocument.Parse(gzip);
    });
    public static IEnumerable<object[]> Cases()
    {
        int i = 0;
        foreach (var c in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
            yield return [i++, c.GetProperty("kind").GetString()! + "/" + (c.TryGetProperty("type", out var type) ? type.GetString() : "copy") + "/" + (c.TryGetProperty("codec", out var codec) ? codec.GetString() : "null")];
    }
    [Theory, MemberData(nameof(Cases))]
    public void NativeEncodedStorageStridesHandlesAndCompactionMatch(int index, string label)
    {
        Assert.NotEmpty(label); Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Fixture.Value.RootElement.GetProperty("commit").GetString());
        var c = Fixture.Value.RootElement.GetProperty("cases")[index];
        if (c.GetProperty("kind").GetString() == "copy") { Copy(c); return; }
        if (c.GetProperty("kind").GetString() is "group" or "group-mask") { Group(c); return; }
        switch (c.GetProperty("type").GetString())
        {
            case "bool": Dispatch(c, e => e.GetInt32() != 0); break;
            case "int32": Dispatch(c, e => e.GetInt32()); break;
            case "int64": Dispatch(c, e => e.GetInt64()); break;
            case "half": Dispatch(c, e => (Half)Number(e)); break;
            case "float": Dispatch(c, e => (float)Number(e)); break;
            case "double": Dispatch(c, Number); break;
            case "vec2i": Dispatch(c, e => new Vec2i(e[0].GetInt32(), e[1].GetInt32())); break;
            case "vec2s": Dispatch(c, e => new Vec2f((float)Number(e[0]), (float)Number(e[1]))); break;
            case "vec2d": Dispatch(c, e => new Vec2d(Number(e[0]), Number(e[1]))); break;
            case "vec3i": Dispatch(c, e => new Vec3i(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32())); break;
            case "vec3s": Dispatch(c, e => new Vec3f((float)Number(e[0]), (float)Number(e[1]), (float)Number(e[2]))); break;
            case "vec3d": Dispatch(c, e => new Vec3d(Number(e[0]), Number(e[1]), Number(e[2]))); break;
            case "vec4i": Dispatch(c, e => new Vec4i(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32())); break;
            case "vec4s": Dispatch(c, e => new Vec4f((float)Number(e[0]), (float)Number(e[1]), (float)Number(e[2]), (float)Number(e[3]))); break;
            case "vec4d": Dispatch(c, e => new Vec4d(Number(e[0]), Number(e[1]), Number(e[2]), Number(e[3]))); break;
            default: throw new InvalidOperationException(label);
        }
    }
    private static void Dispatch<T>(JsonElement c, Func<JsonElement, T> read) where T : unmanaged
    {
        switch (c.GetProperty("codec").GetString())
        {
            case "null": Run<T, NullCodec<T>>(c, read); break;
            case "trnc": Run<T, TruncateCodec<T>>(c, read); break;
            case "fxpt8": Run<T, FixedPoint8Codec<T, PositionRange>>(c, read); break;
            case "fxpt16": Run<T, FixedPoint16Codec<T, PositionRange>>(c, read); break;
            case "ufxpt8": Run<T, FixedPoint8Codec<T, UnitRange>>(c, read); break;
            case "ufxpt16": Run<T, FixedPoint16Codec<T, UnitRange>>(c, read); break;
            case "uvec": Run<T, UnitVecCodec<T>>(c, read); break;
            default: throw new InvalidOperationException();
        }
    }
    private static void Run<T, TCodec>(JsonElement c, Func<JsonElement, T> read) where T : unmanaged where TCodec : unmanaged, IAttributeCodec<T>
    {
        if (c.GetProperty("kind").GetString() == "special")
        {
            T value = read(c.GetProperty("input")); var special = new TypedAttributeArray<T, TCodec>(3, uniformValue: value); special.Expand();
            Assert.Equal(c.GetProperty("equal").GetInt32() != 0, special.NativeEquals(special.DeepCopy()));
            Assert.Equal(c.GetProperty("compacted").GetInt32() != 0, special.Compact()); Snapshot(c.GetProperty("state"), special, read); return;
        }
        T initial = read(c.GetProperty("initial"));
        var array = new TypedAttributeArray<T, TCodec>(c.GetProperty("n").GetInt32(), c.GetProperty("s").GetInt32(), c.GetProperty("constant").GetInt32() != 0, initial);
        Assert.Equal(c.GetProperty("type").GetString(), array.ValueType); Assert.Equal(c.GetProperty("codec").GetString(), array.CodecType);
        Assert.Equal(c.GetProperty("valueSize").GetInt32(), array.ValueTypeSize); Assert.Equal(c.GetProperty("storageSize").GetInt32(), array.StorageTypeSize);
        var traits = c.GetProperty("traits");
        Assert.Equal(traits[0].GetInt32() != 0, array.ValueTypeIsFloatingPoint); Assert.Equal(traits[1].GetInt32() != 0, array.ValueTypeIsClass);
        Assert.Equal(traits[2].GetInt32() != 0, array.ValueTypeIsVector); Assert.Equal(traits[3].GetInt32() != 0, array.ValueTypeIsQuaternion); Assert.Equal(traits[4].GetInt32() != 0, array.ValueTypeIsMatrix);
        var steps = c.GetProperty("steps");
        Snapshot(steps[0], array, read); array.Set(0, array.Get(0)); Snapshot(steps[1], array, read);
        array.Collapse(initial); Snapshot(steps[2], array, read); array.Expand(); Snapshot(steps[3], array, read);
        var writes = c.GetProperty("writes");
        for (int i = 0; i < writes.GetArrayLength(); i++) array.Set(i, read(writes[i]));
        Snapshot(c.GetProperty("written"), array, read);
        var copy = array.DeepCopy(); Assert.Equal(c.GetProperty("copyEqual").GetInt32() != 0, array.NativeEquals(copy));
        copy.Set(copy.DataSize - 1, MakeTail<T>()); Assert.Equal(c.GetProperty("tailEqual").GetInt32() != 0, array.NativeEquals(copy));
        array.Fill(read(c.GetProperty("fill"))); Snapshot(c.GetProperty("filled"), array, read);
        Assert.Equal(c.GetProperty("compacted").GetInt32() != 0, array.Compact()); Snapshot(c.GetProperty("compact"), array, read);
        array.IsHidden = array.IsTransient = array.IsStreaming = true;
        using (var writer = new AttributeWriteHandle<T>(array)) writer.Set(0, initial);
        Snapshot(c.GetProperty("writeHandle"), array, read);
        using (var handle = new AttributeHandle<T>(array))
        {
            var expected = c.GetProperty("handle"); Assert.Equal(expected.GetProperty("size").GetInt32(), handle.Size); Assert.Equal(expected.GetProperty("stride").GetInt32(), handle.Stride);
            Equal(read(expected.GetProperty("last")), handle.Get(handle.Size - 1, handle.Stride - 1));
        }
        Snapshot(c.GetProperty("disposed"), array, read);
    }
    private static T MakeTail<T>() where T : unmanaged
    {
        object value = typeof(T) == typeof(bool) ? true : typeof(T) == typeof(int) ? 9 : typeof(T) == typeof(long) ? 9L :
            typeof(T) == typeof(float) ? 9.75f : typeof(T) == typeof(double) ? 9.75 : typeof(T) == typeof(Half) ? (Half)9.75 :
            typeof(T) == typeof(Vec2i) ? new Vec2i(9, 10) : typeof(T) == typeof(Vec2f) ? new Vec2f(9.75f, 10) : typeof(T) == typeof(Vec2d) ? new Vec2d(9.75, 10) :
            typeof(T) == typeof(Vec3i) ? new Vec3i(9, 10, 10) : typeof(T) == typeof(Vec3f) ? new Vec3f(9.75f, 10, 10.25f) : typeof(T) == typeof(Vec3d) ? new Vec3d(9.75, 10, 10.25) :
            typeof(T) == typeof(Vec4i) ? new Vec4i(9, 10, 10, 10) : typeof(T) == typeof(Vec4f) ? new Vec4f(9.75f, 10, 10.25f, 10.5f) : new Vec4d(9.75, 10, 10.25, 10.5);
        return (T)value;
    }
    private static void Snapshot<T>(JsonElement expected, AttributeArray<T> array, Func<JsonElement, T> read) where T : unmanaged
    {
        Assert.Equal(expected.GetProperty("size").GetInt32(), array.Size); Assert.Equal(expected.GetProperty("stride").GetInt32(), array.Stride);
        Assert.Equal(expected.GetProperty("dataSize").GetInt32(), array.DataSize); Assert.Equal(expected.GetProperty("flags").GetInt32(), (int)array.Flags);
        Assert.Equal(expected.GetProperty("uniform").GetInt32() != 0, array.IsUniform);
        Assert.Equal(expected.GetProperty("storage").GetString(), Convert.ToHexStringLower(array.GetStorageBytes()));
        var values = expected.GetProperty("values"); Assert.Equal(values.GetArrayLength(), array.DataSize);
        for (int i = 0; i < values.GetArrayLength(); i++) Equal(read(values[i]), array.Get(i));
    }
    private static void Equal<T>(T expected, T actual) where T : unmanaged
    {
        if (typeof(T) == typeof(Vec2f) || typeof(T) == typeof(Vec3f) || typeof(T) == typeof(Vec4f))
        {
            var a = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateReadOnlySpan(ref expected, 1)); var b = MemoryMarshal.Cast<T, float>(MemoryMarshal.CreateReadOnlySpan(ref actual, 1));
            for (int i = 0; i < a.Length; i++) Assert.Equal(a[i], b[i]);
        }
        else if (typeof(T) == typeof(Vec2d) || typeof(T) == typeof(Vec3d) || typeof(T) == typeof(Vec4d))
        {
            var a = MemoryMarshal.Cast<T, double>(MemoryMarshal.CreateReadOnlySpan(ref expected, 1)); var b = MemoryMarshal.Cast<T, double>(MemoryMarshal.CreateReadOnlySpan(ref actual, 1));
            for (int i = 0; i < a.Length; i++) Assert.Equal(a[i], b[i]);
        }
        else Assert.Equal(expected, actual);
    }
    private static void Copy(JsonElement c)
    {
        int shape = c.GetProperty("shape").GetInt32(); bool uniform = c.GetProperty("uniform").GetInt32() != 0;
        var source = new TypedAttributeArray<float>(4, shape == 1 ? 3 : shape == 2 ? 7 : 1, shape != 2, 2);
        var target = new TypedAttributeArray<float>(5, shape == 1 ? 3 : shape == 2 ? 9 : 1, shape != 2, 8);
        if (!uniform) { source.Expand(); for (int i = 0; i < source.DataSize; i++) source.Set(i, i + .25f); }
        target.CopyValues(source, [new(2, 1), new(1, 2), new(0, 3)], c.GetProperty("compact").GetInt32() != 0);
        Snapshot(c.GetProperty("state"), target, e => (float)Number(e));
    }
    private static void Group(JsonElement c)
    {
        if (c.GetProperty("kind").GetString() == "group-mask")
        {
            var values = new GroupAttributeArray(256); for (int i = 0; i < 256; i++) values.Set(i, (byte)i);
            var reader = GroupHandle.FromBitMask(values, c.GetProperty("mask").GetByte()); var expected = c.GetProperty("values");
            for (int i = 0; i < 256; i++) { Assert.Equal(expected[i].GetInt32() != 0, reader.Get(i)); Assert.Equal(reader.Get(i), reader.GetUnsafe(i)); }
            return;
        }
        int scene = c.GetProperty("scene").GetInt32(), offset = c.GetProperty("offset").GetInt32();
        var a = new GroupAttributeArray(7, scene == 4 ? 3 : scene == 5 ? 15 : 1, scene != 5, scene == 1 ? (byte)255 : (byte)0);
        if (scene >= 2) { a.Expand(); for (int i = 0; i < a.DataSize; i++) a.Set(i, scene == 2 ? (byte)((i % 2) << offset) : (byte)(i * 11)); }
        Snapshot(c.GetProperty("initial"), a, e => e.GetByte());
        var writer = new GroupWriteHandle(a, offset); Snapshot(c.GetProperty("constructed"), a, e => e.GetByte());
        writer.Set(2, true); Snapshot(c.GetProperty("set"), a, e => e.GetByte());
        Assert.Equal(c.GetProperty("onResult").GetInt32() != 0, writer.Collapse(true)); Snapshot(c.GetProperty("on"), a, e => e.GetByte());
        Assert.Equal(c.GetProperty("offResult").GetInt32() != 0, writer.Collapse(false)); Snapshot(c.GetProperty("off"), a, e => e.GetByte());
        Assert.Equal(c.GetProperty("compactResult").GetInt32() != 0, writer.Compact()); Snapshot(c.GetProperty("compact"), a, e => e.GetByte());
    }
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : value.GetString() switch
    {
        "NaN" => BitConverter.Int64BitsToDouble(0x7ff8000000000000L),
        "Infinity" => double.PositiveInfinity,
        "-Infinity" => double.NegativeInfinity,
        _ => throw new InvalidDataException()
    };
}
