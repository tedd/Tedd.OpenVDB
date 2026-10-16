# Performance design and measurement

Build with the SDK selected by [global.json](https://github.com/tedd/Tedd.OpenVDB/blob/main/global.json). Runtime, JIT tiering, dynamic PGO, GC configuration, and access pattern all affect measurements. A timing from one runtime or workload is not a guarantee for another.

## Cached access

Reuse a `ValueAccessor<T>` for repeated voxel operations. Its primary leaf cache handles repeated access within an 8³ leaf, while a 64-entry direct-mapped secondary cache handles revisited leaves. Full origin checks reject cache-slot collisions. Root and internal-node lookup occurs on a genuine cache miss.

The common read/write hit paths are inlined; secondary and tree-resolution paths are separate non-inlined methods. Dense writes avoid updating activity words that are already fully active. Partial activity, large working sets, and colliding leaf coordinates have different costs.

Each accessor retains managed references to cached leaves. Structural-version checks invalidate those references after topology replacement, pruning, or clearing. Leaf value arrays remain accessible through their owning leaves, so buffer swaps and compacting GC preserve accessor correctness. Call `Clear()` to release cached references when an accessor will be idle.

Keep one accessor per thread. Reads may run concurrently only while the tree is not mutated. Budget per-accessor cache storage when creating many accessors; the library does not provide arbitrary concurrent tree mutation.

## Scoped native comparison

The 2026-10-15 Windows x64 comparison measured an existing UInt16 Tree4 with 32,768 voxels across 64 leaves on an AMD Ryzen 9 5950X. The managed runtime was .NET 11 RC1; native OpenVDB was 13.0.1. Both used the same warmed coherent coordinate stream, with setup and validation outside the timed loops.

| Cached operation | Unmodified native OpenVDB | Tedd.OpenVDB |
|---|---:|---:|
| Read | 3.588 ns/op | 3.112 ns/op |
| Write | 4.627 ns/op | 4.252 ns/op |

These are medians of five process-launch medians, each containing nine batches of 50,003,968 operations. Managed loops allocated zero bytes. Launch order and read/write order alternated; all samples were retained. The [raw observations and protocol](https://github.com/tedd/Tedd.OpenVDB/tree/main/Research/2026-10-15%20Cached%20read-write%20comparison-1) include variability and environment details.

This comparison covers two coherent cached-access kernels. Random access, topology mutation, sparse activity, collisions, other value types, and other public methods require their own measurements. The exhaustive three-way public-API comparison is incomplete; experimental native variants are not the native baseline in the table.

## Reproduce measurements

The general smoke benchmark emits runtime, architecture, GC mode, vector width, elapsed time, and current-thread allocation:

```powershell
dotnet run --project benchmarks/Tedd.OpenVDB.Benchmarks -c Release
```

The dedicated cached-access caller supports coherent, random, partial-activity, 512-leaf, collision, and topology-churn fixtures:

```powershell
dotnet run --project benchmarks/CachedAccess.Managed -c Release -- --pattern coherent
```

Use an otherwise idle host, fixed runtime settings and CPU affinity, representative warmup, fresh matched controls, and complete process-launch budgets for comparative work. The smoke benchmark alone does not establish cross-machine or application-wide performance.

## Other storage paths

Mask Boolean operations use `Vector<ulong>` where available, with tested scalar tails and a scalar implementation when hardware intrinsics are disabled. Span transfers and pooled codec workspaces avoid intermediate allocations on their supported paths. Sparse child-reference tables reduce scans of unused internal entries.

Benchmark applications select their own GC mode; the library does not change host GC configuration. Windows x64 NativeAOT sample validation covers the exercised generic instantiations; it does not establish compatibility for every unmanaged type.
