# Volume advection native verification

`Tools/VolumeAdvection.cs` and `Tools/VolumeAdvection.Arithmetic.cs` port
`openvdb/tools/VolumeAdvect.h` at commit
`f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` (OpenVDB 13.0.1).
`volume-advection-oracle.cpp` executes the unmodified native solver.
`Build-VolumeAdvectionReference.ps1` checks that revision, builds the standalone
reference target, regenerates its JSON, and reports the hash. Native OpenVDB
and TBB are reference-generation dependencies only.

The 538-profile fixture `openvdb-13.0.1-volume-advection.json` has SHA-256
`0BC60C6DAF73577E6F20CDE83188DF70F67FBD7B30D98DA896FA422319AC5D52`.
Comparisons check every one of 92,257 stored active values, 164 stored-value,
activity and depth probes per profile, active voxel counts, leaf/root counts,
maximum velocity and dilation distance. Half and integer results match exactly;
float and Vec3f components use a 3e-6 tolerance relative to max(1, abs(expected));
double components use 3e-13. NaN classification is checked explicitly. Topology,
counts and integer transport distances are exact.

Coverage includes all six native schemes (semi-Lagrangian, midpoint, RK3, RK4,
MacCormack and BFECC), all three limiter settings, Float/Double/Half/Int32,
Vec2d/Vec3f/Vec3d/Vec3i/Vec4d volume values, and both Vec3f and Vec3d velocity
fields. Profiles exercise point, box and quadratic volume sampling, staggered
velocity sampling, substeps, backward transport, nonconstant velocity,
inactive stored velocity, uniform translated/rotated volume maps, Boolean
masks containing active false values, active tiles, zero time, zero velocity,
empty grids and NaN volume components. Reference transforms are preserved as
explicit AffineMap instances through native transform deserialization, matching
managed `new Transform(matrix)`. Specialized map subclasses have their separate
map fixtures; these volume fixtures do not claim to cover every map dispatch.

The 573-test group consists of these native profiles, a provenance test and 34
independent cases. Independent cases cover analytic linear-polynomial transport
under every scheme, backward time, settings and order reporting, independent
metadata/transforms/input trees, masked topology restoration, scalar background
tolerances, exact vector background tolerance, zero-distance tiles, cached
maximum velocity including inactive values and tiles, serial/parallel stage
identity, cancellation, allocation/dilation limits, invalid types/settings/maps,
nonfinite distances and coordinate overflow.

Source-sensitive behavior retained:

- Maximum velocity is cached at construction and includes stored inactive
  values/tiles and background. Tracing subsequently reads the current velocity
  grid. Mutating the field does not refresh the cached padding bound.
- The substep dilation radius is computed in double precision before the
  tracing time step is narrowed to the velocity component precision.
- MacCormack and BFECC follow native stage ordering, auxiliary-buffer contents,
  arithmetic promotions and final narrowing. Half narrowing passes through
  float. Sampling and velocity integration retain their separate precision.
- Velocity sampling uses box interpolation; the selected volume interpolator
  can be point, box or quadratic. Revert limiters always use the source box
  interpolation, and limiter characteristic tracing always uses Euler.
- Vector extrema use the actual lexicographic `cwiseLessThan` implementations
  in Vec2/3/4.h. The ANY-component warning in VolumeAdvect.h is stale. Component
  clamping preserves native ternary behavior even when these vector extrema
  produce reversed bounds for a later component.
- Vector approximate equality has a distinct overload that tests every
  component; it does not use the lexicographic ordering. Scalar Delta is 1e-5
  for float, 1e-9 for double and 0.00390625 for Half. Vector Delta is zero.
  Explicit NaN profiles distinguish these overloads.
- Zero dilation does not materialize active tiles. Leaf-only transport and
  deactivation therefore leave those tiles intact, even when their value is
  background.
- Masked execution intersects and prunes the candidate before transport, then
  restores the original active topology. As in native OpenVDB, values in an
  entirely excluded/pruned leaf can become background when activity is
  restored. The fixture and independent regression preserve this behavior.
- Fully inactive subtrees are pruned after each transport substep. Input grids
  and masks remain unchanged; results deep-copy grid metadata and transforms.

The managed API currently supports the nine value types listed above, and
Vec3f/Vec3d velocities. It does not claim arbitrary C++ custom-value-template
support. Point/box/quadratic volume interpolation and staggered velocity are
supported; custom volume sampler classes are not part of this API.

Managed guards replace unchecked native cases: nonfinite time/dilation and
Int32 coordinate overflow throw; invalid enums/types and misaligned masks are
rejected. `MaximumDilation` limits the radius of each substep and
`MaximumVoxelizedLeaves` limits materialization of existing active tiles. These
are opt-in limits, not a total output-memory budget. Cancellation discards the
private candidate without changing inputs. .NET leaf-range parallelism replaces
TBB, and Span-backed auxiliary/sample storage plus validated native-width
component access avoid per-voxel temporary arrays. No hardware-specific native
library, dynamic code generation or runtime reflection is required.

Targeted verification commands:

```powershell
dotnet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter FullyQualifiedName~VolumeAdvection
dotnet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Debug --filter FullyQualifiedName~VolumeAdvection
# Repeat Release with DOTNET_EnableHWIntrinsic=0 for scalar execution.
```

Verified result: 573 passed, zero failed or skipped in each of Release, Debug and Release with hardware intrinsics disabled.
