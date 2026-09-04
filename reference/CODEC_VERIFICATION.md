# BloscLZ, Snappy and bitshuffle verification

On **2026-09-04**, the Windows x64 native oracle verified **758** compression/filter fixtures and **24** standard `.vdb` archives. The managed codec suite passes **913 tests**: 827 new codec tests and the existing 86 Blosc tests. Native OpenVDB independently read all **24 managed-written archives** and compared topology, values, activity, background, counts and metadata. This establishes semantic interoperability for the exercised formats, not byte-identical compression or complete compatibility with every Blosc compressor.

## Supported behavior

The reader now supports Blosc frame version 2, compressor format version 1, with compressor IDs 0 (BloscLZ), 1 (LZ4/LZ4HC), 2 (Snappy), and 3 (zlib). Byte shuffle, bitshuffle, unfiltered storage, raw-copy frames, raw splits, split/unsplit blocks, multiple blocks and partial blocks are supported. When both shuffle flags are set, byte shuffle takes precedence for element sizes above one, matching native c-blosc. Native bitshuffle copies the entire block unchanged when its complete-element count is not divisible by eight; fractional element bytes are retained.

BloscLZ decoding handles short/extended matches, repeated length extensions, overlapping copies and far offsets through 73,727 bytes. Snappy decoding supports the raw block format used by Blosc: UInt32 varint size headers, literal lengths with up to four extension bytes, and all three copy encodings. Neither decoder allocates a buffer from an untrusted inner length. All copies and length additions are bounded by the caller-provided span; the Blosc frame parser retains its allocation budget for the block table and filter scratch buffer.

The writer remains LZ4 with native OpenVDB's four-byte byte-shuffle element size. There is no new encoder-selection API, native runtime dependency, P/Invoke, or managed package dependency. This milestone was subsequently extended by [dictionary-free Zstd support](ZSTD_VERIFICATION.md). Unknown compressor versions and unknown Blosc frame versions remain explicit `NotSupportedException` cases. A raw-copy frame needs no compressor and keeps the existing native-compatible flag behavior.

## Native evidence

`codec-oracle.cpp` uses pinned upstream native compressors and decoders. Every generated compressed stream is independently decompressed by native code before publication; the fixture records a SHA-256 digest of the actual native output. Managed tests decode into guarded spans, hash every output byte, and compare that digest. Reflection creates typed test delegates to internal span APIs; it does not add a public storage format or a runtime native interface.

| Fixture group | Cases | Coverage |
|---|---:|---|
| Raw BloscLZ | 92 | Native compression levels 1/5/9, literal boundaries, long extensions, near/far offsets, overlapping copies |
| Raw Snappy | 125 | Native compressed patterns, empty block, 60/61-byte literal transition, 65,535/65,536-byte boundaries, 1/2/4-byte copies, 70,000-byte offsets |
| Bitshuffle filter | 170 | Element sizes 1/2/3/4/8/12/16/24/255, zero and partial blocks, counts around multiples of eight, fractional element tails |
| Blosc frames | 371 | Four supported compressors, all three filter settings, split/unsplit/multiple/partial blocks, simultaneous shuffle flags; includes 11 historical compatibility files |

The historical files are the pinned c-blosc `compat/*.cdata` BloscLZ/Snappy files from versions 1.3.0, 1.7.0, 1.11.1 and 1.14.0, plus the 1.18.0 BloscLZ and BloscLZ/LZ4-bitshuffle files. Their four-million-byte outputs were actually decompressed and hashed by the native oracle. Rare raw token encodings are constructed by the generator and accepted/decoded by the independent native decoder before being used as expected results.

`blosc-oracle.cpp generate-codecs` inserts native-compressed frames into native-generated FloatGrid archives and reads those archives back through native OpenVDB. The 24 cases cover BloscLZ/Snappy, no shuffle/byte shuffle/bitshuffle, full split blocks, smaller unsplit blocks, three-byte element partial blocks, and one-byte elements. Native canonical uncompressed archives are retained beside them. Managed tests read these files, and the reproduction script exports the resulting grids through the unchanged managed LZ4 writer for independent native verification.

Negative tests cover truncated raw streams at every prefix, overflowing literal/preamble lengths, invalid or truncated copy offsets, output-size mismatch, absent following BloscLZ sequences, invalid bitshuffle dimensions, guarded destination bounds and allocation rejection before filter decoding. Existing Blosc tests retain header/table/split corruption, unsupported versions, LZ4 corruption, half encoding and writer behavior coverage. Structurally valid corruption remains undetectable in checksum-free codecs.

## Reproduce

```powershell
./reference/Build-CodecReference.ps1 -OpenVdbSource D:/source/openvdb
```

The script checks pinned revisions, builds the native-only static dependencies and two oracle targets, regenerates and atomically publishes verified fixtures, runs the 913 codec tests, and independently verifies 24 exported managed archives. `-SkipBuild` reuses the native targets. `-TbbRoot`, `-CMake` and `-DotNet` can select local toolchains. The complete verification path was executed with `-SkipBuild` after explicitly building both oracle targets; native dependency builds were separately completed by the coordinating task. Ordinary managed test runs use committed fixtures without native tools.

| Component | Observed revision |
|---|---|
| OpenVDB | 13.0.1, `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf` |
| c-blosc | 1.21.6, `616f4b7343a8479f7e71dd3d7025bd92c9a6bbd0` |
| Snappy | 1.2.2, `6af9287fbdb913f0794d0148c6aa43b58e63c8e3` |
| Native | Visual Studio 18 2026, MSVC 19.51.36256, x64 static `/MT` |
| Managed | .NET SDK `11.0.100-preview.7.26381.103` |

The all-codec c-blosc build uses `DEACTIVATE_SNAPPY=OFF`, external Snappy 1.2.2 and zlib 1.3.2, and bundled LZ4/BloscLZ/Zstd. Snappy and zlib static libraries are linked separately into the oracle. MSVC runtime policy `CMP0091=NEW` and `MultiThreaded` are set; c-blosc also requires the quoted `CMAKE_POLICY_VERSION_MINIMUM=3.5` setting with this CMake version. See [the original Blosc verification](BLOSC_VERIFICATION.md) for zlib/oneTBB provenance. Build products and dependency checkouts are ignored.

Implementation sources: c-blosc `blosc/blosclz.c`, `blosc/blosc.c`, `blosc/shuffle.c`, `blosc/bitshuffle-generic.c`, `blosc/bitshuffle-generic.h`; Google's [Snappy format description](https://github.com/google/snappy/blob/6af9287fbdb913f0794d0148c6aa43b58e63c8e3/format_description.txt) and native decoder. Retained FastLZ/bitshuffle MIT and Snappy BSD licenses appear in `THIRD_PARTY_NOTICES.md`.

## Subsequent Zstd implementation

The following source plan was implemented and verified in [the Zstd milestone](ZSTD_VERIFICATION.md).

The pinned c-blosc source embeds Zstd **1.5.6**. Its wrapper calls ordinary `ZSTD_compress` and `ZSTD_decompress`; each compressed split therefore contains a standard Zstd frame, not a special Blosc raw-block variant. The next useful bounded milestone is a managed decoder for dictionary-free Zstd frames produced by this wrapper:

1. Validate the frame header, declared content size, window size and block bounds against the existing output/allocation limits. Decode raw and RLE blocks.
2. Implement a bounded reverse bit reader, Huffman literals (including FSE-compressed weights), FSE sequence tables, repeated tables/offsets, and overlap copies across blocks. These are necessary for actual compressed OpenVDB payloads; raw/RLE support alone is insufficient.
3. Validate optional XXH64 checksums, exact input consumption and final output size. Explicitly reject dictionary-dependent and unsupported legacy forms until supported and tested.
4. Extend the existing native oracle with Zstd compression levels, one/four-stream Huffman literals, sequence-table modes, repeated offsets, multi-block frames, checksum failures and malformed entropy tables. Enable compressor ID 4 only after native conformance passes.

Relevant bundled source: `decompress/zstd_decompress.c` (frame parsing), `decompress/zstd_decompress_block.c` (literals and sequences), `decompress/huf_decompress.c`, `common/fse_decompress.c`, `common/entropy_common.c`, `common/bitstream.h`, and `common/xxhash.c`. The same static native build can produce these fixtures. No Zstd managed runtime code or new dependency is introduced by this milestone.
