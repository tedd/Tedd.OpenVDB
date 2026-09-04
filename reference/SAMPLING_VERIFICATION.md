# Sampling conformance

Reference: OpenVDB 13.0.1, commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`, Windows x64 MSVC.

The native corpus contains 51 sampling profiles with 40 positions each: Int8/UInt8/Int16/UInt16/Int32, Float, Double, Half, Bool, Vec2d, Vec3d, Vec3f, Vec3i and Vec4d; point, box and quadratic kernels; staggered variants for Vec3; active and inactive values; index/world coordinates and aligned/transformed dual-grid samples. Four result vectors and activity are checked per position. Half, integer and Boolean results are exact. Float/Vec3f tolerance is 2e-6 relative to max(1, magnitude); double vectors/scalars use 2e-13.

The comparison found a real Half conversion discrepancy. Native Half accepts float, so double intermediates must narrow through float before Half. Direct double-to-Half rounding changes results near halfway values. Interpolation also preserves native Z/Y/X stage order, component precision and coefficient conversions. The narrow-integer cases detected missing C++ promotion to int before arithmetic: without it, a constant Int16 field of 30,000 changed under quadratic interpolation. The corrected 8/16-bit paths retain native promotion and narrowing.

`GridSampler<T>` captures tree and transform references. Integer samples bypass interpolation even for staggered fields. `DualGridSampler<T>` caches native approximate map alignment at construction. Custom unmanaged payloads support point sampling; the generic grid sampler restricts interpolating kernels to supported built-in scalar/vector types. Existing numeric scalar sampler overloads retain their INumber constraints.

Independent tests cover positive half-cell staggering, zero-weight active support, mutable transform/reference capture, invalid support bounds, lexicographic vector extrema, parallel read safety and zero allocations after warm-up with cached quadratic staggered sampling through compacting GC.

Regenerate with `Build-SamplingReference.ps1 -OpenVdbSource <source> -OpenVdbBuild <static-build> -TbbRoot <tbb-root> -CMake <cmake-path>`. Fixture hashes are in `FIXTURE_MANIFEST.json`.
