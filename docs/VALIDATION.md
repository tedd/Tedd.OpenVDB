# Validation record

Date: 2026-09-04. Platform: Windows x64. SDK: 11.0.100-preview.7.26381.103.
Upstream OpenVDB: `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` (13.0.1).
Original wrapper HEAD: `f05f82318811b97e1591395df10979c6cacb5886`.

The managed replacement is on `main`. Commit `5eb1137` replaced the old wrappers; work continues directly on main. Original Git history remains available.

## Integrated checkpoint: 0.1.0-preview.3

| Validation | Result |
|---|---|
| Release | 4,742 passed; 0 failed; 0 skipped |
| Debug | 4,742 passed; 0 failed; 0 skipped |
| Release, hardware intrinsics disabled | 4,742 passed; 0 failed; 0 skipped |
| Windows x64 NativeAOT sample | Published and executed successfully |
| NuGet package | Generated locally; no publication |

The initial replacement passed 600 tests; checkpoint `a3f1bca` passed 3,056. This checkpoint passes 4,742 and adds native comparisons for merge/leaf transfer, ordered iterators, dense conversion, filters, topology, level sets, CSG, vector composites, math, finite differences, statistics and archives. Review regressions cover Half and narrow-integer promotion, vector background predicates/medians, node-local sibling invalidation, invalid histogram scales, mask-tile inheritance and transformed shared-tree filter masks.

Commands used:

```powershell
dotnet test Tedd.OpenVDB.slnx -c Release --logger 'trx;LogFileName=preview3-release.trx' --results-directory artifacts/test-results
$previousIntrinsic = $env:DOTNET_EnableHWIntrinsic
try {
    $env:DOTNET_EnableHWIntrinsic = '0'
    dotnet test Tedd.OpenVDB.slnx -c Release --no-build --logger 'trx;LogFileName=preview3-scalar.trx' --results-directory artifacts/test-results
} finally { $env:DOTNET_EnableHWIntrinsic = $previousIntrinsic }
dotnet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Debug --artifacts-path artifacts/debug-work --logger 'trx;LogFileName=preview3-debug.trx' --results-directory artifacts/test-results
dotnet publish samples/VoxelBlocks -c Release -r win-x64 -p:PublishAot=true --artifacts-path artifacts/aot-work -o artifacts/native-sample
./artifacts/native-sample/VoxelBlocks.exe
dotnet pack src/Tedd.OpenVDB -c Release -o artifacts/packages
```

The NativeAOT executable verifies voxel editing, dense transfer, pruning, Int32 standard-VDB roundtrip and a Blosc-compressed heterogeneous seven-grid archive, node-manager reductions, vector/Half sampling, grid gradients and level-set measurement. The archive exercises shared tree identity, Int64 values above 2^53, Vec3f, Boolean active-false values, MaskGrid, half encoding and a CSG level set. Output: 128,293 archive bytes and 1,543 level-set samples; four allocated nodes; level-set area 228.983 and volume 325.483. This establishes AOT compatibility for the exercised instantiations, not every possible unmanaged value type.

## Native evidence

The committed fixture manifest records 175 files and 81,248,063 bytes with SHA-256 hashes. Reproduction scripts, exact comparisons and numerical tolerances are documented in `reference/`:

- Original tree: 222 scenarios, 1,235 mutation checkpoints, 99,434 value/activity/depth samples.
- Advanced tree: 243 cases and 122,472 samples; dense: 40 cases and 20,160 samples; ordered iterators: 120 cases; statistics: 110 cases.
- Filters: 48 Float/Double cases and 58,560 samples; review: 104 background cases and 12 Half-filter cases with 3,840 exact samples.
- Volume tools: 93 cases and 318,427 samples; level-set spheres: 16 cases and 219,000 samples.
- Scalar/CSG/leaf-transfer composites: 667 cases and 1,069,270 samples; vector composites: 200 cases with 1,000 grid outputs.
- Math: 276 initial cases; finite differences/stencils: 100 cases; convenience/differential operators: 144 cases; full grid operators: 320 cases, checked in managed serial and parallel execution.
- Node managers: 288 cases with complete per-node ordered value hashes; node-local invalidation: 24 independent regressions.
- Sampling: 51 profiles with 40 positions each, four result vectors and activity per position, including 8/16-bit integers and exact Half rounding.
- Level-set analysis: 154 utility/segmentation cases with 225,822 exact samples plus six Float/Double measurement cases.
- Level-set tracking/filtering: 155 native cases and 160,735 samples plus 43 independent tests.
- Native/managed bidirectional files: seven initial files, 20 compression profiles, eight seven-grid archives, and 32 extended-grid profiles. Mask inheritance adds 24 native-generated cases. Blosc adds 52 bidirectional archive profiles and 11 custom native frames with canonical uncompressed counterparts.

Independent managed tests cover randomized storage operations, malformed archives and allocation limits, signed-coordinate endpoints, callback failure, aliasing, compacting GC, stale managers/handles, parallel reads and allocation-free hot paths. Fixture comparisons do not establish completeness of the whole upstream library.

CI is configured for Windows/Linux; no Linux or remote CI execution is claimed. The local `benchmark-baseline.json` records initial allocation/throughput measurements, without a controlled cross-runtime/native speedup claim. The full upstream library and native suite remain incomplete; [PORT_STATUS.md](PORT_STATUS.md) records remaining work and managed behavior differences.
