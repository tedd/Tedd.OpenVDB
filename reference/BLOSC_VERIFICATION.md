# Blosc verification

On **2026-09-04**, the Windows x64 native oracle generated 52 compressed grid profiles and independently verified all 52 corresponding managed-written files. The Blosc suite passes **86 tests**; the combined IO and mask-inheritance suites pass **286 tests** in Release. This is semantic interoperability, not byte-identical compression or comprehensive compatibility with all Blosc compressors.

The managed codec reads Blosc format 2 frames with compressor format 1 for LZ4/LZ4HC and zlib. It supports byte shuffle, unshuffled buffers, raw-copy frames, raw or compressed splits, multiple blocks, and final partial blocks. It bounds decoded lengths, validates block tables and split boundaries, and applies the archive's allocation budget to scratch allocations. The writer emits LZ4 with byte shuffle using the native OpenVDB four-byte shuffle element size. Compression flag 4 selects Blosc; flags 5, 6 and 7 are also covered, including native Blosc precedence when the zlib bit is simultaneously set.

Actual compressed BloscLZ, Snappy and Zstd streams, bit shuffle and unknown format versions throw `NotSupportedException`. Raw-copy frames do not require a compressor and therefore remain readable regardless of unused compressor/filter flags. The writer does not expose alternate Blosc compressor selection. Native c-blosc includes other compressors only to produce unsupported-input test fixtures; no native library, P/Invoke or external compression package is required at managed runtime.

The native corpus contains:

- 52 full archive profiles: Int32, Float, Double, Int64, Bool, Mask, Vec3i, Vec3s and Vec3d with compression flags 4 through 7, plus half encoding for the four floating-point scalar/vector types.
- 11 specifically constructed native frames, each embedded in a valid native-created Float grid and independently read back by native OpenVDB: split and unsplit LZ4, multiple blocks, a partial block with a three-byte shuffle size, unshuffled data, raw-copy data, random data, zlib, bit shuffle, BloscLZ and Zstd. Eleven uncompressed native canonical files provide their exact decoded values.

Every managed-written archive is read by native OpenVDB and compared for all stored values, activity, topology, counts, background and supplied metadata. Exceptional floating values are compared semantically for NaN and by sign for zero. Tests additionally cover malformed headers, unsupported versions, reserved flags, offset aliases/overlaps, split lengths, trailing data, invalid LZ4 references and lengths, truncation, allocation limits, empty half buffers, and actual compression of repetitive values. Blosc LZ4 has no checksum, so structurally valid payload corruption is not universally detectable.

## Reproduce

```powershell
./reference/Build-BloscReference.ps1 -OpenVdbSource D:/source/openvdb
```

`-SkipBuild` reuses the pinned native build. `-TbbRoot` selects an existing copy of the pinned oneTBB distribution. Generated native/managed build outputs are ignored; fixture publication uses a temporary file followed by rename. Ordinary managed test runs consume committed fixtures without native tools.

Observed dependencies and provenance:

| Component | Observed version / revision |
|---|---|
| OpenVDB | 13.0.1, `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` |
| c-blosc | 1.21.6, `616f4b7343a8479f7e71dd3d7025bd92c9a6bbd0` |
| Bundled LZ4 | 1.9.4 |
| zlib | 1.3.2, official tar SHA256 `bb329a0a2cd0274d05519d61c667c062e06990d72e125ee2dfa8de64f0119d16` |
| oneTBB | 2022.2.0, Windows archive SHA256 `4243442411bae9e3b80bfe2e47b5b571e8ae541ac45cfb991036b3a16a0c6012` |
| Native toolchain | Visual Studio 18 2026, MSVC 19.51.36256, x64 static `/MT` |
| Managed toolchain | .NET SDK `11.0.100-preview.7.26381.103` |

Source references: OpenVDB `io/Compression.cc` and `io/Compression.h`; c-blosc `blosc/blosc.c`, `blosc/shuffle.c` and `README_CHUNK_FORMAT`; bundled LZ4 `internal-complibs/lz4-1.9.4/lz4.c`. Corresponding BSD licenses are reproduced in `THIRD_PARTY_NOTICES.md`. The managed LZ4 implementation follows the block format and produces interoperable compressed output; it does not reproduce native LZ4HC match selection or compression ratios.

Earlier IO verification documents describe their historical milestones. This milestone supersedes their Blosc exclusion for the explicitly supported subset above. Point/custom grid layouts, forward instance references, legacy file versions and delayed loading remain outside the codec's verified format225 scope.
