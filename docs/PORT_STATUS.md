# Port status and compatibility

The requested full 1:1 port is **not complete**. This repository contains a managed implementation with extensive independent native conformance fixtures, rather than a native wrapper or placeholder APIs. The latest upstream reference is OpenVDB 13.0.1, revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`.

## Implemented surface

| Area | Implemented | Remaining limits |
|---|---|---|
| Standard tree | Tree4 5/4/3 hierarchy, tile/voxel states, sparse/dense fill, clip, topology operations, combine, three merge policies, background reset, leaf transfer, counts/bounds | Fixed dimensions, unmanaged values; complete configurable/specialized tree family absent |
| Traversal and managers | Read enumeration, mutable stored-value handles with depth limits, accessor cache, leaf manager buffers, cached/dynamic node managers, scoped parallel callbacks and reductions | Complete iterator overloads and arbitrary concurrent mutation absent |
| Masks | SIMD node masks, bit iteration/counts, restricted distinct MaskGrid API | MaskGrid currently uses Boolean backing arrays, not complete native bit-packed MaskTree storage/API |
| Native archives | Format 225, scalar/Bool/Vec3/Mask grids, heterogeneous archives, forward/backward instances, metadata, affine/frustum maps, active masks, zlib and all c-blosc codec IDs (BloscLZ/LZ4/Snappy/zlib/Zstd), byte/bitshuffle, half encoding | Dictionary/legacy Zstd, legacy/custom/point formats, partial/delayed/mapped I/O absent |
| Grid and metadata | Typed and heterogeneous grids, tree sharing/deep copy, name/class, immutable scalar/Half/Vec2/Vec3/Vec4/Mat4/opaque metadata, registered unmanaged custom codecs, MetaMap mutation/copy semantics and vector-space metadata | Arbitrary reference-valued custom metadata and remaining grid convenience APIs remain |
| Math | Coord/boxes, native vector/matrix storage types, expanded double math, quaternions, native affine/scale/translation/unitary map identities, frustum/camera transforms, proximity, quantized unit vectors, ray/DDA and hierarchical traversal | Complete generic precision family and remaining advanced math utilities remain |
| Numerical kernels | All native first/second finite differences, WENO5/Godunov, stencils, scalar/vector index/world operators, grid differential operations, float/double PCG with Jacobi/IC, sparse Poisson assembly/boundary conditions/solves | Remaining numerical utility/solver families remain |
| Volume topology | Exact/tolerance/inactive/level-set pruning, signed flood fill, dilation/erosion with 6/18/26 neighbors and Ignore/Expand/Preserve tile policies | Complete tools family remains |
| Level sets and composites | Sphere generation, CSG/composites, masks/fog/segmentation, measurements, tracking/filtering, triangle/quad mesh conversion, adaptive volume meshing with reference seams, rebuilding, edge intersections, box construction, Platonic solids, topology-to-level-set conversion, level-set advection, morphing and fracture | Remaining surface generators, particles and simulation remain |
| Sampling and filtering | Scalar/vector point/box/quadratic and staggered sampling, affine/mipmap/nonlinear resampling with level-set rebuild dispatch, mean/Gaussian/median/offset filters, transformed alpha masks, velocity fields and Runge-Kutta integration, scalar/vector volume advection with correction limiters | Remaining overload and value-specialization families remain |
| Dense, queries and statistics | XYZ/ZYX dense memory, sparse conversion, cached regional activity queries, scalar/vector extrema, weighted mean/variance/histograms | Native memory-usage reporting, complete overload/parallel/reduction and value-conversion families remain |
| Multiresolution | Float/double LOD trees, weighted/injection restriction, prolongation, fractional grids and sampling, native copy/steal and metadata semantics | Configurable tree and other value specializations remain |

## Remaining major work

1. Finish tree configurability, specialized storage, iterators/managers, overloads and complete grid/metadata APIs.
2. Finish archive compression/version/type families, partial reading and delayed/mapped storage.
3. Complete generic math/maps, specialized stencils, remaining numerical operators and solvers.
4. Port remaining volume tools: particles, remaining surface/segmentation/conversion APIs, rendering and diagnostics.
5. Port point-data/index grids, attributes, groups, codecs and point tools.
6. Translate the remaining applicable native tests and expand differential coverage for each added module.
7. AX, NanoVDB, Python and host-product integrations remain separate unported upstream components. No parity with those components is claimed.

## Managed contracts and deliberate differences

- C# names, generic constraints and functional callbacks replace C++ templates and functors. Functional point modifiers invoke their callback once and publish after it succeeds; native functors may be evaluated at multiple hierarchy levels.
- Unmanaged value arrays and sparse child dictionaries replace native value/pointer unions. Child and activity masks are disjoint. Managed references are never stored as raw pointers across GC safepoints.
- Root structural emptiness is distinct from activity. An inactive leaf still makes a tree nonempty. `HasActiveVoxels` is the activity-only query.
- Public bounds and sizes are validated. Inclusive coordinate iteration uses 64-bit counters. Accurate counts throw on overflow; CoordBBox also exposes UInt128 volume. The explicitly compatible cached FindActiveValues.Count retains the pinned native narrowing defects. Undefined native casts and signed-overflow loops are not reproduced.
- Exact core pruning retains NaN-valued nodes. Numeric uniformity queries reject NaN; native approximation may treat it as equal. Tools pruning uses the native approximate root-background predicate. Signed background reset follows native scalar/vector predicates, including their NaN behavior.
- Dense import follows upstream's existing behavior: wholly inactive constant blocks are skipped rather than replacing existing blocks. Inherited values outside a partially imported leaf are preserved.
- Mutable iterator `SetValue` preserves activity in the pinned implementation, despite upstream comments saying otherwise. Iterator `ModifyValue` activates leaf voxels but preserves tile activity. These rules have native tests.
- CSG steals selected nodes; it does not guarantee an empty second tree. Copy variants with unequal backgrounds match deterministic serial native execution; native parallel partitioning can normalize inactive backgrounds differently.
- Filters retain native X/Z/Y pass order and float averaging coefficients even for DoubleGrid. At the signed coordinate-domain edge, samples outside Int32 use background rather than invoke undefined native overflow.
- Combine is transactional with respect to callback failure. Other mutable operations are not globally transactional. Callbacks must not mutate tree topology or concurrently modify input trees.
- Leaf/static node managers and tree-wide value handles reject structural staleness. Node-local traversal validates direct node structure and attachment; unrelated sibling edits preserve those handles. Dynamic callbacks may modify their own descendants and use independent state in parallel. Existing live leaf references can become detached. Concurrent reads require an otherwise unmodified tree; each thread needs its own accessor.
- Level-set tracking follows native spatial/Runge-Kutta schemes, trimming/background rules and filter CFL stages. Cancellation throws between safe stages; completed stages can persist. Transformed self-mask offset runs serially to preserve native leaf-order behavior without a data race.
- Half interpolation promotes subtraction to float and narrows double intermediates through float before Half, matching the native Half constructor. Grid samplers capture tree/transform references; integer samples bypass interpolation, and dual-grid alignment is cached once.
- Volume rays visit allocated leaves and active tiles; an inactive allocated leaf can produce a hit. Level-set rays interpolate active narrow-band values. Copies have independent query state, and source level-set grids must remain unchanged. Hits retains short spans that March rejects under the native ray-validity tolerance. Undefined coordinate overflow is not reproduced; degenerate flat-zero linear searches can return a native indeterminate NaN time.
- PCG retains native float/double arithmetic, ordered reductions, convergence rules and four-entry incomplete-Cholesky factors. Managed bounds/alias validation and cancellation replace undefined indexing and native interrupters. Parallel independent scaling uses SIMD; dot-product reductions preserve native order.
- Histogram bounds reject nonfinite/degenerate explicit ranges instead of reproducing undefined native bin-index conversion. Count overflow is checked.

## Evidence

Native fixtures cover storage mutations, all merge policies, ordered iterators, dense conversion, filters, volume topology, level-set spheres, scalar/vector composites, math, finite differences, statistics and file interchange. They do not establish completeness of the whole upstream library. Reproduction scripts and exact limits are in `reference/`; current integrated results are in [VALIDATION.md](VALIDATION.md).


## Further native behavior retained

- Triangle/quad mesh conversion retains native float narrowing, exact polygon indices, signed-distance stages, band expansion and native width failures. Interior callbacks map to the native explicit-interrupter overload because the pinned convenience overload discards its callback arguments.
- Native mesh expansion can leave some corner voxels inactive in the outermost band layer. The box helper transforms two opposite corners before constructing its index-space box. Edge intersections use float distances, include time zero and exclude time one.
- Surface extraction supports native adaptive regions, optional masks, reference-surface seams, cache reuse and polygon/point flags. Rebuilding uses meshed float positions and native polygon order, disables conversion renormalization, and preserves resampling destination metadata.
- Poisson assembly retains native collocated distance-two stencils without an extra quarter factor, staggered distance-one stencils, boundary-callback order and double solver arithmetic. Native failed-convergence results remain available to callers.
- Quantized unit vectors retain the native lookup table, bit packing, normalization and sign encoding. Native half/vector/matrix metadata payloads and truth conversion are preserved.

- Regional queries retain clipped tile ordering and separate active leaf voxels from tiles. FindActiveValues.Count preserves native partial-leaf overcounting and Int32 reduction narrowing; CountActiveValues performs accurate checked accounting. Cached query objects require Update after any mutation.
- Multiresolution restriction/prolongation preserve native summation and fractional float coefficients. Integer grid views share trees, while fractional-grid factories copy. Materialization has explicit voxel/leaf budgets.
- Vector transformation uses native covariant/contravariant and local/world metadata, visits all stored values, and preserves the implicit root background. Integer casts outside the defined range throw.
- Native transform map identity and serialized derived caches are retained. Copies and pre/post operations follow native map simplification and specialization rules. Precision-sensitive velocity boundary fixtures compare specialized maps directly.