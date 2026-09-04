// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Typed grid and immutable metadata adaptation of openvdb/Grid.h and Metadata.h.
using System.Collections.ObjectModel;
using System.Globalization;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

namespace Tedd.OpenVDB;

public enum GridClass { Unknown, LevelSet, FogVolume, Staggered }

/// <summary>An immutable typed metadata value or opaque persistent payload. Native vec3i is stored as Coord and can also be retrieved as Vec3i.</summary>
public sealed partial record MetadataValue
{
    private readonly object _value;
    public MetadataValue(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value is not (bool or int or long or float or double or string or Coord or Vec3d or Vec3i or Vec3f or Mat4d or Mat4f or Half or Vec2i or Vec2f or Vec2d or Vec4i or Vec4f or Vec4d or OpaqueMetadata or IRegisteredMetadata))
            throw new ArgumentException("Metadata must be a supported immutable scalar, string, coordinate, vector or matrix.", nameof(value));
        _value = value is Vec3i vector ? new Coord(vector.X, vector.Y, vector.Z) : value;
    }
    internal object Representation => _value;
    public object Value => _value is IRegisteredMetadata custom ? custom.Value : _value;
    public string TypeName => _value switch
    {
        bool => "bool",
        int => "int32",
        long => "int64",
        float => "float",
        double => "double",
        string => "string",
        Coord => "vec3i",
        Vec3f => "vec3s",
        Vec3d => "vec3d",
        Mat4d => "mat4d",
        Mat4f => "mat4s",
        Half => "half",
        Vec2i => "vec2i",
        Vec2f => "vec2s",
        Vec2d => "vec2d",
        Vec4i => "vec4i",
        Vec4f => "vec4s",
        Vec4d => "vec4d",
        OpaqueMetadata opaque => opaque.TypeName,
        IRegisteredMetadata custom => custom.TypeName,
        _ => throw new InvalidOperationException()
    };
    public TValue GetValue<TValue>()
    {
        if (Value is TValue typed) return typed;
        if (_value is Coord coordinate && typeof(TValue) == typeof(Vec3i)) return (TValue)(object)new Vec3i(coordinate.X, coordinate.Y, coordinate.Z);
        throw new InvalidCastException($"Metadata type {TypeName} cannot be read as {typeof(TValue).Name}.");
    }
    public override string ToString() => FormatNative(_value);
}

/// <summary>A typed sparse tree with an index/world transform and independent grid metadata.</summary>
/// <remarks>The tree supplied to the constructor is shared. DeepCopy clones the tree, transform and metadata map.</remarks>
public sealed partial class Grid<T> : IGrid where T : unmanaged
{
    public const string NameMetadataKey = "name";
    public const string ClassMetadataKey = "class";
    private readonly Dictionary<string, MetadataValue> _metadata = new(StringComparer.Ordinal);
    private readonly ReadOnlyDictionary<string, MetadataValue> _metadataView;
    private Transform _transform = new();
    public Grid(T background = default) : this(new VdbTree<T>(background)) { }
    public Grid(VdbTree<T> tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        Tree = tree;
        _metadataView = new(_metadata);
    }
    public VdbTree<T> Tree { get; private set; }
    /// <summary>Replaces the shared tree reference; metadata and transform are retained.</summary>
    /// <remarks>Existing accessors and other grids continue to reference the previous tree.</remarks>
    public void SetTree(VdbTree<T> tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        Tree = tree;
    }
    public T Background => Tree.Background;
    public Type ValueType => typeof(T);
    public bool IsEmpty => Tree.IsEmpty;
    public ulong ActiveVoxelCount => Tree.ActiveVoxelCount;
    public IReadOnlyDictionary<string, MetadataValue> Metadata => _metadataView;
    public Transform Transform
    {
        get => _transform;
        set { ArgumentNullException.ThrowIfNull(value); _transform = value; }
    }
    public string Name
    {
        get => _metadata.TryGetValue(NameMetadataKey, out var value) ? value.ToString() : string.Empty;
        set { ArgumentNullException.ThrowIfNull(value); SetMetadata(NameMetadataKey, value); }
    }
    public GridClass GridClass
    {
        get => _metadata.TryGetValue(ClassMetadataKey, out var value) && value.Value is string text ? ParseGridClass(text) : GridClass.Unknown;
        set => SetMetadata(ClassMetadataKey, GridClassToString(value));
    }
    public static string GridClassToString(GridClass gridClass) => gridClass switch
    {
        GridClass.Unknown => "unknown",
        GridClass.LevelSet => "level set",
        GridClass.FogVolume => "fog volume",
        GridClass.Staggered => "staggered",
        _ => "unknown"
    };
    public static GridClass ParseGridClass(string text) => text.Trim().ToLowerInvariant() switch
    {
        "level set" => GridClass.LevelSet,
        "fog volume" => GridClass.FogVolume,
        "staggered" => GridClass.Staggered,
        _ => GridClass.Unknown
    };
    public void SetMetadata(string name, object value) => SetMetadata(name, new MetadataValue(value));
    public void SetMetadata(string name, MetadataValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        _metadata[name] = value;
    }
    public bool RemoveMetadata(string name) => _metadata.Remove(name);
    public TValue GetMetadata<TValue>(string name) => _metadata[name].GetValue<TValue>();
    public bool TryGetMetadata<TValue>(string name, out TValue value)
    {
        if (_metadata.TryGetValue(name, out var metadata) && metadata.Value is TValue typed) { value = typed; return true; }
        if (metadata?.Value is Coord coordinate && typeof(TValue) == typeof(Vec3i)) { value = (TValue)(object)new Vec3i(coordinate.X, coordinate.Y, coordinate.Z); return true; }
        value = default!; return false;
    }
    public T this[Coord coordinate] { get => Tree.GetValue(coordinate); set => Tree.SetValueOn(coordinate, value); }
    public ValueAccessor<T> GetAccessor() => Tree.GetAccessor();
    public void Fill(CoordBBox box, T value, bool active = true) => Tree.Fill(box, value, active);
    public bool TryGetActiveVoxelBoundingBox(out CoordBBox box) => Tree.TryGetActiveVoxelBoundingBox(out box);
    public Vec3d IndexToWorld(Coord coordinate) => _transform.IndexToWorld(coordinate);
    public Vec3d IndexToWorld(Vec3d coordinate) => _transform.IndexToWorld(coordinate);
    public Vec3d WorldToIndex(Vec3d coordinate) => _transform.WorldToIndex(coordinate);
    public Grid<T> DeepCopy()
    {
        var copy = new Grid<T>(Tree.DeepCopy()) { Transform = _transform.Copy() };
        foreach (var pair in _metadata) copy._metadata.Add(pair.Key, pair.Value.DeepCopy());
        return copy;
    }
}
