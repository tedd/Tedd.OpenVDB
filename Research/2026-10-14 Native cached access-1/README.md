# Managed versus native OpenVDB cached access

The final repeat measured native OpenVDB at approximately **2.15 times the cached
read throughput** and **2.86 times the cached write throughput** of the managed
port on this coherent, prepopulated workload.

| Operation | Managed milliseconds/operation | Native milliseconds/operation | Managed operations/second | Native operations/second |
|---|---:|---:|---:|---:|
| Cached read | 0.00000805573 | 0.00000374208 | 124 million | 267 million |
| Cached write | 0.00001345045 | 0.00000469788 | 74 million | 213 million |

Values are medians of five process-launch medians from E2. Each launch measures
nine batches of 50,003,968 operations after two seconds of warmup per operation.
Both implementations run serially on logical CPU 16, with alternating implementation
and workload order. The speed ratios divide the managed median by the native median;
paired-ratio distributions are separately retained in `comparison.json`.

## Workload and correctness

Both trees store UInt16 values in a fixed Tree4 5/4/3 hierarchy. The coordinate
sequence covers the same 32 × 32 × 32 region (-16 through 15), with Z changing
fastest. There are 32,768 active voxels and 64 leaves. Seven out of eight successive
operations stay in the same leaf. Setup, warmup, validation and process startup
are excluded from the measured intervals.

Native and managed coordinate hashes and checksums match. Every timed read batch
has an independently calculated expected sum. Every write batch changes values,
then all values, activity and topology are checked outside timing. All 90 managed
timed batches in E2 recorded zero current-thread managed allocation. Native
allocation was not instrumented.

## Environment and scope

- AMD Ryzen 9 5950X, Windows x64, existing High performance power scheme.
- Managed: .NET 11.0.0-rc.1.26425.128, Release, tiered compilation and dynamic PGO
  enabled, server GC; the original historical benchmark used Preview 7 instead.
- Native: OpenVDB 13.0.1 at `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`,
  MSVC 19.51.36260 x64 Release, default safe three-level accessor,
  static native reference library with its SHA-256 retained in environment metadata.
- Shared host performance lock held across dependency staging, both builds,
  correctness validation, warmups and timed launches.

The first epoch (E1, CPU 8) showed significant background load and wider variation.
It is retained in full. E2 repeats the same benchmark source on CPU 16, which had
5% utilization before timing and 0% at the final snapshot. These snapshots do not
prove the host was idle throughout timing. Native write medians still span
0.00000440416–0.00000587976 milliseconds/operation, so ratios remain approximate.

The result covers coherent cached accesses to existing active voxels. It does not
establish a relative advantage for random access, cold data, allocating writes,
meshing, filters, archives or applications. No library implementation was changed.
Only the two user-authorized comparisons were investigated; an optimization
hypothesis catalogue and application profiling are outside this task.

## Artifacts and reproduction

- [Self-contained report](report.html)
- [Final summary and raw samples](comparison.json)
- [Final E2 raw evidence](epochs/E2/raw/)
- [Retained E1 report](epochs/E1/report.html)
- [Hypothesis ledger with both epochs](hypotheses.json)
- [Runner instructions](../../benchmarks/CACHED_COMPARISON.md)

`finalize_report.py` regenerates reviewed reports from existing raw measurements
without rerunning benchmarks. Charts display milliseconds per equal-sized batch
to avoid rounding small per-access values to zero; per-operation values remain
in the JSON, table and report progression text.
