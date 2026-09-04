# Platonic solids and topology-to-level-set conformance

Source: OpenVDB 13.0.1 revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, `tools/LevelSetPlatonic.h` and `tools/TopologyToLevelSet.h`.

`Build-LevelSetSolidsReference.ps1` verifies the source revision and builds the isolated native oracle. Native operations run with one TBB worker. `Fixtures/openvdb-13.0.1-level-set-solids.json.gz` contains **144 native profiles** with exact run-length-encoded leaf values and active bits, root/tile counts, value-depth probes and transform samples. The timestamp-free gzip fixture is **583,501 bytes**; SHA-256 is **9067117A7B2964AA5AE2ECCC652DF54A8A1A374CB95DCAF6B01827A646F3707A**.

The 70 platonic profiles exercise float/double tetrahedra, cubes, octahedra, dodecahedra and icosahedra with native defaults, translated centers, small/wide bands, negative/zero scale, zero width and negative voxel size. Ten zero-width profiles capture the native `ValueError` from level-set pruning; managed code maps it to `ArgumentOutOfRangeException`. These cases are recorded as exceptions, not omitted. Negative voxel sizes succeed and their mirrored index-space results are compared. Vertex constants, triangle/quad ordering and float scaling are copied from the pinned source; conversion uses the native renormalizing mesh-to-level-set convenience path.

The 74 topology profiles use boolean and integer grids, including active false values, nonzero backgrounds, inactive stored values, boxes, single voxels, one-voxel gaps, shell topology, active/inactive leaf tiles and 128-cubed internal tiles. They exercise closing, dilation, smoothing, parameter clamping, scaled/rotated/translated uniform transforms and empty topology. Both input types produce float level sets. Every stored output voxel and active bit is verified; float values use relative tolerance `3e-6`, double solid values `3e-12`, with exact topology.

Twenty-three independent tests cover named solid entry points, analytic cube side length, finite signed bands, invalid face counts, Half rejection, generic ushort/bool/custom block records, parameter clamping, gap closure, dilation displacement, empty grids, source aliases/accessors, metadata and transform ownership, smoothing, scale covariance, nonuniform/frustum rejection, cancellation, voxelization limits and integer overflow.

## Topology conversion contract

* The input's active states define the interface; stored values and grid class are ignored. Root/internal/leaf topology is copied sparsely, including stored inactive structure before native pruning.
* Half-width is clamped to one or greater; negative closing/dilation counts become zero. Only uniform voxel transforms are accepted.
* Native six-neighbor, preserve-tile closing and dilation are retained. Background uses `float(voxelSize) * float(halfWidth)`. Erosion defines the inactive interior; active tiles are voxelized before level-set pruning.
* Distance propagation uses the existing native first-order biased normalization with `3 * halfWidth` iterations. Optional smoothing deep-copies the intermediate grid, applies tracked mean filters, takes the native minimum against the filtered values minus half a world-space voxel, and normalizes again.
* Results own their tree and a transform copy and receive level-set classification. Input metadata is not copied. Caller-owned inputs must remain stable while conversion runs.

`maximumNewLeaves` bounds explicit active-tile voxelization through the existing tree API. It does not cap preexisting input leaves, morphological work or PDE iterations. Cancellation is observed between morphology stages and within buffered normalization/filter stages; input trees remain untouched. Arithmetic that would invoke signed native iteration overflow is rejected explicitly. Native parallel mask/field operations are sequenced in the managed orchestrator; independent leaf filter work can still run in parallel.
