// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Ported from io/Archive.cc, io/GridDescriptor.cc and MetaMap.cc.
using System.Collections.ObjectModel;
using System.Text;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.IO;

/// <summary>An ordered heterogeneous grid collection with independent file-level metadata.</summary>
/// <remarks>The collection and metadata map are copied; grids retain their identity and shared trees.</remarks>
public sealed class VdbArchive
{
    public VdbArchive(IEnumerable<IGrid> grids, IReadOnlyDictionary<string, MetadataValue>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(grids);
        IGrid[] items = grids.ToArray();
        if (items.Any(grid => grid is null)) throw new ArgumentException("Archive grids cannot contain null.", nameof(grids));
        Grids = Array.AsReadOnly(items);
        var copy = new Dictionary<string, MetadataValue>(StringComparer.Ordinal);
        if (metadata is not null)
            foreach (var pair in metadata)
            {
                ArgumentException.ThrowIfNullOrEmpty(pair.Key); ArgumentNullException.ThrowIfNull(pair.Value);
                copy.Add(pair.Key, pair.Value);
            }
        Metadata = new ReadOnlyDictionary<string, MetadataValue>(copy);
    }
    public IReadOnlyList<IGrid> Grids { get; }
    public IReadOnlyDictionary<string, MetadataValue> Metadata { get; }
}

public static partial class VdbFile
{
    private sealed record Descriptor(string Name, string Parent, bool Half, bool Offsets, long BlockPosition, long EndPosition, bool Mask);

    public static MaskGrid ReadMask(Stream stream, bool leaveOpen = true, VdbReadOptions? options = null)
    {
        VdbArchive archive = ReadArchiveCore(stream, leaveOpen, options, singleGrid: true);
        return archive.Grids[0] as MaskGrid ?? throw new NotSupportedException("The archive does not contain a topology-mask grid.");
    }

    public static void Write(Stream stream, MaskGrid grid, bool leaveOpen = true, VdbWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            ArgumentNullException.ThrowIfNull(grid);
            WriteArchive(stream, new VdbArchive([grid]), leaveOpen: true, options);
        }
        finally { if (!leaveOpen) stream.Dispose(); }
    }
    public static VdbArchive ReadArchive(Stream stream, bool leaveOpen = true, VdbReadOptions? options = null) =>
        ReadArchiveCore(stream, leaveOpen, options, singleGrid: false);

    private static VdbArchive ReadArchiveCore(Stream stream, bool leaveOpen, VdbReadOptions? options, bool singleGrid)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            if (!stream.CanRead) throw new ArgumentException("The stream is not readable.", nameof(stream));
            if (stream.CanSeek && stream.Position != 0) throw new ArgumentException("A VDB file must start at stream position zero.", nameof(stream));
            options ??= new(); options.Validate();
            var input = new Input(stream, options);
            if (input.Int64() != Magic) throw new InvalidDataException("Invalid OpenVDB magic number.");
            uint version = input.UInt32();
            if (version != FormatVersion) throw new NotSupportedException($"VDB format {version} is unsupported; only 225 is implemented.");
            input.UInt32(); input.UInt32();
            bool offsets = input.Boolean();
            string uuid = Encoding.ASCII.GetString(input.Bytes(36));
            if (!Guid.TryParseExact(uuid, "D", out _)) throw new InvalidDataException("Invalid VDB UUID.");
            Dictionary<string, MetadataValue> metadata = ReadMetadata(input, allowNonempty: !singleGrid);
            int count = input.Int32();
            if (count < 0) throw new InvalidDataException("Negative grid count.");
            if (singleGrid && count != 1) throw new NotSupportedException("Exactly one grid is supported by Read<T>; use ReadArchive.");
            if (count > options.MaxGrids) throw new InvalidDataException("Grid count limit exceeded.");
            input.Reserve((long)count * 256);
            var grids = new List<IGrid>(count);
            var named = new Dictionary<string, IGrid>(StringComparer.Ordinal);
            for (int index = 0; index < count; ++index)
            {
                input.Limit = long.MaxValue;
                string name = input.String(), treeType = input.String(), parent = input.String();
                if (named.ContainsKey(name)) throw new InvalidDataException("Duplicate unique grid descriptor name.");
                bool half = treeType.EndsWith("_HalfFloat", StringComparison.Ordinal);
                if (half) treeType = treeType[..^10];
                long gridPosition = input.Int64(), blockPosition = input.Int64(), endPosition = input.Int64();
                if (offsets)
                {
                    if (gridPosition != input.Position || endPosition < gridPosition || endPosition > options.MaxBytes ||
                        (parent.Length == 0 ? blockPosition < gridPosition || blockPosition > endPosition : blockPosition != 0) ||
                        (stream.CanSeek && endPosition > stream.Length))
                        throw new InvalidDataException("Invalid grid offsets.");
                    input.Limit = endPosition;
                }
                else if (gridPosition != 0 || blockPosition != 0 || endPosition != 0)
                    throw new InvalidDataException("A streaming VDB descriptor must contain zero offsets.");
                input.Compression = ValidateCompression((VdbCompression)input.UInt32());
                var descriptor = new Descriptor(name, parent, half, offsets, blockPosition, endPosition, treeType == "Tree_mask_5_4_3");
                IGrid grid = treeType switch
                {
                    "Tree_int32_5_4_3" => ReadGrid<int>(input, descriptor, named),
                    "Tree_float_5_4_3" => ReadGrid<float>(input, descriptor, named),
                    "Tree_double_5_4_3" => ReadGrid<double>(input, descriptor, named),
                    "Tree_int64_5_4_3" => ReadGrid<long>(input, descriptor, named),
                    "Tree_bool_5_4_3" => ReadGrid<bool>(input, descriptor, named),
                    "Tree_vec3i_5_4_3" => ReadGrid<Vec3i>(input, descriptor, named),
                    "Tree_vec3s_5_4_3" => ReadGrid<Vec3f>(input, descriptor, named),
                    "Tree_vec3d_5_4_3" => ReadGrid<Vec3d>(input, descriptor, named),
                    "Tree_mask_5_4_3" => new MaskGrid(ReadGrid<bool>(input, descriptor, named)),
                    _ => throw new NotSupportedException($"Grid type '{treeType}' is unsupported.")
                };
                grids.Add(grid); named.Add(name, grid);
            }
            if (stream.CanSeek && input.Position != stream.Length) throw new InvalidDataException("Unexpected bytes after the VDB archive.");
            return new VdbArchive(grids, metadata);
        }
        catch (EndOfStreamException exception) { throw new InvalidDataException("Truncated VDB input.", exception); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Invalid UTF-8 in VDB input.", exception); }
        finally { if (!leaveOpen) stream.Dispose(); }
    }

    /// <summary>Write an archive. Shared trees are instanced by default when their half-precision flags agree.</summary>
    public static void WriteArchive(Stream stream, VdbArchive archive, bool leaveOpen = true, VdbWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            ArgumentNullException.ThrowIfNull(archive);
            if (!stream.CanWrite) throw new ArgumentException("The stream is not writable.", nameof(stream));
            if (stream.CanSeek && stream.Position != 0) throw new ArgumentException("A VDB file must start at stream position zero.", nameof(stream));
            options ??= new();
            VdbCompression compression = ValidateCompression(options.Compression);
            ValidateMetadata(archive.Metadata);
            var halves = new bool[archive.Grids.Count];
            for (int index = 0; index < archive.Grids.Count; ++index)
            {
                IGrid grid = archive.Grids[index];
                switch (grid)
                {
                    case Grid<int> typed: ValidateGrid(typed); break;
                    case Grid<float> typed: ValidateGrid(typed); break;
                    case Grid<double> typed: ValidateGrid(typed); break;
                    case Grid<long> typed: ValidateGrid(typed); break;
                    case Grid<bool> typed: ValidateGrid(typed); break;
                    case Grid<Vec3i> typed: ValidateGrid(typed); break;
                    case Grid<Vec3f> typed: ValidateGrid(typed); break;
                    case Grid<Vec3d> typed: ValidateGrid(typed); break;
                    case MaskGrid typed: ValidateGrid(typed.Storage); typed.ValidateLeafValues(); break;
                    default: throw new NotSupportedException("Unsupported VDB Tree4 grid type.");
                }
                halves[index] = options.SaveFloatAsHalf ?? StorageHalfFlag(grid.Metadata);
                if (halves[index] && !SupportsHalf(grid.ValueType)) throw new NotSupportedException("Half-float encoding requires floating-point scalar or vector grids.");
            }
            var nameCount = archive.Grids.GroupBy(grid => grid.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var uniqueNames = new HashSet<string>(StringComparer.Ordinal);
            var trees = new Dictionary<object, (string Name, bool Half)>(ReferenceEqualityComparer.Instance);
            using var output = new BinaryWriter(stream, Utf8, leaveOpen: true);
            output.Write(Magic); output.Write(FormatVersion); output.Write(13u); output.Write(0u);
            output.Write(stream.CanSeek); output.Write(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("D")));
            WriteMetadata(output, archive.Metadata); output.Write(archive.Grids.Count);
            for (int index = 0; index < archive.Grids.Count; ++index)
            {
                IGrid grid = archive.Grids[index]; bool half = halves[index];
                string name = grid.Name;
                if (name.Length == 0 || nameCount[name] > 1) name += "\u001e0";
                for (int suffix = 1; !uniqueNames.Add(name); ++suffix) name = grid.Name + '\x1e' + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                object tree = grid switch { Grid<int> typed => typed.Tree, Grid<float> typed => typed.Tree, Grid<double> typed => typed.Tree, Grid<long> typed => typed.Tree, Grid<bool> typed => typed.Tree, Grid<Vec3i> typed => typed.Tree, Grid<Vec3f> typed => typed.Tree, Grid<Vec3d> typed => typed.Tree, MaskGrid typed => typed.Storage.Tree, _ => throw new NotSupportedException() };
                string parent = options.EnableInstancing && trees.TryGetValue(tree, out var existing) && existing.Half == half ? existing.Name : string.Empty;
                switch (grid)
                {
                    case Grid<int> typed: WriteGrid(stream, output, typed, name, parent, compression, half); break;
                    case Grid<float> typed: WriteGrid(stream, output, typed, name, parent, compression, half); break;
                    case Grid<double> typed: WriteGrid(stream, output, typed, name, parent, compression, half); break;
                    case Grid<long> typed: WriteGrid(stream, output, typed, name, parent, compression, half); break;
                    case Grid<bool> typed: WriteGrid(stream, output, typed, name, parent, compression, half); break;
                    case Grid<Vec3i> typed: WriteGrid(stream, output, typed, name, parent, compression, half); break;
                    case Grid<Vec3f> typed: WriteGrid(stream, output, typed, name, parent, compression, half); break;
                    case Grid<Vec3d> typed: WriteGrid(stream, output, typed, name, parent, compression, half); break;
                    case MaskGrid typed: WriteGrid(stream, output, typed.Storage, name, parent, compression, half, maskGrid: true); break;
                }
                if (parent.Length == 0) trees[tree] = (name, half);
            }
            output.Flush(); if (stream.CanSeek) stream.SetLength(stream.Position);
        }
        finally { if (!leaveOpen) stream.Dispose(); }
    }

    private static IReadOnlyDictionary<string, MetadataValue> OutputMetadata(IGrid grid, bool half)
    {
        if (!half && !grid.Metadata.ContainsKey("is_saved_as_half_float")) return grid.Metadata;
        var copy = new Dictionary<string, MetadataValue>(grid.Metadata, StringComparer.Ordinal)
        { ["is_saved_as_half_float"] = new MetadataValue(half) };
        return copy;
    }

    private static void ValidateMetadata(IReadOnlyDictionary<string, MetadataValue> metadata)
    {
        foreach (var pair in metadata)
        {
            ArgumentException.ThrowIfNullOrEmpty(pair.Key); ArgumentNullException.ThrowIfNull(pair.Value);
            Utf8.GetByteCount(pair.Key); Utf8.GetByteCount(pair.Value.TypeName);
            if (pair.Value.Value is string text) Utf8.GetByteCount(text);
        }
    }
}
