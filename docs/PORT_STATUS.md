# Port status and compatibility

The requested full 1:1 port is **not complete**. This repository contains a managed implementation with extensive independent native conformance fixtures, rather than a native wrapper or placeholder APIs. The latest upstream reference is OpenVDB 13.0.1, revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`.

## Implemented surface

| Area | Implemented | Remaining limits |
|---|---|---|
| Standard tree | Tree4 5/4/3 hierarchy, tile/voxel states, sparse/dense fill, clip, topology operations, combine, three merge policies, background reset, leaf transfer, counts/bounds | Fixed dimensions, unmanaged values; complete configurable/specialized tree family absent |
| Traversal and managers | Read enumeration, mutable stored-value handles with depth limits, accessor cache, leaf manager buffers, cached/dynamic node managers, scoped parallel callbacks and reductions | Complete iterator overloads and arbitrary concurrent mutation absent |
| Masks | SIMD node masks, bit iteration/counts, restricted distinct MaskGrid API | MaskGrid currently uses Boolean backing arrays, not complete native bit-packed MaskTree storage/API |
| Native archives | Format 225, Int32/Int64/Float/Double/Bool/Vec3i/Vec3s/Vec3d/Mask, heterogeneous grids, backward instances, metadata, affine/frustum maps, zlib/active masks/Blosc-LZ4, real half encoding | Other compressed Blosc codecs/bitshuffle, forward instances, legacy/custom/point formats, partial/delayed/mapped I/O absent |
| Grid and metadata | Typed and heterogeneous grid interfaces, tree sharing/deep copy, name/class, immutable scalar/vector/matrix/opaque metadata | Full dynamic registry, every metadata/math type and grid convenience API absent |
| Math | Coord/boxes, Vec2d/Vec3d/Vec3f/Vec3i/Vec4d, Mat3d/Mat4d, quaternions, affine/frustum/camera transforms, ray and DDA | Complete generic precision family, map API, decomposition, hierarchical traversal and solvers absent |
| Numerical kernels | All native first/second finite-difference schemes, WENO5, Godunov norm, gradient/WENO/curvature stencils, scalar/vector index/world operators, eight grid differential/value operations | Complete solver and numerical utility families remain |
| Volume topology | Exact/tolerance/inactive/level-set pruning, signed flood fill, dilation/erosion with 6/18/26 neighbors and Ignore/Expand/Preserve tile policies | Complete tools family remains |
| Level sets and composites | Float/Double sphere generation, mutating/copy CSG, scalar/vector arithmetic, active replacement/leaf transfer, interior/isosurface masks, fog conversion, active/SDF segmentation, area/volume/curvature measurement, reinitialization/tracking/resize and level-set filters | Surface rebuild/general evolution, general surface construction, meshing and simulation remain |
| Sampling and filtering | Scalar/vector point/box/quadratic, staggered Vec3, world/dual-grid sampling; floating mean/Gaussian/median/offset filters, transformed alpha masks and adaptive tile refinement | Resampling and transform tools remain |
| Dense and statistics | XYZ/ZYX dense memory, sparse conversion with tolerance, weighted extrema/mean/variance/histograms | Complete overload/parallel/reduction and value-conversion families remain |

## Remaining major work

1. Finish tree configurability, specialized storage, iterators/managers, overloads and complete grid/metadata APIs.
2. Finish archive compression/version/type families, partial reading and delayed/mapped storage.
3. Complete generic math/maps, specialized stencils, remaining numerical operators and solvers.
4. Port remaining volume tools: mesh conversion, level-set rebuilding/evolution, particles, advection, remaining segmentation/conversion APIs, ray tracing, resampling and diagnostics.
5. Port point-data/index grids, attributes, groups, codecs and point tools.
6. Translate the remaining applicable native tests and expand differential coverage for each added module.
7. AX, NanoVDB, Python and host-product integrations remain separate unported upstream components. No parity with those components is claimed.

## Managed contracts and deliberate differences

- C# names, generic constraints and functional callbacks replace C++ templates and functors. Functional point modifiers invoke their callback once and publish after it succeeds; native functors may be evaluated at multiple hierarchy levels.
- Unmanaged value arrays and sparse child dictionaries replace native value/pointer unions. Child and activity masks are disjoint. Managed references are never stored as raw pointers across GC safepoints.
- Root structural emptiness is distinct from activity. An inactive leaf still makes a tree nonempty. `HasActiveVoxels` is the activity-only query.
- Public bounds and sizes are validated. Inclusive coordinate iteration uses 64-bit counters. Counts throw on overflow; CoordBBox also exposes UInt128 volume. Undefined native casts and signed-overflow loops are not reproduced.
- Exact core pruning retains NaN-valued nodes. Numeric uniformity queries reject NaN; native approximation may treat it as equal. Tools pruning uses the native approximate root-background predicate. Signed background reset follows native scalar/vector predicates, including their NaN behavior.
- Dense import follows upstream's existing behavior: wholly inactive constant blocks are skipped rather than replacing existing blocks. Inherited values outside a partially imported leaf are preserved.
- Mutable iterator `SetValue` preserves activity in the pinned implementation, despite upstream comments saying otherwise. Iterator `ModifyValue` activates leaf voxels but preserves tile activity. These rules have native tests.
- CSG steals selected nodes; it does not guarantee an empty second tree. Copy variants with unequal backgrounds match deterministic serial native execution; native parallel partitioning can normalize inactive backgrounds differently.
- Filters retain native X/Z/Y pass order and float averaging coefficients even for DoubleGrid. At the signed coordinate-domain edge, samples outside Int32 use background rather than invoke undefined native overflow.
- Combine is transactional with respect to callback failure. Other mutable operations are not globally transactional. Callbacks must not mutate tree topology or concurrently modify input trees.
- Leaf/static node managers and tree-wide value handles reject structural staleness. Node-local traversal validates direct node structure and attachment; unrelated sibling edits preserve those handles. Dynamic callbacks may modify their own descendants and use independent state in parallel. Existing live leaf references can become detached. Concurrent reads require an otherwise unmodified tree; each thread needs its own accessor.
- Level-set tracking follows native spatial/Runge-Kutta schemes, trimming/background rules and filter CFL stages. Cancellation throws between safe stages; completed stages can persist. Transformed self-mask offset runs serially to preserve native leaf-order behavior without a data race.
- Half interpolation promotes subtraction to float and narrows double intermediates through float before Half, matching the native Half constructor. Grid samplers capture tree/transform references; integer samples bypass interpolation, and dual-grid alignment is cached once.
- Histogram bounds reject nonfinite/degenerate explicit ranges instead of reproducing undefined native bin-index conversion. Count overflow is checked.

## Evidence

Native fixtures cover storage mutations, all merge policies, ordered iterators, dense conversion, filters, volume topology, level-set spheres, scalar/vector composites, math, finite differences, statistics and file interchange. They do not establish completeness of the whole upstream library. Reproduction scripts and exact limits are in `reference/`; current integrated results are in [VALIDATION.md](VALIDATION.md).
