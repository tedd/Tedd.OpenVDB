# Volume meshing conformance

Source: OpenVDB 13.0.1 revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, `tools/VolumeToMesh.h`, its lookup tables, and the `math/Mat3.h` symmetric Jacobi helper.

`Build-VolumeMeshReference.ps1` checks the source revision and builds an isolated C++ reference executable against the pinned static OpenVDB build. The executable runs with one TBB worker. `Fixtures/openvdb-13.0.1-volume-mesh.json` contains **586 native profiles**; SHA-256 is **8AC4919AC80AE07C146938A1BC76C993215633D495A2B6E5E714CED07240AEF6**.

The native cases cover all 256 scalar corner configurations, all 256 boolean configurations, float/double spheres and noise, inactive crossings, leaf-sized active tiles, fog/level-set winding, frustum maps, spatial masks with matched and mismatched transforms, mask inversion, adaptive region merging, spatial adaptivity, topology masks, reference surfaces, secondary adaptivity, sequential fragment processing with persistent reference weights, fracture seams, nonplanar subdivision and triangle relaxation. Assertions compare every point, polygon index, winding, pool boundary, polygon flag and point flag. Topology and flags are exact; float point coordinates use relative tolerance `2e-6`.

Twenty-two independent tests check closed-surface edge pairing and orientation, finite geometry, source immutability, reused buffer lifetimes, inactive topology, boolean payload semantics, sparse root-tile budgets, precancellation, exception buffer cleanup, mask payload independence, reference-cache resets, integer grids, tangent-plane feature points, and polygon-pool copying/trimming.

## Preserved source behavior

* Scalar inside tests use the original value type, whereas interpolation uses the original double isovalue. Boolean meshing treats stored true values as inside, independently of isovalue.
* Corner ordering, edge-group lookup, ambiguous-face decisions, leaf-local polygon order and adaptive normal tests follow the native source. Adaptive merging does not clamp the requested threshold.
* Native positive-Y external-edge enumeration omits the last Z column. Native negative-face handling uses its whole internal-neighbor list. These source behaviors are retained, including cases where comments suggest a narrower scan.
* Surface masks and adaptivity masks use active topology. Different mask transforms map through world space with cell-centered nearest lookup. Active tiles are handled as sparse boundary regions rather than expanded throughout their volumes.
* Reference weights are quantized to ten bits per component and cached across fragment calls. Reference data can remain stale after external grid mutation until `SetReferenceGrid` resets the cache, as in native code.
* Native feature-point eigenvalue cutoff and degenerate zero-normal NaN behavior are retained. Undefined index overflow is replaced with checked managed failure.

## Managed storage and execution

`VolumeToMesh` owns contiguous float point arrays and per-leaf `PolygonPool` arrays. `MeshTriangle` and `MeshQuad` use signed indices; `D == -1` represents native `INVALID_IDX` in mixed triangle/quad adapters. Read-only memories retain the old arrays if a later extraction replaces them. Pools intentionally support mutation, copying and trimming.

Orchestration is serial; native TBB scheduling is not reproduced. A mesher instance must not be used concurrently. Candidate budgets include repeated additions from overlapping edge neighborhoods. Input grids must remain stable during extraction. Cancellation is checked between bounded regions; precancellation retains completed output, while failures after extraction starts clear output buffers, preventing exposure of partially initialized unmanaged memory. Reference caches may retain work completed before cancellation.
