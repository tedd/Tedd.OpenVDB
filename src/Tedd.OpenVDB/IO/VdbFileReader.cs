// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.IO;

/// <summary>An open native-format archive with descriptor-based grid selection.</summary>
/// <remarks>Seekable files with grid offsets are indexed without loading grid payloads. Other inputs
/// are loaded eagerly. Seekable regional reads decode topology and overlapping leaf buffers, then clip;
/// non-overlapping value payloads are skipped without decompression. No mapped or delayed loading is provided.
/// The stream must remain exclusively owned by this reader until disposal. Each operation is bounded independently, including the retained index or eager archive.
/// Seekable selected reads return new grids; eager unbounded reads retain native cached-grid identity.
/// Grid names use native name[N] suffixes, with plain-name lookup selecting the first duplicate in
/// random-access files. Native cached streaming lookup requires a unique name for duplicate grids.</remarks>
public sealed class VdbFileReader : IDisposable
{
    private readonly VdbFile.ReaderSession _session;
    internal VdbFileReader(VdbFile.ReaderSession session) { _session = session; }
    public bool IsOpen => _session.IsOpen;
    public bool IsRandomAccess => _session.IsRandomAccess;
    public long? Size => _session.Size;
    public IReadOnlyList<string> GridNames => _session.Names;
    public bool HasGrid(string name) => _session.HasGrid(name);
    public MetaMap GetMetadata() => _session.GetMetadata();
    public IGrid ReadGridMetadata(string name) => _session.ReadMetadata(name);
    public IReadOnlyList<IGrid> ReadAllGridMetadata() => _session.ReadAllMetadata();
    public IGrid ReadGrid(string name) => _session.Read(name, null);
    /// <summary>Reads the node-centered index bounds enclosing the world box. Unsorted bounds mean no clipping, as in native File.</summary>
    public IGrid ReadGrid(string name, BBoxd worldBounds) => _session.Read(name, worldBounds);
    public Grid<T> ReadGrid<T>(string name) where T : unmanaged => ReadGrid(name) as Grid<T> ?? throw new InvalidCastException($"Grid does not store {typeof(T).Name}.");
    public Grid<T> ReadGrid<T>(string name, BBoxd worldBounds) where T : unmanaged => ReadGrid(name, worldBounds) as Grid<T> ?? throw new InvalidCastException($"Grid does not store {typeof(T).Name}.");
    public IReadOnlyList<IGrid> GetGrids() => _session.ReadAll();
    public VdbArchive ReadArchive() => _session.ReadArchive();
    public void Dispose() => _session.Dispose();
}