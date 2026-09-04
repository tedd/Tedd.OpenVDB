# Vector transformation verification

Pinned source: OpenVDB 13.0.1, revision f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf; tools/VectorTransformer.h and Grid.cc.

VectorTransformer, HomogeneousMatMul, MatMul and MatMulNormalize implement transformations for the native Vec3f/Vec3d/Vec3i grid family. Grid.Vector.cs provides vector type and local/world-space metadata, parsing, descriptions, examples and clearing.

Build-VectorTransformReference.ps1 reproduces 160 native grid profiles plus eight native metadata-string cases. The fixture openvdb-13.0.1-vector-transform.json has SHA-256 3351679636A6B520463DA3C1DCE0D980DE65DBCE9353972FD0FB7FCF9B59D2B0.

There are 179 tests: 168 native cases and 11 independent tests. Native grids span five vector modes, local/world space, all three vector precisions and five matrices (identity, nonuniform scale/translation, rotation/shear, projective and singular). Assertions cover every stored-value group by hierarchy depth, activity, transformed vector and multiplicity; 107 coordinate probes per vector profile; background; active counts; allocated leaves; and native singular/type failures. Scalar grids throw even when invariant or local. Integer values and all topology/counts compare exactly. Floating comparisons use relative tolerances 2e-6 for float and 2e-13 for double, relative to max(1, abs(expected)).

The algorithm transforms all stored active and inactive voxels and tiles. It preserves the implicit root background, activity masks, allocated topology, metadata and grid transform. Covectors use the full matrix inverse transpose; unit normals normalize in double with the native 1e-7 near-zero threshold. Contravariant relative vectors ignore translation; absolute vectors use homogeneous division, returning zero when W is zero. Invariant and local-space grids bypass arithmetic.

Native local-space metadata uses asBool, including non-Boolean values. A nonempty string containing the word false is true metadata and therefore indicates local space. Setting IsInWorldSpace replaces the prior field with Boolean metadata; setting VectorType retains native metadata type-conflict errors.

Independent tests verify geometric results, inactive background-valued storage, near-zero and NaN normalization, unchanged source transforms, singular failure before mutation, functors on live handles, metadata archive roundtrip and pre-cancellation. Undefined native nonfinite/out-of-range integer casts throw OverflowException in managed code. Cancellation between groups of writes can retain earlier completed writes.