# Advanced storage and filter conformance

Reference: OpenVDB 13.0.1, commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, built with MSVC on Windows x64. All native code here is a test oracle, not a library runtime dependency.

| Oracle | Cases | Comparison |
|---|---:|---|
| advanced-tree | 243 | 122,472 value/activity/depth samples; active/inactive voxel counts, node counts, root count and source consumption |
| filter | 48 | 58,560 samples, active count and exact node topology; Float/Double, all four filters, transformed/inverted masks, tile processing |
| dense | 40 | 20,160 samples and counts/topology; both layouts, tolerance, active/inactive existing storage |
| iterator | 120 | Full ordered traversal count/hash plus first 20 entries; all modes, depth limits, value/activity mutations |
| statistics | 110 | Weighted online moments, partition/self merges and histogram boundaries |
| background-review | 104 | Scalar/vector signed-background reset and root emptiness |
| half-filter-review | 12 | 3,840 exact Half samples; float promotion, filters and masks |

The native filter comparison detected a real inverted-mask rounding discrepancy: both complementary coefficients must be retained before inversion. Reconstructing the second coefficient from the inverted first coefficient changes DoubleGrid results. The dense comparison detected an incorrect activity-only interpretation of tree emptiness; inactive stored nodes must be inherited. Mutable iterator tests establish the actual implementation's activity effects, not its contradictory comments.

Merge tests preserve the upstream root-to-child background argument ordering, including differing backgrounds. Equal-background leaf transfers must retain inactive NaNs; reset routines return before approximate comparison in that case. Additional review regressions cover vectors and live-view invalidation.

Filter numeric comparisons allow relative tolerance 2e-6 for Float and 1e-12 for Double; topology and activity are exact. Dense and integer merge comparisons are exact. Iterator hashes cover coordinate, value, activity and level in traversal order. Statistics use relative tolerance 2e-12.

Reproduce against a configured native static build:

```powershell
./reference/Build-AdvancedReference.ps1 -OpenVdbSource D:/source/openvdb -OpenVdbBuild <native-build> -TbbRoot <tbb-root> -CMake <cmake-executable>
```

The script verifies the upstream revision and publishes fixtures after successful execution. Managed tests read committed fixtures. Endpoint-safe iteration, GC compaction, zero-allocation buffer swapping, stale handles/managers, transaction failure and caller-owned dense memory have independent managed tests.

The review fixtures are regenerated with `Build-BackgroundReviewReference.ps1`, using the same arguments as above. Independent review regressions cover Half subtraction before tolerance comparison, vector median ordering, invalid histogram scale and inherited mask-tile state. Fixture hashes are recorded in [FIXTURE_MANIFEST.json](FIXTURE_MANIFEST.json).
