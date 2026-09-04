// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
// Managed lifecycle facade for OpenVDB io::File. Delayed/mapped loading is intentionally separate.
using System.Text;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.IO;

/// <summary>A reusable path-based OpenVDB file reader with native UUID change detection.</summary>
/// <remarks><see cref="Open"/> and <see cref="Close"/> mirror native io::File lifecycle behavior.
/// Copy creates a closed handle that retains the last observed UUID and therefore detects whether the
/// same file has changed when opened. Each open uses the eager/seekable managed reader; mapped and delayed loading are not provided.</remarks>
public sealed class VdbFileHandle : IDisposable
{
    private readonly Lock _gate = new();
    private readonly VdbReadOptions _options;
    private VdbFileReader? _reader;
    private string _uniqueTag;
    private bool _disposed;

    public VdbFileHandle(string fileName, VdbReadOptions? options = null)
        : this(fileName, options ?? new(), string.Empty, 0, 0, 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        _options.Validate();
    }
    private VdbFileHandle(string fileName, VdbReadOptions options, string uniqueTag, uint fileVersion, uint libraryMajor, uint libraryMinor)
    {
        FileName = fileName; _options = options; _uniqueTag = uniqueTag;
        FileVersion = fileVersion; LibraryMajorVersion = libraryMajor; LibraryMinorVersion = libraryMinor;
    }
    public string FileName { get; }
    public bool IsOpen { get { lock (_gate) return !_disposed && _reader is not null; } }
    /// <summary>The UUID most recently read, or an empty string before the first successful open.</summary>
    public string UniqueTag { get { lock (_gate) { ThrowIfDisposed(); return _uniqueTag; } } }
    public uint FileVersion { get; private set; }
    public uint LibraryMajorVersion { get; private set; }
    public uint LibraryMinorVersion { get; private set; }
    public string Version => $"{LibraryMajorVersion}.{LibraryMinorVersion}/{FileVersion}";
    public bool IsIdentical(string uniqueTag)
    {
        ArgumentNullException.ThrowIfNull(uniqueTag);
        lock (_gate) { ThrowIfDisposed(); return uniqueTag.Length != 0 && _uniqueTag.Length != 0 && string.Equals(uniqueTag, _uniqueTag, StringComparison.Ordinal); }
    }
    public long GetSize()
    {
        lock (_gate) { ThrowIfDisposed(); return new FileInfo(FileName).Length; }
    }
    /// <summary>Opens and indexes the file. Returns true on the first open or when its UUID changed.</summary>
    public bool Open()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_reader is not null) throw new InvalidOperationException($"{FileName} is already open.");
            var headerStream = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[57];
            try
            {
                try { headerStream.ReadExactly(header); }
                catch (EndOfStreamException error) { throw new InvalidDataException("Truncated VDB header.", error); }
                if (System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(header) != 0x56444220) throw new InvalidDataException($"{FileName} is not a VDB file.");
                uint format = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
                uint major = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
                uint minor = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
                string tag = Encoding.ASCII.GetString(header[21..57]);
                if (!Guid.TryParseExact(tag, "D", out _)) throw new InvalidDataException("Invalid VDB UUID.");
                // Construct the complete reader before committing lifecycle state. A malformed descriptor or
                // exhausted budget leaves this handle closed and retains the prior successful UUID.
                headerStream.Position = 0;
                VdbFileReader opened = VdbFile.OpenRead(headerStream, leaveOpen: false, _options);
                bool changed = _uniqueTag.Length == 0 || !string.Equals(_uniqueTag, tag, StringComparison.Ordinal);
                _reader = opened; _uniqueTag = tag; FileVersion = format; LibraryMajorVersion = major; LibraryMinorVersion = minor;
                return changed;
            }
            catch
            {
                headerStream.Dispose();
                throw;
            }
        }
    }
    public void Close()
    {
        lock (_gate) { _reader?.Dispose(); _reader = null; }
    }
    /// <summary>Copies configuration and last-observed header identity. The returned handle is closed.</summary>
    public VdbFileHandle Copy()
    {
        lock (_gate) { ThrowIfDisposed(); return new(FileName, _options, _uniqueTag, FileVersion, LibraryMajorVersion, LibraryMinorVersion); }
    }
    public IReadOnlyList<string> GridNames => Run(reader => reader.GridNames);
    public bool HasGrid(string name) => Run(reader => reader.HasGrid(name));
    public MetaMap GetMetadata() => Run(reader => reader.GetMetadata());
    public IGrid ReadGridMetadata(string name) => Run(reader => reader.ReadGridMetadata(name));
    public IReadOnlyList<IGrid> ReadAllGridMetadata() => Run(reader => reader.ReadAllGridMetadata());
    public IGrid ReadGrid(string name) => Run(reader => reader.ReadGrid(name));
    public IGrid ReadGrid(string name, BBoxd worldBounds) => Run(reader => reader.ReadGrid(name, worldBounds));
    public Grid<T> ReadGrid<T>(string name) where T : unmanaged => Run(reader => reader.ReadGrid<T>(name));
    public Grid<T> ReadGrid<T>(string name, BBoxd worldBounds) where T : unmanaged => Run(reader => reader.ReadGrid<T>(name, worldBounds));
    public IReadOnlyList<IGrid> GetGrids() => Run(reader => reader.GetGrids());
    public VdbArchive ReadArchive() => Run(reader => reader.ReadArchive());
    private TResult Run<TResult>(Func<VdbFileReader, TResult> operation)
    {
        lock (_gate) { ThrowIfDisposed(); return operation(_reader ?? throw new InvalidOperationException($"{FileName} is not open for reading.")); }
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _reader?.Dispose(); _reader = null; _disposed = true; }
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
