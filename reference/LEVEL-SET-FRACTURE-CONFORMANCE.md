# Level-set fracture and float quaternion conformance

Managed source: `Tools/LevelSetFracture.cs` and `Math/Quatf.cs` under `src/Tedd.OpenVDB`.
Authoritative source: `openvdb/tools/LevelSetFracture.h`, `math/Quat.h` and XYZ extraction in `math/Mat.h`, revision
`f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` (OpenVDB 13.0.1).

## Implemented fracture contract

`LevelSetFracture<T>` supports float and double grids. `Fracture` accepts a mutable residual grid list, a cutter, optional segmentation, optional world-space Vec3f instance positions and Quatf rotations, and fragment-overlap policy. The public editable `Fragments` list persists between calls; `Clear` removes its references without clearing those grids.

Native semantics retained:

- Each candidate intersection and residual is computed with source-compatible CSG copying. A cut is published only when both pass the native validity heuristic: fewer than nine leaves requires at least 27 active leaf voxels and both signs among all stored leaf values. Nine or more leaves bypass that small-fragment check.
- Residual publication clears and merges into the existing input tree. Existing grid/tree identities and persistent accessors are retained. Generated fragments inherit input metadata/transform through CSG copy.
- Newly generated fragments are appended after the current processing pass. Subsequent cutter instances can subdivide prior fragments when overlap is enabled.
- Empty/null positions use the original cutter. Rotation arrays are used only when their count equals a nonempty position array; mismatched rotations are ignored. Translations use point sampling and rotations use box sampling through the generic `DoResampleToMatch` path, without a level-set rebuild.
- Cutter instances use native float quaternion XYZ Euler decomposition, pre-rotations in X/Y/Z order, then world-space post-translation. The cutter tree is shared with its local grid copy, while transforms are independent.
- Optional SegmentSdf processing replaces the residual and fragment lists with disconnected components according to the existing source-compatible segmenter.

Input transforms and narrow bands must match, as required by the native API. They are not silently reconciled. Concurrent mutation is unsupported. Managed cancellation throws and retains previously completed cuts; completed fragments are retained even if a later cut is cancelled. Pending segmentation replaces a list only after it completes. This differs from native interrupter polling, silent partial return and its unconditional final segmentation. `MaximumResamplingSamples` is a managed per-instance sampling budget, not an aggregate memory limit.

The Quatf dependency supplies contiguous XYZW storage, equality, arithmetic/composition, axis-angle construction, normalization, matrix/vector rotation, span copying, and XYZ Euler extraction. It is a bounded float counterpart; all non-XYZ Euler orders, matrix-to-quaternion construction and the remaining interpolation/advanced Quat.h conveniences are not yet included in this type. Near-zero rotation quaternions map to identity at the native squared-length threshold. Float square-root rounding before Euler atan2 is preserved; evaluating that square root in double changed double-grid fracture values by approximately 3e-8.

## Native verification

`reference/level-set-fracture-oracle.cpp` executes the pinned native implementation under serial TBB. It contains 112 fracture scenarios spanning float/double, intersecting and disconnected sphere sets, tiny/empty fields, sparse active tiles, small synthetic fields, original/translated/identity-rotated/compound-rotated cutters, mismatched rotation counts, repeated overlap/nonoverlap cuts, segmentation, cutter/input aliases, and empty residual lists.

The fixture stores all residuals and fragments in native order, including every stored leaf value and active bit, structural counts, metadata, transform probes and distant background probes. Values use 3e-6 float and 3e-12 double relative/absolute comparison, with exact topology. An additional 52 native quaternion profiles compare matrix coefficients and XYZ angles exactly, covering each axis, scaled quaternions, gimbal angles, compound rotations, degenerate and NaN values. Constructor and composition results are also exact-checked for the relevant profiles.

Seventeen independent tests check sign partition/reconstruction, unchanged invalid cuts, residual tree/accessor identity, metadata, retained fragment references, rotation-count semantics, source/cutter immutability, budget rejection, cancellation before and between instances, segmentation, parallel independent objects and quaternion storage/normalization.

- Fixture: `tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-level-set-fracture.json.gz`.
- Cases: 112 native fractures + 52 native quaternion profiles + 17 independent tests = 181 tests.
- Bytes: 201,411.
- SHA256: `6919DD366FD400BD226654025D697958A7AE438344172041A3AB44E5FF6106F7`.

`Build-LevelSetFractureReference.ps1` validates the pinned revision and regenerates deterministic timestamp-free gzip with .NET GZipStream SmallestSize compression. It accepts upstream source/build, TBB, CMake and generator paths; no native library is required by the managed runtime.

Validation: the combined morph/fracture Release suite passed 420/420 on .NET SDK 11.0.100-preview.7.26381.103, including all double rotation cases at the unchanged 3e-12 tolerance. See `reference/level-set-morph-fracture-tests.log`. Broader integration gates are run by root.
