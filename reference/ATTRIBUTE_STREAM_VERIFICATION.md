# Standalone attribute stream verification

This fixture family uses OpenVDB 13.0.1 commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, `points/AttributeArray.h` and `points/StreamCompression.cc`. It verifies actual native `TypedAttributeArray::write`, `readMetadata`, and `readBuffers` framing, without placing a native dependency in the managed library.

## Executed evidence

The C++17 oracle in `attribute-stream/oracle.cpp` compiled with MSVC 19.51.36256, x64 Release `/MT`, and the existing Blosc-enabled OpenVDB core. Its native dependencies are the pinned OpenVDB, oneTBB 2022.2.0, C-Blosc 1.21.6, zlib 1.3.2 and Snappy 1.2.2 builds documented in `CODEC_VERIFICATION.md`. They are used only for reference execution.

Native execution produced **500 profiles across 25 type/codec combinations**, each with raw and compression-enabled output. The combinations are null int32/float/double/vec3s; truncation int32/float/vec3s; fixed-point 8/16-bit position/unit ranges for float/double/vec3s/vec3d; and unit-vector vec3s/vec3d. Profiles cover uniform storage, explicitly expanded constant storage, varying/repeating/high-entropy values, constant stride three, variable strides, normalized zero-length construction, hidden/transient/streaming flags, and transient-output omission. Storage lengths straddle the 48-byte compression threshold and 128-byte padding boundary.

`AttributeStreamNativeTests` passed **1,001 tests** using .NET 11 SDK `11.0.100-preview.7.26381.103`, Release. Each fixture reconstructs the managed source array from native-recorded input values, compares exact encoded storage and decoded values, reads native raw/Blosc output through combined and split metadata/buffer APIs, verifies byte consumption, and checks managed round trips. Raw output matches the native bytes exactly. Compression output is compared semantically because a compatible compressor need not emit the same LZ4 parse.

The test export harness then wrote **1,000 managed streams**. The independent C++ verifier rebuilt each expected array from its own native construction code and read each managed stream using `TypedAttributeArray::read`. All 1,000 matched native shape, flags, uniformity, encoded storage bytes, decoded value bits, and complete stream consumption; omitted transient streams remained empty. Thus this evidence includes both native-to-managed and managed-to-native execution, rather than managed self-round-trips alone.

The compressed fixture SHA-256 is `251136b3aef3a5f8f276e7c18f148b741a5116c780998026282869bf6ab39686`.

## Wire details and scope

The uint64 length includes six bytes for flags and element count, but excludes the optional four-byte stride field and the one-byte nonuniform compression marker. Uniform arrays store one codec word. Variable stride serializes the total stored element count. Blosc compression is attempted only above 48 storage bytes; native buffers between 49 and 127 bytes are padded with zeros to 128 for compression, and the retained decoded storage excludes padding. Compression is retained only when it is smaller than the original unpadded storage. Native point-attribute compression uses LZ4 and four-byte byte shuffle regardless of codec word width.

The public managed stream API covers standalone nonpaged attribute streams. Type and codec names are supplied by the surrounding descriptor, not encoded in this stream. Paged buffers, mapped/delayed loading, attribute-set descriptors and complete PointDataGrid serialization are separate layouts and are not claimed here. Malformed lengths, compression headers, resource limits, partial reads and ownership are covered separately by the runtime owner's `AttributeStreamsTests`.

## Reproduction

```powershell
./reference/Build-AttributeStreamReference.ps1 -OpenVdbSource D:/source/openvdb -CMake 'C:/Program Files/Microsoft Visual Studio/18/Insiders/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' -TbbRoot C:/Users/tedd/AppData/Local/Temp/Tedd.OpenVDB-net11-port/managed/reference/dependencies/oneapi-tbb-2022.2.0
```

The script provisions the existing codec reference dependencies, builds this standalone oracle, generates and atomically publishes the gzip JSON fixture, runs managed parity/export tests, and invokes native readback. `-SkipBuild` reuses existing native binaries. Exported `.bin` streams and native binaries remain under ignored build/artifact directories; only source, scripts and the gzip JSON fixture are checked in.
