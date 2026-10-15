"""Summarize the complete, fixed-budget cache backport experiment."""
import hashlib
import json
from pathlib import Path
import re
import zipfile
import winreg

ROOT = Path(__file__).resolve().parents[2]
EVIDENCE = ROOT / 'Research/2026-10-15 Managed cache backport-1'
patterns = ['coherent', 'random', 'partial', 'large', 'large-random', 'collisions', 'churn']
rows = []
for pattern in patterns:
    data = json.loads((EVIDENCE / f'confirmation-{pattern}-summary.json').read_text())
    for name, control in data['final_base'].items():
        port = data['final_hot'][name]
        original, modified = control['nsPerOperation'], port['nsPerOperation']
        rows.append(dict(pattern=pattern, method=name, originalNs=original, portNs=modified,
            elapsedChangePercent=100 * (modified / original - 1),
            throughputChangePercent=100 * (original / modified - 1),
            originalLaunchMedians=control['launchMedians'], portLaunchMedians=port['launchMedians']))
raw_runs = sorted((EVIDENCE / 'raw').glob('confirmation-*-final_*-*.json'))
allocation_failures = []
for path in raw_runs:
    run = json.loads(path.read_text())
    if run['pattern'] == 'churn':
        continue
    for result in run['results']:
        if any(result['allocatedBytes']):
            allocation_failures.append(path.name)
regressions = [row for row in rows if row['elapsedChangePercent'] > 5]
decision = dict(retained=False, reason='Mandatory regression limit exceeded; library implementation restored.',
    regressionLimitPercent=5, failedMetrics=[r['method'] for r in regressions],
    steadyStateAllocationFailures=allocation_failures, confirmationProcesses=len(raw_runs),
    sourceBaseline='3e9e35c7713a5ac7164b32e712646ec8a9ebb34e', results=rows)
test_log = (EVIDENCE / 'raw/tests.log').read_text(encoding='utf-8')
test_summary = re.search(r'Passed!\s+- Failed:\s+(\d+), Passed:\s+(\d+), Skipped:\s+(\d+), Total:\s+(\d+)', test_log)
if not test_summary or int(test_summary[1]):
    raise ValueError('A passing retained-source test suite is required')
decision['retainedSourceTests'] = dict(zip(('failed', 'passed', 'skipped', 'total'), map(int, test_summary.groups())))
with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r'HARDWARE\DESCRIPTION\System\CentralProcessor\0') as key:
    decision['processor'] = winreg.QueryValueEx(key, 'ProcessorNameString')[0].strip()
if not regressions:
    raise RuntimeError('No failed guardrail: this report must not prescribe rejection without evidence')
(EVIDENCE / 'comparison.json').write_text(json.dumps(decision, indent=2) + '\n')

# Preserve complete diagnostic output in compressed form; selected caller code
# remains readily inspectable without unrelated serializer assembly listings.
disassembly = EVIDENCE / 'disassembly'
disassembly.mkdir(exist_ok=True)
diagnostics = {}
with zipfile.ZipFile(disassembly / 'complete-jit.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
    for tag in ('base', 'hot', 'nomask'):
        path = EVIDENCE / 'raw' / (tag + '-jit.txt')
        if not path.exists():
            raise ValueError('Missing diagnostic output: ' + tag)
        content = path.read_bytes()
        diagnostics[tag] = hashlib.sha256(content).hexdigest()
        archive.writestr(path.name, content)
        selected = []
        for section in content.decode('utf-8').split('; Assembly listing for method ')[1:]:
            header = section.splitlines()[0]
            if header.startswith('Tedd.OpenVDB.Tree.ValueAccessor`1[ushort]') or (
                    header.startswith('Program:') and ('g__Read|' in header or 'g__Write|' in header)):
                selected.append('; Assembly listing for method ' + section)
        (disassembly / (tag + '.txt')).write_text(''.join(selected).rstrip() + '\n', encoding='utf-8')
(disassembly / 'source-hashes.json').write_text(json.dumps(diagnostics, indent=2) + '\n')

lines = [
    '# Managed cache backport experiment', '',
    'The C++-style primary-hit rewrite was implemented and measured, then rejected because it exceeded the predefined regression limit. The library source remains at its original implementation. The 64-leaf cache and non-inlined secondary/miss paths were already present in .NET.', '',
    '## Fresh-build measurements', '',
    'Nanoseconds per completed operation; lower is faster. Each number is the median of launch medians. Positive elapsed change is a regression. All runs and samples are retained.', '',
    '| Workload | Operation | Original ns/op | Experimental port ns/op | Elapsed change |',
    '|---|---|---:|---:|---:|']
for row in rows:
    operation = row['method'].split('.')[1]
    lines.append(f"| {row['pattern']} | {operation} | {row['originalNs']:.3f} | {row['portNs']:.3f} | {row['elapsedChangePercent']:+.1f}% |")
lines += ['', '## Interpretation', '',
    'The coherent-read median improved, but this did not generalize to partial activity. Launch variability is substantial: coherent-read original launch medians span 3.180–5.265 ns/op and port medians span 3.033–4.381 ns/op. These are descriptive results on a shared host, not a statistically established universal speedup. No result was discarded or replaced.', '',
    'Diagnostic runs showed different synthesized/dynamic PGO decisions. The port reduced the observed optimized read caller from 267 to 233 bytes and the write caller from 331 to 299 bytes. These code-size observations support a code-shape mechanism; they do not isolate the cause of timing variability or prove a hardware bottleneck. Diagnostic processes are separate from timing processes.', '',
    '## Source changes evaluated', '',
    '- `hot`: flatten the primary-hit condition and put topology-cache clearing in the existing non-inlined miss methods. Rejected after confirmation guardrails.',
    '- `keys`: cache the primary origin inline, as native OpenVDB does. Screen coherent reads were 4.5% slower and random reads 8.2% slower; rejected.',
    '- `nomask`: replace the saturated-word guard with the native unconditional activity OR. Screen write results varied materially and included a 7.6% regression in the write-first pair; no consistent gain established.',
    '- Cached native buffer pointers were not transferred: `LeafManager.SwapBuffer` replaces the value array without a topology change. Existing managed leaf references observe replacement safely; a cached array would become stale.',
    '- Native mutation callbacks were not substituted for topology version checks. Prior experiments H15/H20 rejected their managed lifecycle/churn cost. Prior bounded-byref experiments H6/H17 did not justify unsafe indexing; this task does not reintroduce that burden.', '',
    'All experimental source patches and source hashes are preserved. A regression test exercises primary/secondary cached reads and writes across buffer swaps and compacting GC.', '',
    f"The restored implementation passed the complete test suite: {decision['retainedSourceTests']['passed']:,} passed, zero failed or skipped. The rejected experimental port was checked by the benchmark's value/activity/topology oracles; the full test-suite result applies to the retained source.", '',
    '## Protocol and scope', '',
    f"- Baseline source: `3e9e35c7713a5ac7164b32e712646ec8a9ebb34e`; {decision['processor']}; runtime .NET 11.0.0-rc.2.26504.105, Windows x64, logical processor affinity 8, server GC, tiering/PGO/intrinsics enabled.",
    '- Screen: four variants, three launches per variant and pattern, five samples of 25,001,984 operations, 1500 ms warmup. Initial fixture compilation failed on a signed/unsigned leaf-count comparison; it was corrected before any measurement, and the failed build log/hashes are retained.',
    '- Confirmation: fresh original/port builds; coherent workload five launches, nine samples of 50,003,968 operations. Each guardrail uses three launches, five samples of 25,001,984 operations; churn uses 32,768 operations. Warmup is 4000 ms per workload.',
    '- Arms rotate; read/write order alternates. Complete checksum, activity and topology validations run outside timed samples. The existing shared host lock covers builds and execution. No independence or significance claim is made.',
    '- Adoption required at least 3% throughput improvement in a coherent metric, no other median-time regression above 5%, correctness, and zero steady-state allocation. Partial-activity reads failed the performance requirement.',
    '- All non-churn timed loops allocated zero bytes. Churn includes leaf allocation and garbage collection by design.',
    '- Coherent/random fixtures contain 32³ voxels in 64 leaves; partial activity contains half-active voxels and writes only those voxels. Larger fixtures contain 32,768 sampled voxels across 512 leaves. Collision fixtures use 64 leaves with identical direct-map slots and shuffled voxel order. Inputs include signed coordinates.',
    '- This report evaluates cached access only. The exhaustive public-API three-way comparison remains incomplete. Historical C++ timings use a different toolchain/fixture and are not combined with these fresh .NET measurements.', '',
    'The screen runner inherited a legacy threshold description in its generated protocol files; the prospective `plan.json` contains the task-specific screen rule. No screen measurement is used as adoption evidence. `confirmation-plan.json` defines the independent adoption experiment.', '',
    '## Reproduction', '',
    'Run `python benchmarks/ManagedCacheBackport/run.py screen` and `confirm` with fresh `--workspace D:/Workspaces/AI/<task>` and `--evidence "Research/<new-run>"` directories. The existing host lock must be available. Source snapshots are pinned to the baseline revision; the common benchmark caller includes the checked-in adversarial fixtures. `tests final_hot` tests the experimental snapshot; `tests` tests the retained source.', '',
    'See `comparison.json` for full launch medians, `raw/` for all timings/build logs/validation records, and `disassembly/complete-jit.zip` for complete diagnostic output.', '']
(EVIDENCE / 'report.md').write_text('\n'.join(lines), encoding='utf-8')
print(json.dumps(dict(retained=False, failedMetrics=decision['failedMetrics'], confirmationProcesses=len(raw_runs)), indent=2))
