# Zstd verification

On **2026-09-04**, the managed dictionary-free Zstd decoder passed **514 tests**, including **478 independently generated/native-decoded fixtures** and **12 native `.vdb` profiles**. Native OpenVDB independently read all **12 managed-written exports** and verified topology, values, activity, counts, background and metadata. The existing LZ4 writer remains unchanged.

## Supported format

Blosc compressor ID 4 now decodes standard dictionary-free Zstd frames. The implementation supports raw, RLE and compressed blocks; raw/RLE/Huffman/repeated literals; direct and FSE-compressed Huffman weights; one- and four-stream Huffman decoding; predefined/RLE/compressed/repeated FSE sequence tables; repeated offsets including the zero-literal and repeat-minus-one cases; cross-block references; optional content sizes; XXH64 checksums; concatenated frames; and skippable frames. Blosc byte shuffle and bitshuffle apply after decompression as before.

Every sequence, entropy-table size, input read, match reference and decoded length is bounded. The decoder uses the supplied output span as history, so a declared window does not cause a window-sized allocation. Table/literal scratch storage is reserved against the existing archive allocation budget before allocation, then reused across blocks and frames. The native 64-bit window-log maximum of 31 is enforced; actual match distances must fit both the declared window and produced frame output. Per-frame state resets prevent references into a previous concatenated frame.

Nonzero dictionary IDs and unsupported legacy/magicless formats throw `NotSupportedException`. Undeclared external dictionary references or absent entropy tables fail validation. Dictionary loading, Zstd encoding, and alternate writer compressor selection are not implemented. No native runtime library, P/Invoke or managed package dependency was added.

## Native evidence

`zstd-oracle.cpp` links the same pinned static c-blosc build as [the other codecs](CODEC_VERIFICATION.md), using its bundled Zstd **1.5.6**. Each fixture is compressed and independently decompressed by native Zstd or native Blosc; the fixture contains the SHA-256 of every actual native output byte. Tests decode into guarded spans and compare complete-output digests.

The 478 fixtures comprise 451 standard Zstd streams, 24 Blosc Zstd/filter combinations, and three native historical `compat/blosc-{1.11.1,1.14.0,1.18.0}-zstd.cdata` streams. The historical outputs contain four million bytes each. Generated inputs range from empty through 262,144 bytes, with compression levels -5, 1, 3, 9 and 19; constant, periodic, random and skewed alphabets; known/unknown content sizes; checksums; and target compressed-block sizing. Native-accepted crafted streams cover the otherwise absent literal-RLE/sequence-RLE combination and repeat-offset transitions.

The generator reports observed native compressed-block feature coverage:

| Feature | Observed occurrences |
|---|---:|
| Raw / RLE / compressed blocks | 182 / 15 / 961 |
| Raw / RLE / compressed / repeated literals | 330 / 1 / 104 / 526 |
| Direct / FSE Huffman weights | 96 / 8 |
| One / four Huffman streams | 34 / 596 |
| Literal-length FSE modes 0 / 1 / 2 / 3 | 214 / 19 / 76 / 636 |
| Offset FSE modes 0 / 1 / 2 / 3 | 198 / 27 / 84 / 636 |
| Match-length FSE modes 0 / 1 / 2 / 3 | 186 / 43 / 76 / 640 |
| Checksummed frames | 213 |
| Inputs containing multiple blocks | 82 |
| Explicit window descriptors | 114 |

These counters describe the generated standard-stream corpus; additional Blosc and historical cases are independently checked but not included in the block counters.

Twelve native FloatGrid archives cover no shuffle/byte shuffle/bitshuffle, split and unsplit blocks, multiple blocks, partial three-byte elements and one-byte elements. Native OpenVDB reads every generated archive before publication. Managed reads are compared with native canonical archives; exported managed LZ4 archives are then independently read and verified by native OpenVDB. This is semantic interoperability, not reproduction of native Zstd compression choices.

Negative tests cover malformed headers, reserved flags, excessive windows/content sizes, dictionary IDs, truncated blocks and entropy streams at every prefix of representative fixtures, missing repeat tables, invalid repeat distances, checksum corruption, destination-size mismatch, and allocation rejection before entropy decoding. A further 1,024 deterministic compressed-stream mutations assert bounded output and controlled errors. This is a focused corruption corpus, not an exhaustive fuzzing proof.

## Reproduce

```powershell
./reference/Build-ZstdReference.ps1 -OpenVdbSource D:/source/openvdb
```

`-SkipBuild` reuses the static native targets. `-TbbRoot`, `-CMake` and `-DotNet` select installed tools. The script regenerates and atomically publishes verified fixtures, runs 514 Zstd tests, and verifies all 12 managed exports through native OpenVDB. The complete `-SkipBuild` workflow was executed after explicitly rebuilding the Zstd and OpenVDB oracle targets. Dependency build provenance remains in [codec verification](CODEC_VERIFICATION.md): OpenVDB `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, c-blosc `616f4b7343a8479f7e71dd3d7025bd92c9a6bbd0`, MSVC 19.51.36256 x64 `/MT`, .NET SDK `11.0.100-preview.7.26381.103`.

Ported source paths in bundled Zstd 1.5.6: `decompress/zstd_decompress.c`, `decompress/zstd_decompress_block.c`, `decompress/huf_decompress.c`, `common/fse_decompress.c`, `common/entropy_common.c`, `common/bitstream.h`, `common/zstd_internal.h`, and `common/xxhash.h`. The bounded managed implementation replaces speculative native reads/copies with span checks; it does not reproduce native SIMD dispatch or assembly. The upstream BSD license, including the bundled checksum implementation, is retained in `THIRD_PARTY_NOTICES.md`.

This milestone supersedes earlier Zstd exclusions only for the explicitly documented dictionary-free format. It does not expand `.vdb` support to point/custom tree layouts, old archive versions, delayed loading or memory mapping.
