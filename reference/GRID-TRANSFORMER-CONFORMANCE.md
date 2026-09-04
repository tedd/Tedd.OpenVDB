# Grid transformation and resampling conformance

Source: OpenVDB 13.0.1, revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, `openvdb/tools/GridTransformer.h` and its `math/Mat.h` XYZ decomposition helper.

`Build-GridTransformerReference.ps1` verifies the source revision and builds the isolated native oracle with the source build's static OpenVDB and TBB dependencies. The reference executable uses one TBB worker and explicitly disables transformer threading where that option is public. Native output is stored in `tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-grid-transformer.json`.

The fixture exercises float/double scalar values and Vec3f vectors with point, box, quadratic and all three staggered samplers. Cases cover affine identity, integer/fractional translation, nonuniform scale, successive-halving mipmaps, reflections, rotation, shear, both component-order constructors, gimbal-lock decomposition, projective matrices, affine-to-frustum, frustum-to-affine, equal/different frustums, active/inactive tiles at all tree levels, tile suppression, stored inactive voxel values, preexisting destination values, native generic matching and equal-transform level-set copies. Assertions compare values, activity, value depth, active voxel count, leaf/root/tile counts and decomposed matrices. Scalar tolerances are relative `3e-6` for float/vector components and `3e-12` for double, with exact topology/activity. Independent tests verify affine-field reproduction, alias snapshots, parallel determinism, clipping, sampling budgets, cancellation, old accessor lifetimes, metadata, boolean active payloads and ushort block IDs.

## Preserved native behavior

* Transformation operates on index coordinates. Input/output transforms and destination metadata remain unchanged by `TransformGrid`; `ResampleToMatch` derives the index mapping from those transforms.
* Native leaf regions use inclusive `origin + DIM`, then transform all corners of `[min, max + 1]`. Sampler radius expands integer output bounds.
* Active samples overwrite output values; inactive samples cannot overwrite existing active values.
* Native `TileSampler` calls `BBox.expand(-radius)`, whose implementation takes the padding's absolute value. Consequently the tile cache expands despite its source comment saying it shrinks. This behavior is retained and tested.
* Ordinary tiles are sampled voxel by voxel. Level sets skip input tiles and prune/flood-fill the result. Native successive-halving temporary grids lose the input's level-set classification, and the identity final remainder replaces the destination tree; these details are retained.
* Point/box non-staggered affine identity matching deep-copies the source tree; quadratic and staggered kernels still resample. Level sets with equal transforms copy regardless of kernel, including equal frustums.

## Managed extensions

* Shared input/output trees are snapshotted. Parallel row sampling commits in deterministic serial source-region order; native TBB reduction scheduling is not reproduced.
* `OutputClip` applies in final output coordinates only. Intermediate mip grids retain the support required for the final result. Clipping can alter floating-point affine stepping roundoff by changing its starting coordinate.
* `MaximumSamples` counts overlapping regions and all mip stages, preventing accidental unbounded tile expansion when configured. The default preserves native unrestricted behavior. Cancellation and later-pass budget failures retain completed changes; the initial sample-budget preflight runs before output background mutation.
* Native undefined coordinate conversion/overflow and shifts beyond 30 mip levels are rejected explicitly. User mappings must be pure and stable during a call.
* `ResampleToMatch` reconstructs differing-transform float/double level sets through `LevelSetRebuild`, `VolumeToMesh` and `MeshToVolume`. See [rebuild conformance](LEVEL-SET-REBUILD-CONFORMANCE.md) for 104 additional native profiles. The separate `DoResampleToMatch` remains the native generic path; it does not substitute for signed-distance reconstruction under scale/shear. Rebuild budgets bound each mesh stage separately, and tree replacement occurs only after successful reconstruction.
* Background remapping uses the supported scalar/vector/bool closed arithmetic types in `ChangeBackground`; arbitrary unmanaged point payloads do not imply generic signed background operators.
