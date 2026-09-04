// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Native io/File.cc descriptor selection and eager fallback. Regional trees are clipped after decoding.
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;
namespace Tedd.OpenVDB.IO;

public static partial class VdbFile
{
    public static VdbFileReader OpenRead(Stream stream, bool leaveOpen = true, VdbReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try { return new VdbFileReader(new ReaderSession(stream, leaveOpen, options ?? new())); }
        catch { if (!leaveOpen) stream.Dispose(); throw; }
    }
    public static VdbFileReader OpenRead(string path, VdbReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return OpenRead(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), leaveOpen: false, options);
    }

    internal sealed class ReaderSession : IDisposable
    {
        private sealed record Indexed(Descriptor Descriptor, string TreeType, long GridPosition, int Ordinal);
        private Stream _stream;
        private readonly bool _leaveOpen;
        private readonly Lock _gate = new();
        private Input _input;
        private Indexed[] _index;
        private int[] _sorted;
        private string[] _plain;
        private readonly Dictionary<string, int> _unique = new(StringComparer.Ordinal);
        private IReadOnlyDictionary<string, MetadataValue> _metadata;
        private IReadOnlyList<string> _names;
        private VdbArchive? _cached;
        private readonly long _retained;
        private readonly int _retainedNodes;
        private bool _disposed, _busy;
        internal ReaderSession(Stream stream, bool leaveOpen, VdbReadOptions options)
        {
            if (!stream.CanRead) throw new ArgumentException("The stream is not readable.", nameof(stream));
            if (stream.CanSeek && stream.Position != 0) throw new ArgumentException("A VDB file must start at stream position zero.", nameof(stream));
            options.Validate(); _stream = stream; _leaveOpen = leaveOpen; Size = stream.CanSeek ? stream.Length : null;
            try
            {
                bool random = false;
                if (stream.CanSeek)
                {
                    Span<byte> prefix = stackalloc byte[21];
                    if (options.MaxBytes < prefix.Length) throw new InvalidDataException("VDB byte limit exceeded.");
                    stream.ReadExactly(prefix); random = prefix[20] == 1; stream.Position = 0;
                }
                if (!random)
                {
                    Input? input = null; Descriptor[]? descriptors = null;
                    _cached = ReadArchiveCore(stream, true, options, false, (d, i) => { descriptors = d; input = i; }, fileOrder: true);
                    _input = input!; _metadata = _cached.Metadata;
                    _index = new Indexed[descriptors!.Length];
                    for (int i = 0; i < _index.Length; ++i) _index[i] = new(descriptors[i], string.Empty, 0, i);
                }
                else
                {
                    _input = new Input(stream, options);
                    if (_input.Int64() != Magic) throw new InvalidDataException("Invalid OpenVDB magic number.");
                    uint version = _input.UInt32();
                    if (version != FormatVersion) throw new NotSupportedException($"VDB format {version} is unsupported; only 225 is implemented.");
                    _input.UInt32(); _input.UInt32();
                    if (!_input.Boolean()) throw new InvalidDataException("Invalid VDB random-access flag.");
                    if (!Guid.TryParseExact(Encoding.ASCII.GetString(_input.Bytes(36)), "D", out _)) throw new InvalidDataException("Invalid VDB UUID.");
                    _metadata = VdbFile.ReadMetadata(_input);
                    int count = _input.Int32();
                    if (count < 0 || count > options.MaxGrids) throw new InvalidDataException("Grid count limit exceeded.");
                    _input.Reserve(512L + count * 768L); _index = new Indexed[count];
                    for (int i = 0; i < count; ++i)
                    {
                        _input.Limit = long.MaxValue;
                        string name = _input.String(), type = _input.String(), parent = _input.String();
                        bool half = type.EndsWith("_HalfFloat", StringComparison.Ordinal); if (half) type = type[..^10];
                        ValidateTreeType(type);
                        long start = _input.Int64(), blocks = _input.Int64(), end = _input.Int64();
                        if (start != _input.Position || end < start || end > options.MaxBytes || end > Size ||
                            (parent.Length == 0 ? blocks < start || blocks > end : blocks != 0)) throw new InvalidDataException("Invalid grid offsets.");
                        var descriptor = new Descriptor(name, parent, half, true, blocks, end, type == "Tree_mask_5_4_3");
                        _index[i] = new(descriptor, type, start, i); _input.Seek(end);
                    }
                    if (_input.Position != Size) throw new InvalidDataException("Unexpected bytes after the VDB archive.");
                }
                _input.Reserve(512L + _index.Length * 384L);
                for (int i = 0; i < _index.Length; ++i)
                    if (!_unique.TryAdd(_index[i].Descriptor.Name, i)) throw new InvalidDataException("Duplicate unique grid descriptor name.");
                ValidateGraph();
                _plain = new string[_index.Length];
                for (int i = 0; i < _index.Length; ++i) { _input.Reserve(32L + _index[i].Descriptor.Name.Length * 4L); _plain[i] = Plain(_index[i].Descriptor.Name); }
                _sorted = Enumerable.Range(0, _index.Length).OrderBy(i => _plain[i], MetadataNameComparer.Instance).ToArray();
                _names = Array.AsReadOnly(_sorted.Select(i => DisplayName(_index[i].Descriptor.Name)).ToArray());
                _retained = _input.AllocatedBytes; _retainedNodes = _input.NodesRead;
                IsRandomAccess = random;
            }
            catch (EndOfStreamException error) { throw new InvalidDataException("Truncated VDB input.", error); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid UTF-8 in VDB input.", error); }
        }
        public bool IsOpen { get { lock (_gate) return !_disposed; } }
        public bool IsRandomAccess { get; }
        public long? Size { get; }
        public IReadOnlyList<string> Names => Run(() => _names);
        public bool HasGrid(string name) { ArgumentNullException.ThrowIfNull(name); return Run(() => Find(name, false) >= 0); }
        public MetaMap GetMetadata() => Run(() => { ChargeMetadataCopy(_metadata); return new MetaMap(_metadata); });
        public IGrid ReadMetadata(string name) { ArgumentNullException.ThrowIfNull(name); return Run(() => MetadataAt(FindRequired(name, cached: _cached is not null))); }
        public IReadOnlyList<IGrid> ReadAllMetadata() => Run(() =>
        {
            _input.Reserve(128L + _index.Length * 32L);
            IEnumerable<int> order = _cached is null ? _sorted : Enumerable.Range(0, _index.Length);
            return (IReadOnlyList<IGrid>)Array.AsReadOnly(order.Select(MetadataAt).ToArray());
        });
        public IGrid Read(string name, BBoxd? bounds) { ArgumentNullException.ThrowIfNull(name); return Run(() => ReadAt(FindRequired(name, cached: _cached is not null), bounds)); }
        public IReadOnlyList<IGrid> ReadAll() => Run(ReadAllCore);
        public VdbArchive ReadArchive() => Run(() => { IReadOnlyList<IGrid> grids = ReadAllCore(); ChargeMetadataCopy(_metadata); _input.Reserve(128L + grids.Count * 16L); return new VdbArchive(grids, new MetaMap(_metadata)); });
        private IReadOnlyList<IGrid> ReadAllCore()
        {
            if (_cached is not null) return _cached.Grids;
            _input.Reserve(512L + _index.Length * 512L);
            var grids = new List<IGrid>(_index.Length); var descriptors = new Descriptor[_index.Length]; var names = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (int index in _sorted) { descriptors[grids.Count] = _index[index].Descriptor; names.Add(descriptors[grids.Count].Name, grids.Count); grids.Add(Decode(index, false)); }
            ConnectInstances(grids, descriptors, names, _input.Options.InstanceResolution);
            return (IReadOnlyList<IGrid>)grids.AsReadOnly();
        }
        private T Run<T>(Func<T> action)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, typeof(VdbFileReader));
                if (_busy) throw new InvalidOperationException("A VDB reader operation cannot be reentered.");
                if (Size is long size && _stream.Length != size) throw new InvalidDataException("The open VDB stream changed length.");
                _busy = true;
                try { _input.BeginOperation(_retained, _retainedNodes); return action(); }
                catch (EndOfStreamException error) { throw new InvalidDataException("Truncated VDB input.", error); }
                catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid UTF-8 in VDB input.", error); }
                finally { _busy = false; }
            }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_busy) throw new InvalidOperationException("A VDB reader cannot be disposed during its read callback.");
                _disposed = true;
                Stream stream = _stream;
                _stream = Stream.Null; _input = null!;
                _cached = null; _index = []; _sorted = []; _plain = []; _names = Array.Empty<string>();
                _metadata = new Dictionary<string, MetadataValue>(); _unique.Clear();
                if (!_leaveOpen) stream.Dispose();
            }
        }
        private static string Plain(string name) { int at = name.IndexOf('\x1e'); return at < 0 ? name : name[..at]; }
        private static string DisplayName(string name) { int at = name.IndexOf('\x1e'); return at < 0 ? name : name[..at] + "[" + name[(at + 1)..] + "]"; }
        private static string UniqueName(string name) { int at = name.IndexOf('['); return at < 0 || !name.EndsWith(']') ? name : name[..at] + '\x1e' + name[(at + 1)..^1]; }
        private int Find(string name, bool cached)
        {
            string unique = UniqueName(name);
            if (cached) return _unique.TryGetValue(unique, out int index) || _unique.TryGetValue(name, out index) ? index : -1;
            string plain = _plain.Contains(name, StringComparer.Ordinal) ? name : Plain(unique);
            for (int i = 0; i < _index.Length; ++i)
            {
                var descriptor = _index[i].Descriptor;
                if (_plain[i] == plain && (name == unique || descriptor.Name == unique || descriptor.Name == name)) return i;
            }
            return -1;
        }
        private int FindRequired(string name, bool cached)
        {
            int index = Find(name, cached); return index >= 0 ? index : throw new KeyNotFoundException($"The archive has no grid named '{name}'.");
        }
        private void ValidateGraph()
        {
            var states = new byte[_index.Length];
            for (int start = 0; start < _index.Length; ++start)
            {
                int cursor = start;
                while (states[cursor] == 0)
                {
                    states[cursor] = 1; string parent = _index[cursor].Descriptor.Parent;
                    if (parent.Length == 0) break;
                    if (!_unique.TryGetValue(parent, out int next)) throw new InvalidDataException($"Missing instance parent '{parent}'.");
                    if (_cached is null ? _index[cursor].TreeType != _index[next].TreeType : _cached.Grids[cursor].GetType() != _cached.Grids[next].GetType()) throw new InvalidDataException("Instance parent grid type differs.");
                    cursor = next;
                    if (states[cursor] == 1) throw new InvalidDataException("Cyclic instance references.");
                }
                cursor = start;
                while (states[cursor] == 1) { states[cursor] = 2; string parent = _index[cursor].Descriptor.Parent; if (parent.Length == 0) break; cursor = _unique[parent]; }
            }
        }
        private IGrid MetadataAt(int index)
        {
            IGrid source = _cached is null ? Decode(index, metadataOnly: true) : _cached.Grids[index];
            return CopyMetadataOnly(source);
        }
        private IGrid Decode(int index, bool metadataOnly, CoordBBox? indexBounds = null, BBoxd? worldBounds = null)
        {
            Indexed entry = _index[index]; _input.Limit = entry.Descriptor.EndPosition; _input.Seek(entry.GridPosition);
            _input.Compression = ValidateCompression((VdbCompression)_input.UInt32());
            if (!metadataOnly) return DecodeGrid(_input, entry.Descriptor, entry.TreeType, indexBounds, worldBounds);
            var metadata = VdbFile.ReadMetadata(_input); _input.Half = StorageHalfFlag(metadata);
            if (_input.Half != entry.Descriptor.Half) throw new InvalidDataException("Half-float descriptor and storage metadata disagree.");
            Transform transform = ReadTransform(_input); _input.Reserve(256);
            IGrid grid = CreateEmptyGrid(entry.TreeType);
            if (_input.Half && !SupportsHalf(grid.ValueType)) throw new NotSupportedException("Half-float encoding requires floating-point scalar or vector grids.");
            grid.Transform = transform; AssignMetadata(grid, metadata, entry.Descriptor.Name); return grid;
        }
        private IGrid ReadAt(int index, BBoxd? bounds)
        {
            bool clip = bounds is BBoxd box && box.IsValid;
            if (clip && (!Finite(bounds!.Value.Min) || !Finite(bounds.Value.Max))) throw new ArgumentOutOfRangeException(nameof(bounds), "Clip bounds must be finite.");
            if (_cached is not null)
            {
                IGrid cached = _cached.Grids[index];
                if (!clip) return cached;
                IGrid copy = CopyMetadataOnly(cached);
                CloneAndClip(copy, cached, cached.Transform.WorldToIndexNodeCentered(bounds!.Value)); return copy;
            }
            IGrid grid = Decode(index, false, worldBounds: clip ? bounds : null); string parent = _index[index].Descriptor.Parent;
            if (parent.Length != 0)
            {
                int parentIndex = _unique[parent];
                if (_input.Options.InstanceResolution == VdbInstanceResolution.ResolveDependencies)
                    while (_index[parentIndex].Descriptor.Parent.Length != 0) parentIndex = _unique[_index[parentIndex].Descriptor.Parent];
                IGrid source = Decode(parentIndex, false, indexBounds: clip ? grid.Transform.WorldToIndexNodeCentered(bounds!.Value) : null); SetTreeFrom(grid, source);
            }
            if (clip) ClipGrid(grid, grid.Transform.WorldToIndexNodeCentered(bounds!.Value));
            return grid;
        }
        private static bool Finite(Vec3d v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
        private void ChargeMetadataCopy(IReadOnlyDictionary<string, MetadataValue> metadata)
        {
            _input.Reserve(128 + metadata.Count * 128L);
            foreach (MetadataValue value in metadata.Values) _input.Reserve(MetadataWire.Size(value.Representation));
        }
        private IGrid CopyMetadataOnly(IGrid source)
        {
            ChargeMetadataCopy(source.Metadata); _input.Reserve(3072);
            IGrid copy = source switch
            {
                Grid<int> g => new Grid<int>(g.Background),
                Grid<float> g => new Grid<float>(g.Background),
                Grid<double> g => new Grid<double>(g.Background),
                Grid<long> g => new Grid<long>(g.Background),
                Grid<bool> g => new Grid<bool>(g.Background),
                Grid<Vec3i> g => new Grid<Vec3i>(g.Background),
                Grid<Vec3f> g => new Grid<Vec3f>(g.Background),
                Grid<Vec3d> g => new Grid<Vec3d>(g.Background),
                MaskGrid g => new MaskGrid(g.Background),
                _ => throw new NotSupportedException()
            };
            copy.Transform = source.Transform.Copy();
            AssignMetadata(copy, source.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value.DeepCopy()), null); return copy;
        }
        private void CloneAndClip(IGrid target, IGrid source, CoordBBox bounds)
        {
            switch (source)
            {
                case Grid<int> s: ((Grid<int>)target).SetTree(CloneTree(s.Tree)); break;
                case Grid<float> s: ((Grid<float>)target).SetTree(CloneTree(s.Tree)); break;
                case Grid<double> s: ((Grid<double>)target).SetTree(CloneTree(s.Tree)); break;
                case Grid<long> s: ((Grid<long>)target).SetTree(CloneTree(s.Tree)); break;
                case Grid<bool> s: ((Grid<bool>)target).SetTree(CloneTree(s.Tree)); break;
                case Grid<Vec3i> s: ((Grid<Vec3i>)target).SetTree(CloneTree(s.Tree)); break;
                case Grid<Vec3f> s: ((Grid<Vec3f>)target).SetTree(CloneTree(s.Tree)); break;
                case Grid<Vec3d> s: ((Grid<Vec3d>)target).SetTree(CloneTree(s.Tree)); break;
                case MaskGrid s: ((MaskGrid)target).Storage.SetTree(CloneTree(s.Storage.Tree)); break;
            }
            ClipGrid(target, bounds);
        }
        private VdbTree<T> CloneTree<T>(VdbTree<T> tree) where T : unmanaged
        {
            _input.Reserve(128L + tree.Entries.Count * 128L);
            foreach (var entry in tree.Entries.Values) if (entry.Child is not null) ChargeNode(entry.Child);
            return tree.DeepCopy();
        }
        private void ChargeNode<T>(InternalNode<T> node) where T : unmanaged
        {
            _input.Node(2048L + node.Values.Length * Unsafe.SizeOf<T>() + node.Values.Length / 2L + node.Children.Count * 128L);
            foreach (var child in node.Children.Values)
                if (child is InternalNode<T> inner) ChargeNode(inner); else _input.Node(2048L + LeafNode<T>.Size * Unsafe.SizeOf<T>() + 256);
        }
        private void ClipGrid(IGrid grid, CoordBBox bounds)
        {
            switch (grid)
            {
                case Grid<int> g: ClipTree(g.Tree, bounds); break;
                case Grid<float> g: ClipTree(g.Tree, bounds); break;
                case Grid<double> g: ClipTree(g.Tree, bounds); break;
                case Grid<long> g: ClipTree(g.Tree, bounds); break;
                case Grid<bool> g: ClipTree(g.Tree, bounds); break;
                case Grid<Vec3i> g: ClipTree(g.Tree, bounds); break;
                case Grid<Vec3f> g: ClipTree(g.Tree, bounds); break;
                case Grid<Vec3d> g: ClipTree(g.Tree, bounds); break;
                case MaskGrid g: ClipTree(g.Storage.Tree, bounds, true); break;
            }
        }
        private void ClipTree<T>(VdbTree<T> tree, CoordBBox bounds, bool mask = false) where T : unmanaged
        {
            _input.Reserve(128L + tree.Entries.Count * 128L);
            foreach (Coord origin in tree.Entries.Keys.ToArray())
            {
                var entry = tree.Entries[origin]; CoordBBox region = new(origin, origin + new Coord(4095));
                if (!region.Intersects(bounds)) { tree.Entries.Remove(origin); continue; }
                if (bounds.Contains(region)) continue;
                InternalNode<T>? child = entry.Child;
                if (child is null)
                {
                    if (!entry.Active && ValueEquality<T>.Equals(entry.Value, tree.Background)) continue;
                    _input.Node(2048L + 32768L * Unsafe.SizeOf<T>() + 16384);
                    child = new InternalNode<T>(2, origin, entry.Value, entry.Active); tree.Entries[origin] = new(default, false, child);
                }
                ClipNode(child, bounds, tree.Background, mask);
            }
            foreach (var entry in tree.Entries.Values) if (entry.Child is not null) ChargePrune(entry.Child);
            tree.Prune();
        }
        private void ChargePrune<T>(InternalNode<T> node) where T : unmanaged
        {
            _input.Reserve(32L + node.Children.Count * 4L);
            foreach (var child in node.Children.Values) if (child is InternalNode<T> inner) ChargePrune(inner);
        }
        private void ClipNode<T>(InternalNode<T> node, CoordBBox bounds, T background, bool mask) where T : unmanaged
        {
            int dimension = node.Level == 2 ? 128 : 8;
            for (int index = 0; index < node.Values.Length; ++index)
            {
                Coord origin = node.ChildOrigin(index); CoordBBox region = new(origin, origin + new Coord(dimension - 1));
                if (!region.Intersects(bounds)) { node.RemoveChild(index); node.Values[index] = background; node.Active.SetOff(index); continue; }
                if (bounds.Contains(region)) continue;
                if (!node.Children.TryGetValue(index, out object? child))
                {
                    if (!node.Active.IsOn(index) && ValueEquality<T>.Equals(node.Values[index], background)) continue;
                    if (node.Level == 2) { _input.Node(2048L + 4096L * Unsafe.SizeOf<T>() + 2048); child = new InternalNode<T>(1, origin, node.Values[index], node.Active.IsOn(index)); }
                    else
                    {
                        _input.Node(2048L + LeafNode<T>.Size * Unsafe.SizeOf<T>() + 256);
                        T value = node.Values[index]; bool active = node.Active.IsOn(index);
                        if (mask) { bool bit = Unsafe.As<T, bool>(ref value) || active; value = Unsafe.As<bool, T>(ref bit); active = bit; }
                        child = new LeafNode<T>(origin, value, active);
                    }
                    _input.Reserve(128); node.AddChild(index, child); node.Active.SetOff(index);
                }
                if (child is InternalNode<T> inner) ClipNode(inner, bounds, background, mask);
                else ((LeafNode<T>)child).Clip(bounds, mask ? default : background);
            }
        }
    }
    private static void ValidateTreeType(string type)
    {
        if (type is not ("Tree_int32_5_4_3" or "Tree_float_5_4_3" or "Tree_double_5_4_3" or "Tree_int64_5_4_3" or "Tree_bool_5_4_3" or "Tree_vec3i_5_4_3" or "Tree_vec3s_5_4_3" or "Tree_vec3d_5_4_3" or "Tree_mask_5_4_3")) throw new NotSupportedException($"Grid type '{type}' is unsupported.");
    }
    private static IGrid CreateEmptyGrid(string type) => type switch
    {
        "Tree_int32_5_4_3" => new Grid<int>(),
        "Tree_float_5_4_3" => new Grid<float>(),
        "Tree_double_5_4_3" => new Grid<double>(),
        "Tree_int64_5_4_3" => new Grid<long>(),
        "Tree_bool_5_4_3" => new Grid<bool>(),
        "Tree_vec3i_5_4_3" => new Grid<Vec3i>(),
        "Tree_vec3s_5_4_3" => new Grid<Vec3f>(),
        "Tree_vec3d_5_4_3" => new Grid<Vec3d>(),
        "Tree_mask_5_4_3" => new MaskGrid(),
        _ => throw new NotSupportedException($"Grid type '{type}' is unsupported.")
    };
    private static IGrid DecodeGrid(Input input, Descriptor descriptor, string type, CoordBBox? indexBounds = null, BBoxd? worldBounds = null) => type switch
    {
        "Tree_int32_5_4_3" => ReadGrid<int>(input, descriptor, indexBounds, worldBounds),
        "Tree_float_5_4_3" => ReadGrid<float>(input, descriptor, indexBounds, worldBounds),
        "Tree_double_5_4_3" => ReadGrid<double>(input, descriptor, indexBounds, worldBounds),
        "Tree_int64_5_4_3" => ReadGrid<long>(input, descriptor, indexBounds, worldBounds),
        "Tree_bool_5_4_3" => ReadGrid<bool>(input, descriptor, indexBounds, worldBounds),
        "Tree_vec3i_5_4_3" => ReadGrid<Vec3i>(input, descriptor, indexBounds, worldBounds),
        "Tree_vec3s_5_4_3" => ReadGrid<Vec3f>(input, descriptor, indexBounds, worldBounds),
        "Tree_vec3d_5_4_3" => ReadGrid<Vec3d>(input, descriptor, indexBounds, worldBounds),
        "Tree_mask_5_4_3" => new MaskGrid(ReadGrid<bool>(input, descriptor, indexBounds, worldBounds)),
        _ => throw new NotSupportedException($"Grid type '{type}' is unsupported.")
    };
    private static void AssignMetadata(IGrid grid, IReadOnlyDictionary<string, MetadataValue> metadata, string? descriptorName)
    {
        foreach (var pair in metadata)
        {
            switch (grid)
            {
                case Grid<int> g: g.SetMetadata(pair.Key, pair.Value); break;
                case Grid<float> g: g.SetMetadata(pair.Key, pair.Value); break;
                case Grid<double> g: g.SetMetadata(pair.Key, pair.Value); break;
                case Grid<long> g: g.SetMetadata(pair.Key, pair.Value); break;
                case Grid<bool> g: g.SetMetadata(pair.Key, pair.Value); break;
                case Grid<Vec3i> g: g.SetMetadata(pair.Key, pair.Value); break;
                case Grid<Vec3f> g: g.SetMetadata(pair.Key, pair.Value); break;
                case Grid<Vec3d> g: g.SetMetadata(pair.Key, pair.Value); break;
                case MaskGrid g: g.SetMetadata(pair.Key, pair.Value); break;
            }
        }
        if (descriptorName is not null)
        {
            string plain = descriptorName.Split('\x1e')[0];
            if (metadata.TryGetValue("name", out var value)) { if (value.Value is not string name || name != plain) throw new InvalidDataException("Grid metadata name disagrees with its descriptor."); }
            else if (plain.Length != 0) grid.Name = plain;
        }
    }
    private static void SetTreeFrom(IGrid target, IGrid source)
    {
        switch (target)
        {
            case Grid<int> g: g.SetTree(((Grid<int>)source).Tree); break;
            case Grid<float> g: g.SetTree(((Grid<float>)source).Tree); break;
            case Grid<double> g: g.SetTree(((Grid<double>)source).Tree); break;
            case Grid<long> g: g.SetTree(((Grid<long>)source).Tree); break;
            case Grid<bool> g: g.SetTree(((Grid<bool>)source).Tree); break;
            case Grid<Vec3i> g: g.SetTree(((Grid<Vec3i>)source).Tree); break;
            case Grid<Vec3f> g: g.SetTree(((Grid<Vec3f>)source).Tree); break;
            case Grid<Vec3d> g: g.SetTree(((Grid<Vec3d>)source).Tree); break;
            case MaskGrid g: g.Storage.SetTree(((MaskGrid)source).Storage.Tree); break;
        }
    }
    private sealed partial class Input
    {
        internal long AllocatedBytes => _allocated;
        internal int NodesRead => _nodes;
        internal void BeginOperation(long allocated, int nodes) { _allocated = allocated; _nodes = nodes; Limit = long.MaxValue; }
        internal void Seek(long position)
        {
            if (!stream.CanSeek || position < 0 || position > Options.MaxBytes || position > Limit || position > stream.Length) throw new InvalidDataException("Invalid VDB seek offset.");
            stream.Position = position; Position = position;
        }
    }
}