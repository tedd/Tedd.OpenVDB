# Upstream conformance oracle

`oracle.cpp` executes the actual upstream `openvdb::Int32Tree` implementation and emits deterministic JSON. It does not calculate expected values using a duplicate model. The managed conformance tests replay each operation and compare values, activity, hierarchy depth, leaf count, active voxel count, active bounds and structural topology equality. Copy checkpoints additionally mutate and clear the managed source to check independence.

The committed fixture was generated on 2026-09-04, Windows x64, with:

- OpenVDB 13.0.1, commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`.
- MSVC 19.51.36256.0 (toolset directory 14.51.36231), Release, static C++ runtime.
- CMake 4.3, Visual Studio 18 2026 generator.
- [Official oneTBB 2022.2.0 Windows package](https://github.com/uxlfoundation/oneTBB/releases/tag/v2022.2.0), whose downloaded SHA256 matched the published `4243442411bae9e3b80bfe2e47b5b571e8ae541ac45cfb991036b3a16a0c6012`.
- OpenVDB static core; optional Blosc, zlib, delayed loading, Imath-half dependency and explicit instantiation disabled. Bundled OpenVDB half implementation enabled. These options do not replace the exercised tree algorithms.

Observed output: **222 cases, 1,235 checkpoints, 99,434 coordinate samples**. Every sample contains a value, active flag and depth. The SHA256 of the committed UTF-8 fixture is `b8d98c4539e51a105b5cf7f20b0ba0eb7091c4eadc13d86b8107e4f18a9111a2`.

Regenerate from a clean Windows x64 machine with CMake and the corresponding Visual Studio C++ workload:

```powershell
./reference/Build-Reference.ps1 -OpenVdbSource D:/source/openvdb -CMake cmake
```

The script verifies the exact upstream revision and the dependency checksum. Build products and downloaded dependencies remain under ignored `reference/build*` and `reference/dependencies` directories. It publishes the fixture only after native execution and JSON validation succeed.

Coverage includes all four tile levels, signed 8/128/4096 boundaries, min/max integer point coordinates, independent activity and stored values, inherited tiles, replacement of child subtrees, inclusive aligned and overlapping fill, pruning, copying, clearing, four deterministic random traces and 196 pairwise topology cases. These compare union with and without tile preservation, intersection and difference across empty trees, inactive/active root tiles, inactive/active leaves, and inactive/active leaf-level tiles.

An attempted native `fill()` with an inclusive maximum coordinate of `int.MaxValue` did not terminate and was stopped. `RootNode.h` advances its signed `int` loop indices with `tileMax + 1`; this endpoint overflows. This native case is excluded from generated fixtures; min/max point writes and minimum-coordinate fills remain. Managed endpoint-safe fill behavior requires independent tests and is an intentional deviation from this observed upstream defect.

Passing these fixtures establishes the listed observations, not completeness of OpenVDB, exhaustive equivalence, concurrent mutation safety, native `.vdb` interchange, or equivalence for every scalar and vector type.

## Additional native reference families

The original tree oracle above is supplemented by independently executed source implementations:

- [Advanced storage, filtering, dense conversion, iterators and statistics](ADVANCED_VERIFICATION.md).
- [CSG and compositing, including serial-copy background behavior](CSG-CONFORMANCE.md).
- [Math, frusta, camera maps, finite differences and stencils](MATH_VERIFICATION.md).
- [Standard archive interoperability](IO_VERIFICATION.md), [compression/half profiles](COMPRESSION_VERIFICATION.md), [heterogeneous archives](ARCHIVE_VERIFICATION.md), and [extended grid types](GRID_TYPES_VERIFICATION.md).
- `Build-VolumeToolsReference.ps1` generates pruning, flood-fill, morphology, sphere and scalar/vector composite fixtures.

All managed conformance tests consume committed fixtures; native builds are only needed to reproduce or extend those fixtures. The native and managed reference generators are deliberately separate.

Additional conformance records: [Blosc](BLOSC_VERIFICATION.md), [node managers](NODE_MANAGER_VERIFICATION.md), [sampling](SAMPLING_VERIFICATION.md), [level-set analysis](LEVELSET-ANALYSIS-CONFORMANCE.md), and [level-set tracking/filtering](LEVELSET-TRACKING-CONFORMANCE.md).
