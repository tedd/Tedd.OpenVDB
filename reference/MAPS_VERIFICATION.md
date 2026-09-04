# Specialized maps and transform verification

This family ports the seven native affine map types from `math/Maps.h` and `Maps.cc`, and map dispatch/factory/copy behavior from `math/Transform.h` and `Transform.cc`, at OpenVDB commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` (13.0.1). It retains the existing nonlinear frustum implementation through the common `MapBase` interface.

## Observed native evidence

`maps-oracle.cpp` compiled with MSVC 19.51.36256, x64 Release `/MT`, against the pinned OpenVDB build used by [CODEC_VERIFICATION.md](CODEC_VERIFICATION.md). Native execution generated **552 map cases from 22 source maps**, including default and explicit map types, negative/nonuniform scales, translation, rotation/reflection, shear, affine inverse fallback, a deliberately altered finite scale cache, frusta, and large translations. Cases cover map copies, transform construction/copies, inversion, pre/post translation, scaling, all three rotation and shear axes, matrix multiplication and factories. Native exceptions are explicit fixture outcomes.

Managed comparisons check native map type, identity, uniform-scale classification, determinant, voxel size, affine/inverse matrices, curvature, and seven point/derivative vectors per successful case. Specialized scale and translation point/derivative values compare exactly; general affine/frustum numeric comparisons use a relative/absolute tolerance of 2e-12. **Serialized map payloads compare byte-for-byte for every successful case**, including signed-zero caches and cached reciprocals.

Three native archives exercise uncompressed, active-mask and active-mask plus Blosc storage. They contain **20 grids each**. Native OpenVDB independently read all **60 managed-written grids**, compared exact map type/payload, mapped/inverse-mapped sample points, and voxel values. Two extreme-translation source maps remain in operation fixtures but are excluded from archives: native OpenVDB itself fails to write them because transform-copy simplification loses the unit-scale difference beside a 1e17 translation and then rejects zero scale.

The oracle also regenerated **12 velocity boundary profiles**, comprising float/double precision, point/box/quadratic interpolation and staggered/nonstaggered sampling at eight locations. Every regenerated profile exactly matched the historical observations in `velocity-specialized-map-boundaries.json`. These are now executable fixtures in `Fixtures/maps/native-map-velocity.json`. They verify both sample values and activity, including float world positions. The earlier velocity oracle's explicit affine profiles remain explicit affine profiles.

The map family contains **576 managed tests**: 552 native operation cases, three archive round trips, nine additional malformed-input/copy/boundary tests, and 12 velocity profiles. Additional tests cover accumulated unitary rotation rounding, overlapping spans, nonfinite scale-cache fields, invalid uniform-scale primaries, nested nonlinear maps, truncation, allocation limits and stream ownership.

## API and compatibility behavior

`AffineMap`, `ScaleMap`, `UniformScaleMap`, `TranslationMap`, `ScaleTranslateMap`, `UniformScaleTranslateMap` and `UnitaryMap` are immutable maps. Composition returns a new map, following native type transitions. `MapBase` exposes mapping, inverse mapping, Jacobian/transposed Jacobian and curvature operations; `NonlinearFrustumMap` implements the same interface. Mutable native accumulation operations are represented through returned immutable maps and the existing mutable `Transform` facade.

`Transform()` uses native `ScaleMap` identity. `CreateLinearTransform(double)` returns `UniformScaleMap`; the matrix factory simplifies diagonal matrices as native. `Transform(MapBase)` likewise simplifies linear maps except `UniformScaleMap`. `Transform.Copy()` follows native copy simplification: it can change map identity. `Transform(Mat4d)` retains explicit `AffineMap` behavior for existing callers; `Transform.FromMap(map, simplify: false)` retains exact map identity, matching native transform deserialization. `BaseMap` returns the immutable map snapshot. Transform equality compares equal map types directly and promotes differing linear map types to affine form after comparing voxel size.

Native scale-and-translate inversion subtracts translation before multiplying by cached reciprocal scale. It is not replaced by inverse-matrix multiplication. One regression maps the same world point to node-centered Y=8 with a specialized map and Y=7 with an affine map. Matrix scale, rotation and shear composition retains native operation order.

Native transform wire layout is preserved, including all five scale vectors (scale, voxel size, reciprocal, squared reciprocal and half reciprocal), and the translation vector when present. Finite serialized caches are retained rather than recomputed. `Transform.Read` / `Write` support standalone native transform framing, as well as grid archive serialization. Unitary maps read through native `UnitaryMap` semantics: finite affine matrices are read without repeating the constructor's unitary test, which would incorrectly reject accumulated rotation rounding. New matrices passed to the public unitary constructor are checked.

Managed validation rejects nonfinite/singular affine data, invalid uniform-scale primary values, malformed strings, nested frustum second maps, unsupported map classes, truncation and excessive allocation. These checks strengthen native preconditions in places where native readers trust serialized data. Native frustum secondary maps are flattened to their affine representation, as upstream. User-defined map serialization/registration, compound/decomposed map helpers and some mutable native map convenience methods remain outside this milestone.

## Reproduction

```powershell
./reference/Build-MapsReference.ps1 -OpenVdbSource D:/source/openvdb -CMake 'C:/Program Files/Microsoft Visual Studio/18/Insiders/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' -TbbRoot C:/Users/tedd/AppData/Local/Temp/Tedd.OpenVDB-net11-port/managed/reference/dependencies/oneapi-tbb-2022.2.0
```

Use `-SkipBuild` to reuse the native binaries. The script generates native map and velocity fixtures, publishes through temporary files, runs managed tests, exports managed archives, then verifies those archives in native OpenVDB. Native builds/dependencies remain ignored; the runtime package has no native dependency.

## Additional inverse boundary regression

The native PointPartitioner oracle subsequently exposed a half-voxel rounding difference in Mat4d.Inverse: native Mat4.h uses six expanded triple products for its 3x3 block determinant. The managed inverse now preserves that determinant and cofactor operation order. All 72 native partition profiles and the affected map/math suite pass; an additional focused regression checks cell-centered z=1 after scale .75, translation(.25,-.375,.125) and Z rotation .2 at world(.5,.5,.5). The map-specific suite now contains 577 tests.

## Finite extreme-scale constructor boundary

Native `ParticleAtlas` empty-input handling subsequently exposed another constructor boundary: `ScaleMap` and `ScaleTranslateMap` reject determinants smaller in magnitude than 3e-15, but permit finite scale components whose product overflows. The managed constructor now preserves that behavior instead of rejecting the infinite determinant. It continues to reject nonfinite scale components and nonfinite cached reciprocals.

`maps-extreme-oracle.h`, invoked by `maps-oracle.cpp extreme`, generated **20 native profiles**: all four scale/scale-translate classes at positive/negative double maximum and 1e200, plus two nonuniform overflow configurations. Tests compare determinant sign/infinity, voxel size, copied determinant, five mapped/inverse-mapped points by double bits, and standalone transform bytes exactly, including deserialization and reserialization. `Build-MapsReference.ps1` now regenerates this fixture alongside the existing map fixtures.

The affected final Release gate passed **1,638 tests**: 597 map tests, 1,001 native attribute-stream tests, and 40 standalone attribute-stream regressions. The finite extreme-scale change is one constructor-validation condition shared by the scale-translate classes; it does not alter affine inversion or normalize specialized maps through an affine representation.
