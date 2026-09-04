# Point-index grids and ParticleAtlas conformance

Upstream: OpenVDB 13.0.1 at `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, primarily `tools/PointIndexGrid.h`, `tools/ParticleAtlas.h`, and the existing `tools/PointPartitioner.h` port.

## Representation

Native `PointIndexLeafNode<PointIndex32, 3>` derives from a scalar leaf but stores two coupled structures: 512 cumulative end offsets and a contiguous point-index array. Its ordinary scalar leaf mutators assert because changing prefix values independently corrupts the index array. Core managed `LeafNode<T>`, `VdbTree<T>`, and `Grid<T>` remain sealed. `PointIndexGrid` therefore owns a private, non-escaping `VdbTree<uint>` for exact 5/4/3 topology and matching prefix values, plus exclusively owned `PointIndexLeafNode` wrappers for each index array. Public access is read-only; `CopyOffsetTree` and `CopyOffsetGrid` return independent diagnostic copies. Metadata and transforms retain the standard grid surface without exposing scalar mutation.

`PointIndexGrid.Create` reuses the native point partitioner's cell-centered convention and stable index order. Per-leaf histograms become inclusive prefix sums. `PointIndexTree` supports background/value/activity/depth probes, native tree/node counts, value iteration, leaf probing and sparse spatial query traversal. `PointIndexLeafNode` supports copied validated construction, offsets, activity, ranges, emptiness, masks and deep copies. The representation is reusable by future point-data work without changing core volume leaf layout.

`PointIndexIterator` ports exact voxel and region prefix-range queries, index/world-space boxes, accurate/coarse radial predicates, native exclusive-face decomposition, reset/current/next behavior and safe copied result lifetime. Queries traverse only occupied leaves in the requested X interval instead of stepping through arbitrarily large empty coordinate regions. `PointIndexFilter` ports exact world-space squared-distance callbacks and retains its construction transform. Frozen grids and external points support concurrent independent query objects.

`ParticleAtlas` ports radius extrema, thresholds, stable splitting, global index remapping, native append-on-construct behavior and the source loop's maximum-levels-plus-one result. Its iterator caches the level count at construction, clamps update levels, and ports sphere/box overlap predicates. Native behaviors retained include duplicate results where degenerate exclusive face regions overlap and the uniform-level optimization reading radius zero from the global particle source. Those are observable native contracts, even though deduplication and level-local radius caching could appear preferable.

## Native evidence

`Build-ParticleAtlasReference.ps1` builds `particle-atlas-oracle.cpp` against the pinned static library with oneTBB constrained to one worker. The deterministic gzip fixture contains:

- 30 point-index grids: six point/radius scenes and five transform profiles. Every 5/4/3 node count, root count, active prefix voxel, full 512-value/activity leaf runs, stable index array, origin, voxel size and world origin is compared.
- 1,260 exact grid-query result lists: coordinate, inclusive coordinate box, index/world floating box, accurate/coarse index radial and accurate world radial queries.
- 90 atlas constructions: six scenes, three minimum voxel sizes, and maximum levels 0, 1, 2, 3 and 50. Every level's extrema, point-index grid, clamped level iteration, 540 radial queries and 540 box queries is compared in exact native result order.

All **120 native profiles pass**. The independent suite adds prefix corruption rejection, memory ownership, metadata/deep-copy isolation, staleness/rebuild behavior, nonfinite and full signed-coordinate cases, seeded brute-force searches, exact/coarse and boundary predicates, iterator reset/copy/compacting-GC lifetime, transform snapshots, callbacks, cancellation, concurrent frozen reads, level thresholds, append semantics, empty extrema and the two native query quirks. All **144 tests** (120 native profiles and 24 independent tests) pass in Release, Debug, and scalar mode (`DOTNET_EnableHWIntrinsic=0`).

Fixture: `openvdb-13.0.1-particle-atlas.json.gz`, 46345 bytes. SHA-256: **9DB6CDE7BA564E978DC1B1FBE576453037107366CD54B580436B953FC4218A1E**.

## Explicit limits

The generic C++ templates accept alternate point vector precision and alternate point-index integer wrappers. This milestone uses the existing managed double-position `IPointSource`/`IParticleSource` and native standard 32-bit point-index specialization. Custom signed/64-bit point-index trees are not exposed. Standard volume VDB I/O cannot encode the specialized index sidecar yet; `PointIndexGrid` implements `IGrid` metadata inspection, but archive serialization remains unsupported until the point-grid codec owns this node layout. Native specialized-leaf buffer framing and partial-read clipping therefore remain unported in this family.

Native index-space point queries document a uniform-scale, linear transform precondition. The implementation preserves source behavior rather than rejecting affine/frustum maps; the native fixture includes such maps to prove exact behavior. External sources must remain stable during construction and queries. Managed cancellation and finite-query validation avoid native undefined coordinate/count conversions. Construction publishes complete new atlas levels atomically on success.
