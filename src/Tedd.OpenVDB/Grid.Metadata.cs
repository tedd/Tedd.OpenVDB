// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB;

public sealed partial class Grid<T> where T : unmanaged
{
    public int MetaCount => _metadata.Count;
    public MetadataValue? GetMetadata(string name) => _metadata.GetValueOrDefault(name);
    public TValue MetaValue<TValue>(string name) => GetMetadata<TValue>(name);
    public MetaMap CopyMeta() => MetaMap.FromDecoded(_metadata);
    public MetaMap DeepCopyMeta() => new(_metadata);
    public void ClearMetadata() => _metadata.Clear();
    public bool RemoveMeta(string name) => RemoveMetadata(name);
    public void InsertMeta(string name, object value) => InsertMeta(name, new MetadataValue(value));
    /// <summary>Inserts a copy and rejects replacement with a different native type name.</summary>
    public void InsertMeta(string name, MetadataValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name); ArgumentNullException.ThrowIfNull(value);
        _ = MetadataWire.Utf8.GetByteCount(name);
        if (_metadata.TryGetValue(name, out var previous) && previous.TypeName != value.TypeName)
            throw new InvalidCastException($"Cannot assign metadata type '{value.TypeName}' to '{name}' of type '{previous.TypeName}'.");
        _metadata[name] = value.DeepCopy();
    }
    public void InsertMeta(IReadOnlyDictionary<string, MetadataValue> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var pair in other.OrderBy(pair => pair.Key, MetadataNameComparer.Instance).ToArray()) InsertMeta(pair.Key, pair.Value);
    }
}
