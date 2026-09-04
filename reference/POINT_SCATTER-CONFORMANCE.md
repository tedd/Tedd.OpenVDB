# Point scattering and random-engine conformance

Upstream: OpenVDB 13.0.1, commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`.

Sources: `openvdb/openvdb/tools/PointScatter.h` and the `Rand01`/`RandInt` contracts in `openvdb/openvdb/math/Math.h`. The managed MT19937 engine implements the standard 624-word 32-bit state transition. Its canonical-double and bounded-unsigned distributions adapt Microsoft STL `random` (`generate_canonical`, `uniform_int_distribution`) and `algorithm` (`_Rng_from_urng_v2`) from the installed MSVC 14.51.36231 headers. Native builds use Visual Studio 18 2026, the pinned static OpenVDB build and oneTBB 2022.2.0. There is no native runtime dependency.

## Implemented surface

- `Mt19937`: seed, deep clone, raw unsigned output and discard; reusable `IRandomEngine32` requires a full-range 32-bit engine and explicit clone/seed semantics.
- `Random01`: canonical double values in `[0,1)`, copied engine, reseed and independent engine snapshots.
- `RandomUInt64`: inclusive unsigned bounds, normalized reversed ranges, temporary ranges, copied engine and reseed. Multiply/reject sampling covers the full 64-bit range using `UInt128` where required.
- `UniformPointScatter`: fixed count and float points-per-world-volume modes. Virtual voxel IDs are selected and sorted, while emitted positions sample active tile bounds directly. No tile voxelization occurs.
- `DenseUniformPointScatter`: integral and Bernoulli-fractional voxel density, including native tile float arithmetic and per-tile random draw consumption.
- `NonUniformPointScatter`: scalar-value weighting, including the native product of axis lengths rather than transform determinant; generic numeric, bool and explicit custom-value conversion overloads.
- All scatter sinks receive world-space `Vec3d` positions through `Action<Vec3d>`. All active topology and source metadata remain unchanged. Frustum and affine mappings use the current native map operations.

## Native contracts retained

Counters accumulate across successful and interrupted invocations. Uniform fixed-count construction sets a float density on the first call; a subsequent call takes the positive-density branch and recalculates its target. Empty/disabled paths update voxel counters at the same stage as upstream. Zero-count positive density succeeds. Negative active scalar values generate no points but retain the fractional random draw. Spread uses native `Clamp01`, including NaN-to-zero behavior.

The scatter object owns a copy of its supplied engine. Uniform voxel-ID selection makes a further copy and therefore does not advance the position generator. Zero spread still consumes position draws. The pinned MSVC native compiler evaluates the three `Vec3` constructor arguments from right to left, so position draws are assigned Z, Y, X. This order and STL distribution output are toolchain-specific; this port deliberately matches the pinned oracle rather than claiming cross-toolchain C++ distribution identity.

The exposed RNG helper set is the double and unsigned-64 subset required by these scatter algorithms. Other `Rand01<FloatType>` and signed `RandInt<IntType>` template instantiations remain outside this milestone. Native print-to-stream convenience methods and interrupter start/end callbacks are not exposed; counters/properties and .NET cancellation provide their managed equivalents.

## Managed execution boundaries

Input grids and transforms must remain read-only while scattering. Source replacement, topology-version changes, map replacement and recursive/concurrent reuse of one scatter instance are rejected. In-place value/activity writes on an existing leaf are not universally versioned by the core tree and remain outside that read-only contract. A conversion delegate has the same read-only requirement.

Cancellation checks occur before work, during ID generation, at stored entries and emitted points; cancellation throws while retaining already delivered points. Source-mutating append callbacks are detected after a successful append, so that delivered point remains counted. An append callback that itself throws does not increment the native count. Point budgets bound each invocation and can reject an integral group before emission. Uniform sorting needs one managed `ulong` per target point and rejects counts beyond `Array.MaxLength`; dense/nonuniform per-entry counts retain native signed-32-bit capacity. Nonfinite/out-of-range count conversions throw instead of invoking native undefined float-to-integer conversion behavior.

## Verification

`Build-PointScatterReference.ps1` compiles `point-scatter-oracle.cpp` and deterministically compresses its JSON using `GZipStream` with `SmallestSize` (no timestamp). The test fixture is `tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-point-scatter.json.gz`.

- 384 native scatter profiles: eight topology/value scenes, four transform configurations, four count/density algorithms, three seed/spread profiles, and two calls per object. Cases include empty/inactive-only trees, negative coordinates, active zero/negative values, mixed leaf/tile topology, level-two tiles, level-three root tiles and nonlinear frusta. Every emitted coordinate and every cumulative count/density/engine-ownership result is compared.
- 4 engine profiles: seeds 0, 42, 5489 and `uint.MaxValue`; each compares 1,300 raw words across multiple state twists and 100 canonical doubles exactly.
- 68 integer profiles: the same four seeds across 17 ranges, each with 100 exact outputs. Includes singleton/reversed ranges, rejection-sensitive bounds, 32-bit/64-bit transitions, full unsigned ranges and values near `ulong.MaxValue`.
- 46 independent regressions: published MT19937 sequence, clone/reseed isolation, custom engines, spread edges, tile centers, root sparsity, virtual-voxel weighting, jitter mean/variance/axis correlation, density arithmetic, bool/Half/custom values, disabled paths, cancellation, callback failure, recursion, source mutation, point limits and invalid scalar conversions.

All **502 tests pass in Release, Debug and scalar mode** (`DOTNET_EnableHWIntrinsic=0`). Native coordinates use relative/absolute tolerance `3e-13`; RNG output, counts and float density are exact. Statistical tests have fixed seeds and bounded sample sizes.

Fixture size: **121,310 bytes**.
SHA-256: **66447EA7517E1B0425A09B1AC5C853CDED00D05E3812D79EEEF873D347EFA987**.

## Distribution license

`RandomDistributions.cs` retains Microsoft and OpenVDB attribution and is marked `Apache-2.0 WITH LLVM-exception`. The complete Microsoft STL license is retained in `licenses/Microsoft-STL.txt`, obtained from the official source at <https://raw.githubusercontent.com/microsoft/STL/main/LICENSE.txt>. License SHA-256: **1FC6B6DB121D4555D8F661937D7F6013693CD8FD67495D5E5D98C1D18FB83EDA**. The project-level package license/notice aggregation must include this file and expression.
