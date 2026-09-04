// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: Apache-2.0
namespace Tedd.OpenVDB.IO;

public static partial class VdbFile
{
    internal static MetaMap ReadMetadataMap(Stream stream, bool leaveOpen, VdbReadOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            if (!stream.CanRead) throw new ArgumentException("The stream is not readable.", nameof(stream));
            options ??= new(); options.Validate(); var input = new Input(stream, options);
            var values = ReadMetadata(input); input.Reserve(96L * values.Count);
            return MetaMap.FromDecoded(values);
        }
        finally { if (!leaveOpen) stream.Dispose(); }
    }
    internal static void WriteMetadataMap(Stream stream, IReadOnlyDictionary<string, MetadataValue> values, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            if (!stream.CanWrite) throw new ArgumentException("The stream is not writable.", nameof(stream));
            ValidateMetadata(values);
            using var output = new BinaryWriter(stream, Utf8, leaveOpen: true); WriteMetadata(output, values);
        }
        finally { if (!leaveOpen) stream.Dispose(); }
    }
}
