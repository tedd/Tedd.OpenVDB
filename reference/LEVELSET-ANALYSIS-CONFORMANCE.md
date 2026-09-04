# Level-set analysis conformance

The managed level-set utilities, component segmentation and measurements derive from `openvdb/tools/LevelSetUtil.h` and `openvdb/tools/LevelSetMeasure.h` at OpenVDB revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` (13.0.1).

`Build-LevelSetAnalysisReference.ps1` compiles the independent C++ oracle in `levelset-analysis-oracle.cpp` against the pinned static OpenVDB library. TBB is limited to one worker for reproducible reduction and component ordering. The production .NET library has no OpenVDB or TBB runtime dependency.

The fixture `openvdb-13.0.1-levelset-analysis.json` contains 154 mask, fog and segmentation cases, 183 output grids and 225,822 value/activity/depth samples, plus six float/double measurement cases with 13 geometric results each. Its SHA-256 is `690F3CBD8B5DA32465ABA6BDE1E7D951BCFAF89F2FDC5EFFDF3974D6E41A7975`.

The native cases include empty trees, inactive stored interiors, three tile levels, disjoint root regions, crossing leaf boundaries, face/edge connectivity, finite and NaN values, zero and shallow fog cutoffs, offset spheres, disconnected spheres and asymmetric narrow bands. Checks cover background, active counts, leaf/root/tile topology, sampled values, activity, depth, grid class and metadata. Independent tests additionally check analytic sphere geometry, input/output aliasing, cutoff singularities, generic block payloads, bool payloads, coordinate limits, allocation limits and seeded component searches.

## Preserved source behavior

- Interior masks classify all stored leaf values, including inactive values, using a strict comparison and a background-relative isovalue clamp. Output paths are seeded only by interior leaf voxels. A negative tile in a path without such a leaf does not independently create an interior mask.
- Isosurface masks consider active leaf voxels only, comparing their signs with six face neighbors. Inactive neighbor values participate; active tiles are not candidates.
- Fog conversion steals leaves from the original tree, converts them and replaces the grid's tree. Existing references to the original tree observe leaf removal, while retained leaf references observe converted data. Metadata and transforms remain attached to the converted grid.
- The native fog formula does not saturate leaf densities at one. A shallow cutoff can yield larger densities; a zero cutoff can yield infinity and NaN. Negative root tiles are explicitly retained as active unit-density tiles, while tile-only internal paths are not independently seeded.
- Segmentation mask payloads are false; activity records membership. Connectivity uses six faces. Active tiles are expanded for component discovery and restored to their original depth by active-value segment extraction. `maximumNewLeaves` exposes a managed allocation budget before expansion.
- No active components produces an empty mask list, but `SegmentActiveVoxels` and `SegmentSdf` produce one empty tree retaining the input background. A single active-voxel component deep-copies all inactive input data. Multiple components copy only the selected active values. Grid segments inherit metadata and an independent transform; mask grids inherit only the transform.
- SDF segments start from zero-crossing components and expand only across strictly increasing absolute distance in active leaves. Existing signed flood filling restores asymmetric interior/exterior bands. Segment order follows decreasing crossing-component size, as in the source; it is not recomputed after expansion. Equal-size managed components use deterministic coordinate order, whereas native tie order is unspecified.
- Measurements integrate active leaf voxels, using the native compact cosine Dirac delta, scalar-rounded stencil gradients and sorted per-leaf double reductions. Results are cached until `Init`. Tiles do not contribute. A structurally nonempty tree containing only inactive tiles therefore has zero integrals. Float and double are supported; Half is rejected like the native floating-point type guard.

## Boundaries and remaining dependencies

Coordinate-neighbor traversal stops at the signed 32-bit coordinate limits rather than overflowing native signed integers. Measurements are serial, and no native interrupter/grain-size interface is exposed in this batch. There is no mutation tracking for cached measurements: callers reinitialize after changing the grid.

`ExtractEnclosedRegion` and `DistanceFieldToSdf` remain dependent on mesh-volume boundary tracing and level-set normalization. Global `ClosestSurfacePoint` from `VolumeToSpheres.h` depends on the surface extraction and auxiliary topology in `VolumeToMesh.h`; it is not approximated by these utilities. Local closest-point stencils and closest-point grid transforms are supplied by the differential-operator module.
