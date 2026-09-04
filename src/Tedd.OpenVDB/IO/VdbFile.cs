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
    public int MaxRootEntries { get; init; } = 100_000;
    public int MaxMetadataItems { get; init; } = 4096;
    public int MaxStringBytes { get; init; } = 1024 * 1024;
    public int MaxMetadataBytes { get; init; } = 16 * 1024 * 1024;
    internal void Validate()
    {
        if (MaxBytes <= 0 || MaxAllocatedBytes <= 0 || MaxNodes <= 0 || MaxRootEntries <= 0 ||
            MaxMetadataItems <= 0 || MaxStringBytes <= 0 || MaxMetadataBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(VdbReadOptions), "Every read limit must be positive.");
    }
}

/// <summary>Standard OpenVDB format 225 interchange for one uncompressed int, float or double Tree4 grid.</summary>
/// <remarks>
/// No native runtime is required. Supports affine and common linear transforms and the immutable
/// metadata types exposed by Grid. Compression, half-float encoding, instances, multiple grids,
/// nonempty file-level metadata and unknown persistent metadata are rejected explicitly.
/// Temporary __delayedload metadata is consumed and discarded. Reading and writing require
/// exclusive access to the stream, grid and tree. Seekable streams must start at position zero.
/// A failed write may leave partial output; use a temporary file for atomic replacement.
/// </remarks>
public static class VdbFile
{
    private const long Magic = 0x56444220;
    private const uint FormatVersion = 225;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static Grid<T> Read<T>(Stream stream, bool leaveOpen = true, VdbReadOptions? options = null) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            string scalar = ScalarName<T>();
            if (!stream.CanRead) throw new ArgumentException("The stream is not readable.", nameof(stream));
            if (stream.CanSeek && stream.Position != 0) throw new ArgumentException("A VDB file must start at stream position zero.", nameof(stream));
            options ??= new(); options.Validate();
            var input = new Input(stream, options);
            if (input.Int64() != Magic) throw new InvalidDataException("Invalid OpenVDB magic number.");
            uint version = input.UInt32();
            if (version != FormatVersion) throw new NotSupportedException($"VDB format {version} is unsupported; only 225 is implemented.");
            input.UInt32(); input.UInt32(); // Library major/minor do not select the on-disk layout.
            bool offsets = input.Boolean();
            string uuid = Encoding.ASCII.GetString(input.Bytes(36));
            if (!Guid.TryParseExact(uuid, "D", out _)) throw new InvalidDataException("Invalid VDB UUID.");
            if (input.UInt32() != 0) throw new NotSupportedException("File-level metadata is not represented by this single-grid API.");
            int gridCount = input.Int32();
            if (gridCount < 0) throw new InvalidDataException("Negative grid count.");
            if (gridCount != 1) throw new NotSupportedException("Exactly one grid is supported.");
            string descriptorName = input.String();
            string treeType = input.String();
            if (treeType != $"Tree_{scalar}_5_4_3") throw new NotSupportedException($"Grid type '{treeType}' does not match the requested uncompressed Tree4<{scalar}> layout.");
            if (input.String().Length != 0) throw new NotSupportedException("Instanced grids are unsupported.");
            long gridPosition = input.Int64(), blockPosition = input.Int64(), endPosition = input.Int64();
            if (offsets)
            {
                if (gridPosition != input.Position || blockPosition < gridPosition || endPosition < blockPosition || endPosition > options.MaxBytes)
                    throw new InvalidDataException("Invalid grid offsets.");
                if (stream.CanSeek && endPosition != stream.Length) throw new InvalidDataException("Grid end offset does not match the file length.");
            }
            else if (gridPosition != 0 || blockPosition != 0 || endPosition != 0)
                throw new InvalidDataException("A streaming VDB descriptor must contain zero offsets.");
            if (input.UInt32() != 0) throw new NotSupportedException("Compressed VDB grids, including active-mask compression, are unsupported.");
            var metadata = ReadMetadata(input);
            ValidateStorageMetadata(metadata);
            Transform transform = ReadTransform(input);
            if (input.Int32() != 1) throw new NotSupportedException("Only single-buffer trees are supported.");
            T background = input.Scalar<T>();
            var tree = new VdbTree<T>(background);
            uint tileCount = input.UInt32(), childCount = input.UInt32();
            if ((ulong)tileCount + childCount > (ulong)options.MaxRootEntries) throw new InvalidDataException("Root entry limit exceeded.");
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
                var node = ReadInternal<T>(input, 2, origin, leaves);
                tree.Entries.Add(origin, new(default, false, node));
            }
            if (offsets && input.Position != blockPosition) throw new InvalidDataException("Topology does not end at the leaf-buffer offset.");
            foreach (LeafNode<T> leaf in leaves)
            {
                NodeMask mask = ReadMask(input, 3);
                if (!mask.Equals(leaf.ValueMask)) throw new InvalidDataException("Leaf buffer activity differs from its topology mask.");
                RequireRawValues(input);
                var values = new T[LeafNode<T>.Size];
                input.Values(values.AsSpan());
                for (int index = 0; index < values.Length; ++index) leaf.SetValueOnly(index, values[index]);
            }
            if (offsets && input.Position != endPosition) throw new InvalidDataException("Grid buffer data does not match its end offset.");
            if (stream.CanSeek && input.Position != stream.Length) throw new InvalidDataException("Unexpected bytes after the single VDB grid.");
            var grid = new Grid<T>(tree) { Transform = transform };
            foreach (var pair in metadata) grid.SetMetadata(pair.Key, pair.Value);
            string plainName = descriptorName.Split('\x1e')[0];
            if (metadata.TryGetValue(Grid<T>.NameMetadataKey, out MetadataValue? name))
            {
                if (name.Value is not string text || text != plainName) throw new InvalidDataException("Grid metadata name disagrees with its descriptor.");
            }
            else if (plainName.Length != 0) grid.Name = plainName;
            return grid;
        }
        catch (EndOfStreamException exception) { throw new InvalidDataException("Truncated VDB input.", exception); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Invalid UTF-8 in VDB input.", exception); }
        finally { if (!leaveOpen) stream.Dispose(); }
    }

    public static void Write<T>(Stream stream, Grid<T> grid, bool leaveOpen = true) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            ArgumentNullException.ThrowIfNull(grid);
            string scalar = ScalarName<T>();
            if (!stream.CanWrite) throw new ArgumentException("The stream is not writable.", nameof(stream));
            if (stream.CanSeek && stream.Position != 0) throw new ArgumentException("A VDB file must start at stream position zero.", nameof(stream));
            ValidateGrid(grid); // Reject unsupported metadata and malformed structure before emitting a header.
            using var output = new BinaryWriter(stream, Utf8, leaveOpen: true);
            output.Write(Magic); output.Write(FormatVersion); output.Write(13u); output.Write(0u);
            output.Write(stream.CanSeek); output.Write(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("D")));
            output.Write(0u); output.Write(1); // Empty file metadata; one grid.
            WriteString(output, grid.Name); WriteString(output, $"Tree_{scalar}_5_4_3"); WriteString(output, string.Empty);
            long offsetPosition = stream.CanSeek ? stream.Position : 0;
            output.Write(0L); output.Write(0L); output.Write(0L);
            long gridPosition = stream.CanSeek ? stream.Position : 0;
            output.Write(0u); // COMPRESS_NONE
            WriteMetadata(output, grid.Metadata);
            WriteString(output, "AffineMap"); WriteMatrix(output, grid.Transform.Matrix);
            output.Write(1); WriteScalar(output, grid.Background);
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
                WriteCoord(output, pair.Key); WriteInternal(output, pair.Value.Child);
            }
            long blockPosition = stream.CanSeek ? stream.Position : 0;
            foreach (var pair in entries)
                if (pair.Value.Child is not null) WriteBuffers(output, pair.Value.Child);
            output.Flush();
            if (stream.CanSeek)
            {
                long endPosition = stream.Position;
                stream.Position = offsetPosition;
                output.Write(gridPosition); output.Write(blockPosition); output.Write(endPosition);
                output.Flush(); stream.Position = endPosition; stream.SetLength(endPosition);
            }
        }
        finally { if (!leaveOpen) stream.Dispose(); }
    }

    private static string ScalarName<T>() where T : unmanaged => typeof(T) == typeof(int) ? "int32" :
        typeof(T) == typeof(float) ? "float" : typeof(T) == typeof(double) ? "double" :
        throw new NotSupportedException("VDB interchange supports only Int32, Float and Double Tree4 grids.");

    private static Coord ReadRootOrigin(Input input)
    {
        Coord origin = input.Coord();
        if ((origin.X & 4095) != 0 || (origin.Y & 4095) != 0 || (origin.Z & 4095) != 0)
            throw new InvalidDataException("Root origin is not aligned to the 4096-voxel Tree4 boundary.");
        return origin;
    }

    private static InternalNode<T> ReadInternal<T>(Input input, int level, Coord origin, List<LeafNode<T>> leaves) where T : unmanaged
    {
        int log2Dim = level == 2 ? 5 : 4;
        int size = 1 << (3 * log2Dim);
        input.Node(2048L + size * Unsafe.SizeOf<T>() + size / 2);
        NodeMask childMask = ReadMask(input, log2Dim), active = ReadMask(input, log2Dim);
        for (int word = 0; word < childMask.WordCount; ++word)
            if ((childMask.Words[word] & active.Words[word]) != 0) throw new InvalidDataException("Child and active-tile masks overlap.");
        RequireRawValues(input);
        var node = new InternalNode<T>(level, origin, default, false);
        input.Values(node.Values.AsSpan()); node.Active.OrWith(active);
        foreach (int index in childMask.OnIndices)
        {
            input.Reserve(128); // Dictionary slot and leaf-traversal reference allowance.
            Coord childOrigin = node.ChildOrigin(index);
            if (level == 2) node.Children.Add(index, ReadInternal<T>(input, 1, childOrigin, leaves));
            else
            {
                input.Node(2048L + 2L * LeafNode<T>.Size * Unsafe.SizeOf<T>() + 256);
                var leaf = new LeafNode<T>(childOrigin);
                NodeMask mask = ReadMask(input, 3);
                foreach (int on in mask.OnIndices) leaf.SetValueOn(on);
                node.Children.Add(index, leaf); leaves.Add(leaf);
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
    private static void RequireRawValues(Input input)
    {
        if (input.Byte() != 6) throw new InvalidDataException("Uncompressed buffers must use NO_MASK_AND_ALL_VALS metadata (6).");
    }
    private static void WriteMask(BinaryWriter output, NodeMask mask)
    {
        foreach (ulong word in mask.Words) output.Write(word);
    }
    private static void WriteInternal<T>(BinaryWriter output, InternalNode<T> node) where T : unmanaged
    {
        var childMask = new NodeMask(node.Log2Dim);
        foreach (int index in node.Children.Keys) childMask.SetOn(index);
        WriteMask(output, childMask); WriteMask(output, node.Active); output.Write((byte)6);
        var values = (T[])node.Values.Clone();
        foreach (int index in node.Children.Keys) values[index] = default;
        WriteValues(output, values.AsSpan());
        foreach (int index in childMask.OnIndices)
        {
            if (node.Children[index] is InternalNode<T> child) WriteInternal(output, child);
            else WriteMask(output, ((LeafNode<T>)node.Children[index]).ValueMask);
        }
    }
    private static void WriteBuffers<T>(BinaryWriter output, InternalNode<T> node) where T : unmanaged
    {
        foreach (var pair in node.Children.OrderBy(pair => pair.Key))
        {
            if (pair.Value is InternalNode<T> child) WriteBuffers(output, child);
            else
            {
                var leaf = (LeafNode<T>)pair.Value;
                WriteMask(output, leaf.ValueMask); output.Write((byte)6); WriteValues(output, leaf.Values);
            }
        }
    }
    private static void WriteValues<T>(BinaryWriter output, ReadOnlySpan<T> values) where T : unmanaged
    {
        if (BitConverter.IsLittleEndian) output.Write(MemoryMarshal.AsBytes(values));
        else foreach (T value in values) WriteScalar(output, value);
    }
    private static void WriteScalar<T>(BinaryWriter output, T value) where T : unmanaged
    {
        if (typeof(T) == typeof(int)) output.Write(Unsafe.As<T, int>(ref value));
        else if (typeof(T) == typeof(float)) output.Write(Unsafe.As<T, float>(ref value));
        else if (typeof(T) == typeof(double)) output.Write(Unsafe.As<T, double>(ref value));
        else throw new NotSupportedException();
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

    private static Transform ReadTransform(Input input)
    {
        string type = input.String();
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

    private static Dictionary<string, MetadataValue> ReadMetadata(Input input)
    {
        uint count = input.UInt32();
        if (count > input.Options.MaxMetadataItems) throw new InvalidDataException("Metadata item limit exceeded.");
        var result = new Dictionary<string, MetadataValue>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        for (uint index = 0; index < count; ++index)
        {
            input.Reserve(256);
            string name = input.String(), type = input.String();
            if (name.Length == 0 || !keys.Add(name)) throw new InvalidDataException("Empty or duplicate metadata name.");
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
                "mat4d" when size == 128 => input.Matrix(),
                "bool" or "int32" or "int64" or "float" or "double" or "vec3i" or "vec3d" or "mat4d" => throw new InvalidDataException("Metadata size disagrees with its type."),
                _ => throw new NotSupportedException($"Persistent metadata type '{type}' is unsupported.")
            };
            if (input.Position != end) throw new InvalidDataException("Metadata payload length mismatch.");
            result.Add(name, new MetadataValue(value));
        }
        return result;
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
                case Mat4d v: output.Write(128u); WriteMatrix(output, v); break;
                default: throw new NotSupportedException("Unsupported metadata value.");
            }
        }
    }
    private static void ValidateStorageMetadata(IReadOnlyDictionary<string, MetadataValue> metadata)
    {
        // Native GridBase::saveFloatAsHalf uses Metadata::asBool, not just a bool cast.
        // Only an explicit false boolean is accepted; other representations are rejected.
        if (metadata.TryGetValue("is_saved_as_half_float", out var half) && half.Value is not false)
            throw new NotSupportedException("Half-float storage metadata is unsupported unless explicitly Boolean false.");
    }
    private static void ValidateGrid<T>(Grid<T> grid) where T : unmanaged
    {
        ValidateStorageMetadata(grid.Metadata);
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
            if (buffer.Length > Options.MaxBytes - Position) throw new InvalidDataException("VDB byte limit exceeded.");
            stream.ReadExactly(buffer); Position += buffer.Length;
        }
        public byte Byte() { Span<byte> bytes = stackalloc byte[1]; Exact(bytes); return bytes[0]; }
        public bool Boolean() => Byte() switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Boolean value is not zero or one.") };
        public uint UInt32() { Span<byte> bytes = stackalloc byte[4]; Exact(bytes); return BinaryPrimitives.ReadUInt32LittleEndian(bytes); }
        public int Int32() => unchecked((int)UInt32());
        public ulong UInt64() { Span<byte> bytes = stackalloc byte[8]; Exact(bytes); return BinaryPrimitives.ReadUInt64LittleEndian(bytes); }
        public long Int64() => unchecked((long)UInt64());
        public byte[] Bytes(int length) { Reserve(length); var bytes = new byte[length]; Exact(bytes); return bytes; }
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
            if (typeof(T) == typeof(int)) { int value = Int32(); return Unsafe.As<int, T>(ref value); }
            if (typeof(T) == typeof(float)) { float value = BitConverter.Int32BitsToSingle(Int32()); return Unsafe.As<float, T>(ref value); }
            if (typeof(T) == typeof(double)) { double value = BitConverter.Int64BitsToDouble(Int64()); return Unsafe.As<double, T>(ref value); }
            throw new NotSupportedException();
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
