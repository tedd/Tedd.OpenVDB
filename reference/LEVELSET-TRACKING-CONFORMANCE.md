# Level-set tracking and filtering conformance

This module ports `tools/LevelSetTracker.h`, `tools/LevelSetFilter.h`, `tools/ChangeBackground.h` and the `tools/Interpolation.h` alpha-mask helper from OpenVDB 13.0.1, revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`.

The standalone C++ oracle is `levelset-tracking-oracle.cpp`. `Build-LevelSetTrackingReference.ps1` checks the upstream revision, builds the oracle against the pinned static library, runs it with one TBB worker, and publishes `openvdb-13.0.1-levelset-tracking.json`. The managed runtime does not depend on native OpenVDB or TBB.

The fixture contains 155 Float/Double cases and 160,735 value/activity/depth samples. SHA-256: `16E5985A583FA08B59986F1F8AC7D123E6DC70B76289568B0AB19546943DBA23`. Topology counts, backgrounds, activity and depth are exact comparisons. Evolved scalar values use relative tolerances of 1e-6 for float and 1e-11 for double, allowing floating-point compiler differences.

Coverage includes all five biased spatial schemes, all three TVD Runge-Kutta schemes, masked normalization, tracking, four trimming modes, dilation with and without normalization, erosion, resize, seven surface filters, aligned/transformed/inverted alpha masks, a transformed mask sharing the target tree, and general/symmetric/asymmetric background updates. Independent regressions verify Eikonal-gradient improvement, finite outputs, inclusive trimming, masked inactive voxels, serial/parallel agreement, buffer cleanup after cancellation, pre-cancelled no-ops, shared-tree identity, CFL stepping, background policy distinctions and tree replacement.

## Native semantics retained

- Normalization uses the native scheme-specific stencil, Godunov gradient, smoothed sign function, typed tolerance and original float CFL constants. Auxiliary buffers follow native RK1/RK2/RK3 stage rotation. An activity mask can update inactive target voxels without activating them; mask tiles are ignored.
- Tracking dilates one six-neighbor layer with the ignore-tiles policy, normalizes and trims/prunes. Dilating the band updates background values and normalizes only newly active voxels when normalization is enabled.
- Trimming is inclusive at both band boundaries. Pruning rebuilds leaf-manager snapshots. The tracker captures voxel size at construction, as in native OpenVDB.
- Mean filtering uses the actual native X-Z-Y axis order. Gaussian filtering performs four complete mean-filter/tracking iterations. Median, Laplacian, mean-curvature and fillet filters each track after the buffered update. Zero alpha suppresses the local filter update but does not disable subsequent tracking.
- Offset filtering uses half-voxel CFL substeps and the native small-offset threshold, tracking after each substep. The target tree remains shared with other grids and accessors.
- Alpha masks use float smoothstep weights, transformed trilinear sampling, range normalization and optional inversion. Active state does not suppress stored mask values.
- General background replacement changes approximately matching inactive values of either sign, including the native same-background NaN case. Level-set replacement changes inactive root/leaf values and **all internal tile values**, preserving their activity. Active root tiles and leaf voxels retain their values.

## Managed safety and scheduling

Cancellation uses `CancellationToken`. Pre-cancelled calls leave the grid unchanged. Completed stages can persist; a cancelled buffered stage is not swapped into the grid, and auxiliary buffers are released. In-place stages finish before observing cancellation. This replaces native TBB's partially executed interruption behavior.

Positive grain sizes enable .NET parallel leaf processing; zero or negative values select serial processing. The integer does not reproduce TBB partition boundaries. An offset mask sharing the target tree through a different transform forces serial native leaf order to avoid read/write races. Aligned self-masks and buffered operations remain parallel-capable.

Invalid erosion widths are rejected before changing topology, whereas native OpenVDB rejects the resulting background after erosion. Nonfinite offsets and non-progressing CFL steps are rejected instead of allowing undefined or unbounded work. Coordinate arithmetic is checked. Replacing the grid's tree rebinds the managed leaf snapshot on the next operation; concurrent mutation remains unsupported.

`LevelSetTracker<T>` and `LevelSetFilter<T>` support float and double, matching native floating-point type guards. Masks use the native default float mask type. Arbitrary mask scalar templates, native interrupter callbacks and exact TBB scheduling are not exposed.
