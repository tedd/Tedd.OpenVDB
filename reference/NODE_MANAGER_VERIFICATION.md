# Node-manager conformance

Reference: OpenVDB 13.0.1, commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, Windows x64 MSVC.

The fixture covers 288 native cases: eight tree configurations, zero through three cached levels, top-down/bottom-up traversal, direct value mutation, dynamic child creation and subtree filtering. Every visited node compares level, origin, child count, direct value/activity counts and an ordered hash of all direct coordinates, values and activity. Final topology and active voxel counts are exact. Native callbacks run serially for deterministic order; separate managed tests verify parallel level barriers and reductions.

`NodeManager<T>` snapshots the top requested levels below the root. Counts exclude the root but callbacks include it. `DynamicNodeManager<T>` discovers children after their parent callback and assigns indices within each accepted level. Parallel callbacks must use independent state and remain within their own subtree; this is not arbitrary concurrent tree mutation.

Node-local handles survive sibling/descendant edits when their own direct table remains unchanged. Direct structural changes or detachment invalidate them. Tree-wide handles retain global invalidation. Independent regressions cover sibling races, tile-child-tile replacement, merge/prune/topology changes, compacting GC, reductions, bounds and staleness.

Regenerate with `Build-NodeManagerReference.ps1 -OpenVdbSource <source> -OpenVdbBuild <static-build> -TbbRoot <tbb-root> -CMake <cmake-path>`. The script verifies the pinned revision and publishes JSON only after successful generation. Fixture SHA-256 is in `FIXTURE_MANIFEST.json`.
