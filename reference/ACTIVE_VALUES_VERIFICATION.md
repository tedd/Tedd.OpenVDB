# Regional active values and extrema verification

Pinned source: OpenVDB 13.0.1, revision f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf.

FindActiveValues.cs ports FindActiveValues.h queries, clipped active-tile collection and cached counting. Count.cs and Count.Extrema.cs port Count.h activity counts and serial top-down extrema. C++ object memory-usage estimates are not exposed as managed heap measurements.

The active-values oracle contains 567 profiles spanning seven independently constructed scenes and 81 regions each: empty and sparse leaves, dense leaves, all tile levels, negative coordinates, partially intersected leaves/tiles, a 4096-cubed active region with one inactive voxel, mixed fills and holes, and deterministic scattered values. Assertions compare any/no activity, separate voxel/tile activity, both count implementations, full-tree counts, scalar extrema and exact clipped tile-list order, levels, values and states. The extrema oracle contains 91 profiles across Boolean, int, float, double and all Vec2/3/4 integer/float/double value types. It includes empty activity, inactive values, lexicographic ordering, infinities and NaNs.

Build-ActiveValuesReference.ps1 and Build-CountExtremaReference.ps1 build isolated native executables against the pinned static library. The former was regenerated end to end after enabling gzip output. Native execution fixes TBB concurrency at one worker. Fixture SHA-256:

- openvdb-13.0.1-active-values.json.gz: C15B9796875F7BFB983109CD2EF7934A845398FBF534B5F0E9EE4687617AD0F1
- openvdb-13.0.1-count-extrema.json: B611B37A24B2268A3214C26829D35A8156FE76975DB53579F5ED434C33B437B7

There are 674 dedicated tests: 658 native profiles and 16 independent cases. Independent coverage includes signed coordinate endpoints, reversed bounds, topology-cache invalidation, active false values, zero-allocation warmed cached queries, parallel read-only use, signed zero, NaN-aware component assertions and brute-force region counts.

## Pinned upstream behavior

FindActiveValues.Count intentionally retains two native defects. Its partial-leaf containment shortcut can return the whole leaf count for a smaller query. Its TBB internal-node reductions infer Int32 from the integer zero identity despite UInt64 body results. Each node's child and tile sums narrow separately. A fully active root-sized region with one inactive voxel consequently returns UInt64.MaxValue from the native cached count. CountActiveValues and Count.ActiveVoxels(region) use accurate checked UInt64 accounting, matching the native standalone function.

Vector extrema use the native lexicographic cwiseLess/cwiseGreater predicates and retain whole vectors. The first encountered active value initializes both extrema; subsequent unordered NaN comparisons do not replace it. Empty activity returns zero/zero irrespective of background.

Cached queries require Update after any tree mutation. Managed structural version checks catch topology changes; they are not a general modification detector. An unchanged tree supports parallel queries, but the accessor shortcut requires one finder per worker. Cached nodes and masks remain normal managed references. No GC relocation bypass is used.

Empty or reversed queries return no activity. Midpoint arithmetic widens before addition, avoiding native signed overflow. Accurate count overflow throws rather than wrapping.