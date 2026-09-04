# Builtin metadata verification

On **2026-09-04**, **73 tests** passed, including **68 native metadata cases** across all **18 builtin metadata types** and persistent opaque payloads. Native OpenVDB independently verified all **three managed-written archives** using compression flags 0, 4 and 6. File-level metadata was included in those comparisons.

The codec now reads and writes native `half`, `vec2i`, `vec2s`, `vec2d`, `vec4i`, `vec4s`, `vec4d` and `mat4s` metadata, alongside the existing Boolean, Int32/Int64, Float/Double, string, Vec3 and Mat4d types. Payload sizes are checked before decoding. Floating-point binary payloads preserve signed zero and NaN bits; half metadata stores its native two-byte payload independently of grid half-encoding options.

`MetadataValue.AsBool()` follows native exact-zero/empty-value semantics. For example, the nonempty strings `"false"` and `"0"` are true, negative zero is false, NaN is true, an all-zero matrix is false, and an opaque payload's truth value depends on whether it contains bytes. Native metadata text formatting is invariant: lowercase Boolean and special floating values, six significant digits for scalars/vectors, and six fixed decimal places for matrix entries. Persistent opaque values format as `<binary data>` or an empty string.

The new immutable `Vec2f`, `Vec2i`, `Vec4f`, `Vec4i` and `Mat4f` types supply the native contiguous layouts required for these metadata values. Their constructors/indexing/basic arithmetic follow the existing managed value conventions; this metadata milestone does not claim a complete port of every arithmetic overload for every precision. Native `vec3i` metadata continues to use `Coord`, with `Vec3i` retrieval compatibility.

## Evidence and reproduction

`metadata-oracle.cpp` constructs native typed metadata, serializes its payload, and records native type names, Boolean conversions and exact UTF-8 text. Committed tests compare every payload byte and all three native properties. Cases include zero, nonzero, mixed components, signed zero, NaN/infinity, empty and nonempty strings, zero/identity/nontrivial matrices, and opaque bytes. The native readback compares metadata using upstream byte-based `Metadata::operator==` for both grid and file metadata.

```powershell
./reference/Build-MetadataReference.ps1 -OpenVdbSource D:/source/openvdb
```

The `-SkipBuild` workflow was executed after building the native oracle. It atomically publishes fixtures, runs all 73 tests, exports three managed archives and reads them through native OpenVDB. `-TbbRoot`, `-CMake` and `-DotNet` select installed dependencies. The native configuration uses OpenVDB `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, c-blosc `616f4b7343a8479f7e71dd3d7025bd92c9a6bbd0`, MSVC 19.51.36256 x64 `/MT` and .NET SDK `11.0.100-preview.7.26381.103`; see [codec provenance](CODEC_VERIFICATION.md) for dependency details.

Source: upstream `Metadata.h`, `Metadata.cc`, `MetaMap.h`, `MetaMap.cc`, and `math/{Vec2,Vec4,Mat4}.h`. The current `MetadataValue` API remains immutable. Mutable native metadata references, standalone `MetaMap` behavior and public custom-type registration remain separate work; this milestone adds builtin value coverage and preserves existing archive resource limits.
