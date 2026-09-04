// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.IO.Compression;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Points;

namespace Tedd.OpenVDB.Tests;

public sealed class AttributeSetNativeTests
{
    private const string Commit = "f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf";
    private static readonly AttributeType Vec3fType = new("vec3s", "null");
    private static readonly AttributeType FloatType = new("float", "null");
    private static readonly AttributeType IntType = new("int32", "null");
    private static readonly Lazy<JsonDocument> Fixture = new(() =>
    {
        using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "openvdb-13.0.1-attribute-set.json.gz"));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        return JsonDocument.Parse(gzip);
    });

    public static IEnumerable<object[]> DescriptorCases()
    {
        foreach (JsonElement item in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
            if (item.GetProperty("kind").GetString() == "descriptor")
                yield return [item.GetProperty("profile").GetInt32()];
    }

    public static IEnumerable<object[]> SetCases()
    {
        foreach (JsonElement item in Fixture.Value.RootElement.GetProperty("cases").EnumerateArray())
            if (item.GetProperty("kind").GetString() == "set")
                foreach (bool outputTransient in new[] { false, true })
                    yield return [item.GetProperty("profile").GetInt32(), outputTransient];
    }

    [Theory, MemberData(nameof(DescriptorCases))]
    public void DescriptorOperationsAndWireBytesMatchNative(int profile)
    {
        VerifyProvenance();
        JsonElement item = Find("descriptor", profile);
        AttributeDescriptor descriptor = BuildDescriptor(profile);
        using var stream = new MemoryStream(); descriptor.Write(stream);
        Assert.Equal(Convert.FromHexString(item.GetProperty("wire").GetString()!), stream.ToArray());
        stream.Position = 0; Assert.Equal(descriptor, AttributeDescriptor.Read(stream));
        Assert.Equal(stream.Length, stream.Position);
    }

    [Theory, MemberData(nameof(SetCases))]
    public void AttributeSetOperationsAndRawWireBytesMatchNative(int profile, bool outputTransient)
    {
        VerifyProvenance();
        JsonElement item = Find("set", profile);
        using AttributeSet set = BuildSet(profile);
        using var stream = new MemoryStream(); set.Write(stream, outputTransient);
        string field = outputTransient ? "all" : "persistent";
        Assert.Equal(Convert.FromHexString(item.GetProperty(field).GetString()!), stream.ToArray());
        stream.Position = 0; using AttributeSet decoded = AttributeSet.Read(stream);
        Assert.Equal(outputTransient ? set.Size : set.Size - ((profile & 4) != 0 ? 1 : 0), decoded.Size);
        Assert.Equal(stream.Length, stream.Position);
        if (outputTransient || (profile & 4) == 0) Assert.Equal(set, decoded);
    }

    private static AttributeDescriptor BuildDescriptor(int profile)
    {
        AttributeDescriptor result = AttributeDescriptor.Create(Vec3fType);
        if ((profile & 1) != 0) result = result.DuplicateAppend("density", FloatType);
        if ((profile & 2) != 0) result = result.DuplicateAppend("id", IntType);
        int groups = (profile >> 2) % 3;
        for (int i = 0; i < groups; i++) result = result.DuplicateAppend("__group" + i, AttributeTypes.Group);
        result.Metadata.InsertMeta("purpose", "profile-" + profile);
        if ((profile & 1) != 0)
        {
            result.SetDefaultValue("density", new MetadataValue((profile - 20) * .125f));
            if ((profile & 16) != 0) result.Rename("density", "mass");
        }
        if ((profile & 2) != 0) result.SetDefaultValue("id", new MetadataValue(profile * 3 - 17));
        for (int i = 0; i < groups; i++) result.SetGroup("g" + i, i * 8 + profile % 8);
        if ((profile & 64) != 0 && result.Find("id") != AttributeDescriptor.InvalidPosition)
            result = result.DuplicateDrop([result.Find("id")]);
        return result;
    }

    private static AttributeSet BuildSet(int profile)
    {
        AttributeDescriptor schema = AttributeDescriptor.Create(Vec3fType);
        schema.SetDefaultValue("P", new MetadataValue(new Vec3f(profile * .01f, -.25f, .5f)));
        int size = 1 + profile % 11;
        var set = new AttributeSet(schema, size);
        var points = Assert.IsAssignableFrom<AttributeArray<Vec3f>>(set.Get("P"));
        if ((profile & 1) != 0)
        {
            points.Expand();
            for (int i = 0; i < points.DataSize; i++)
                points.Set(i, new Vec3f(i * .125f, profile - i, i * i * .03125f));
        }
        var ids = Assert.IsAssignableFrom<AttributeArray<int>>(set.AppendAttribute("id", IntType, defaultValue: new MetadataValue(profile - 7)));
        if ((profile & 2) != 0)
        {
            ids.Expand();
            for (int i = 0; i < ids.DataSize; i++) ids.Set(i, profile * 100 + i * 17);
        }
        if ((profile & 4) != 0)
        {
            AttributeArray temporary = set.AppendAttribute("temporary", FloatType);
            temporary.IsTransient = true; temporary.IsHidden = (profile & 8) != 0;
        }
        if ((profile & 16) != 0)
        {
            var group = Assert.IsAssignableFrom<AttributeArray<byte>>(set.AppendAttribute("__group", AttributeTypes.Group));
            set.Descriptor.SetGroup("selected", profile % 8);
            group.Expand();
            for (int i = 0; i < group.DataSize; i++) group.Set(i, (byte)((i * 29 + profile) & 255));
        }
        points.IsHidden = (profile & 8) != 0;
        if ((profile & 32) != 0) ids.IsStreaming = true;
        return set;
    }

    private static JsonElement Find(string kind, int profile) =>
        Fixture.Value.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("kind").GetString() == kind && item.GetProperty("profile").GetInt32() == profile);

    private static void VerifyProvenance()
    {
        Assert.Equal(Commit, Fixture.Value.RootElement.GetProperty("commit").GetString());
        Assert.Equal(144, Fixture.Value.RootElement.GetProperty("cases").GetArrayLength());
    }
}
