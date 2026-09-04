# Value transformer verification

The managed implementation adapts OpenVDB 13.0.1 tools/ValueTransformer.h at
f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf. It provides foreach-style stored-value callbacks,
output-tree transformations, accumulate/join reductions, and activation with minimum,
maximum, addition and multiplication.

Build-ValueTransformerReference.ps1 compiles value-transformer-oracle.cpp against that
upstream revision. The compressed JSON contains 750 native profiles: 720 arithmetic
profiles (15 value types, six initial tree states, four operations, tree and accessor
dispatch), 12 foreach profiles, six reductions and 12 output transformations. Each
arithmetic profile has five successive writes with exact value, activity, leaf count,
depth and neighboring-value checks. Output callbacks compare every stored-value group
and 97 coordinate probes. Int64 values above 2^53 are serialized as integers and compared
without a floating-point conversion.

Native callback fixtures use one TBB worker. Managed callback fixtures likewise use one
partition, including the threaded execution path. This isolates native operation-copy,
initial-accumulator, iterator and merge semantics from scheduler-dependent split counts
and ordering. Independent tests exercise multiple partitions, shared counters with
Interlocked, explicit reference-operation copies, per-partition reduction seeds, worker
backgrounds, overlapping and disjoint output writes, all three merge policies, empty
input, topology invalidation, callback failures, cancellation and materialization budgets.

Parallel managed partitions have a deterministic index order for joining and merging.
This is not an emulation of TBB's scheduling or its exact number of operation copies.
The root output worker writes the supplied tree; other output workers start with zero
background, including when the caller's output background is nonzero. Mutable nonshared
operations are copied even for serial execution. Action overloads correspond to shared
native const operations. Reference-type operation objects require an explicit copier
when copies are needed.

Callbacks may update their own stored values or their supplied output accessor.
Threaded callbacks must synchronize shared operation state themselves. Tree structure
must not change while it is traversed; activity-mask writes within a shared leaf also
require external coordination. An input tree cannot also be the output tree; the managed
API rejects this alias rather than merely logging it. Cancellation or callback failures
may retain already-completed writes, with no retry. Parallel input materialization is
bounded by MaximumItems and completes before callbacks begin; serial budget failures
can follow completed callbacks.

Arithmetic modifiers use closed generic unmanaged dispatch and spans over local values,
without boxing, dynamic code, delegates per write, pinned objects or persistent raw
pointers. Warmed integer tree/accessor modifier loops are verified to allocate zero
managed bytes on the calling thread. Vector min/max selects a whole vector using native
lexicographic order; it does not take independent component extrema. Boolean sum/max
uses OR and product/min uses AND. Active false values remain valid data. Unchanged
active tiles retain their sparse representation. Additional .NET numeric types are
supported by the same arithmetic implementation; the native fixture covers bool,
Int32, Int64, float, double, half and Vec2/3/4 with Int32/float/double components.

Fixture: tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-value-transformer.json.gz.
SHA-256: dc5ce9aa24e8f2ed3a278f472ea27dda95859e3c4f7d854cd1ff7323d46a703b.
The family currently contains 785 tests (750 native profiles and 35 independent cases).
