// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.Points;

/// <summary>Codec-independent point-attribute reader. Dispose implements native streaming destruction deterministically.</summary>
public class AttributeHandle<T> : IDisposable where T : unmanaged
{
    private AttributeArray<T>? _storage;
    protected AttributeArray<T> Storage => _storage ?? throw new ObjectDisposedException(nameof(AttributeHandle<T>));
    private readonly bool _collapseOnDispose;
    private bool _disposed;
    public int Stride { get; }
    public int Size { get; }
    public bool IsUniform { get { CheckDisposed(); return Storage.IsUniform; } }
    public bool HasConstantStride { get { CheckDisposed(); return Storage.HasConstantStride; } }
    public AttributeArray<T> Array { get { CheckDisposed(); return Storage; } }
    public AttributeHandle(AttributeArray array, bool collapseOnDestruction = true)
    {
        ArgumentNullException.ThrowIfNull(array);
        _storage = array as AttributeArray<T> ?? throw new ArgumentException("Attribute value type does not match the handle.", nameof(array));
        Stride = array.HasConstantStride ? array.Stride : 1; Size = array.HasConstantStride ? array.Size : array.DataSize;
        _collapseOnDispose = collapseOnDestruction && array.IsStreaming;
    }
    public T Get(int index, int component = 0) => Storage.GetUnsafe(Flatten(index, component));
    protected int Flatten(int index, int component)
    {
        CheckDisposed();
        if (index < 0 || component < 0) throw new ArgumentOutOfRangeException(nameof(index));
        long flattened = (long)index * Stride + component;
        if (flattened >= Storage.DataSize) throw new ArgumentOutOfRangeException(nameof(index));
        return (int)flattened;
    }
    protected void CheckDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        if (_disposed) return;
        try { if (_collapseOnDispose) Storage.Collapse(); }
        finally { _storage = null; _disposed = true; }
        GC.SuppressFinalize(this);
    }
}
public class AttributeWriteHandle<T> : AttributeHandle<T> where T : unmanaged
{
    public AttributeWriteHandle(AttributeArray array, bool expand = true) : base(array, collapseOnDestruction: false) { if (expand) Storage.Expand(); }
    public void Set(int index, T value) => Storage.SetUnsafe(Flatten(index, 0), value);
    public void Set(int index, int component, T value) => Storage.SetUnsafe(Flatten(index, component), value);
    public void Expand(bool fill = true) { CheckDisposed(); Storage.Expand(fill); }
    public void Collapse() { CheckDisposed(); Storage.Collapse(); }
    public void Collapse(T value) { CheckDisposed(); Storage.Collapse(value); }
    public bool Compact() { CheckDisposed(); return Storage.Compact(); }
    public void Fill(T value) { CheckDisposed(); Storage.Fill(value); }
}
/// <summary>A handle bound to an explicit compile-time codec.</summary>
public class AttributeHandle<T, TCodec> : AttributeHandle<T> where T : unmanaged where TCodec : unmanaged, IAttributeCodec<T>
{
    protected TypedAttributeArray<T, TCodec> TypedStorage => (TypedAttributeArray<T, TCodec>)Storage;
    public AttributeHandle(AttributeArray array, bool collapseOnDestruction = true) : base(Compatible(array), collapseOnDestruction) { }
    public new T Get(int index, int component = 0) => TypedStorage.GetUnsafe(Flatten(index, component));
    private static AttributeArray Compatible(AttributeArray array) => array is TypedAttributeArray<T, TCodec> ? array : throw new ArgumentException("Attribute codec does not match the handle.", nameof(array));
}
public sealed class AttributeWriteHandle<T, TCodec> : AttributeHandle<T, TCodec> where T : unmanaged where TCodec : unmanaged, IAttributeCodec<T>
{
    public AttributeWriteHandle(AttributeArray array, bool expand = true) : base(array, collapseOnDestruction: false) { if (expand) TypedStorage.Expand(); }
    public void Set(int index, T value) => TypedStorage.SetUnsafe(Flatten(index, 0), value);
    public void Set(int index, int component, T value) => TypedStorage.SetUnsafe(Flatten(index, component), value);
    public void Expand(bool fill = true) { CheckDisposed(); TypedStorage.Expand(fill); }
    public void Collapse() { CheckDisposed(); TypedStorage.Collapse(); }
    public void Collapse(T value) { CheckDisposed(); TypedStorage.Collapse(value); }
    public bool Compact() { CheckDisposed(); return TypedStorage.Compact(); }
    public void Fill(T value) { CheckDisposed(); TypedStorage.Fill(value); }
}

/// <summary>Scoped, thread-safe point-attribute factory registry. Factories are captured before invocation.</summary>
public sealed class AttributeArrayRegistry
{
    public delegate AttributeArray Factory(int length, int strideOrTotalSize, bool constantStride, MetadataValue? metadata);
    private readonly object _gate = new();
    private readonly Dictionary<AttributeType, Factory> _factories = [];
    public void Register(AttributeType type, Factory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var probe = factory(0, 0, false, null);
        if (probe is null || probe.Type != type) throw new ArgumentException("Factory creates a different attribute type.", nameof(factory));
        lock (_gate) _factories[type] = factory;
    }
    public void Register<T, TCodec>() where T : unmanaged where TCodec : unmanaged, IAttributeCodec<T>
        => Register(new(AttributeValueTraits<T>.Name, TCodec.Name), static (length, stride, constant, metadata) => TypedAttributeArray<T, TCodec>.Create(length, stride, constant, metadata));
    public bool IsRegistered(AttributeType type) { lock (_gate) return _factories.ContainsKey(type); }
    public bool Unregister(AttributeType type) { lock (_gate) return _factories.Remove(type); }
    public void Clear() { lock (_gate) _factories.Clear(); }
    public AttributeArray Create(AttributeType type, int length, int strideOrTotalSize = 1, bool constantStride = true, MetadataValue? metadata = null)
    {
        Factory factory;
        lock (_gate) factory = _factories.TryGetValue(type, out var entry) ? entry : throw new KeyNotFoundException($"Unregistered attribute type {type}.");
        return factory(length, strideOrTotalSize, constantStride, metadata);
    }
}
