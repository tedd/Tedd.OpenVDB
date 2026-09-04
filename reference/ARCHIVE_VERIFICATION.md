# Heterogeneous archive verification

On **2026-09-04**, `Build-ArchiveReference.ps1` passed **32 managed archive tests and eight independent native verifications**, using the OpenVDB revision and toolchain recorded in [compression verification](COMPRESSION_VERIFICATION.md).

`archive-oracle.cpp` creates seven-grid archives containing Int32, Float and Double trees, two pairs of shared trees, duplicate and empty grid names, independent instance metadata/transforms, half-precision storage, file metadata and opaque custom metadata. Eight files cover seekable/streaming layouts, instances/materialized copies, and no compression/zlib plus active-mask compression. Native OpenVDB independently loads the eight managed outputs and compares every stored value, activity, topology, counts, metadata, transform application and tree-sharing identity.

The corpus also exercises native AffineMap, UnitaryMap, TranslationMap, ScaleMap, UniformScaleMap, ScaleTranslateMap, UniformScaleTranslateMap and NonlinearFrustumMap serialization. The managed writer emits full affine matrices for linear maps; preserving an equivalent transform does not preserve its original native map subclass. Frustum bounds, taper, depth and the secondary affine map are retained.

The API exposes `IGrid`, `VdbArchive`, `VdbFile.ReadArchive` and `WriteArchive`. `Grid<T>` implements `IGrid`. Archive construction copies the collection and file-metadata map while retaining grid objects and tree sharing. `VdbWriteOptions.EnableInstancing` defaults to true; sharing is encoded only when half-precision flags agree. `Read<T>` remains a single-grid convenience method and explicitly rejects nonempty file metadata so that metadata is not silently discarded.

Unregistered persistent metadata is retained as immutable `OpaqueMetadata` with its native type name and uninterpreted bytes. Registered supported metadata still validates exact payload sizes. Temporary `__delayedload` metadata is consumed and discarded, as the reader eagerly materializes data. Metadata and transform objects of an instance remain independent; its tree is shared with its parent.

Malformed tests cover duplicate descriptor names, impossible or overlapping grid offsets, invalid instance block offsets, missing or incompatible parents, bounded grid counts, truncated archives, ownership, unsupported output preflight, malformed frustum parameters/matrices and nonlinear secondary maps. Allocation and byte budgets remain cumulative across the entire archive.

Reproduce:

```powershell
./reference/Build-ArchiveReference.ps1 -OpenVdbSource D:/source/openvdb
```

Native instance parents must currently precede their instances. Forward references are explicitly rejected. This verification does not establish legacy version, Blosc, point-grid, arbitrary tree-layout, random-access/lazy-loading or complete OpenVDB compatibility.
