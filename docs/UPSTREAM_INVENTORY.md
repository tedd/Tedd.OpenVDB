# Upstream inventory and completion boundary

Reference: OpenVDB **13.0.1**, commit **f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf**. The local checkout was inspected directly. No previous Tedd.OpenVDB wrapper source was used.

This implementation is a managed port of selected OpenVDB storage, math and sampling behavior. **It is not a complete 1:1 port of OpenVDB.** The native differential fixture covers selected `Tree4<int32_t,5,4,3>` operations. It does not establish completeness of every tree API, every value specialization, the tools library or `.vdb` serialization.

## Measured source inventory

Counts below are tracked C/C++ source/header paths with extensions `.h`, `.cc`, `.cpp`, `.cuh`, `.cu` or `.h.in`; they are not class counts, function counts, or a completion percentage. Generated build products are excluded.

| Upstream directory | Files | Scope |
|---|---:|---|
| `openvdb` | 324 | Core grids, tree, math, tools, I/O, points, Python and core tests |
| `nanovdb` | 200 | Compact CPU/GPU grids, conversions, builders and tools |
| `openvdb_ax` | 119 | AX parser, compiler, execution engine and tests |
| `openvdb_houdini` | 83 | Houdini integration |
| `openvdb_wolfram` | 46 | Wolfram integration |
| `openvdb_cmd` | 23 | Command-line tools |
| `openvdb_maya` | 15 | Maya integration |
| `ext` | 24 | Bundled third-party source |
| `doc` | 3 | Compilable documentation examples |
| **Total** | **837** | |

The 324 files under `openvdb/openvdb` split as follows:

| Area | Files | Managed status / remaining work |
|---|---:|---|
| Root grid/metadata/platform files | 15 | Selected generic grid façade. Registry, full metadata semantics, platform abstractions and complete grid API remain. |
| `tree` | 13 | Fixed Tree4 5/4/3 hierarchy, tiles, masks, accessors, topology, all three merge policies, leaf transfer, live value iterators, leaf manager, cached/dynamic node managers and node-local traversal. Full configurable tree family, remaining iterator overloads and specialized storage remain. |
| `math` | 27 | Coordinates/boxes, expanded vectors/matrices/quaternions, affine/frustum/camera maps, rays/DDA, finite differences, gradient/WENO/curvature stencils, differential operators and statistics. Full precision/map family, remaining numerical utilities and solvers and numerical utilities remain. |
| `tools` | 57 | Sampling, scalar/vector composites and CSG, sphere level sets, pruning, flood fill, morphology, filtering, differential grid operators, level-set interior/fog conversion, segmentation, measurements, level-set tracking/filtering, dense conversion and statistics. Meshing, level-set rebuilding/general evolution, advection, remaining segmentation APIs, particles, point conversion and remaining tools are not ported. |
| `io` | 17 | Native format225 heterogeneous Int32/Int64/Float/Double/Bool/Vec3/Mask archives, instances, file/opaque metadata, affine/frustum maps, active-mask/zlib/Blosc LZ4 compression and half encoding are verified bidirectionally. Unsupported Blosc codecs/filters, forward instance references, point/custom grid layouts, legacy versions and delayed loading remain unsupported. MaskGrid is a restricted Boolean-backed specialization. |
| `points` | 47 | Not implemented: point-data/index grids, attributes, groups, codecs and point tools. |
| `util` | 13 | Selected node-mask/value helpers. Full utility API remains. |
| `simd` | 1 | Managed operations use .NET primitives where applicable; upstream SIMD wrapper API is not ported. |
| `thread` | 1 | TBB integration and equivalent threaded execution semantics are not ported. |
| `python` | 12 | Not ported. |
| `unittest` | 121 | New managed tests plus selected native conformance traces; upstream suite is not completely translated. |

These counts can be reproduced with `git ls-files` at the pinned revision. Source file count alone is not a meaningful measure of API parity: large template headers contain many independent algorithms and overloads.

## Verified sparse-storage contract

The C++ generator and provenance are in [`reference/README.md`](../reference/README.md). The committed output records 222 cases, 1,235 operation checkpoints and 99,434 sampled coordinates. Each sample includes value, activity and hierarchy depth; checkpoints also cover counts, bounds and structural topology comparison. The checks span signed node boundaries, explicit active/inactive tiles, descendant inheritance, replacement, pruning, fill, no-op allocation behavior, copy and topology operations.

Upstream's `SetValueOnly(background)` analogue creates an upper internal node when its root entry is absent, despite allocating no leaf. `SetValueOff(coord, background)` leaves an absent root unchanged. Activity is independent of value. A background-valued voxel can be active; an inactive voxel can retain a non-background value.

An inclusive fill ending at `int.MaxValue` stalled in the native generator and was terminated. The upstream signed `tileMax + 1` loop advance can overflow at this endpoint (`tree/RootNode.h`, fill implementation). Managed endpoint-safe behavior is an intentional deviation and is tested independently; the hanging native operation is excluded from fixtures.

## Standard `.vdb` I/O implementation boundary

A restricted interoperable codec for **file format 225, Int32/Float/Double Tree4 grids with no compression, active-mask compression, zlib, and Float/Double half encoding** is implemented in `IO/VdbFile.cs`. Seven actual native-produced files pass managed reads, and seven managed-produced files pass independent native full-value/topology/metadata/transform verification. The tested platform is Windows x64. See [native I/O provenance](../reference/IO_VERIFICATION.md) for the exact scope and reproduction command. Twenty additional compressed/half profiles pass both directions; see [compression provenance](../reference/COMPRESSION_VERIFICATION.md). This is standard archive and node serialization, with no native runtime dependency.

The implementation follows these concrete stages, derived from the pinned source:

1. `io/Archive.cc:941` and `:1010`: read/write the 64-bit magic value `0x56444220`, uint32 file version (225), uint32 library major/minor, one-byte offsets flag, and fixed 36-byte ASCII UUID. Then file metadata and int32 grid count (`Archive::write`). Reject unsupported versions explicitly.
2. `util/Name.h`: strings are uint32 byte length followed by bytes. `MetaMap.cc` stores uint32 item count, then each item's name, type name and `Metadata` size-framed payload. The archive API preserves unknown persistent metadata as opaque immutable payloads, supports file-level metadata, imposes length/allocation limits, and rejects truncation. Grid metadata is separate from file metadata.
3. `io/GridDescriptor.cc:53`: each descriptor contains unique name, registered tree type name, instance-parent name and three signed 64-bit offsets (grid, block, end). Handle parent-before-instance tree sharing and `_HalfFloat` types. Validate offsets and multiple-grid boundaries.
4. `io/Archive.cc:1366`: each non-instance grid stores uint32 compression flags, grid metadata, typed transform, topology, then leaf buffers. The codec supports `COMPRESS_NONE == 0`, zlib bit 1, active-mask bit 2 and Blosc bit 4 (LZ4/zlib byte-shuffle subset). Active-mask compression is a separate bit and cannot be mistaken for uncompressed data simply because zlib/Blosc are absent.
5. `math/Transform.cc:67`: transform payload begins with a map type string. `AffineMap` is a 4x4 double matrix; scale maps also serialize derived vectors (scale, voxel size, inverse scale, inverse-square scale, inverse-twice scale). Supporting only `AffineMap` requires explicit rejection of other maps, not assuming that every linear map has the same byte layout.
6. `tree/Tree.h:1144`: topology starts with int32 buffer count (one). `RootNode.h:2348` then stores scalar background, uint32 root-tile and child counts, sorted root tiles (three int32 coordinates, scalar value, one-byte activity), and sorted root child origins followed by child topology.
7. `tree/InternalNode.h:2393`: every internal node serializes its full child mask and value mask, followed by its full value array (child slots serialize zero), followed recursively by child topology in mask-index order. Upper nodes have 32,768 slots and lower nodes 4,096. Sparse managed overrides must be expanded to the required native representation while preserving inherited values and states.
8. `tree/LeafNode.h:1327` and `:1458`: leaf topology is its 512-bit activity mask. In a subsequent depth-first buffer pass, each leaf repeats the activity mask and writes its 512 values. Internal nodes do not serialize their leaf buffers inline with topology. Traversal order must match the native child-mask order and sorted root keys.
9. `io/Compression.h:648`: **even with compression flags zero**, every internal value array and every leaf buffer starts with one metadata byte, `NO_MASK_AND_ALL_VALS == 6`; raw scalar bytes follow. Omitting that byte corrupts alignment. `writeData` does not write an additional length prefix for uncompressed fixed-size values.

Native-produced fixtures cover empty grids, nonzero background, all tile levels, negative coordinates, inactive non-background values, affine transforms and float exceptional values. Both native-write/managed-read and managed-write/native-read pass. Tests reject truncated input, invalid offsets, mask inconsistencies, excessive lengths and unsupported-version/compression cases. Heterogeneous archive methods support multiple grids and retain file metadata; the single-grid convenience reader rejects multiple grids. Exact byte equality additionally requires controlling UUID and optional metadata. The `is_saved_as_half_float` storage-control metadata must be Boolean and agree with the descriptor suffix. Half encoding follows native per-section scalar widths and rounding; contradictory metadata is rejected.

Eight seven-grid archive profiles additionally verify instances, file/opaque metadata and nonlinear frustum maps; see [archive provenance](../reference/ARCHIVE_VERIFICATION.md). Unsupported Blosc codecs/filters, point grids, forward instance references, legacy-version compatibility, partial I/O, memory mapping and delayed loading remain unsupported. Thirty-two additional extended-value profiles verify Int64, Boolean, Mask and Vec3 grids; see [grid-type provenance](../reference/GRID_TYPES_VERIFICATION.md). The verified interchange claim applies only to the explicitly documented format225 subset.

Fifty-two additional Blosc archive profiles pass native verification in both directions. Eight supported and three explicitly unsupported native frame variants exercise framing, shuffle and compressor handling; see [Blosc provenance](../reference/BLOSC_VERIFICATION.md). The managed writer emits LZ4 byte-shuffle frames; the reader also supports Blosc zlib frames. Compressed BloscLZ/Snappy/Zstd and bit shuffle remain unsupported.
