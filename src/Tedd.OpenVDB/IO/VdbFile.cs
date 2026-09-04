// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Format port of io/Archive.cc, GridDescriptor.cc, Compression.h, Metadata.h,
// math/Maps.h and tree/{Tree,RootNode,InternalNode,LeafNode}.h at
// f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf.
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;

namespace Tedd.OpenVDB.IO;

/// <summary>Resource limits for untrusted .vdb input. All limits must be positive.</summary>
public sealed record VdbReadOptions
{
    public long MaxBytes { get; init; } = 512L * 1024 * 1024;
    public long MaxAllocatedBytes { get; init; } = 512L * 1024 * 1024;
    public int MaxNodes { get; init; } = 100_000;
    public int MaxGrids { get; init; } = 4096;
    public int MaxRootEntries { get; init; } = 100_000;
    public int MaxMetadataItems { get; init; } = 4096;
    public int MaxStringBytes { get; init; } = 1024 * 1024;
    public int MaxMetadataBytes { get; init; } = 16 * 1024 * 1024;
    internal void Validate()
    {
        if (MaxBytes <= 0 || MaxAllocatedBytes <= 0 || MaxNodes <= 0 || MaxGrids <= 0 || MaxRootEntries <= 0 ||
            MaxMetadataItems <= 0 || MaxStringBytes <= 0 || MaxMetadataBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(VdbReadOptions), "Every read limit must be positive.");
    }
}

/// <summary>Standard OpenVDB format 225 interchange for scalar, Vec3 and topology-mask Tree4 grids.</summary>
/// <remarks>
/// No native runtime is required. Supports linear and nonlinear frustum transforms, typed and opaque
/// metadata, heterogeneous archives, and instances whose parents precede them. Zlib, active-mask
/// compression, Blosc LZ4/zlib byte-shuffle frames and floating-point scalar/vector half encoding are supported. Unsupported Blosc codecs/filters, grid types or
/// legacy format versions are rejected explicitly.
/// Temporary __delayedload metadata is consumed and discarded. Reading and writing require
/// exclusive access to the stream, grid and tree. Seekable streams must start at position zero.
/// A failed write may leave partial output; use a temporary file for atomic replacement.
/// </remarks>
public static partial class VdbFile
{
    private const long Magic = 0x56444220;
    private const uint FormatVersion = 225;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Read exactly one grid. Use ReadArchive to retain file metadata or multiple grids.</summary>
    public static Grid<T> Read<T>(Stream stream, bool leaveOpen = true, VdbReadOptions? options = null) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            _ = ScalarName<T>();
            VdbArchive archive = ReadArchiveCore(stream, leaveOpen: true, options, singleGrid: true);
            return archive.Grids[0] as Grid<T> ?? throw new NotSupportedException($"Grid scalar type does not match {typeof(T).Name}.");
        }
        finally { if (!leaveOpen) stream.Dispose(); }
    }

    public static void Write<T>(Stream stream, Grid<T> grid, bool leaveOpen = true, VdbWriteOptions? options = null) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            ArgumentNullException.ThrowIfNull(grid);
            WriteArchive(stream, new VdbArchive([grid]), leaveOpen: true, options);
        }
        finally { if (!leaveOpen) stream.Dispose(); }
    }

    private static Grid<T> ReadGrid<T>(Input input, Descriptor descriptor, IReadOnlyDictionary<string, IGrid> previous) where T : unmanaged
    {
        bool instance = descriptor.Parent.Length != 0;
        VdbTree<T> tree;
        var metadata = ReadMetadata(input);
        input.Half = StorageHalfFlag(metadata);
        if (input.Half != descriptor.Half) throw new InvalidDataException("Half-float descriptor and storage metadata disagree.");
        if (input.Half && !SupportsHalf(typeof(T))) throw new NotSupportedException("Half-float encoding requires floating-point scalar or vector grids.");
        Transform transform = ReadTransform(input);
        if (instance)
        {
            if (!previous.TryGetValue(descriptor.Parent, out IGrid? parent))
                throw new NotSupportedException("Instance parents must precede their instances in the archive.");
            if ((parent is MaskGrid) != descriptor.Mask || (parent is MaskGrid maskParent ? maskParent.Storage : parent) is not Grid<T> typed || StorageHalfFlag(parent.Metadata) != input.Half)
                throw new InvalidDataException("Instance parent scalar type or half-float encoding differs.");
            tree = typed.Tree;
        }
        else
        {
            if (input.Int32() != 1) throw new NotSupportedException("Only single-buffer trees are supported.");
            T background = input.Scalar<T>();
            tree = new VdbTree<T>(background);
            uint tileCount = input.UInt32(), childCount = input.UInt32();
            if ((ulong)tileCount + childCount > (ulong)input.Options.MaxRootEntries) throw new InvalidDataException("Root entry limit exceeded.");
            input.Reserve(((long)tileCount + childCount) * 128);
            for (uint index = 0; index < tileCount; ++index)
            {
                Coord origin = ReadRootOrigin(input);
                var entry = new VdbTree<T>.RootEntry(input.Scalar<T>(), input.Boolean());
                if (!tree.Entries.TryAdd(origin, entry)) throw new InvalidDataException("Duplicate root coordinate.");
            }
            var leaves = new List<LeafNode<T>>();
            for (uint index = 0; index < childCount; ++index)
            {
                Coord origin = ReadRootOrigin(input);
                if (tree.Entries.ContainsKey(origin)) throw new InvalidDataException("Duplicate root coordinate.");
                var node = ReadInternal<T>(input, 2, origin, leaves, background);
                tree.Entries.Add(origin, new(default, false, node));
            }
            if (descriptor.Offsets && input.Position != descriptor.BlockPosition) throw new InvalidDataException("Topology does not end at the leaf-buffer offset.");
            foreach (LeafNode<T> leaf in leaves)
            {
                NodeMask mask = ReadMask(input, 3);
                if (!mask.Equals(leaf.ValueMask)) throw new InvalidDataException("Leaf buffer activity differs from its topology mask.");

                var values = new T[LeafNode<T>.Size];
                if (typeof(T) == typeof(bool))
                {
                    if (input.Coord() != leaf.Origin) throw new InvalidDataException("Boolean leaf origin differs from its topology position.");
                    NodeMask valuesMask = descriptor.Mask ? mask : ReadMask(input, 3);
                    for (int index = 0; index < values.Length; ++index) { bool value = valuesMask.IsOn(index); values[index] = Unsafe.As<bool, T>(ref value); }
                }
                else ReadCompressedValues(input, values.AsSpan(), mask, background);
                for (int index = 0; index < values.Length; ++index) leaf.SetValueOnly(index, values[index]);
            }

        }
        if (descriptor.Offsets && input.Position != descriptor.EndPosition)
            throw new InvalidDataException("Grid buffer data does not match its end offset.");
        var grid = new Grid<T>(tree) { Transform = transform };
        foreach (var pair in metadata) grid.SetMetadata(pair.Key, pair.Value);
        string plainName = descriptor.Name.Split('\x1e')[0];
        if (metadata.TryGetValue(Grid<T>.NameMetadataKey, out MetadataValue? name))
        {
            if (name.Value is not string text || text != plainName) throw new InvalidDataException("Grid metadata name disagrees with its descriptor.");
        }
        else if (plainName.Length != 0) grid.Name = plainName;
        return grid;
    }

    private static void WriteGrid<T>(Stream stream, BinaryWriter output, Grid<T> grid, string name, string parent,
        VdbCompression compression, bool half, bool maskGrid = false) where T : unmanaged
    {
        IReadOnlyDictionary<string, MetadataValue> metadata = OutputMetadata(grid, half);
        WriteString(output, name); WriteString(output, $"Tree_{(maskGrid ? "mask" : ScalarName<T>())}_5_4_3{(half ? "_HalfFloat" : "")}"); WriteString(output, parent);
        long offsetPosition = stream.CanSeek ? stream.Position : 0;
        output.Write(0L); output.Write(0L); output.Write(0L);
        long gridPosition = stream.CanSeek ? stream.Position : 0;
        output.Write((uint)compression); WriteMetadata(output, metadata); WriteTransform(output, grid.Transform);
        long blockPosition = 0;
        if (parent.Length == 0)
        {
            output.Write(1); WriteScalar(output, half ? QuantizeHalf(grid.Background) : grid.Background);
            var entries = grid.Tree.Entries.OrderBy(pair => pair.Key).ToArray();
            output.Write((uint)entries.Count(pair => pair.Value.Child is null));
            output.Write((uint)entries.Count(pair => pair.Value.Child is not null));
            foreach (var pair in entries)
            {
                if (pair.Value.Child is not null) continue;
                WriteCoord(output, pair.Key); WriteScalar(output, pair.Value.Value); output.Write(pair.Value.Active);
            }
            foreach (var pair in entries)
            {
                if (pair.Value.Child is null) continue;
                WriteCoord(output, pair.Key); WriteInternal(output, pair.Value.Child, grid.Background, compression, half);
            }
            blockPosition = stream.CanSeek ? stream.Position : 0;
            foreach (var pair in entries)
                if (pair.Value.Child is not null) WriteBuffers(output, pair.Value.Child, grid.Background, compression, half, maskGrid);

        }
        output.Flush();
        if (stream.CanSeek)
        {
            long endPosition = stream.Position; stream.Position = offsetPosition;
            output.Write(gridPosition); output.Write(blockPosition); output.Write(endPosition);
            output.Flush(); stream.Position = endPosition;
        }
    }
    private static string ScalarName<T>() where T : unmanaged => typeof(T) == typeof(int) ? "int32" :
        typeof(T) == typeof(long) ? "int64" : typeof(T) == typeof(bool) ? "bool" :
        typeof(T) == typeof(float) ? "float" : typeof(T) == typeof(double) ? "double" :
        typeof(T) == typeof(Vec3i) ? "vec3i" : typeof(T) == typeof(Vec3f) ? "vec3s" : typeof(T) == typeof(Vec3d) ? "vec3d" :
        throw new NotSupportedException("Unsupported VDB Tree4 value type.");
    private static Coord ReadRootOrigin(Input input)
    {
        Coord origin = input.Coord();
        if ((origin.X & 4095) != 0 || (origin.Y & 4095) != 0 || (origin.Z & 4095) != 0)
            throw new InvalidDataException("Root origin is not aligned to the 4096-voxel Tree4 boundary.");
        return origin;
    }

    private static InternalNode<T> ReadInternal<T>(Input input, int level, Coord origin, List<LeafNode<T>> leaves, T background) where T : unmanaged
    {
        int log2Dim = level == 2 ? 5 : 4;
        int size = 1 << (3 * log2Dim);
        input.Node(2048L + size * Unsafe.SizeOf<T>() + size / 2);
        NodeMask childMask = ReadMask(input, log2Dim), active = ReadMask(input, log2Dim);
        for (int word = 0; word < childMask.WordCount; ++word)
            if ((childMask.Words[word] & active.Words[word]) != 0) throw new InvalidDataException("Child and active-tile masks overlap.");

        var node = new InternalNode<T>(level, origin, default, false);
        ReadCompressedValues(input, node.Values.AsSpan(), active, background); node.Active.OrWith(active);
        foreach (int index in childMask.OnIndices)
        {
            input.Reserve(128); // Dictionary slot and leaf-traversal reference allowance.
            Coord childOrigin = node.ChildOrigin(index);
            if (level == 2) node.AddChild(index, ReadInternal<T>(input, 1, childOrigin, leaves, background));
            else
            {
                input.Node(2048L + 2L * LeafNode<T>.Size * Unsafe.SizeOf<T>() + 256);
                var leaf = new LeafNode<T>(childOrigin);
                NodeMask mask = ReadMask(input, 3);
                foreach (int on in mask.OnIndices) leaf.SetValueOn(on);
                node.AddChild(index, leaf); leaves.Add(leaf);
            }
        }
        return node;
    }

    private static NodeMask ReadMask(Input input, int log2Dim)
    {
        var mask = new NodeMask(log2Dim);
        for (int word = 0; word < mask.WordCount; ++word)
        {
            ulong bits = input.UInt64();
            while (bits != 0)
            {
                int bit = System.Numerics.BitOperations.TrailingZeroCount(bits);
                mask.SetOn(word * 64 + bit); bits &= bits - 1;
            }
        }
        return mask;
    }
    private static void WriteMask(BinaryWriter output, NodeMask mask)
    {
        foreach (ulong word in mask.Words) output.Write(word);
    }
    private static void WriteInternal<T>(BinaryWriter output, InternalNode<T> node, T background, VdbCompression compression, bool half) where T : unmanaged
    {
        var childMask = new NodeMask(node.Log2Dim);
        foreach (int index in node.Children.Keys) childMask.SetOn(index);
        WriteMask(output, childMask); WriteMask(output, node.Active);
        var values = (T[])node.Values.Clone();
        foreach (int index in node.Children.Keys) values[index] = default;
        WriteCompressedValues(output, values.AsSpan(), node.Active, childMask, background, compression, half);
        foreach (int index in childMask.OnIndices)
        {
            if (node.Children[index] is InternalNode<T> child) WriteInternal(output, child, background, compression, half);
            else WriteMask(output, ((LeafNode<T>)node.Children[index]).ValueMask);
        }
    }
    private static void WriteBuffers<T>(BinaryWriter output, InternalNode<T> node, T background, VdbCompression compression, bool half, bool maskGrid = false) where T : unmanaged
    {
        foreach (var pair in node.Children.OrderBy(pair => pair.Key))
        {
            if (pair.Value is InternalNode<T> child) WriteBuffers(output, child, background, compression, half, maskGrid);
            else
            {
                var leaf = (LeafNode<T>)pair.Value;
                NodeMask mask = leaf.ValueMask;
                WriteMask(output, mask);
                if (typeof(T) == typeof(bool))
                {
                    WriteCoord(output, leaf.Origin);
                    if (!maskGrid)
                    {
                        var valuesMask = new NodeMask(3);
                        for (int index = 0; index < LeafNode<T>.Size; ++index) { T value = leaf.GetValue(index); if (Unsafe.As<T, bool>(ref value)) valuesMask.SetOn(index); }
                        WriteMask(output, valuesMask);
                    }
                }
                else WriteCompressedValues(output, leaf.Values, mask, null, background, compression, half);
            }
        }
    }
    private static void WriteScalar<T>(BinaryWriter output, T value) where T : unmanaged
    {
        Span<byte> bytes = stackalloc byte[Unsafe.SizeOf<T>()]; EncodeScalar(bytes, value); output.Write(bytes);
    }
    private static void WriteCoord(BinaryWriter output, Coord value) { output.Write(value.X); output.Write(value.Y); output.Write(value.Z); }
    private static void WriteVector(BinaryWriter output, Vec3d value) { output.Write(value.X); output.Write(value.Y); output.Write(value.Z); }
    private static void WriteMatrix(BinaryWriter output, Mat4d matrix)
    {
        for (int row = 0; row < 4; ++row) for (int column = 0; column < 4; ++column) output.Write(matrix[row, column]);
    }
    private static void WriteString(BinaryWriter output, string value)
    {
        byte[] bytes = Utf8.GetBytes(value); output.Write((uint)bytes.Length); output.Write(bytes);
    }

    private static void WriteTransform(BinaryWriter output, Transform transform)
    {
        if (transform.FrustumMap is { } frustum)
        {
            WriteString(output, "NonlinearFrustumMap");
            WriteVector(output, frustum.BoundingBox.Min); WriteVector(output, frustum.BoundingBox.Max);
            output.Write(frustum.Taper); output.Write(frustum.Depth);
        }
        WriteString(output, "AffineMap"); WriteMatrix(output, transform.Matrix);
    }

    private static Transform ReadTransform(Input input, bool allowFrustum = true)
    {
        string type = input.String();
        if (type == "NonlinearFrustumMap")
        {
            if (!allowFrustum) throw new InvalidDataException("A frustum secondary map must be linear.");
            var bounds = new BBoxd(input.Vector(), input.Vector());
            double taper = input.Scalar<double>(), depth = input.Scalar<double>();
            Transform secondary = ReadTransform(input, allowFrustum: false);
            try { return new Transform(new NonlinearFrustumMap(bounds, taper, depth, secondary.Matrix)); }
            catch (Exception exception) when (exception is ArgumentException or ArithmeticException)
            { throw new InvalidDataException("Invalid nonlinear frustum transform.", exception); }
        }
        Mat4d matrix;
        if (type is "AffineMap" or "UnitaryMap") matrix = input.Matrix();
        else if (type == "TranslationMap") matrix = Mat4d.CreateTranslation(input.Vector());
        else if (type is "ScaleMap" or "UniformScaleMap" or "ScaleTranslateMap" or "UniformScaleTranslateMap")
        {
            bool translated = type is "ScaleTranslateMap" or "UniformScaleTranslateMap";
            Vec3d translation = translated ? input.Vector() : Vec3d.Zero;
            Vec3d scale = input.Vector();
            // Native maps persist four cached vectors as well as their scale. Consume and
            // validate finite numbers, then recompute the derived transform from the primary data.
            for (int index = 0; index < 4; ++index)
            {
                Vec3d cached = input.Vector();
                if (!double.IsFinite(cached.X) || !double.IsFinite(cached.Y) || !double.IsFinite(cached.Z))
                    throw new InvalidDataException("Nonfinite cached linear-map data.");
            }
            matrix = Mat4d.CreateScale(scale) * Mat4d.CreateTranslation(translation);
        }
        else throw new NotSupportedException($"VDB transform map '{type}' is unsupported.");
        try { return new Transform(matrix); }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException)
        { throw new InvalidDataException("Invalid affine transform.", exception); }
    }

    private static Dictionary<string, MetadataValue> ReadMetadata(Input input, bool allowNonempty = true)
    {
        uint count = input.UInt32();
        if (!allowNonempty && count != 0) throw new NotSupportedException("Use ReadArchive for file-level metadata.");
        if (count > input.Options.MaxMetadataItems) throw new InvalidDataException("Metadata item limit exceeded.");
        var result = new Dictionary<string, MetadataValue>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        for (uint index = 0; index < count; ++index)
        {
            input.Reserve(256);
            string name = input.String(), type = input.String();
            if (name.Length == 0 || type.Length == 0 || !keys.Add(name)) throw new InvalidDataException("Empty or duplicate metadata name.");
            uint size = input.UInt32(); total += size;
            if (size > input.Options.MaxMetadataBytes || total > input.Options.MaxMetadataBytes) throw new InvalidDataException("Metadata byte limit exceeded.");
            if (type == "__delayedload") { input.Skip((int)size); continue; }
            long end = input.Position + size;
            object value = type switch
            {
                "bool" when size == 1 => input.Boolean(),
                "int32" when size == 4 => input.Int32(),
                "int64" when size == 8 => input.Int64(),
                "float" when size == 4 => input.Scalar<float>(),
                "double" when size == 8 => input.Scalar<double>(),
                "string" => input.Utf8((int)size),
                "vec3i" when size == 12 => input.Coord(),
                "vec3d" when size == 24 => input.Vector(),
                "vec3s" when size == 12 => input.Scalar<Vec3f>(),
                "mat4d" when size == 128 => input.Matrix(),
                "bool" or "int32" or "int64" or "float" or "double" or "vec3i" or "vec3s" or "vec3d" or "mat4d" => throw new InvalidDataException("Metadata size disagrees with its type."),
                _ => ReadOpaque(input, type, (int)size)
            };
            if (input.Position != end) throw new InvalidDataException("Metadata payload length mismatch.");
            result.Add(name, new MetadataValue(value));
        }
        return result;
    }
    private static OpaqueMetadata ReadOpaque(Input input, string type, int size)
    {
        input.Reserve(size); return new OpaqueMetadata(type, input.Bytes(size));
    }
    private static void WriteMetadata(BinaryWriter output, IReadOnlyDictionary<string, MetadataValue> metadata)
    {
        output.Write((uint)metadata.Count);
        foreach (var pair in metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            WriteString(output, pair.Key); WriteString(output, pair.Value.TypeName);
            object value = pair.Value.Value;
            switch (value)
            {
                case bool v: output.Write(1u); output.Write(v); break;
                case int v: output.Write(4u); output.Write(v); break;
                case long v: output.Write(8u); output.Write(v); break;
                case float v: output.Write(4u); output.Write(v); break;
                case double v: output.Write(8u); output.Write(v); break;
                case string v: WriteString(output, v); break; // Metadata's size is the string byte count.
                case Coord v: output.Write(12u); WriteCoord(output, v); break;
                case Vec3d v: output.Write(24u); WriteVector(output, v); break;
                case Vec3f v: output.Write(12u); WriteScalar(output, v); break;
                case Mat4d v: output.Write(128u); WriteMatrix(output, v); break;
                case OpaqueMetadata v: output.Write((uint)v.Data.Length); output.Write(v.Data); break;
                default: throw new NotSupportedException("Unsupported metadata value.");
            }
        }
    }
    private static bool StorageHalfFlag(IReadOnlyDictionary<string, MetadataValue> metadata)
    {
        if (!metadata.TryGetValue("is_saved_as_half_float", out var half)) return false;
        return half.Value is bool enabled ? enabled : throw new NotSupportedException("Half-float storage metadata must be Boolean.");
    }
    private static void ValidateGrid<T>(Grid<T> grid) where T : unmanaged
    {
        _ = StorageHalfFlag(grid.Metadata);
        ValidateMetadata(grid.Metadata);
        if (grid.Metadata.TryGetValue(Grid<T>.NameMetadataKey, out var name) && name.Value is not string)
            throw new NotSupportedException("The grid name metadata must be a string.");
        if (grid.Name.Contains('\x1e')) throw new NotSupportedException("Grid names containing the native instance separator are unsupported.");
        foreach (var pair in grid.Metadata)
        {
            Utf8.GetByteCount(pair.Key);
            if (pair.Value.Value is string text) Utf8.GetByteCount(text);
        }
        foreach (var pair in grid.Tree.Entries)
        {
            if (VdbTree<T>.Align(pair.Key, 12) != pair.Key) throw new InvalidDataException("Unaligned root origin.");
            if (pair.Value.Child is not null) ValidateNode(pair.Value.Child, pair.Key, 2);
        }
    }
    private static void ValidateNode<T>(InternalNode<T> node, Coord origin, int level) where T : unmanaged
    {
        if (node.Origin != origin || node.Level != level) throw new InvalidDataException("Invalid internal-node hierarchy.");
        foreach (var pair in node.Children)
        {
            if ((uint)pair.Key >= (uint)node.Values.Length || node.Active.IsOn(pair.Key)) throw new InvalidDataException("Invalid child index or overlapping masks.");
            Coord childOrigin = node.ChildOrigin(pair.Key);
            if (level == 2 && pair.Value is InternalNode<T> child) ValidateNode(child, childOrigin, 1);
            else if (level == 1 && pair.Value is LeafNode<T> leaf && leaf.Origin == childOrigin) { }
            else throw new InvalidDataException("Invalid Tree4 child layout.");
        }
    }

    private sealed class Input(Stream stream, VdbReadOptions options)
    {
        public VdbReadOptions Options { get; } = options;
        public long Position { get; private set; }
        public VdbCompression Compression { get; set; }
        public bool Half { get; set; }
        public long Limit { get; set; } = long.MaxValue;
        private long _allocated;
        private int _nodes;
        public void Reserve(long bytes)
        {
            if (bytes < 0 || bytes > Options.MaxAllocatedBytes - _allocated) throw new InvalidDataException("VDB allocation limit exceeded.");
            _allocated += bytes;
        }
        public void Node(long bytes)
        {
            if (++_nodes > Options.MaxNodes) throw new InvalidDataException("VDB node limit exceeded.");
            Reserve(bytes);
        }
        public void Exact(Span<byte> buffer)
        {
            if (buffer.Length > Options.MaxBytes - Position || buffer.Length > Limit - Position) throw new InvalidDataException("VDB byte limit exceeded.");
            stream.ReadExactly(buffer); Position += buffer.Length;
        }
        public byte Byte() { Span<byte> bytes = stackalloc byte[1]; Exact(bytes); return bytes[0]; }
        public bool Boolean() => Byte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Boolean value is not zero or one.") };
        public uint UInt32() { Span<byte> bytes = stackalloc byte[4]; Exact(bytes); return BinaryPrimitives.ReadUInt32LittleEndian(bytes); }
        public int Int32() => unchecked((int)UInt32());
        public ulong UInt64() { Span<byte> bytes = stackalloc byte[8]; Exact(bytes); return BinaryPrimitives.ReadUInt64LittleEndian(bytes); }
        public long Int64() => unchecked((long)UInt64());
        public byte[] Bytes(int length)
        {
            if (length < 0 || length > Options.MaxBytes - Position || length > Limit - Position || (stream.CanSeek && length > stream.Length - stream.Position))
                throw new InvalidDataException("Declared byte length exceeds the available input.");
            Reserve(length); var bytes = new byte[length]; Exact(bytes); return bytes;
        }
        public string Utf8(int length)
        {
            if (length > Options.MaxStringBytes) throw new InvalidDataException("VDB string limit exceeded.");
            Reserve((long)length * 2); return VdbFile.Utf8.GetString(Bytes(length));
        }
        public string String()
        {
            uint length = UInt32();
            if (length > Options.MaxStringBytes) throw new InvalidDataException("VDB string limit exceeded.");
            return Utf8((int)length);
        }
        public void Skip(int length)
        {
            Span<byte> buffer = stackalloc byte[1024];
            while (length > 0) { int chunk = System.Math.Min(length, buffer.Length); Exact(buffer[..chunk]); length -= chunk; }
        }
        public T Scalar<T>() where T : unmanaged
        {
            Span<byte> bytes = stackalloc byte[Unsafe.SizeOf<T>()]; Exact(bytes); return DecodeScalar<T>(bytes);
        }
        public void Values<T>(Span<T> values) where T : unmanaged
        {
            if (BitConverter.IsLittleEndian) Exact(MemoryMarshal.AsBytes(values));
            else for (int index = 0; index < values.Length; ++index) values[index] = Scalar<T>();
        }
        public Coord Coord() => new(Int32(), Int32(), Int32());
        public Vec3d Vector() => new(Scalar<double>(), Scalar<double>(), Scalar<double>());
        public Mat4d Matrix()
        {
            Span<double> values = stackalloc double[16];
            for (int index = 0; index < values.Length; ++index) values[index] = Scalar<double>();
            return Mat4d.FromSpan(values);
        }
    }
}
