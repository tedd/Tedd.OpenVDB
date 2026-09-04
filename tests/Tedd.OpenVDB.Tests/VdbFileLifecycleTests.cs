// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Math;
namespace Tedd.OpenVDB.Tests;

public sealed class VdbFileLifecycleTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "file-lifecycle");
    private static readonly JsonDocument Native = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, "native-file-lifecycle.json")));
    private static string CopyFixture()
    {
        string path = Path.Combine(Path.GetTempPath(), "tedd-openvdb-file-" + Guid.NewGuid().ToString("N") + ".vdb");
        File.Copy(Path.Combine(Folder, "native-file-lifecycle.vdb"), path); return path;
    }
    [Fact]
    public void NativeCopyCloseReopenAndUuidChangeSemanticsMatch()
    {
        Assert.Equal("f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf", Native.RootElement.GetProperty("commit").GetString());
        foreach (var item in Native.RootElement.GetProperty("events").EnumerateObject()) Assert.Equal(item.Name == "initialIdentical" ? 0 : 1, item.Value.GetInt32());
        string path = CopyFixture();
        try
        {
            using var file = new VdbFileHandle(path);
            Assert.False(file.IsOpen); Assert.Equal(string.Empty, file.UniqueTag); Assert.False(file.IsIdentical(string.Empty)); Assert.False(file.IsIdentical("anything"));
            Assert.Equal(new FileInfo(path).Length, file.GetSize()); Assert.True(file.Open()); Assert.True(file.IsOpen);
            Assert.Equal(Native.RootElement.GetProperty("finalTag").GetString(), file.UniqueTag); Assert.Equal(225u, file.FileVersion); Assert.Equal(13u, file.LibraryMajorVersion); Assert.Equal(0u, file.LibraryMinorVersion); Assert.Equal("13.0/225", file.Version);
            Assert.Equal(["density"], file.GridNames); Assert.True(file.HasGrid("density")); Assert.Equal("lifecycle", file.GetMetadata().MetaValue<string>("marker"));
            Assert.Equal(9.25f, file.ReadGrid<float>("density").Tree.GetValue(new(-1, 2, 3)));
            Assert.Throws<InvalidOperationException>(() => file.Open());
            using var copy = file.Copy(); Assert.False(copy.IsOpen); Assert.Equal(file.UniqueTag, copy.UniqueTag); Assert.True(copy.IsIdentical(file.UniqueTag));
            Assert.Throws<InvalidOperationException>(() => copy.GetMetadata()); Assert.False(copy.Open()); Assert.Equal(9.25f, copy.ReadGrid<float>("density").Tree.GetValue(new(-1, 2, 3))); copy.Close(); copy.Close();
            file.Close(); file.Close(); Assert.False(file.IsOpen); Assert.False(file.Open()); file.Close();
            const string changedTag = "12345678-1234-1234-1234-123456789ABC";
            PatchUuid(path, changedTag); Assert.True(file.Open()); Assert.Equal(changedTag, file.UniqueTag); Assert.True(file.IsIdentical(changedTag)); Assert.False(file.IsIdentical(changedTag.ToLowerInvariant())); file.Close();
            Assert.False(file.Open()); using var changedCopy = file.Copy(); file.Close(); Assert.False(changedCopy.Open()); changedCopy.Close();
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void FailedReopenIsTransactionalAndRestoredFileKeepsIdentity()
    {
        string path = CopyFixture();
        try
        {
            using var file = new VdbFileHandle(path); Assert.True(file.Open()); string tag = file.UniqueTag; file.Close();
            byte[] valid = File.ReadAllBytes(path); File.WriteAllBytes(path, valid[..40]);
            Assert.Throws<InvalidDataException>(() => file.Open()); Assert.False(file.IsOpen); Assert.Equal(tag, file.UniqueTag);
            File.WriteAllBytes(path, valid); Assert.False(file.Open()); Assert.Equal(9.25f, file.ReadGrid<float>("density").Tree.GetValue(new(-1, 2, 3))); file.Close();
            valid[0] = 0; File.WriteAllBytes(path, valid); Assert.Throws<InvalidDataException>(() => file.Open()); Assert.False(file.IsOpen); Assert.Equal(tag, file.UniqueTag);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void DisposeIsTerminalButCloseAllowsReopen()
    {
        string path = CopyFixture();
        try
        {
            var file = new VdbFileHandle(path); file.Open(); file.Dispose(); file.Dispose();
            Assert.False(file.IsOpen); Assert.Throws<ObjectDisposedException>(() => file.Open()); Assert.Throws<ObjectDisposedException>(() => _ = file.UniqueTag); Assert.Throws<ObjectDisposedException>(() => file.Copy());
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void ManagedWritesGenerateDistinctValidUuidsAndExportForNativeReadback()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tedd-vdb-write-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string first = Path.Combine(directory, "first.vdb"), second = Path.Combine(directory, "second.vdb");
            Write(first, 1.25f); Write(second, 2.25f); string firstTag = HeaderTag(first), secondTag = HeaderTag(second);
            Assert.True(Guid.TryParseExact(firstTag, "D", out _)); Assert.True(Guid.TryParseExact(secondTag, "D", out _)); Assert.NotEqual(firstTag, secondTag);
            using var file = new VdbFileHandle(first); Assert.True(file.Open()); file.Close(); File.Copy(second, first, true); Assert.True(file.Open()); Assert.Equal(secondTag, file.UniqueTag);
            string? export = Environment.GetEnvironmentVariable("TEDD_OPENVDB_FILE_LIFECYCLE_OUTPUT_DIR");
            if (!string.IsNullOrEmpty(export)) { Directory.CreateDirectory(export); File.Copy(Path.Combine(directory, "first.vdb"), Path.Combine(export, "managed-second.vdb.tmp"), true); File.Move(Path.Combine(export, "managed-second.vdb.tmp"), Path.Combine(export, "managed-second.vdb"), true); Write(Path.Combine(export, "managed-first.vdb.tmp"), 1.25f); File.Move(Path.Combine(export, "managed-first.vdb.tmp"), Path.Combine(export, "managed-first.vdb"), true); }
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void ConstructionAndClosedOperationsValidateState()
    {
        Assert.Throws<ArgumentNullException>(() => new VdbFileHandle(null!)); Assert.Throws<ArgumentException>(() => new VdbFileHandle(string.Empty));
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".vdb");
        using var file = new VdbFileHandle(missing); Assert.Throws<FileNotFoundException>(() => file.Open()); Assert.Throws<FileNotFoundException>(() => file.GetSize());
        Assert.Throws<InvalidOperationException>(() => file.ReadArchive()); Assert.Throws<ArgumentNullException>(() => file.IsIdentical(null!));
    }
    private static void Write(string path, float value)
    {
        var grid = new Grid<float>(-2.5f) { Name = "managed" }; grid.Tree.SetValueOn(new(1, 2, 3), value);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None); VdbFile.Write(stream, grid, options: new() { Compression = VdbCompression.None });
    }
    private static string HeaderTag(string path) { byte[] header = File.ReadAllBytes(path); return Encoding.ASCII.GetString(header, 21, 36); }
    private static void PatchUuid(string path, string value)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None); stream.Position = 21; stream.Write(Encoding.ASCII.GetBytes(value));
    }
}
