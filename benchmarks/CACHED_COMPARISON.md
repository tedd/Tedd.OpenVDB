# Managed and native cached access comparison

The runner compares default safe cached accessors over the same populated UInt16
Tree4 5/4/3. Its 32 × 32 × 32 coordinate sequence matches the existing coherent
access benchmark: signed coordinates -16 through 15, Z changing fastest. Both
implementations use 64 leaves, 32,768 active voxels and the same value sequence.
Consecutive accesses remain in the same leaf seven out of eight times.

Reads accumulate values into a checked checksum. Writes update existing active
values with a different phase for each measured pass. The complete values,
activity and topology are validated before timing and after every sample.
No topology mutation, dataset construction, validation or process startup is timed.

The two harnesses use explicit identical loops and high-resolution elapsed-time
clocks rather than different benchmark frameworks. This makes operation counts,
warmup, batching and observable output directly comparable across languages.
The .NET harness enables tiered compilation and dynamic PGO; the native harness
uses MSVC x64 Release defaults. Both use the same single logical CPU. Implementation
and workload order alternate across launches; medians and all raw samples are kept.

This configuration requires Windows x64, the .NET 11 SDK selected by `global.json`, Visual Studio 2026,
the OpenVDB 13.0.1 checkout, and existing Release native reference libraries from
the reference generators. The runner downloads the SHA-256-verified oneTBB 2022.2
archive if `--tbb-root` is omitted. It does not install dependencies globally.
Builds and restores only write inside `--workspace`.

Run under the shared host performance lock, holding it across the entire driver:

```powershell
python C:/Users/tedd/.codex/skills/scientific-method-performance/scripts/run_with_performance_lock.py --cwd D:/SourceCode/Tedd.OpenVDB -- python benchmarks/run_cached_comparison.py --workspace D:/Workspaces/AI/openvdb-native-cached-access --evidence 'Research/2026-10-14 Native cached access-1'
```

The allocated evidence directory must already contain `hypotheses.json` with the
two preregistered comparison questions; use the committed research record as an
example. For another investigation, allocate a new directory with the skill's
`create_research_run.py` and copy the questions with state `pending` before timing.

Defaults: five paired process launches, nine samples per operation, 50,003,968
operations per sample and two seconds of warmup per operation. `--cpu` selects the
single logical processor (default 8). The driver emits milliseconds per operation
and operations per second, paired ratios, managed allocation counters, raw JSON,
source/library hashes, environment metadata and a self-contained HTML report.

These are coherent cached-access kernel measurements. They do not establish
relative performance for random access, allocating writes, larger working sets,
volume algorithms, file interchange, or complete applications.
