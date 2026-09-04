# Validation record

Date: 2026-09-04. Platform: Windows x64. SDK: 11.0.100-preview.7.26381.103.
Upstream OpenVDB: f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf (13.0.1).
Original wrapper HEAD: f05f82318811b97e1591395df10979c6cacb5886.

The managed replacement is on main. Commit 5eb1137 replaced the wrappers; development continues directly on main. Original Git history remains available.

## Integrated checkpoint: 0.1.0-preview.6

| Validation | Result |
|---|---|
| Release | 20,424 passed; 0 failed; 0 skipped |
| Debug | 20,424 passed; 0 failed; 0 skipped |
| Release, hardware intrinsics disabled | 20,424 passed; 0 failed; 0 skipped |
| Windows x64 NativeAOT sample | Published and executed successfully |
| NuGet package and symbols | Generated locally; no publication |
| Native fixture manifest | 409 files; 195,086,284 bytes; hashes verified |

Release took 61 seconds and Debug took 151 seconds. Scalar Release took 56 seconds
for 20,420 tests; the four forced-GC handle-lifetime tests were run separately and
passed in 92 ms to isolate compacting collections from the concurrent Debug process.
These are local validation durations, not performance benchmarks.

This checkpoint adds 7,364 tests: 785 value-transformer tests; 493 particle/partition
tests; 502 scatter/RNG tests; 970 diagnostics tests; 1,680 scalar Math.h tests; 2,178
attribute/codec/group/stream tests; 735 selected/regional file-reader tests; 20
extreme-scale map profiles; and one focused inverse-order regression. Native sources
contribute 1,074 attribute/group profiles, 500 standalone attribute-stream profiles,
750 value-transformer profiles, 468 particle/partition profiles, 456 scatter/RNG
profiles, 928 diagnostics cases, 1,653 scalar-math cases, 516 selected-file observations
and 20 extreme-map profiles. Native OpenVDB also read and verified 1,000 managed
attribute streams.

The attribute review caught two defects before this gate: noncanonical raw Boolean
bytes produced invalid CLR Boolean representations, and disposed handles retained
large nonstreaming arrays. Both now have focused regressions. ValueTransformer received
an independent source review with no actionable findings.

NativeAOT exercises standard archives, selected reads, metadata registration, attribute
codecs and Blosc streams, group bits, point scattering, diagnostics, value callbacks,
regional queries, LOD, volume advection, vector transforms, rays and both geometry
directions. Package 0.1.0-preview.6 includes symbols, source metadata, third-party
notices and the Microsoft STL Apache-2.0 WITH LLVM-exception notice.
## Previous integrated checkpoint: 0.1.0-preview.5

| Validation | Result |
|---|---|
| Release | 13,060 passed; 0 failed; 0 skipped |
| Debug | 13,060 passed; 0 failed; 0 skipped |
| Release, hardware intrinsics disabled | 13,060 passed; 0 failed; 0 skipped |
| Windows x64 NativeAOT sample | Published and executed successfully |
| NuGet package and symbols | Generated locally; no publication |
| Native fixture manifest | 392 files; 181,630,934 bytes; hashes verified |

Release took 54 seconds, Debug 105 seconds and scalar Release 50 seconds on this run. These are local validation durations, not performance benchmarks. Separate artifact directories prevented concurrent builds from overwriting test assemblies.

The earlier checkpoints passed 600 tests (initial replacement), 3,056 (a3f1bca) 4,742 (589aa33, preview.3), and 9,909 (92256b1, preview.4). This checkpoint adds specialized map identities and wire caches, registered metadata/MetaMap operations, regional queries/extrema, multiresolution grids, vector transformations, level-set solids/morphing/fracture and level-set/volume advection.

## Reproduction

Run from the repository root with the pinned SDK:

~~~powershell
dotnet test Tedd.OpenVDB.slnx -c Release --artifacts-path artifacts/preview5-release --logger 'trx;LogFileName=preview5-release.trx' --results-directory artifacts/test-results
dotnet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Debug --artifacts-path artifacts/preview5-debug --logger 'trx;LogFileName=preview5-debug.trx' --results-directory artifacts/test-results
$previousIntrinsic = $env:DOTNET_EnableHWIntrinsic
try {
    $env:DOTNET_EnableHWIntrinsic = '0'
    dotnet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --artifacts-path artifacts/preview5-release --no-build --logger 'trx;LogFileName=preview5-scalar.trx' --results-directory artifacts/test-results
} finally { $env:DOTNET_EnableHWIntrinsic = $previousIntrinsic }
dotnet publish samples/VoxelBlocks -c Release -r win-x64 -p:PublishAot=true --artifacts-path artifacts/preview5-aot -o artifacts/native-sample-preview5
./artifacts/native-sample-preview5/VoxelBlocks.exe
./reference/Update-FixtureManifest.ps1 -Verify
~~~

The NativeAOT executable checks voxel editing/dense transfer/pruning, a standard Int32 VDB roundtrip, a Blosc-compressed seven-grid archive with tree instances/Int64/Vec3/Bool/Mask/half fields, node-manager reductions, vector/Half sampling, gradient/level-set measurement, hierarchical volume and surface ray queries, double mesh conversion, adaptive surface meshing, cached regional queries, multiresolution restriction/fractional grids, volume advection, vector transformation and registered custom metadata codecs.

Observed output includes 128,237 compressed archive bytes, 1,543 level-set samples, four allocated nodes, level-set area 228.983 and volume 325.483, occupied ray interval [12, 20], surface distance 16.063, and a converted box with 637 distance samples and 86 surface points. Regional queries retain 32,768 uniform blocks; restricted impulse densities are 8 and 1, and advection transports density 64. This establishes AOT compatibility for exercised instantiations, not every possible unmanaged value type.

## New native evidence

Every family retains pinned C++ generators and reproducible fixtures. The fixture manifest includes raw JSON, native VDB files and gzip-compressed exact JSON. The library and managed tests require no native runtime dependency.

| Family | Evidence |
|---|---|
| Regional queries and extrema | 567 native regional profiles and 91 scalar/vector extrema profiles; 674 tests |
| Multiresolution grids | 304 native grid/transfer/sample/ownership profiles; 319 tests |
| Vector transformations | 160 native grid profiles and eight metadata cases; 179 tests |
| Registered metadata and MetaMap | 85 native observations, six native archives; 64 new tests and six managed exports verified native |
| Specialized maps | 552 native operation/wire cases and 12 velocity boundary profiles; 576 tests and 60 managed grids verified native |
| Level-set solids and topology conversion | 144 native profiles; 167 tests |
| Level-set advection | 150 native profiles; 179 tests |
| Volume advection | 538 native profiles across nine value types; 573 tests |
| Level-set morphing | 212 native profiles; 239 tests |
| Level-set fracture and float quaternions | 112 native fracture profiles and 52 exact-bit quaternion profiles; 181 tests |
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

The full OpenVDB port and complete native test suite remain unfinished. Generic precision families, remaining advanced map helpers, configurable/specialized trees, point grids, remaining tools and legacy/delayed I/O still require work. Specialized map arithmetic and the previously recorded velocity-boundary differences now have executable native conformance tests.

CI is configured for Windows/Linux; no Linux or remote CI execution is claimed. No remote push or NuGet publication was performed. The existing benchmark records local allocation/throughput observations without a controlled cross-runtime or native speedup claim. See PORT_STATUS.md and UPSTREAM_INVENTORY.md for the remaining implementation boundary.
