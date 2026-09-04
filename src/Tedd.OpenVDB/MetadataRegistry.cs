// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Runtime.CompilerServices;
namespace Tedd.OpenVDB;

public delegate byte[] MetadataEncoder<T>(T value) where T : unmanaged;
public delegate T MetadataDecoder<T>(ReadOnlySpan<byte> payload) where T : unmanaged;

/// <summary>Thread-safe metadata type factories. A read captures a registry snapshot before decoding.</summary>
/// <remarks>Custom codecs are trusted application code. Their value types are unmanaged; encoded bytes are copied into immutable metadata snapshots.</remarks>
public sealed class MetadataRegistry
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, MetadataType> _types = new(StringComparer.Ordinal);
    public static MetadataRegistry Default { get; } = CreateDefault();
    public static MetadataRegistry CreateDefault()
    {
        var registry = new MetadataRegistry();
        foreach (string name in MetadataWire.BuiltinNames) registry._types.Add(name, new BuiltinMetadataType(name));
        registry._types.Add("ptidx32", new BuiltinMetadataType("int32"));
        registry._types.Add("ptidx64", new BuiltinMetadataType("int64"));
        return registry;
    }
    /// <summary>Registers one native builtin factory, including ptidx32/ptidx64 aliases.</summary>
    public void RegisterBuiltinType(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        string canonical = name switch { "ptidx32" => "int32", "ptidx64" => "int64", _ => name };
        _ = MetadataWire.FixedSize(canonical);
        lock (_sync) if (!_types.TryAdd(name, new BuiltinMetadataType(canonical))) throw new ArgumentException($"Metadata type '{name}' is already registered.", nameof(name));
    }
    public bool IsRegisteredType(string name) { ArgumentNullException.ThrowIfNull(name); lock (_sync) return _types.ContainsKey(name); }
    public void RegisterType<T>(string name, MetadataEncoder<T> encoder, MetadataDecoder<T> decoder, Func<T, bool> asBool, Func<T, string> format, int? fixedSize = null) where T : unmanaged
    {
        ArgumentException.ThrowIfNullOrEmpty(name); _ = MetadataWire.Utf8.GetByteCount(name); ArgumentNullException.ThrowIfNull(encoder); ArgumentNullException.ThrowIfNull(decoder); ArgumentNullException.ThrowIfNull(asBool); ArgumentNullException.ThrowIfNull(format);
        if (name == "__delayedload") throw new NotSupportedException("Delayed-load metadata is reserved and is not materialized by this reader.");
        if (fixedSize < 0) throw new ArgumentOutOfRangeException(nameof(fixedSize));
        var type = new CustomMetadataType<T>(name, encoder, decoder, asBool, format, fixedSize);
        lock (_sync) if (!_types.TryAdd(name, type)) throw new ArgumentException($"Metadata type '{name}' is already registered.", nameof(name));
    }
    public bool UnregisterType(string name) { ArgumentNullException.ThrowIfNull(name); lock (_sync) return _types.Remove(name); }
    public void ClearRegistry() { lock (_sync) _types.Clear(); }
    public MetadataValue CreateMetadata(string name) => Get(name).CreateDefault();
    public MetadataValue CreateMetadata<T>(string name, T value) where T : unmanaged
    {
        MetadataType type = Get(name);
        if (type is BuiltinMetadataType builtin)
        {
            var result = new MetadataValue(value);
            if (result.TypeName != builtin.Name) throw new InvalidCastException($"Metadata '{name}' cannot hold {typeof(T).Name}.");
            return result;
        }
        if (type is not CustomMetadataType<T> custom) throw new InvalidCastException($"Registered metadata '{name}' is not a custom {typeof(T).Name} value.");
        return custom.Create(value);
    }
    private MetadataType Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_sync) return _types.TryGetValue(name, out var type) ? type : throw new KeyNotFoundException($"Metadata type '{name}' is not registered.");
    }
    internal IReadOnlyDictionary<string, MetadataType> Snapshot(Action<long> reserve)
    {
        lock (_sync) { reserve(128L + 96L * _types.Count); return new Dictionary<string, MetadataType>(_types, StringComparer.Ordinal); }
    }
}

internal abstract class MetadataType
{
    internal abstract int? ExpectedSize { get; }
    internal abstract int DecodedValueBytes { get; }
    internal abstract MetadataValue Decode(ReadOnlySpan<byte> payload);
    internal abstract MetadataValue CreateDefault();
}
internal sealed class BuiltinMetadataType(string name) : MetadataType
{
    internal string Name => name;
    internal override int? ExpectedSize => MetadataWire.FixedSize(name);
    internal override int DecodedValueBytes => MetadataWire.FixedSize(name) ?? 0;
    internal override MetadataValue Decode(ReadOnlySpan<byte> payload) => new(MetadataWire.Decode(name, payload));
    internal override MetadataValue CreateDefault() => Decode(new byte[MetadataWire.FixedSize(name) ?? 0]);
}
internal sealed class CustomMetadataType<T>(string name, MetadataEncoder<T> encoder, MetadataDecoder<T> decoder, Func<T, bool> asBool, Func<T, string> format, int? fixedSize) : MetadataType where T : unmanaged
{
    internal override int? ExpectedSize => fixedSize;
    internal override int DecodedValueBytes => Unsafe.SizeOf<T>();
    internal override MetadataValue CreateDefault() => Create(default);
    internal MetadataValue Create(T value)
    {
        byte[] bytes = encoder(value) ?? throw new InvalidOperationException("A metadata encoder returned null.");
        ValidateSize(bytes.Length); return new MetadataValue(new RegisteredMetadata<T>(name, value, bytes, asBool, format));
    }
    internal override MetadataValue Decode(ReadOnlySpan<byte> payload)
    {
        ValidateSize(payload.Length); T value;
        try { value = decoder(payload); }
        catch (Exception error) when (error is ArgumentException or ArithmeticException or EndOfStreamException)
        { throw new InvalidDataException($"Custom metadata '{name}' rejected its payload.", error); }
        return new MetadataValue(new RegisteredMetadata<T>(name, value, payload, asBool, format));
    }
    private void ValidateSize(int length)
    {
        if (fixedSize is int size && size != length) throw new InvalidDataException($"Custom metadata '{name}' requires {size} bytes.");
    }
}
internal interface IRegisteredMetadata
{
    string TypeName { get; }
    object Value { get; }
    ReadOnlySpan<byte> Data { get; }
    bool AsBool();
    string Format();
    IRegisteredMetadata Copy();
}
internal sealed class RegisteredMetadata<T> : IRegisteredMetadata where T : unmanaged
{
    private readonly T _value;
    private readonly byte[] _data;
    private readonly Func<T, bool> _asBool;
    private readonly Func<T, string> _format;
    internal RegisteredMetadata(string name, T value, ReadOnlySpan<byte> payload, Func<T, bool> asBool, Func<T, string> format)
    { TypeName = name; _value = value; _data = payload.ToArray(); _asBool = asBool; _format = format; }
    public string TypeName { get; }
    public object Value => _value;
    public ReadOnlySpan<byte> Data => _data;
    public bool AsBool() => _asBool(_value);
    public string Format() => _format(_value);
    public IRegisteredMetadata Copy() => new RegisteredMetadata<T>(TypeName, _value, _data, _asBool, _format);
}
