# Performance design and measurement

Target runtime: .NET 11 Preview 7, SDK `11.0.100-preview.7.26381.103`.
[Microsoft's runtime notes](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/runtime)
describe the bounds-check/branch optimizations relevant to the Span and array loops.
The code does not assume undocumented runtime object layouts.

Hot leaf reads and writes use a reused `ValueAccessor<T>`. Repeated operations in the
same leaf skip root/internal dictionary lookups. Accessor invalidation uses a tree
structural version; no unsafe pointer can outlive a leaf replacement or compacting GC.
Values are unmanaged and buffers are fully initialized before exposure. Sparse child
reference tables avoid GC scans across dense pointer-sized internal node tables.
Mask Boolean operations use `Vector<ulong>` when available, with tested scalar tails
and a complete scalar implementation when hardware intrinsics are disabled.

The smoke benchmark warms each operation five times, then reports the median of seven
measurements with current-thread allocated bytes. The runtime, architecture, GC mode,
vector width and processor count are emitted in JSON. It is not a substitute for a
controlled BenchmarkDotNet study, JIT disassembly, or representative game workloads.
Do not compare its numbers between machines without controlling power, frequency,
process load, runtime settings and working-set size.

```powershell
dotnet run --project benchmarks/Tedd.OpenVDB.Benchmarks -c Release
```

The first measured x64 run on the development machine (32 logical processors, 256-bit
Vector, server GC) observed approximately 9.8 ns/cached coherent read and 17.0 ns/cached
coherent write, with zero bytes allocated over 1,000,000 operations. This is a local
observation, not a performance guarantee or an asserted advantage over native OpenVDB.
The tracked baseline JSON can be regenerated with the command above.

The benchmark application opts into server GC; the reusable library does not change
host GC configuration. Unsafe pointer arithmetic, pinning, no-GC regions, pooled
ownership or hardware-specific paths should be introduced only when a measured
bottleneck justifies their added lifetime and correctness obligations.

Windows x64 NativeAOT publication and execution were also verified for the sample, including Int32 native-format VDB round-tripping. This does not establish AOT compatibility for all generic instantiations.
