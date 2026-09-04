# Native `.vdb` interchange verification

On **2026-09-04**, the managed codec was tested in both directions against actual OpenVDB **13.0.1**, revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`. The reference compiler, runtime and oneTBB dependency are recorded in [README.md](README.md).

`io-oracle.cpp` writes native format225 files with `COMPRESS_NONE`, then independently reads managed output using native `openvdb::io::Stream`. The native verification checks all metadata supplied by the fixture, affine transform application, background, leaf and active voxel counts, structural topology, and **every stored tile and every active/inactive leaf value**, in both traversal directions. Floating-point comparisons preserve signed zero and recognize NaNs.

Observed results:

| Scalar/scenario | Native leaf count | Native active voxel count |
|---|---:|---:|
| Int32 empty | 0 | 0 |
| Float empty | 0 | 0 |
| Double empty | 0 | 0 |
| Int32 mixed | 4 | 68,719,477,250 |
| Float mixed | 5 | 68,719,477,253 |
| Double mixed | 5 | 68,719,477,253 |
| Int32 mixed, streaming archive | 4 | 68,719,477,250 |

All **seven native-written fixtures** were read and validated by managed tests. All **seven managed-written files** were independently read and validated by native OpenVDB. The focused suite passed **64 tests** (53 codec tests and 11 independent review tests), including malformed input, every truncated prefix of a small grid, resource limits, ownership and half-float-control metadata regressions.

The mixed fixture includes root and internal tiles, inactive non-background values, positive and negative node boundaries, `int.MinValue` and `int.MaxValue` point coordinates, independent activity, and a nontrivial affine scale/shear/translation. Float and Double cases additionally include fractional values, positive/negative infinity, NaN and negative zero. Metadata covers Boolean, Int32, Int64, Float, Double, UTF-8 strings, Vec3i, Vec3d and Mat4d.

Reproduce with Visual Studio C++, CMake and .NET 11:

```powershell
./reference/Build-IoReference.ps1 -OpenVdbSource D:/source/openvdb
```

Use `-CMake` for an explicit CMake executable, and `-SkipBuild` only when the verified native build already exists. The script generates fixtures in ignored staging first, runs the managed tests, exports managed files to an ignored build directory, and invokes the native verifier. Native UUIDs are random, so regenerated `.vdb` bytes need not have identical hashes.

The managed library has no native dependency. Native builds are needed only to regenerate the reference corpus or run this separate bidirectional verification. Ordinary `dotnet test` uses committed native fixtures.

Compatibility is intentionally restricted: one Tree4 Int32/Float/Double grid, format225, compression flags exactly zero, supported immutable metadata and affine transforms. Seekable and streaming archive layouts are implemented. AffineMap is verified natively; the common scale/translation/unitary-map reader branches follow the pinned native layouts but are not represented in this native corpus. No claim is made for compression, half floats, instances, multiple grids, nonlinear transforms, unknown persistent metadata, file-level metadata, point grids, legacy versions, lazy loading or arbitrary third-party VDB files.
