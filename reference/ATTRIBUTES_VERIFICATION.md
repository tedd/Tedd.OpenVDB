# Point-attribute storage verification

The in-memory implementation adapts points/AttributeArray.h, AttributeArray.cc and
AttributeGroup.h/.cc from OpenVDB 13.0.1,
f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf. It contains uniform/expanded encoded attributes,
constant and variable strides, typed and codec-independent handles, deep copies, raw
stride copying, scoped factory registration, attribute flags and group bit handles.

Build-AttributesReference.ps1 reproduces 1,074 native profiles. The fixture includes
740 array profiles, 20 exceptional scalar profiles, ten encoded-copy profiles, 48 group
transition profiles and 256 complete mask profiles. Every storage snapshot compares
exact encoded bytes, every decoded element, flags, uniformity, logical length, stride
and total data size. Group mask cases compare all 65,536 combinations of mask and byte.

Codec coverage includes Null, Truncate, position/unit fixed point at 8 and 16 bits, unit
vectors, and Group. Values include Boolean, Int32, Int64, Half, float, double and the
available Vec2/3/4 scalar families. Native fixed-point multiplication and division use
the value's own precision. Native unit-vector decoding produces float components even
for a double-valued attribute. Truncate narrows float to Half or Int32 to Int16; vectors
narrow each component. Exceptional-value profiles retain signed zero, infinities,
NaNs and half-rounding boundary behavior.

Forty-three independent array tests and nine group tests cover bounds/overflow, exclusive
ownership, safe uniform-stride copies, self-copy order, registry isolation/reentrancy,
metadata defaults, streaming handle disposal, invalid codecs, mask semantics and
storage budgets. Warmed typed attribute read/write loops allocate zero managed bytes.

Native contracts retained:
- Requested length zero normalizes to one; variable storage must cover every element.
- Set expands a uniform array even when writing its existing value.
- Fill preserves the current representation; Compact tests decoded values and re-encodes
  the first value if all values compare exactly equal.
- Numeric signed zeros compare equal. Expanded NaN values do not compact.
- NativeEquals deliberately reproduces native comparison of only Size encoded entries
  in an expanded strided array. StorageEquals checks the complete data buffer instead.
- Handles flatten constant strides; variable-stride handles expose dataSize entries with
  handle stride one. A component index can cross a logical element boundary if the
  resulting flattened index is valid.
- Streaming read handles capture the collapse policy when constructed. Dispose resets
  the attribute to uniform encoded zero. Write handles do not perform this destruction.
- GroupWriteHandle construction does not expand. Collapse compacts before changing a
  bit and does not compact again afterward. Explicitly strided group arrays retain
  native iteration over Size rather than dataSize.
- Empty group masks match every valid index; multibit masks require every bit.

Managed adaptations:
- Static abstract codec operations replace C++ associated storage types. Encoded byte
  arrays contain no object references; local spans provide bounded access. No raw
  pointer or pinned address survives a GC safepoint.
- Expand(false) returns zero-initialized encoded bytes, not native uninitialized memory.
  This need not decode to numeric zero for a quantizing codec.
- Unsafe entry points retain bounds checks. Reference ownership is explicit; no
  concurrent mutation of one attribute is permitted.
- CopyValues repeats a uniform encoded value across a stride rather than reproducing
  native memcpy reading beyond that single stored value. Other out-of-buffer stride
  indices and mismatched categories are rejected.
- Fixed-point NaN conversion, invalid group shifts and impossible managed lengths fail
  explicitly instead of invoking native undefined behavior.
- Factories live in a scoped thread-safe registry; no mutable process-global registry
  is required. User factories execute outside the registry lock.
- StorageByteCount reports actual encoded payload bytes; C++ sizeof object overhead is
  not presented as CLR heap usage.

Standalone raw/Blosc stream serialization is implemented in AttributeStreams, with
bounded headers, decoded storage, total allocations, cancellation, exact frame
consumption and independent malformed-input tests. See ATTRIBUTE_STREAM_VERIFICATION.md
for native stream readback evidence. PointDataGrid integration, descriptors,
string attributes and paged/delayed attribute buffers remain separate work.

Fixture: tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-attributes.json.gz.
SHA-256: 5bf7bf131d9a5fa5e116a215faa37ace926e06d9d38c7f0c7c9e98b528bdc407.
