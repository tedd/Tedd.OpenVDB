# Ray tracer verification

`Tools/Film.cs`, `Cameras.cs`, `Shaders.cs`, `LevelSetRayTracer.cs`, and
`VolumeRenderer.cs` port the bounded CPU preview-rendering family in pinned
`openvdb/tools/RayTracer.h`. The source revision is
`f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` (OpenVDB 13.0.1).

## Native evidence

`reference/ray-tracer-oracle.cpp` includes and invokes the unmodified pinned
header. It generates
`tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-ray-tracer.json`.
The fixture contains 45 profiles, 114,271 bytes, with SHA-256:

`91D348420C7D40D7AFBBED17A9B07D69D5D138D5E0855A9A99BFAC43F099EF1B`

The profiles comprise:

- native RGBA arithmetic, default film initialization, bit-mask checkerboards,
  and 140 converted channel bytes;
- 40 perspective/orthographic rays across four camera configurations,
  rotations, translations, look-at orientations, clipping ranges, raster
  corners, centers, and asymmetric subpixel offsets;
- 36 constant-shader outputs and 40 sampled Vec3s/Vec3d shader outputs;
- 1,008 level-set image pixels across float/double grids, both cameras, all
  four shaders, and one/four sample paths;
- 504 single-scattering volume pixels across float/double fog grids, both
  cameras, default and customized optical parameters.

That is 1,665 RGBA observations (6,660 float channels), 320 camera ray values,
and 140 converted bytes. Film operations and constant shaders compare exactly.
Camera rays use relative tolerance `2e-13`; sampled shaders allow one float ULP
(`1e-7`). Level-set and volume images use an absolute float tolerance of `3e-6`, to cover the already independently verified managed ray
intersection and sampling kernels.

`reference/Build-RayTracerReference.ps1` verifies the exact upstream commit,
builds the oracle against the pinned static OpenVDB library and oneTBB, emits
the fixture, and reports its count, byte length, and hash.

## Managed contracts and deliberate boundaries

The film stores float RGBA values. Native color-producing `+` and `*` operators
reset alpha to one, while sample accumulation adds alpha; both quirks are
preserved. Byte conversion truncates valid `[0, 1]` components exactly and
rejects nonfinite/out-of-range inputs because the corresponding native integer
cast is undefined outside that documented domain. `SavePpm` retains the native
rule that `.ppm` is appended only if no dot occurs anywhere in the supplied
path, but managed I/O failures throw rather than writing to stderr.

Cameras retain native row-vector X/Y/Z post-rotation order, affine evaluation,
Houdini aperture formulas, ray-time scaling, and silent rejection of degenerate
look-at orientations. Construction rejects empty films and nonfinite or
nonpositive projection parameters before they can produce invalid rays.

Constant and sampled matte, normal, position, and two-sided diffuse shaders are
implemented. Sampled materials support Vec3f, Vec3d, and Vec3i grids with point,
box, or quadratic sampling. Shader copies retain a captured tree/transform
snapshot through `GridSampler`; concurrent source mutation is outside the
contract.

The level-set tracer supports the native float/double grid family and standard
managed `LevelSetRayIntersector`. Its fixed 16-value `Random01` table matches the
pinned MSVC `std::uniform_real_distribution<double>` stream. Serial
supersampling follows native source order. Parallel rendering assigns a global
pixel sequence so output is deterministic and identical to serial execution;
native TBB resets its sequence at blocked-range boundaries and is inherently
partition-dependent. Each managed worker owns an intersector and shader copy.

The volume renderer supports float/double density grids, box sampling, primary
and shadow intervals, native extinction/albedo arithmetic, early transmittance
termination, and all optical setters. Positive finite step validation prevents
malformed inputs from creating nonterminating loops. Render methods add
cooperative cancellation. Custom C++ template intersectors/samplers and the
native textual `print` method are outside this bounded port.

## Managed validation

`RayTracerTests` provides 14 independent tests for allocation initialization,
alpha behavior, checkerboard masks, P6 layout, malformed dimensions/channels,
camera geometry and guards, shader sampling/copy isolation, hit/miss rendering,
supersampling determinism, fog integration, setter guards, and cancellation.
`RayTracerNativeTests` provides five differential groups over every native
profile. All 19 tests pass in Release, Debug, and Release with
`DOTNET_EnableHWIntrinsic=0` on .NET SDK 11.0.100-preview.7.26381.103.