# Metadata registry and map verification

The managed implementation follows `Metadata.h`, `Metadata.cc`, `MetaMap.h`, `MetaMap.cc`, and the registration list in `openvdb.h` / `openvdb.cc` at OpenVDB commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` (13.0.1). This extends the builtin-value checks in [METADATA_VERIFICATION.md](METADATA_VERIFICATION.md).

## Observed native comparisons

`registry-oracle.cpp` was compiled with MSVC 19.51.36256, Windows x64, Release `/MT`, against the previously built native OpenVDB core with Blosc and zlib. Dependencies and their pinned revisions are documented in [CODEC_VERIFICATION.md](CODEC_VERIFICATION.md). The managed package has no native dependency.

The native oracle generated **85 observations** in `tests/Tedd.OpenVDB.Tests/Fixtures/registry/native-registry.json` and **6 standard .vdb files**. The observations cover:

- All 18 builtin default values and the `ptidx32` / `ptidx64` factory aliases, including their canonical `int32` / `int64` names.
- Registration, duplicate registration, missing factories, unregistration and clearing.
- A registered eight-byte material record, with a signed int32 identifier and float32 weight, plus a registered temporary type.
- Map insertion, type conflicts, partial insertion before a later conflict, missing lookups, assignment, shallow and deep copies, replacement, text, and serialized key ordering. The ordering fixture includes BMP U+E000 and supplementary U+10000 to distinguish UTF-8 order from UTF-16 ordinal order.
- Exact metadata equality for signed zero, matching and different NaN payloads, vectors with NaNs, and typed versus opaque payloads with identical type names and bytes.
- Unregistered builtin payload preservation and unregistered temporary metadata removal.

The 64 managed registry/map tests passed, alongside all 73 earlier builtin metadata tests. Native OpenVDB independently read **6/6 managed-written archives**: compression modes 0, 4 and 6, each with registered factories and with unknown persistent metadata. Native metadata equality compared every field and file-level metadata; managed checks also verified voxel data.

## API and deliberate adaptations

`MetadataRegistry` is an independently configurable registry with a mutable process-wide `Default`. `CreateDefault()` supplies the 18 builtin factories and two point-index aliases. `RegisterBuiltinType()` restores an individual builtin after unregistration; generic custom factories accept unmanaged value types, encoder/decoder delegates, and boolean/text callbacks. Registration changes are synchronized, and archive/standalone-map reads capture one registry snapshot. Application callbacks run outside the registry lock.

`MetadataValue` holds an immutable value and payload snapshot. Equality uses native type names and exact payload bytes rather than .NET numeric equality. `DeepCopy()` creates a distinct wrapper; custom and opaque bytes are copied. Encoded custom bytes are copied when the value is created or decoded and retained, so changing or unregistering the factory does not change existing payloads. Custom decoder/encoder pairs are responsible for a compatible representation; custom decoded payloads are preserved verbatim, including noncanonical encodings.

`MetaMap` provides native insertion, lookup, assignment, clear, copy, equality, text, and standalone metadata stream operations. `Grid<T>` exposes corresponding map operations. Existing `Grid.SetMetadata` remains the replacement convenience API; `Grid.InsertMeta` enforces native type-name consistency. Managed metadata values are immutable, so native mutation through a shared `Metadata::Ptr` is represented by replacing the value, not by exposing a mutable reference. `CopyMeta()` shares immutable wrappers; `DeepCopyMeta()` creates copies.

Unknown persistent types retain opaque bytes. Unregistered type names beginning with `__` are discarded. Registered custom temporary types are retained. Internal `__delayedload` remains reserved and is consumed/discarded; delayed-loading metadata and lazy tree loading are not implemented by this milestone.

Read limits cover registry snapshot allocation, payload copies, decoded unmanaged value size, metadata counts, strings and total input. Fixed custom payload sizes are checked before allocating or invoking the decoder. Arbitrary allocations or side effects inside trusted application callbacks are outside these limits. Invalid Boolean encodings, malformed lengths, invalid UTF-8, duplicate names and empty names are rejected. Duplicate-name rejection is stronger validation than native map overwrite behavior. Failed standalone-map reads leave the destination map empty; stream ownership follows `leaveOpen`.

## Reproduction

From the repository root on Windows with the dependencies described above:

```powershell
./reference/Build-RegistryReference.ps1 -OpenVdbSource D:/source/openvdb -CMake 'C:/Program Files/Microsoft Visual Studio/18/Insiders/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' -TbbRoot C:/Users/tedd/AppData/Local/Temp/Tedd.OpenVDB-net11-port/managed/reference/dependencies/oneapi-tbb-2022.2.0
```

Use `-SkipBuild` to reuse the native build. The script generates native fixtures, publishes them through temporary files, runs managed tests, exports managed archives, and independently reads them with native OpenVDB. Native build products and dependency directories are ignored and are not included in the package.
