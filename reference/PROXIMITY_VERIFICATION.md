# Closest-point verification

Reference: OpenVDB 13.0.1, commit f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf, math/Proximity.h.

Math/Proximity.cs ports closest points on segments and triangles, including segment parameters, triangle barycentric coordinates, Ericson Voronoi-region branches, native degenerate tests and double arithmetic.

The fixture openvdb-13.0.1-proximity.json contains 678 independent native cases. SHA-256: 5B9D2B8FB6B743D2BC4BDFD3FD3BFCCF6D535A802732F749C321AFBA6334AD77. Ten additional analytic tests cover segment clamping, triangle regions, barycentric reconstruction and degenerate geometry. Release, Debug and scalar-only Release: 688 passed in each configuration.

Build-ProximityReference.ps1 verifies the pinned source commit and builds/runs proximity-oracle.cpp using the independent proximity/CMakeLists.txt project. Supply the source, static native build and oneTBB paths as -OpenVdbSource, -OpenVdbBuild and -TbbRoot. Run the managed suite with filter FullyQualifiedName~ProximityTests.

Large-coordinate approximate point equality follows the native relative-or-absolute vector predicate; an apparently short edge can be treated as degenerate at large magnitudes. The port preserves that numerical behavior.
