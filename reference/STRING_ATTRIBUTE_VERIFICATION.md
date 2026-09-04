# String point-attribute verification

This family ports OpenVDB 13.0.1 `points/AttributeArrayString.h` and `AttributeArrayString.cc` at commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`. The managed implementation stores OpenVDB string-table indices in `uint32/str` attributes, provides the native `uint16` truncated `str_trnc` codec, resolves `string:N` metadata through read/write handles, and uses the upstream string-index block allocator.

## Executed evidence

The C++17 oracle in `string-attribute-oracle.cpp` compiled with MSVC 19.51.36256, x64 Release `/MT`, against the pinned Blosc-enabled OpenVDB static library. Its native-only dependencies are oneTBB 2022.2.0, C-Blosc 1.21.6, zlib 1.3.2 and Snappy 1.2.2, as provisioned by `Build-CodecReference.ps1`. None are runtime dependencies of the managed library.

Native execution produced **25 cases**: four metadata-cache snapshots, one insertion/hint sequence, 16 standalone stream profiles, one handle-mutation sequence, and three malformed-key observations. The stream profiles cover `str` and `str_trnc`, uniform and expanded arrays, constant stride one and three, variable total-size storage, hidden/streaming/transient flags, empty-length normalization, indices at and beyond 16-bit boundaries, raw framing, Blosc framing, padding and compression fallback.

`StringAttributeNativeTests` and `StringAttributeTests` passed **63 tests** with .NET 11 SDK `11.0.100-preview.7.26381.103`, Release. Managed readers consumed all 32 native raw/Blosc streams and matched native shape, flags, encoded storage and decoded indices. Uncompressed managed output matched native bytes exactly. The export harness wrote 32 managed streams; the independent C++ verifier reconstructed each expected native array and read every managed stream through `TypedAttributeArray::read`. All matched native metadata and encoded storage with exact stream consumption.

The fixture `openvdb-13.0.1-string-attributes.json.gz` has SHA-256 `2d81f88a2f1f91d6539aee51291be11366debe637bd74b8b9dbd7bf8cb3f0bf3`.

## Semantics and strengthened validation

Encoded index zero denotes the empty string. Positive index `N` resolves through string metadata key `string:{N-1}`. Read handles consult live metadata. Write handles cache the mapping at construction and require `ResetCache()` after external metadata changes. The truncated codec narrows on encode exactly as native storage does.

The allocator preserves the upstream 13.0.1 block-list behavior, including its observable high-hint overwrite sequence recorded by the native fixture. Managed cache parsing deliberately rejects signs, whitespace, trailing characters and `string:4294967295`, whose native unsigned addition would wrap the encoded index to the reserved zero sentinel. Non-string metadata and keys without the exact lowercase prefix are ignored. A stream can contain an index absent from its surrounding metadata; stream decoding retains the index and string resolution throws `KeyNotFoundException`.

Standalone attribute streams do not contain the metadata map or type/codec names. Complete PointDataGrid descriptor and attribute-set serialization remains separate work.

## Reproduction

```powershell
./reference/Build-StringAttributeReference.ps1 -OpenVdbSource D:/source/openvdb -CMake 'C:/Program Files/Microsoft Visual Studio/18/Insiders/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' -TbbRoot C:/Users/tedd/AppData/Local/Temp/Tedd.OpenVDB-net11-port/managed/reference/dependencies/oneapi-tbb-2022.2.0
```

The script validates the upstream revision, provisions/reuses the pinned codec reference build, builds the oracle, generates and atomically publishes the gzip fixture, runs managed parity/export tests, and invokes native readback. `-SkipBuild` reuses existing native binaries. Native build products and managed export streams remain under ignored build/artifact directories.
