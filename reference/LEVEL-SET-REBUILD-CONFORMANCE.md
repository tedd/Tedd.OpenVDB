# Level-set rebuilding conformance

Source: OpenVDB 13.0.1 revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, `tools/LevelSetRebuild.h` and the level-set dispatch in `tools/GridTransformer.h`.

`Build-LevelSetRebuildReference.ps1` verifies the pinned revision, builds an isolated native C++ oracle, and generates **104 float/double profiles** using one TBB worker. The fixture is `Fixtures/openvdb-13.0.1-level-set-rebuild.json.gz`; SHA-256 is **BAED20EAC97E1621B31C111B35C368C0DF5AC125A48D41FAE7AB89D3D8F6C69E**. JSON run-length encoding retains every leaf value and active-state bit. Gzip generation uses the .NET encoder's timestamp-free header. The compressed fixture is 7,880,266 bytes.

Profiles cover sphere and fog inputs, inactive crossings, isolated active voxels, active leaf tiles, affine and frustum source/target transforms, default and asymmetric bands, effectively unlimited interior widths, nonzero isovalues, native parameter rounding, equal-transform copying, different-transform rebuilding, and all three matching sampler orders. Tests compare every stored voxel, every active bit, leaf/root/tile counts, inactive background probes, value depth, classification, naming and metadata behavior. Tolerances are relative `3e-6` for float and `3e-12` for double; topology and activity are exact.

Eighteen independent tests cover typed-parameter narrowing, symmetric overload equivalence, signed inactive interiors, analytic sphere distance error, empty output, unsupported Half grids, NaN isovalues, source and transform ownership, old accessor validity, shared-tree resampling, output clipping, sampler independence, target-band selection, precancellation and failures in all resource-budget stages. An initial larger fixture also passed six nonlinear target-band cases with 1,952,916 active voxels each; the committed fixture uses a smaller nonlinear target background to bound routine test cost.

## Native reconstruction contract

* Rebuild extracts the isosurface with zero adaptivity, transforms the float world-space mesh points through double arithmetic, and narrows output index-space points back to float. Double source grids therefore still pass through float mesh vertices.
* Isovalues and both widths are narrowed to float before meshing/conversion, including the `RebuildTyped` convenience entry point. Widths are measured in output voxels.
* Each native polygon pool is flattened in quad-then-triangle order, using mixed quads with a final `-1` index for triangles. Mesh conversion runs with `DisableRenormalization` and the default native interior classifier. The optional source-oracle compile-time macro is disabled in the pinned build.
* Standalone rebuild copies the selected transform and uses mesh conversion's classification; source metadata is not copied. Empty and invalid-band behavior follows the mesh converter.
* Matching float/double level sets with different transforms invokes reconstruction regardless of point/box/quadratic sampling order. Output level sets derive half-width from their own background and voxel size; other output classes use the input background and voxel size. Only the target tree is replaced; output transform, metadata and grid class remain intact. Equivalent transforms copy the source tree directly.

## Managed limits

`MeshToVolumeOptions` supplies conversion limits and cancellation for the entire standalone rebuild; `maximumCells` independently bounds meshing candidates. In `ResampleToMatch`, `maximumSamples` bounds each reconstruction stage separately: meshing candidates and mesh-distance evaluations. It is not an additive total across those stages. Output clipping occurs after the full reconstruction so that off-clip surface support remains available.

A failed or cancelled rebuild leaves source grids untouched. Matching resampling replaces its output tree only after successful rebuilding/clipping; existing aliases and accessors retain the old tree. Half is excluded by the native scalar floating-point type contract. Nonlinear and anisotropic transforms retain native index-distance behavior; the implementation does not claim a world-space exact SDF for arbitrary maps.
