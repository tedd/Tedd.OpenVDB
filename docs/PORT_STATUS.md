# Port status and compatibility

This repository is **not yet the requested complete 1:1 port**. It provides an executable,
tested managed storage core. Unsupported components have no placeholder implementations
and are not silently emulated by a native wrapper.

## Implemented surface

| Upstream area | Managed implementation | Limits |
|---|---|---|
| `math/Coord.h` | Coord, inclusive CoordBBox, enumeration, alignment, overflow-aware volume | .NET exceptions replace undefined numeric casts/overflow in checked APIs |
| `util/NodeMasks.h` | activity masks, Boolean operations, counts, bit iteration | runtime log2 dimensions 1–5 |
| `tree/LeafNode.h` | values, independent activity, fill/clip, copy, exact/numeric-tolerance constants, topology | fixed log2 dimension 3; no out-of-core buffers |
| `tree/InternalNode.h`, `RootNode.h`, `Tree.h` | Tree4 hierarchy, values/tiles, sparse fill, materialization, pruning, counts/bounds, dense transfers, clipping, enumeration | fixed standard 5/4/3 shape; `T : unmanaged`; internal-node representation adapted to managed GC |
| `tree/ValueAccessor.h` | cached reads/writes with automatic structural invalidation | leaf cache; no native multi-level template variants or concurrent mutation |
| Tree topology | union with preserveTiles, intersection, difference, structural comparison | native branch-deletion semantics retained |
| Tree combination | sparse combine, copying combine, explicit activity callback | managed callbacks and transactional publication; no native node stealing |
| `io/Archive`, `File`, descriptors, raw node serialization | format 225 single uncompressed Int32/Float/Double grid read/write; supported linear maps and metadata | no compression, half storage, multiple grids, instances, nonlinear maps, unknown/file-level metadata or delayed loading |
| `Grid.h`, metadata | typed grid, name/class, immutable typed metadata, independent copy | limited typed metadata, no dynamic grid registry |
| `math/Vec3.h`, `Mat4.h`, `Maps.h`, `Transform.h` | Vec3d, Mat4d, affine scale/rotation/shear/translation, inversion, batch transforms | only double vectors/matrices and affine maps |
| `math/BBox.h`, `Ray.h` | BBoxd and Rayd intersections/transforms | double precision subset |
| `math/DDA.h` | voxel/node DDA with source tie ordering | runtime log2 dimensions 0–12; no hierarchical level-set/volume DDA |
| `tools/Interpolation.h` | point, trilinear box, quadratic samplers | scalar floating-point generic types |
| `tools/Composite.h` | numeric min/max/sum/multiply and active replacement | no signed-distance CSG, vector composites or division |

The library is new code derived from the pinned upstream. The old C++/CLI wrappers
are not used by the managed build, tests, package or samples.

## Required work before claiming a complete port

1. Complete the native `.vdb` codec family: compression, half storage, multiple grids,
   instances, unknown/file-level metadata, nonlinear maps, legacy formats and delayed loading.
   Expand native-generated cross-language coverage to every supported type and option.
2. Configurable tree shapes; mask/bool/string/vector/specialized leaf types; full root,
   internal, leaf, iterator, manager, merge, combine and tolerance API coverage.
3. Full math family (vector/matrix types, quaternions, nonlinear/frustum maps,
   stencils, finite differences, solvers and complete hierarchical traversal).
4. The remaining volume tool algorithms, including level-set creation and evolution,
   CSG, filtering, morphology, meshing, particles, ray tracing and resampling.
5. Point grids, attributes, codecs, point tools and their substantial test suite.
6. Decide and document the requested .NET mapping of AX, NanoVDB and the platform
   integrations before claiming parity with the entire upstream repository.
7. Port the remaining applicable upstream tests and add native differential/round-trip
   coverage beyond the Int32 sparse-tree fixture family.

## Deliberate managed differences

* C# naming, generic constraints and callbacks replace C++ templates and functors.
* Internal nodes store tile values contiguously and child references sparsely instead
  of an overlapping C++ value/pointer union. Child and activity masks remain disjoint.
* Exact pruning uses IEEE equality for floating-point values; NaN is not treated as a
  uniform constant. Native approximate equality can treat NaN comparisons as uniform.
  Numeric leaf tolerance queries are implemented; tree-wide tolerance pruning remains unported.
* Public validation throws for invalid buffers/indices and undefined numeric casts.
  CoordBBox supports exact `UInt128` full-domain volume; `ulong` volume/count APIs throw
  on overflow rather than wrap. No finite allocation can materialize the entire domain.
* Fill loops use 64-bit counters at signed coordinate endpoints. The native oracle's
  `int.MaxValue` box-fill case did not terminate and was excluded from native fixtures;
  managed endpoint behavior has independent tests. Native min/max point cases remain.
* Tree counts and bounds are computed from the hierarchy. Iteration and aggregate
  operations prioritize correctness; not every native parallel implementation is ported.
* Combine publishes a new result after callback completion, then empties the second
  tree; a throwing callback leaves both inputs intact. Callbacks must not mutate inputs.
* `.vdb` compatibility is limited to the implemented format-225 subset. No C++ ABI
  compatibility, Linux execution results, thread-safe mutation, or full API parity is claimed.

Tests distinguish source-derived cases, independent dense-reference models, and
fixtures actually executed by native OpenVDB. See `reference/` for provenance.

The voxel sample was successfully published and executed with .NET 11 NativeAOT on Windows x64. This validates that sample, not every generic type or library operation under AOT.



## Validation of this checkpoint

* 600 xUnit tests passed in the integrated Release run (the final target run may add
  review cases; see VALIDATION.md for final totals).
* Native sparse-tree oracle: 222 cases, 1,235 operation checkpoints, 99,434 coordinate
  samples, reproduced from the pinned upstream build.
* Native I/O oracle: seven upstream-written fixtures were read by the managed codec;
  seven managed-written files were independently read by OpenVDB. Verification covers
  every stored tile/voxel, value, active state, depth, topology, counts, metadata and
  affine transform, including streaming output and exceptional floating-point values.
* Invalid headers, lengths, offsets, masks, metadata, unsupported encodings, truncation,
  resource limits and stream ownership have explicit tests.
