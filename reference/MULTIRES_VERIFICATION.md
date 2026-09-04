# Multiresolution grids verification

Pinned source: OpenVDB 13.0.1, revision f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf; tools/MultiResGrid.h.

MultiResGrid<T> implements the explicitly instantiated FloatTree/DoubleTree family: empty and copied/stolen construction, integer grid views, independent fractional grids, level coordinate conversion, point/box/quadratic sampling, full-weight restriction, injection, prolongation, active-leaf transfers, level-set flood/pruning and grid metadata.

Build-MultiResGridReference.ps1 builds an isolated native generator against the pinned static library and regenerates 304 profiles. The fixture openvdb-13.0.1-multires-grid.json.gz is 903,340 bytes; SHA-256 9FD02E42639572942A21146FFD51DC2E356A415EDE9F345782B7EC6FA715430F.

There are 319 tests: 304 native profiles and 15 independent tests. Profiles cover eight source scenes, both precisions, full weighting/injection, all three sampling orders, fractional levels, coarse/fine transfer after edits, active tiles and inactive values, level-set sign propagation, constructor failures, metadata type conflicts and ownership transfer. Assertions compare exact activity, leaf/tile counts and depths, all stored active values, 257 additional value/activity/depth probes per grid, inter-level samples, restriction and prolongation values, names, transforms and metadata. Floating-value tolerances are 2e-6 for float and 2e-13 for double, relative to max(1, abs(expected)); topology is exact.

## Arithmetic and ownership

Restriction retains the native 8/4/2/1 weights, addition order and 1/64 multiplier. Prolongation retains native parity-dependent summation, including zero initialization only for the all-odd case. Fractional topology derives from active coarse leaf voxels; manually added coarse tiles are ignored. Fractional coordinate scales and blend weights use float even for double grids. Native rounding is floor(x + 0.5), including negative positions.

GetGrid shares a level tree and copies metadata/transform. CreateGrid returns an independent tree even at integer levels. The input-copy constructor first voxelizes active tiles on its private copy. The stealing constructor retains tree identity and replaces the input grid tree after voxelization; later failure can leave that input empty. Metadata conflicts retain native TypeError semantics through InvalidCastException.

The equivalent coarse mask is constructed directly from fine-voxel parity rather than allocating and dilating a second fine mask. Exact native topology comparisons verify this transformation. Transfers reuse one accessor and write active leaf values without changing masks or allocating new leaves.

## Managed limits

Native integer shifts are bounded to coordinate levels 0 through 30; construction requires 2 through 31 levels. Invalid or nonfinite fractional levels and out-of-domain integer stencil positions throw. MaximumLeaves and MaximumActiveVoxels bound each materialized level, including tile expansion and fractional growth. Cancellation occurs between leaf/stage boundaries. Independent native C++ diagnostic text formatting and unsupported template value types are not claimed.