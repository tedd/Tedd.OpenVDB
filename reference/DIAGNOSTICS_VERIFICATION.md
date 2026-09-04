# Diagnostics native verification

Pinned upstream: OpenVDB 13.0.1, commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`.

The managed port covers the `tools/Diagnostics.h` predicate family, `Diagnose`, `CheckLevelSet`, `CheckFogVolume`, and `uniqueInactiveValues`. Runtime code is managed .NET 11; native OpenVDB and TBB are used only to regenerate independent fixtures.

## Fixture and coverage

`tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-diagnostics.json` contains **927 native profiles** (2,378,261 bytes), SHA-256:

`2BF94AD2802755198AE69875548E4159AA5DCF2C2EAAD4FC7EB76EF7DBA9FC6C`

- 21 predicate profiles cover Float, Double, Half, Int32, Int64, UInt32, UInt64, Bool, and Vec2/3/4 Float/Double/Int32; NaN/infinity, open/closed bounds, signed zero, negative magnitude/tolerance, and 64-bit integers above 2^53.
- 162 diagnosis profiles cover Float/Double/Vec3f, all three iteration modes, background inclusion, independently selected voxel/tile checks, mask updates, repeated counts, negative coordinates, leaf values, and active/inactive tiles at all three levels. Five sequential checks compare exact messages, cumulative counts, sparse mask voxel counts, and 50 value/activity probes.
- 112 inactive-value profiles cover empty trees, root tiles, leaf values, distinct-value limits, scalar ordering and lexicographic vector ordering.
- 168 gradient profiles compare **122,472 per-voxel decisions**, raw predicate values, messages, and whole-grid masks. They cover all five biased-gradient schemes, WENO/Grad Eikonal stencils, Float/Double, three voxel sizes, polynomial/planar/discontinuous fields, NaN/infinity, and negative bounds.
- 84 divergence profiles compare **10,500 derivative values and decisions**, Float/Double vectors, 14 derivative schemes, and three voxel sizes. Scalar predicates/masks are exact. Derivatives use relative tolerances of 3e-6 for Float and 3e-13 for Double; no tolerance is applied to failure decisions.
- 380 level-set/fog profiles cover every check-count boundary, empty valid grids, wrong class, nonuniform transforms, invalid backgrounds, active tiles, nonfinite/out-of-range values, invalid inactive values, and zero-crossing failures. Ordered messages, counts and mask probes must match exactly.

`DiagnosticsTests` additionally checks analytic derivatives, cancellation/exception publication, mutable mask behavior, accessor/scale copy semantics, integer precision, default tolerances, unsupported numeric classes, invariant diagnostic text, and zero allocation for repeated numeric predicate calls. Parallel and serial diagnosis are compared independently.

## Native defects and explicit adaptations

The oracle uses the unmodified pinned source. `diagnostics-invalid-templates.cpp` retains three optional compile-failure probes:

1. `Diagnostics.h:572–611`: `CheckDivergence` declares bounds and inverse voxel size as vector `ValueType` but compares a scalar divergence to them. Instantiating `CheckDivergence<Vec3fGrid>` fails MSVC C2676/C2677. Managed `CheckDivergence<Vec3f/Vec3d>` accepts scalar bounds and uses native derivative arithmetic. It is an explicit correction, not a claim that the ill-formed native functor ran.
2. `Diagnostics.h:667`: `Diagnose::clear()` assigns `new MaskType()` directly to `std::shared_ptr<MaskType>`, failing C2679 when instantiated. Managed `Clear` follows the written intent: a new identity-transform mask and zero failures. That behavior has independent managed tests; the native oracle does not call the invalid method.
3. `FiniteDifference.h:313–315,1493`: vector `D1Vec<FD_WENO5>` instantiates scalar WENO weights with vector expressions, failing C2440. Divergence schemes 0–9 call native `ISDivergence` directly. Schemes 10–13 call unmodified native scalar `D1` through component accessors, then add x/y/z derivatives in source order. This verifies the managed componentwise extension without modifying upstream.

Native `CheckEikonal` compares **squared** gradient norm directly to its supplied bounds; `CheckNormGrad` instead squares its bounds first. Both source behaviors, including the shared `NormGrad` message label, are preserved. The source fog-class diagnostic incorrectly names `GRID_LEVEL_SET`; its literal text and missing newline are preserved.

A failed tile adds one to `FailureCount` and its full voxel volume to the mask. Background failures add no mask voxels. Repeated checks accumulate failures but union mask activity. Implicit background is excluded from `UniqueInactiveValues`.

Managed guards reject unsupported types, invalid enums and negative limits, and preserve existing published counts/mask on cancellation or predicate exceptions. Signed minimum absolute values and checked integral overflow throw rather than reproduce undefined C++ signed overflow. Narrow integer diagnostic text uses numeric text rather than C++ character insertion. Unique-value failure returns a deterministic partial set with more than the requested limit; native parallel cancellation may return a different partial set. NaN is not a valid ordered-set key contract and is not claimed as a portable distinct-value ordering. Successful finite results must match exactly.

The native inactive-tile reducer scans the remaining iterator range repeatedly. Fixture shapes use active background root tiles around explicitly inactive samples, preserving the default 5/4/3 tree while avoiding quadratic native traversal over tens of thousands of identical inactive tiles. The managed traversal visits each stored tile once.

## Reproduction

Run `reference/Build-DiagnosticsReference.ps1` with `-OpenVdbSource`, `-OpenVdbBuild`, and `-TbbRoot` pointing to the pinned checkout, static Release OpenVDB build, and oneTBB installation. `-CMake` and `-Generator` select the local CMake/Visual Studio installation. The script checks the source revision, builds the separate oracle, runs with one native TBB worker, validates JSON provenance, and prints the fixture hash.

To reproduce an expected compilation failure, configure `reference/diagnostics` with the same include paths and `-DDIAGNOSTICS_INVALID_PROBE=1`, `2`, or `3`, then build target `diagnostics-invalid-probe`. This target is excluded from ordinary builds and is expected to fail at the source locations above.

Managed verification uses the `FullyQualifiedName~Diagnostics` test filter in Release, Debug, and Release with `DOTNET_EnableHWIntrinsic=0`. Final gate results are recorded below.

Final targeted gates: **970/970 passed in Release, Debug, and Release with hardware intrinsics disabled**, with zero skipped tests. The 970 comprise 927 native profiles, one revision/coverage assertion, and 42 independent behavioral/performance tests. Build and formatter complete without diagnostics errors or warnings. Runtime sources and tests are active.
