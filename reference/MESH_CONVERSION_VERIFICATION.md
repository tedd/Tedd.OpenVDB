# Mesh conversion verification

Reference: OpenVDB 13.0.1 at f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf, tools/MeshToVolume.h.
All fixtures are produced by the pinned C++ implementation with one TBB worker. Runtime code is managed C#.

## Native comparisons

| Fixture | Profiles | SHA-256 |
|---|---:|---|
| openvdb-13.0.1-mesh-to-volume.json | 148 | 781BE6A7AA1D07951BE43665FF6FAC357B0D0927A5750147B69001D4CB651ADE |
| openvdb-13.0.1-mesh-helpers.json | 120 | AF8BC54F755D29FA901F702B67473F84792118A1D422F005627BC2066FC47434 |
| openvdb-13.0.1-mesh-edge.json | 54 | 94D956B3D8C7EEDB89B458D4C2A1E8F56F7A69F204E2ED81EFEBC148A2E386C1 |

Distance profiles include float/double, signed/unsigned fields, asymmetric and infinite bands, all conversion flags, closed/open/reversed/overlapping/degenerate meshes, triangle subdivision, exact lattice alignment, and both interior strategies. Compare every allocated leaf value and active state, polygon index, leaf count, active count, background, class, and sampled tile/depth behavior. Float tolerance is relative/absolute 3e-6; double tolerance is 3e-12. Polygon indices and topology match exactly.

Helper profiles cover world-space conversion, empty conveniences, transformed boxes, nonuniform transforms, zero/negative/nonfinite widths and native pruning exceptions. Eight float/double exterior-tracing profiles compare every allocated voxel, sparse probes, retained activity and unaffected tiles.

Edge profiles include native float arithmetic, triangle/quad splitting, repeated polygons, negative/large coordinates, tiny meshes, exact edge endpoints, cyclic axis permutations and native append/order behavior. Every allocated leaf's three distances, primitive indices and activity match exactly. Cell intersection positions and primitive order also match exactly.

Independent tests cover analytic cube distances, native outer-band coverage, geometry validation, cancellation, allocation/evaluation limits, cache invalidation, copy isolation, callback execution, metadata construction, default edge values and triangle endpoint rules.

## Native behavior retained

- Exterior tracing uses the native 0.75 squared-distance boundary and Z/Y/X sweep order, then an equivalent six-connected queue closure over allocated leaves.
- Native narrow-band expansion does not guarantee every voxel with Euclidean distance below the requested width is active. Propagation requires distance + voxelSize < width. Integer-aligned cube corners expose this outermost-layer limitation.
- World convenience methods convert double transformed points back to float before conversion. Empty point lists return unclassified identity grids with the unscaled requested background.
- The box convenience transforms only the two opposite world-space bounds to float index-space points before constructing its eight corners.
- The pinned no-interrupter meshToVolume overload silently drops its interior-test arguments. Managed callbacks map to the explicit native interrupter overload, which forwards them. Fixtures invoke that overload for callback profiles.
- EVAL_EVERY_TILE retains the pinned leaf-boundary predicate. Renormalization retains native scalar rounding, Godunov updates and epsilon offsets.
- Zero interior widths and negative widths can reach the native pruning error; the managed equivalent throws ArgumentOutOfRangeException. Nonfinite exterior widths/NaN interior widths instead return the native empty maximum-background grid.
- Edge distances are narrowed to float after double intersections. Segments include the origin and exclude time 1. Equality compares primitive indices only, as native EdgeData does.
- The managed implementation validates coordinates and resources, throws cancellation between safe stages and runs deterministic primitive traversal. It does not reproduce C++ out-of-bounds access or signed overflow. MeshToVolume may publish intermediate polygon-index data on failure; MeshToVoxelEdgeData replaces previous data only after successful conversion.

## Reproduction

Run Build-MeshToVolumeReference.ps1, Build-MeshHelpersReference.ps1 and Build-MeshEdgeReference.ps1 with -OpenVdbSource, -OpenVdbBuild and -TbbRoot pointing at the pinned source, its static Release build and oneTBB. Optional -CMake selects the installed CMake binary. The scripts verify the source commit, compile their independent CMake projects, generate temporary JSON and validate provenance before replacing each fixture.

Run managed tests with filter FullyQualifiedName~MeshToVolume|FullyQualifiedName~MeshHelpers|FullyQualifiedName~MeshEdge.

The combined managed mesh conversion/helper/edge suite passes 353 tests in Release, Debug and scalar-only Release.
