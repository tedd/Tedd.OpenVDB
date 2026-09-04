# Paged Stream Verification

This family ports OpenVDB 13.0.1 `openvdb/points/StreamCompression.h` and `.cc` page composition for point-attribute storage.

## Managed surface

- `PagedOutputStream` groups blocks into exact 1 MiB pages and emits blocks larger than one page independently.
- The size pass emits native signed raw-page headers or compressed-size plus decoded-size headers.
- The data pass emits raw pages or Blosc/LZ4 pages, including OpenVDB's 48-byte compression threshold and zero padding below 128 bytes.
- `PagedInputStream`, `Page`, and `PageHandle` perform the native two-pass header/data replay with shared decoded page ownership.
- Caller-owned streams remain open. Output buffers use `ArrayPool<byte>` and are returned by `Dispose`.
- Decode limits, cancellation, checked arithmetic, truncated input, corrupt Blosc frames, sticky load failures, cached-page replay, and exact handle order are validated.
- The writer reuses pooled Blosc/LZ4 workspaces. After warm-up, repeated one-mebibyte pages allocate no managed heap storage in the measured hot path.

`Dispose` does not flush pending bytes because native `PagedOutputStream` requires an explicit `flush()`. Delayed mapped-file ownership is not present; requests are loaded eagerly, matching native behavior when no mapped-file owner is installed.

## Native differential corpus

`paged-stream-oracle.cpp` was built against OpenVDB 13.0.1 commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, TBB 2022.2.0, c-blosc 1.21.6, and LZ4 1.9.4.

The 25 profiles cover:

- 1, 47, 48, 60, 127, 128, 129, 4,096, 1,048,576, and 1,048,577-byte thresholds;
- exact page fills, boundary spill, independent oversized pages, and three-page block sequences;
- constant, sequential, pseudo-random, and run-pattern data.

Raw pages match native bytes exactly. Compressed pages have equivalent native headers and are verified bidirectionally: managed reads every native stream and the native oracle reads every managed stream. The pure managed LZ4 encoder may choose different valid matches from LZ4 1.9.4, so compressed payload identity is not required.

Canonical fixture: `tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-paged-stream.json.gz`

- Profiles: 25
- SHA-256: `CD37CD4D1D787B0C8115F05B1D80880387E4C23BAA2B7A85FBB2A2735F8BC0ED`

## Managed evidence

- Release: 57/57
- Debug: 57/57
- Release with `DOTNET_EnableHWIntrinsic=0`: 57/57
- Native managed-output verification: 25/25
