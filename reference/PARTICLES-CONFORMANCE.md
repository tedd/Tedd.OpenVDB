# Particle rasterization and point partitioning conformance

Sources: `openvdb/tools/ParticlesToLevelSet.h` and `tools/PointPartitioner.h`, pinned revision
`f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` (OpenVDB 13.0.1).
Managed files: `Tools/ParticleSources.cs`, `PointPartitioner.cs`, `ParticlesToLevelSet.cs` and `ParticlesToLevelSet.Finalize.cs`.

## Rasterization and attribute contract

`ParticlesToLevelSet<T>` supports float, double and bool. The attribute-bearing `ParticlesToLevelSet<T,TAttribute>` supports arbitrary unmanaged attributes. Both retain the supplied output grid, cache its initial voxel size and half-width, expose radius cutoffs and ignored-particle counters, and rasterize variable spheres, fixed spheres or tapered trails. `ParticleConversion` provides the six native convenience operations, which rasterize and prune. No level-set classification is silently assigned.

`IPointSource`, `IParticleSource` and `IParticleSource<TAttribute>` are external data adapters. `PointArray`, `ParticleArray` and `ParticleArray<TAttribute>` provide array/ReadOnlyMemory implementations. Inputs remain caller-owned and must be stable during a call. Positions are read during partitioning and again during rasterization, as in native code.

The implementation follows native serial raster order. `GrainSize` is retained as configuration but does not currently enable TBB-style per-worker grids and parallel reduction; native parallel attribute tie ownership and topology changes from intermediate worker pruning are not promised. The managed port uses contiguous indices, spans, initialized portions of uninitialized scratch arrays, sparse cube fills and direct tree accessors.

Key source semantics:

- Radius limits are in voxel units, defaulting to 1.5 and 100. Cutoff endpoints are included. Setting minimum does not retroactively raise maximum. Variable-radius counters count only valid-position particles actually visited; a rejected fixed radius counts the entire source, including invalid-position records. Counters reset on each rasterization call.
- SDF rasterization uses double geometry and casts the final world-space distance to the grid scalar. The exterior band boundary is excluded, the interior band boundary is inactive, and existing negative inactive values suppress further work. CSG distance ties keep the earlier particle attribute.
- Mask rasterization sparsely fills an inscribed cube, then visits the native six overlapping exterior slabs with a strict sphere-distance comparison. Mask attribute overwrites follow source order, so later particles replace earlier attributes in overlaps.
- Trails advance opposite the transformed velocity by half delta times current radius; radius tapers linearly with traveled distance toward the minimum radius. Zero velocity produces one sphere. Position and velocity mappings remove translation in the same way as the source.
- Attribute-enabled rasterization accumulates in an internal visible/attribute tree whose equality ignores attributes. Finalization optionally prunes that tree, topology-copies both outputs, transfers active attributes and native inactive-leaf values, then flood-fills inactive SDF values. Attributes are generated only for particle topology, not existing destination values.
- An empty destination has its tree reference replaced during attribute finalization; a nonempty destination is updated by CSG union or topology union/pruning. Existing destination metadata and transform remain intact. Attribute grids receive their captured transform and default metadata. Before finalization the attribute grid is null and the destination is untouched.

Managed safety additions include cancellation exceptions, per-call `MaximumSphereInstances` and `MaximumVoxelEvaluations`, rejection of nonfinite rasterized radii and invalid trail spacing, checked coordinate conversion, and a nonadvancing-trail exception. Cancellation/budget failure can retain already completed direct raster writes or partial private attribute accumulation; this is not a transactional operation. Voxel evaluations count actual dense sphere/slab visits, including repeated slab overlaps, and do not count sparsely filled interior voxels.

The native zero-minimum-radius trail loop can stall indefinitely when its radius and spacing shrink to zero while distance remains equal to the velocity length. This occurred in the native fixture generator and was terminated without changing upstream. Finite oracle profiles use minimum .25; the exact zero-minimum scene is an independent managed regression for progress detection. Native progress callbacks and silent partial returns are represented by managed CancellationToken checks and exceptions.

## Partition contract

`PointPartitioner` exposes bucket origins/bounds, contiguous point-index spans, optional local voxel offsets, cell/node-centered interpretation, clear, swap and reusable construction. Native order first compares signed coarse-bin origins (bucket log2 dimension plus five), then local bucket offsets, then optional voxel offsets, then original input index. It is not a global lexicographic ordering of leaf origins. The managed implementation preserves this order with a total-key array sort instead of the native parallel radix passes.

Nonfinite world positions are omitted. Their managed recorded voxel offsets are -1; native leaves those entries uninitialized. Managed source indices/counts use Int32 and bucket log2 dimensions 0 through 10, keeping voxel offsets within Int32. The native template permits other index/offset widths. Construction publishes its new arrays only after success; cancellation or a throwing position source leaves the prior partition intact.

## Reference coverage

`reference/particles-oracle.cpp` contains 396 native particle profiles and 72 partition profiles. Particle coverage includes float/double/bool, fixed/variable/trail modes, pruned and unpruned finalization, integer attributes, existing output values, repeated calls, shifted transforms, tied attributes, radius cutoffs, invalid positions, zero velocity, distant bins and large sparse masks. Partition coverage includes log2 sizes 0/3/5, all voxel-order/record-offset/cell-centered combinations and uniform, rotated affine and nonlinear-frustum transforms, including negative half-voxel and coarse-bin boundaries.

Every stored output and attribute leaf value/activity is recorded, with root/leaf/active-tile counts, class/name and transform probes. Topology, attribute values, counters and partition indices/offsets are exact. Floating values use 3e-6 float and 3e-12 double relative/absolute tolerances. Twenty-five independent tests cover Euclidean SDF bands, strict mask boundaries, sparse tiles, custom block attributes, tie rules, SetTree/accessor effects, radius counters, immutable references, progress and budgets, cancellation, convenience operations, atomic partition construction and coordinate limits.

Fixture: `tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-particles.json.gz`.

- Native profiles: 468 (396 rasterization, 72 partitioning).
- Bytes: 6,375,707.
- SHA256: `55A3426E6DA3D158B741FC7C21922309CC2A85B879635B25F7936B1E4C274B62`.

`Build-ParticlesReference.ps1` verifies the upstream commit and generates deterministic timestamp-free gzip with GZipStream SmallestSize compression. OpenVDB/TBB are used only for fixture generation. The managed package has no native OpenVDB dependency.

Release verification passed all 493 tests (396 native particle profiles, 72 native partition profiles, 25 independent tests) on .NET SDK 11.0.100-preview.7.26381.103. The boundary cases also verified the shared Mat4d.Inverse correction to native expanded determinant/cofactor evaluation order. See `reference/particles-tests.log`.

Debug and scalar-fallback gates also passed 493/493 each. Scalar verification used DOTNET_EnableHWIntrinsic=0. Logs: `reference/particles-debug-tests.log` and `reference/particles-scalar-tests.log`.
