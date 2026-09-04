# Native ray verification

Reference: OpenVDB 13.0.1, commit f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf.
The oracle calls math::VolumeHDDA through tools::VolumeRayIntersector and
math::LevelSetHDDA through tools::LevelSetRayIntersector/LinearSearchImpl.
No native library is required by the managed runtime or tests.

Reproduce with reference/Build-RayReference.ps1, supplying OpenVdbSource,
OpenVdbBuild, TbbRoot and CMake as in the other reference scripts. The script
validates the pinned source and output provenance before replacing the fixture.

Fixture: openvdb-13.0.1-rays.json, 198 profiles and 5,499 native rays.
SHA-256: CDDB9B98977B307C3FB999CF176A2197FB52D36E0C5BF9F4BF3A85A62A8AF62D.

The 54 volume profiles cover three sparse scenes, every hierarchy entry level,
zero/one/two face dilations and index/world rays. They compare padded bounds,
clip outcomes, complete Hits lists, repeated March lists, positions and world
distance. Scenes include inactive leaves, inactive nonbackground tiles,
background-valued active tiles, leaf/lower/root boundaries and negative coordinates.
Nonunit index directions, misses, inside origins and short intervals are included.

The 144 level-set profiles cover float/double sphere and plane fields, all four
hierarchy levels, zero/two/five linear refinement iterations, three iso-values,
and identity or rotated/transformed index/world rays. They compare hit flags,
times, positions and world-space normals using native inputs. Numerical comparison
tolerances are 3e-6 for float and 3e-12 for double, scaled by magnitude.

Independent tests cover analytic plane roots/normals, topology snapshots,
per-worker copies, explicit bounds, activity/value separation, upper Int32
coordinate padding, empty automatic bounds, custom class/value-type traversal
policies, invalid configurations, flat-zero indeterminate roots and allocation.
After warmup, 1,000 repeated surface and volume queries allocate zero managed
bytes on the tested .NET 11 Windows x64 runtime.

Native distinctions retained:
- Volume traversal stops at allocated leaf blocks or active tiles. An allocated
  inactive leaf is occupied for this query; inactive tiles are not.
- Volume bounds include whole active leaves and active tiles, with an upper-face
  padding of one. Level-set bounds use whole leaves without this padding.
- March uses the native 1e-5 ray-validity threshold; Hits can retain intervals
  longer than the 1e-9 double TimeSpan threshold.
- Recursive traversal mutates the ray interval. Hits and March operate on that
  state; reset the ray when beginning a new query.
- Linear search uses native scalar multiplication for zero crossing, including
  signed-zero/underflow behavior. Both endpoint values zero yield NaN time.
  A flat zero field has no unique surface root or normal.

Managed differences:
- Query copies use GC-owned topology references instead of native master-pointer
  lifetime rules. Public CopyTopology returns an independent mutable tree.
- Result access before a successful search throws. Failed out-parameter searches
  return NaN time/default positions instead of retaining caller-owned variables.
- Invalid levels, nonfinite iso-values and nonuniform transforms are rejected.
- DDA coordinate overflow stops traversal rather than wrapping. The padded upper
  volume face uses double precision, so int.MaxValue+1 cannot overflow.
- A March increment that rounds back to the previous exit uses BitIncrement to
  guarantee progress. No precision improvement beyond native root interpolation
  is claimed.
