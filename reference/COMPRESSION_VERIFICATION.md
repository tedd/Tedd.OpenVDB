# Native compression and half-float verification

On **2026-09-04**, `Build-CompressionReference.ps1` completed **39 managed tests and 20 independent native verifications** against OpenVDB 13.0.1, revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`.

The corpus contains 20 native-written format225 files: Int32 with flags 0/1/2/3, and Float/Double with each flag combination in full and half precision. Flags are none, zlib, active mask, and zlib plus active mask. Five additional canonical files are produced by native readback followed by native uncompressed full-precision writing. Managed expectations therefore use observed native half quantization, not a second copy of the managed rounding algorithm.

Every native file passes managed decoding; every corresponding managed-written file passes independent native decoding and comparison of all stored values, activity, structural topology, counts, background, metadata and affine transform. The corpus covers all seven active-mask metadata cases, raw fallback and compressed chunks, empty half buffers, non-background inactive values, tiles, negative coordinates, signed zero, infinities, quiet NaN, half subnormal/overflow/tie boundaries and Double-to-Float-to-Half rounding. Native root tiles retain their full scalar precision even when the remaining tree is half encoded; the managed writer preserves this behavior. Quiet NaNs compare semantically; this does not establish bit-identical preservation of every signaling NaN payload.

Malformed input tests cover invalid mask codes, unsupported compression bits, impossible signed chunk sizes, damaged zlib headers/checksums, truncated trailers, decompressed-size mismatches and resource limits. `ZLibStream` provides compression without a native runtime dependency. An explicit Adler-32 check also rejects missing or damaged trailers.

The public options are `VdbWriteOptions.Compression` (`VdbCompression.None`, `Zlib`, `ActiveMask`, or their bitwise combination) and nullable `SaveFloatAsHalf`. An omitted half option follows Boolean `is_saved_as_half_float` metadata; an explicit option overrides it in output without changing the source grid. Descriptor suffix and metadata must agree on read. Non-Boolean half-control metadata remains unsupported.

Reproduce on Windows x64:

```powershell
./reference/Build-CompressionReference.ps1 -OpenVdbSource D:/source/openvdb
```

The actual verification used Visual Studio 18 2026, MSVC 19.51.36256, .NET SDK `11.0.100-preview.7.26381.103`, oneTBB 2022.2.0, and zlib 1.3.2. Zlib was built statically with `/MT`; its official source archive SHA-256 is `bb329a0a2cd0274d05519d61c667c062e06990d72e125ee2dfa8de64f0119d16`. OpenVDB was built with zlib enabled and Blosc, Imath, delayed loading and explicit template instantiation disabled. Native binaries/dependencies remain ignored build artifacts. Fixture replacement is atomic after generation; UUIDs can change during regeneration.

This extends the earlier [uncompressed verification](IO_VERIFICATION.md). It does not imply support for Blosc, legacy file versions, arbitrary tree layouts, point grids or full upstream API coverage.
