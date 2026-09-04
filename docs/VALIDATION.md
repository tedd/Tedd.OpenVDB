# Validation record

Date: 2026-09-04. Platform: Windows x64. SDK: 11.0.100-preview.7.26381.103.
Upstream OpenVDB: f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf (13.0.1).
Original wrapper HEAD: f05f82318811b97e1591395df10979c6cacb5886.

The managed replacement is on main. Commit 5eb1137 replaced the wrappers; development continues directly on main. Original Git history remains available.

## Integrated checkpoint: 0.1.0-preview.4

| Validation | Result |
|---|---|
| Release | 9,909 passed; 0 failed; 0 skipped |
| Debug | 9,909 passed; 0 failed; 0 skipped |
| Release, hardware intrinsics disabled | 9,909 passed; 0 failed; 0 skipped |
| Windows x64 NativeAOT sample | Published and executed successfully |
| NuGet package and symbols | Generated locally; no publication |
| Native fixture manifest | 371 files; 155,904,771 bytes; hashes verified |

Release took 44 seconds, Debug 95 seconds and scalar Release 34 seconds on this run. These are local validation durations, not performance benchmarks. Separate artifact directories prevented concurrent builds from overwriting test assemblies.

The earlier checkpoints passed 600 tests (initial replacement), 3,056 (a3f1bca) and 4,742 (589aa33, preview.3). This checkpoint adds native archive instances and compressor coverage, metadata, numerical solvers, ray traversal, geometry conversion, surface meshing/rebuilding and velocity integration.

## Reproduction

Run from the repository root with the pinned SDK:

~~~powershell
dotnet test Tedd.OpenVDB.slnx -c Release --artifacts-path artifacts/preview4-release --logger 'trx;LogFileName=preview4-release.trx' --results-directory artifacts/test-results
dotnet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Debug --artifacts-path artifacts/preview4-debug --logger 'trx;LogFileName=preview4-debug.trx' --results-directory artifacts/test-results
$previousIntrinsic = $env:DOTNET_EnableHWIntrinsic
try {
    $env:DOTNET_EnableHWIntrinsic = '0'
    dotnet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --artifacts-path artifacts/preview4-release --no-build --logger 'trx;LogFileName=preview4-scalar.trx' --results-directory artifacts/test-results
} finally { $env:DOTNET_EnableHWIntrinsic = $previousIntrinsic }
dotnet publish samples/VoxelBlocks -c Release -r win-x64 -p:PublishAot=true --artifacts-path artifacts/preview4-aot -o artifacts/native-sample-preview4
./artifacts/native-sample-preview4/VoxelBlocks.exe
./reference/Update-FixtureManifest.ps1 -Verify
~~~

The NativeAOT executable checks voxel editing/dense transfer/pruning, a standard Int32 VDB roundtrip, a Blosc-compressed seven-grid archive with tree instances/Int64/Vec3/Bool/Mask/half fields, node-manager reductions, vector/Half sampling, gradient/level-set measurement, hierarchical volume and surface ray queries, double mesh conversion and adaptive surface meshing.

Observed output includes 128,293 compressed archive bytes, 1,543 level-set samples, four allocated nodes, level-set area 228.983 and volume 325.483, occupied ray interval [12, 20], surface distance 16.063, and a converted box with 637 distance samples and 86 surface points. This establishes AOT compatibility for exercised instantiations, not every possible unmanaged value type.

## New native evidence

Every family retains pinned C++ generators and reproducible fixtures. The fixture manifest includes raw JSON, native VDB files and gzip-compressed exact JSON. The library and managed tests require no native runtime dependency.

| Family | Evidence |
|---|---|
| PCG and Poisson | 588 native PCG profiles, 162 native Poisson profiles; 827 managed tests |
| Quantized unit vectors | All 65,536 decodes/sign flips and 8,192 encodings; 397 tests |
| Proximity | 678 native segment/triangle cases plus ten analytic tests |
| Hierarchical rays | 198 native profiles, 5,499 rays; 227 tests |
| Resampling | 234 native profiles, 361,764 sampled coordinates; 269 tests |
| Adaptive volume meshing | 586 native profiles including masks/reference seams; 608 tests |
| Level-set rebuilding | 104 native profiles with exact stored values/activity; 122 tests |
| Mesh distance/helper/edge conversion | 322 native profiles plus 31 independent tests |
| Velocity fields and integration | 64 native profiles, 34,560 RK results; 83 tests |
| Archive instances | 179 new tests; 64 managed files independently read by native OpenVDB |
| BloscLZ/Snappy/bitshuffle | 758 native raw/frame/filter cases and 24 native archives; 24 managed exports verified native |
| Zstd | 478 native streams and 12 native archives; 12 managed exports verified native |
| Builtin metadata | 68 native payload/type/truth/text cases; three managed archives verified native |

Full per-family scope, hashes, scalar tolerances, mutation semantics and reproduction commands are documented in reference/. Historical fixtures also cover storage mutations, merge/leaf transfer, iterators, dense conversion, filters, topology, level sets, composites, numerical math, finite differences, statistics and archives.

Independent tests cover analytic geometry, randomized storage, malformed/over-budget input, signed-coordinate boundaries, callback failure, copy/aliasing, compacting GC, accessor/manager staleness, parallel reads and allocation-free hot paths. Native equivalence preserves several documented upstream quirks, including mesh outer-band coverage, callback-overload forwarding, Poisson stencils and scalar rounding.

## Limits

The full OpenVDB port and complete native test suite remain unfinished. Generic precision/map families, configurable/specialized trees, point grids, remaining tools and legacy/delayed I/O still require work. Optimized native scale-map subclasses can select different cells at exact floating-point boundaries than the managed generic affine representation; velocity fixtures explicitly compare native AffineMap, and separate specialist-map evidence records the remaining gap.

CI is configured for Windows/Linux; no Linux or remote CI execution is claimed. No remote push or NuGet publication was performed. The existing benchmark records local allocation/throughput observations without a controlled cross-runtime or native speedup claim. See PORT_STATUS.md and UPSTREAM_INVENTORY.md for the remaining implementation boundary.
