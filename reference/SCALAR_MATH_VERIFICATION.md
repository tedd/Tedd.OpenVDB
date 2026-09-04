# Scalar Math.h kernel verification

Pinned upstream: OpenVDB 13.0.1 at `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`.

`Math/VdbMath.cs` and `Math/VdbMath.Floating.cs` port the scalar clamp, cubic smooth step, powers, reciprocal, sign/zero crossing, extrema, rounding, remainder, decimal rounding, bit conversion and ULP comparison kernels from `math/Math.h`. They also expose native constants and scalar transcendental wrappers. Existing approximate comparison and random-number APIs remain separate families. This is not a claim of complete Math.h/template API parity for arbitrary native user-defined types.

The implementation uses managed .NET 11 numeric interfaces and Span. Native code is used only to regenerate reference fixtures; no native runtime dependency is introduced.

## Reference fixture

`tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-scalar-math.json`

- Size: **563,359 bytes**.
- SHA-256: **926523A3AB2EBFC6D81C5AF37BC21E09FB6FFDA51D47078494104C8EA479FD32**.
- **1,652 profiles**: 840 core Float/Double/Half cases, 560 Float/Double rounding/transcendental/ULP cases, and 252 Float/Double/Half min/max and extrema-index cases.
- Each floating type includes 256 deterministic pseudorandom inputs and 24 edge inputs: negative ties, signed zero, subnormal values, large magnitudes, infinities and NaN.
- The profiles contain **29,344 numeric results and 9,184 predicate/index results**. Both fixed-arity and Span extrema are tested against the same native outputs.
- Core arithmetic, Half results, non-transcendental rounding, signed zeros, predicates and indices must match exactly. NaN output is compared by classification. Float transcendental results permit relative error 3e-7; Double permits 3e-15, scaled by max(1, absolute expected result). Half power results remain exact.

`MathScalarTests` adds **27 independent cases** covering analytic smooth-step values, NaN clamp behavior, grouped extrema, signed-zero priorities, index ties, negative rounding/remainder semantics, binary-exact decimal ties, integer promotion, unsigned arithmetic, safe integral conversion, NaN ULP payloads, excessive representation distances, argument guards, and zero allocation for repeated Span/scalar operations.

Final targeted gates: **1,680/1,680 passed in Release, Debug, and Release with hardware intrinsics disabled**, with no skipped tests. The total comprises 1,652 native profiles, one provenance/coverage assertion, and 27 independent cases. Formatting completed successfully.

## Source semantics retained

- `Clamp01(NaN)` returns zero; `ClampTest01(ref NaN)` returns true and writes one. `SmoothUnitStep(NaN)` returns zero.
- `Pow(x, int)` uses repeated multiplication in the native order. It does not substitute exponentiation by squaring. Half `Pow3` evaluates promoted Float products before its final Half conversion; integer-exponent Half powers round each compound product as native does.
- Min/max retain the first argument when comparisons are unordered or equal, preserving NaN position and signed-zero selection. Native overload groupings for two through eight inputs are retained in both fixed-arity and Span APIs. Longer spans are an explicit managed extension using an ordered left fold. Index extrema resolve finite ties to the highest component index.
- `ZeroCrossing` evaluates the literal product test. Underflow of two positive values can produce a zero crossing; zero multiplied by infinity yields NaN and does not pass that test.
- Native `Round` computes floor(x + 0.5), so ties go toward positive infinity. Basis rounding uses the truncating remainder formula, including its unusual behavior for negative operands.
- Native `Truncate` rounds decimal digits rather than truncating them. Its Float intermediate product and Double 0.5 addition are preserved. The decimal factor follows native x64 `size_t` modulo-2^64 multiplication, including wrapped factors above 19 digits and zero factors at 64 digits or more. The latter produces IEEE NaN for finite inputs; this is intentional source parity.
- ULP comparison includes nonfinite representations and can accept identical or adjacent NaN payloads, as native code does. Signed zeros compare equal.

## Managed boundaries

Numeric generic methods support .NET numeric implementations; native fixture claims are limited to the named tested types. Other generic types are not evidence of native custom-type compatibility. Non-generic rounding/transcendental entry points cover Float and Double, with the native Half floating-power overload exposed separately.

Invalid clamp/smooth-step bounds are rejected. `Pow(x, int.MinValue)` and decimal digit counts above `int.MaxValue` are rejected because native signed conversion/negation is not a valid portable contract. `Floor` and `Ceil` use checked Int32 conversions instead of undefined out-of-range native casts. Signed integer arithmetic requires representable results; unsigned arithmetic preserves wrapping. Signed minimum absolute values throw through the .NET numeric implementation.

The ULP implementation computes representation distance without signed overflow. Close pairs, zero boundaries and nonfinite neighbors are compared to native. For distant opposite-sign values whose native signed subtraction or absolute value would overflow, the managed result uses the actual unsigned distance; this is an explicit safety correction rather than a native overflow-parity claim.

The decimal factor becomes zero after 64 native unsigned multiplications, so large valid digit counts use that proven result directly instead of repeating zero multiplications.

## Reproduction

`reference/Build-ScalarMathReference.ps1` accepts the pinned OpenVDB source, static Release build, oneTBB root and optional CMake/generator paths. It checks the source revision, builds `reference/scalar-math-oracle.cpp`, emits the fixture and prints its hash. The oracle calls the original source templates and functions; it does not copy the managed formulas.

Managed filter: `FullyQualifiedName~MathScalar`. For the scalar gate, set `DOTNET_EnableHWIntrinsic=0` and run the Release test binary.
