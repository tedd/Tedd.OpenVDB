# PointDataGrid conformance

The managed `PointDataLeafNode`, `PointDataTree`, and `PointDataGrid` composition follows OpenVDB 13.0.1 at commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`:

- `openvdb/points/PointDataGrid.h`
- `openvdb/points/PointConversion.h`
- `openvdb/points/impl/PointConversionImpl.h`
- `openvdb/points/PointCount.h`
- `openvdb/points/impl/PointCountImpl.h`
- `openvdb/unittest/TestPointDataLeaf.cc`
- `openvdb/unittest/TestPointConversion.cc`

`Build-PointDataGridReference.ps1` pins the source revision and generates 12 deterministic differential profiles. Eight use `NullCodec` over four point scenes and identity/affine transforms. Four use native `FixedPointCodec<false>` (managed `FixedPoint16Codec<Vec3f, PositionRange>`) over boundary and hierarchy-crossing scenes. The fixture records the native `Tree_ptdataidx32_5_4_3` identity, 5/4/3 node topology, root entries, cumulative 512-value leaf offsets, independent activity masks, source-index ordering, decoded local positions, reconstructed world positions, and active/inactive point ranges. Affine profiles deliberately deactivate a populated voxel and activate an empty voxel in every leaf.

The managed representation composes the validated `VdbTree<uint>` hierarchy with exclusive `PointDataLeafNode` sidecars. No mutable scalar tree escapes; `CopyOffsetGrid` is an independent diagnostic copy. Prefix values are restricted to the nonnegative `PointDataIndex32` domain and attribute arrays use `AttributeSet` copy-on-write ownership.

OpenVDB's PointData archive protocol is not part of this milestone. Native PointData I/O requires topology-pass counting, descriptor deduplication, paged delayed loading, multipass buffer ordering, and per-leaf page handles. `AttributeSet` standalone streams do not substitute for that grid-level framing. `PointDataGrid` therefore remains an in-memory specialized grid and is not accepted by standard scalar-grid VDB serialization.

Fixture SHA-256: `C7C163F48AC663BD8C3C6AEEB14B5D34A6E0CB0A946909DFCC0CDC685960EEC0`. The 12 native profiles and 17 independent regressions pass in Release, Debug, and Release with `DOTNET_EnableHWIntrinsic=0` on .NET SDK 11.0.100-preview.7.26381.103.
