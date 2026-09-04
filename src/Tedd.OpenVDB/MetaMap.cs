// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Collections;
namespace Tedd.OpenVDB;

/// <summary>Native metadata map operations over immutable metadata values.</summary>
public sealed class MetaMap : IReadOnlyDictionary<string, MetadataValue>, IEquatable<MetaMap>
{
    private SortedDictionary<string, MetadataValue> _values = new(MetadataNameComparer.Instance);
    public MetaMap() { }
    public MetaMap(IReadOnlyDictionary<string, MetadataValue> other) { ArgumentNullException.ThrowIfNull(other); InsertMeta(other); }
    public MetadataValue this[string key] => _values[key];
    public IEnumerable<string> Keys => _values.Keys;
    public IEnumerable<MetadataValue> Values => _values.Values;
    public int Count => _values.Count;
    public bool ContainsKey(string key) => _values.ContainsKey(key);
    public bool TryGetValue(string key, out MetadataValue value) => _values.TryGetValue(key, out value!);
    public IEnumerator<KeyValuePair<string, MetadataValue>> GetEnumerator() => _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public MetadataValue? GetMetadata(string name) => _values.GetValueOrDefault(name);
    public T MetaValue<T>(string name) => _values[name].GetValue<T>();
    public bool TryGetMetadata<T>(string name, out T value)
    {
        if (_values.TryGetValue(name, out var metadata)) return metadata.TryGetValue(out value);
        value = default!; return false;
    }
    public void InsertMeta(string name, object value) => InsertMeta(name, new MetadataValue(value));
    public void InsertMeta(string name, MetadataValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name); ArgumentNullException.ThrowIfNull(value);
        _ = MetadataWire.Utf8.GetByteCount(name);
        if (_values.TryGetValue(name, out var previous) && previous.TypeName != value.TypeName)
            throw new InvalidCastException($"Cannot assign metadata type '{value.TypeName}' to '{name}' of type '{previous.TypeName}'.");
        _values[name] = value.DeepCopy();
    }
    /// <summary>Inserts in native UTF-8 name order. As upstream, fields inserted before a later type conflict remain in the map.</summary>
    public void InsertMeta(IReadOnlyDictionary<string, MetadataValue> other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var pair in other.OrderBy(pair => pair.Key, MetadataNameComparer.Instance).ToArray()) InsertMeta(pair.Key, pair.Value);
    }
    public bool RemoveMeta(string name) => _values.Remove(name);
    public void ClearMetadata() => _values.Clear();
    public MetaMap CopyMeta()
    {
        var copy = new MetaMap(); foreach (var pair in _values) copy._values.Add(pair.Key, pair.Value); return copy;
    }
    public MetaMap DeepCopyMeta() => new(this);
    internal static MetaMap FromDecoded(IReadOnlyDictionary<string, MetadataValue> fields)
    {
        var result = new MetaMap(); foreach (var pair in fields) result._values.Add(pair.Key, pair.Value); return result;
    }
    /// <summary>Replaces this map from native metadata framing. Failed reads leave this map empty.</summary>
    public void ReadMeta(Stream stream, bool leaveOpen = true, IO.VdbReadOptions? options = null)
    {
        ClearMetadata(); _values = IO.VdbFile.ReadMetadataMap(stream, leaveOpen, options)._values;
    }
    public void WriteMeta(Stream stream, bool leaveOpen = true) => IO.VdbFile.WriteMetadataMap(stream, this, leaveOpen);
    public void Assign(MetaMap other)
    {
        ArgumentNullException.ThrowIfNull(other); if (ReferenceEquals(this, other)) return;
        _values.Clear(); InsertMeta(other);
    }
    public bool Equals(MetaMap? other)
    {
        if (other is null || other.Count != Count) return false;
        foreach (var pair in _values) if (!other._values.TryGetValue(pair.Key, out var value) || !pair.Value.Equals(value)) return false;
        return true;
    }
    public override bool Equals(object? obj) => obj is MetaMap other && Equals(other);
    public override int GetHashCode() { var hash = new HashCode(); foreach (var pair in _values) { hash.Add(pair.Key, StringComparer.Ordinal); hash.Add(pair.Value); } return hash.ToHashCode(); }
    public string ToString(string indent) => string.Join("\n", _values.Select(pair => indent + pair.Key + (pair.Value.ToString() is { Length: > 0 } text ? ": " + text : string.Empty)));
    public override string ToString() => ToString(string.Empty);
}
