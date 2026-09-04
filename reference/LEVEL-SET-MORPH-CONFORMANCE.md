# Level-set morphing conformance

Managed source: `src/Tedd.OpenVDB/Tools/LevelSetMorphing.cs`.
Authoritative source: `openvdb/tools/LevelSetMorph.h` at OpenVDB commit
`f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` (13.0.1).

## Implemented contract

`LevelSetMorphing<T>` supports native FloatGrid/DoubleGrid scalar operations. It retains source, target, and optional same-scalar alpha-mask references; exposes morph and tracker spatial/temporal schemes, normalization count, grain size, mask range/inversion, target replacement and forward `Advect` returning the CFL iteration count. Source values and topology are updated through tracked leaf-manager buffers; the source tree, transform and metadata identities remain intact.

The implementation reproduces source behavior which differs from intuitive interpretations of the operation:

- Only FIRST_BIAS and HJWENO5_BIAS morph differences are accepted. The tracker retains its independent supported schemes. RK1/RK2/RK3 use the native .3/.9/1 over sqrt(3) CFL constants and exact convex-combination stages.
- The Euler update uses the squared Godunov gradient norm, with no square root. Uniform map inverse-scale factors are cast at the same source boundary.
- Native dispatch accepts UniformScaleMap, UniformScaleTranslateMap, UnitaryMap and TranslationMap. A numerically uniform AffineMap and the identity default ScaleMap remain rejected. Exact Unitary/Translation identities are created with `Transform.FromMap(map, simplify: false)`; a normal map constructor or transform Copy can simplify those types according to native behavior.
- Both target and mask use index-aligned access only when all participating transforms compare equal. Otherwise both are sampled with world-space trilinear interpolation, including supported nonlinear target/mask maps.
- A maximum speed near 1e-6 stops evolution; the per-voxel approximate-zero threshold is 1e-8 for float and 1e-15 for double. Native NaN comparisons, mask smooth-step behavior, maximum-speed reduction and the maximum-value per-leaf sentinel are retained.
- Reverse and equal time bounds do nothing. This follows the native loop despite the upstream method comment suggesting backward evolution.
- Speed and Euler operations visit active leaf voxels. Existing active tiles remain sparse and are not independently evolved. Empty, inactive-leaf-only and active-tile-only inputs perform no CFL steps.

Managed additions are nullable mask clearing, a pre-allocation `MaximumBufferedLeaves` budget, finite-time checks and detection of an unrepresentable advancing CFL step. Cancellation uses `CancellationToken`, throws `OperationCanceledException`, retains already swapped stages, and discards uncommitted buffers. Native interrupter progress/start/end callbacks and silent partial-return cancellation are not the managed API. Inputs must remain stable during each call; sequential source/target/mask aliases are supported.

## Native verification

`reference/level-set-morph-oracle.cpp` runs the pinned C++ implementation with serial TBB orchestration. Its 212 cases cover float/double, both morph schemes, all RK stages, alternate tracker settings, growing/shrinking/constant/identical fields, tiny and nonfinite speeds, constant/varying/inverted/NaN/source-alias masks, source/target aliases, aligned and translated/nonlinear target sampling, all accepted exact map types, native rejected map types and schemes, active tiles, inactive leaves, empty fields and reversed/equal bounds.

Every stored leaf value and active bit is serialized as runs, with active voxel/tile counts, root and leaf counts, grid class/name, transform probes, and distant tile/background probes. Comparisons require exact topology and CFL counts, with value tolerances 3e-6 for float and 3e-12 for double. Independent tests cover buffer budgets, cancellation, retained aliases/accessors, source SetTree, metadata, target/mask immutability, native NaN gates, generic-scalar rejection and serial/parallel determinism.

- Fixture: `tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-level-set-morph.json.gz`
- Cases: 212 native plus 27 independent tests = 239 tests.
- Bytes: 1,153,832.
- SHA256: `06F2A450335378CDB6F9D21011A8F7AB4430C2CF5F4764592090129A578676F2`.

`Build-LevelSetMorphReference.ps1` validates the upstream revision, builds against the supplied static OpenVDB/TBB paths and emits deterministic timestamp-free gzip through .NET GZipStream with SmallestSize compression. Native libraries are reference-generation dependencies only; the managed runtime has no OpenVDB native dependency.

Validation: the combined morph/fracture Release suite passed 420/420 on .NET SDK 11.0.100-preview.7.26381.103. See `reference/level-set-morph-fracture-tests.log`. Root integration owns broader Debug, scalar-intrinsics and NativeAOT gates.
