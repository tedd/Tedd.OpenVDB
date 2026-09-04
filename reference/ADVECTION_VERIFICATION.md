# Level-set advection native verification

The implementation in `Tools/LevelSetAdvection.cs` ports
`openvdb/tools/LevelSetAdvect.h` at commit
`f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` (OpenVDB 13.0.1).
`levelset-advection-oracle.cpp` invokes the native solver and tracker without
replacing their numerical kernels. `Build-AdvectionReference.ps1` checks the
pinned revision, builds the oracle against native OpenVDB/TBB, regenerates its
JSON, and reports its hash. Native libraries are required only for reference
regeneration; the managed library has no native dependency.

The 150-profile fixture `openvdb-13.0.1-levelset-advection.json` has SHA-256
`5150819E75956976EF0B7C8EFCE5A3A4DDAFC07D8149A2BA612D597EE4F28494`.
It covers all five biased-gradient schemes and TVD RK1/2/3, float and double
level sets, float and double discrete velocity fields, Enright velocity fields,
forward/backward transport, aligned/misaligned field transforms, uniform scale,
scale/translation and unitary maps. It includes zero, small, NaN and infinite
velocities, empty grids, and up to four CFL steps. Comparisons check exact CFL
counts, active counts, leaf/root topology, every one of 239,917 active voxel
values, and 307 stored-value/activity/depth probes per profile. Values use
2e-6 float or 1e-11 double tolerance relative to max(1, abs(expected)); activity,
counts and depths match exactly.

The 179-test group includes 28 independent regressions and a provenance check.
Independent tests exercise linear-plane transport under all 15 scheme pairs,
field copying and transform alignment, fixed velocity samples within each CFL
step, time-dependent resampling between steps, native velocity-delta cutoffs,
zero/nonfinite velocity behavior, buffer limits, invalid map/scheme/time input,
nonadvancing float time, cancellation during sampling, cached accessor validity,
external tree replacement, and serial/parallel equivalence. The analytic plane
has sufficient support for two transports with three radius-three stages each;
a short finite patch would propagate its boundary discontinuity into the test
point and would not have the analytic infinite-plane result.

The public `LevelSetAdvection<T>` accepts float or double grids and an
`IVelocityField` (default EnrightField<T>). It exposes separate transport and
tracker spatial/temporal schemes, normalization count, grain size, voxel size,
an active-velocity-buffer budget, and forward/backward `Advect` with cancellation.
The velocity field is copied at construction and again for independent leaf
work. Velocity arrays preserve the field precision and are reused between CFL
steps. Leaf-buffer stages commit only after completion; tracking follows each
completed CFL step.

Precision-sensitive source details are preserved:

- CFL constants use the level-set scalar type, with maximum squared velocity
  computed first in the field's vector precision and then converted to the
  level-set scalar type. Delta cutoffs are 1e-5 for float and 1e-9 for double.
- Velocity is sampled at the start of each CFL step and retained through all its
  RK stages. Backward transport negates this velocity.
- The aligned fast path calls the field's integer-coordinate query directly.
- Gradient bias selection converts velocity to level-set precision; the resulting
  gradient is converted back to velocity precision for native Vec3::dot. Mixed
  field/grid arithmetic then follows the native promotions before writing phi.
- Native all-NaN extrema and infinite-speed zero-step behavior remain represented
  in the fixture. These cases are not silently coerced into finite transport.

Native advection accepts UniformScaleMap, UniformScaleTranslateMap,
TranslationMap and UnitaryMap, not arbitrary affine or nonlinear maps. Because
managed Transform currently stores an affine matrix rather than these distinct
map classes, this tool validates corresponding uniform-scale/translation or
translation-free unitary forms. Uniform maps use the native component scaling
and mapping formulas locally. Rotation combined with nonunit scaling or with
translation is rejected. Native reference unitary maps are preserved through
the public transform reader; ordinary native Transform construction can simplify
or replace map subclasses. Discrete velocity fields use the same explicit
AffineMap representation as the velocity-field verification. Specialized
velocity-grid map boundary arithmetic remains the separately documented generic
Transform limitation in `MATH_VERIFICATION.md`.

Managed safeguards replace unchecked native behavior: active tiles must first
be voxelized; active velocity arrays are checked against Int32 length and the
configured budget; nonfinite time intervals and time steps that cannot advance
at the scalar precision throw. Invalid schemes are validated before updates.
Cancellation during a buffered stage leaves that stage unpublished; previously
completed stages or topology tracking remain visible. Generic C++ interrupters
and TBB scheduling are represented by .NET cancellation and leaf parallelism.
