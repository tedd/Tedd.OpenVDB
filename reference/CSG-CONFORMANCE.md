# CSG native conformance

`csg-oracle.cpp` uses the pinned OpenVDB commit
`f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`. Run
`Build-VolumeToolsReference.ps1` with the same source/build/TBB arguments as the
other volume tool fixtures. Native dependencies are used only to regenerate tests.

The oracle contains 667 cases: 435 cover union, intersection, difference, and their
copy variants; 232 additionally cover sum, product, min/max, division, active-value replacement, and active-leaf transfer/compositing. It records values, activity, value depth, active count and leaf
count for the output and the second input. Cases include signed tiles at every
level, active tiles, zero tiles, background-only entries, different background
widths, overlapping and disjoint leaves, operand ties, cancellation pruning and
level-set spheres. The managed test also verifies accessors created before mutation.

The oracle explicitly restricts TBB to one thread because the managed algorithms
currently execute serially. This matters for CSG copy when the inputs have different
background widths: native `BuildPrimarySegment` thread-local trees use the right
input's background while the main segment uses the left input's background. Native
segment merges can therefore normalize inactive leaf values differently with
multiple workers. Four cases in this fixture differed between unrestricted TBB and
one-thread execution; all cases match the managed implementation with one thread.
The copy methods follow the serial native algorithm; they do not claim identical
inactive values for native executions with different worker counts and unequal widths.

Two further source behaviors are deliberately preserved:

* Mutating CSG may leave overlapping or unused nodes in B. The current `Merge.h`
  implementation does not clear B, despite the blanket statement in `Composite.h`.
* Copy CSG constructs results from selected leaves and fills internal signs.
  Entirely tile-based inputs therefore produce an empty output, unlike mutating CSG.

Managed IEEE floating types are `Half`, `float`, and `double`. Source-derived tests
cover NaN comparisons, signed-zero ties, sparse root tiles, cancellation gating,
node transfer ownership, independent copies, and grid metadata/transform copies.
The managed mutating API rejects aliased tree inputs before mutation; native mutation
with aliased operands is not a supported ownership pattern.


`vector-composite-oracle.cpp` adds 200 native cases for Vec2d, Vec3d, Vec3f,
Vec3i and Vec4d. Each case checks five operations: sum, component product/division,
and native magnitude-ordered min/max with lexicographic ties. Integer division
uses nonzero component divisors; managed division by zero throws instead of
invoking C++ undefined behavior. Additional managed tests cover unordered magnitude
and signed-zero ties. These fixtures regenerate through the same build script.
