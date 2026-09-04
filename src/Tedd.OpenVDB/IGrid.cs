// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB;

/// <summary>Common grid metadata and transform interface for heterogeneous archives.</summary>
public interface IGrid
{
    Type ValueType { get; }
    string Name { get; set; }
    Transform Transform { get; set; }
    IReadOnlyDictionary<string, MetadataValue> Metadata { get; }
    ulong ActiveVoxelCount { get; }
}

/// <summary>Immutable uninterpreted payload for an unregistered persistent metadata type.</summary>
/// <remarks>Bytes are retained without interpretation, matching native UnknownMetadata.</remarks>
public sealed class OpaqueMetadata : IEquatable<OpaqueMetadata>
{
    private readonly byte[] _data;
    public OpaqueMetadata(string typeName, ReadOnlySpan<byte> data)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        if (typeName is "bool" or "int32" or "int64" or "float" or "double" or "string" or "vec3i" or "vec3s" or "vec3d" or "mat4d" or "mat4s" or "half" or "vec2i" or "vec2s" or "vec2d" or "vec4i" or "vec4s" or "vec4d" or "__delayedload")
            throw new ArgumentException("Recognized metadata types require their typed representation.", nameof(typeName));
        TypeName = typeName; _data = data.ToArray();
    }
    public string TypeName { get; }
    public ReadOnlySpan<byte> Data => _data;
    public bool Equals(OpaqueMetadata? other) => other is not null && TypeName == other.TypeName && Data.SequenceEqual(other.Data);
    public override bool Equals(object? obj) => obj is OpaqueMetadata other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(TypeName, StringComparer.Ordinal);
        foreach (byte value in _data) hash.Add(value);
        return hash.ToHashCode();
    }
    public override string ToString() => $"{TypeName} ({_data.Length} bytes)";
}
