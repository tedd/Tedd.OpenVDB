"""Matched normal-build experiments for transferring native accessor code shapes.

Uses the existing shared host lock, without creating lock files in the user home.
All disposable output is confined to the selected task workspace.
"""
import argparse
import difflib
import hashlib
import json
import msvcrt
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import optimize_cached_access as rig

WORK = Path(r'D:\Workspaces\AI\openvdb-managed-backport-20261015')
EVIDENCE = rig.REPO / 'Research/2026-10-15 Managed cache backport-1'
ACCESSOR = 'src/Tedd.OpenVDB/Tree/ValueAccessor.cs'
LEAF = 'src/Tedd.OpenVDB/Tree/LeafNode.cs'
SOURCE_REVISION = '3e9e35c7713a5ac7164b32e712646ec8a9ebb34e'


def edit(text, old, new):
    if text.count(old) != 1:
        raise ValueError('Ambiguous patch anchor: ' + old[:100])
    return text.replace(old, new)


def transform(text, tag):
    tag = tag.removeprefix('final_')
    if tag in ('hot', 'hot_keys', 'hot_nomask'):
        # A single cold branch; version mismatch is cleared in the cold method.
        text = edit(text, '''        var leaf = Cached(coordinate);
        return leaf is not null ? leaf.GetValue(coordinate) : GetValueUncached(coordinate);''', '''        var leaf = _leaf;
        if (leaf is not null && _version == _tree.TopologyVersion)
        {
            var origin = leaf.Origin;
            if ((((coordinate.X ^ origin.X) | (coordinate.Y ^ origin.Y) | (coordinate.Z ^ origin.Z)) & -8) == 0)
                return leaf.GetValue(coordinate);
        }
        return GetValueUncached(coordinate);''')
        text = edit(text, '''    private T GetValueUncached(Coord coordinate)
    {
        var leaf''', '''    private T GetValueUncached(Coord coordinate)
    {
        if (_version != _tree.TopologyVersion) Clear();
        var leaf''')
        text = edit(text, '''        var leaf = Cached(coordinate);
        if (leaf is not null) leaf.SetValueOn(coordinate, value);
        else SetValueOnUncached(coordinate, value);''', '''        var leaf = _leaf;
        if (leaf is not null && _version == _tree.TopologyVersion)
        {
            var origin = leaf.Origin;
            if ((((coordinate.X ^ origin.X) | (coordinate.Y ^ origin.Y) | (coordinate.Z ^ origin.Z)) & -8) == 0)
            {
                leaf.SetValueOn(coordinate, value);
                return;
            }
        }
        SetValueOnUncached(coordinate, value);''')
        text = edit(text, '''    private void SetValueOnUncached(Coord coordinate, T value)
    {
        var leaf''', '''    private void SetValueOnUncached(Coord coordinate, T value)
    {
        if (_version != _tree.TopologyVersion) Clear();
        var leaf''')
    if tag in ('keys', 'hot_keys'):
        text = edit(text, '    private LeafNode<T>? _leaf;', '    private LeafNode<T>? _leaf;\n    private Coord _leafOrigin;')
        text = text.replace('var origin = leaf.Origin;', 'var origin = _leafOrigin;')
        text = text.replace('            _leaf = leaf;', '            _leaf = leaf;\n            _leafOrigin = leaf.Origin;')
        text = edit(text, '        _leaf = leaf;\n        return leaf;', '        _leaf = leaf;\n        _leafOrigin = leaf.Origin;\n        return leaf;')
        text = edit(text, '        if (_leaf is { } leaf) _leaves[CacheSlot(coordinate)] = leaf;',
                    '        if (_leaf is { } leaf) { _leaves[CacheSlot(coordinate)] = leaf; _leafOrigin = leaf.Origin; }')
    return text


def build(tag, env):
    source = WORK / tag / 'source'
    if source.exists() or (EVIDENCE / 'raw' / (tag + '-source.json')).exists():
        raise ValueError('Immutable build exists: ' + tag)
    tracked = subprocess.check_output(['git', 'ls-files', 'src', 'benchmarks/CachedAccess.Managed',
        'Directory.Build.props', 'global.json'], cwd=rig.REPO, text=True).splitlines()
    hashes = {}
    for name in tracked:
        target = source / name
        target.parent.mkdir(parents=True, exist_ok=True)
        if name.startswith('src/'):
            target.write_bytes(subprocess.check_output(['git', 'show', SOURCE_REVISION + ':' + name], cwd=rig.REPO))
        else:
            shutil.copy2(rig.REPO / name, target)
        if name == ACCESSOR:
            original = target.read_text(encoding='utf-8')
            modified = transform(original, tag)
            target.write_text(modified, encoding='utf-8')
            (EVIDENCE / (tag + '.patch')).write_text(''.join(difflib.unified_diff(original.splitlines(True),
                modified.splitlines(True), fromfile='a/' + name, tofile='b/' + name)), encoding='utf-8')
        if name == LEAF and tag.removeprefix('final_') in ('nomask', 'hot_nomask'):
            original = target.read_text(encoding='utf-8')
            modified = edit(original, '        if (word != ulong.MaxValue) word |= 1UL << (offset & 63);',
                            '        word |= 1UL << (offset & 63);')
            target.write_text(modified, encoding='utf-8')
            (EVIDENCE / (tag + '-leaf.patch')).write_text(''.join(difflib.unified_diff(original.splitlines(True),
                modified.splitlines(True), fromfile='a/' + name, tofile='b/' + name)), encoding='utf-8')
        hashes[name] = hashlib.sha256(target.read_bytes()).hexdigest()
    rig.save(EVIDENCE / 'raw' / (tag + '-source.json'), hashes)
    rig.run(['dotnet', 'build', str(source / 'benchmarks/CachedAccess.Managed/CachedAccess.Managed.csproj'),
        '-c', 'Release', '--artifacts-path', str(WORK / tag / 'artifacts'), '-p:GeneratePackageOnBuild=false'],
        env, EVIDENCE / 'raw' / (tag + '-build.log'))
    for pattern in ('coherent', 'random', 'partial', 'large', 'large-random', 'collisions'):
        data = json.loads(rig.run(rig.command(tag) + ['--validate', '--pattern', pattern], env, WORK / 'validation.log'))
        rig.save(EVIDENCE / 'raw' / (tag + '-' + pattern + '-validation.json'), data)
    print('Built and validated ' + tag, flush=True)


def main():
    global WORK, EVIDENCE
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['screen', 'confirm', 'measure', 'build', 'disasm', 'tests', 'inventory'])
    parser.add_argument('tags', nargs='*')
    parser.add_argument('--phase', default='screen')
    parser.add_argument('--pattern', choices=['coherent', 'random', 'partial', 'large', 'large-random', 'collisions', 'churn'], default='coherent')
    parser.add_argument('--launches', type=int, default=3)
    parser.add_argument('--warmup-ms', type=int, default=1500)
    parser.add_argument('--samples', type=int, default=5)
    parser.add_argument('--operations', type=int, default=25_001_984)
    parser.add_argument('--workspace', type=Path, default=WORK)
    parser.add_argument('--evidence', type=Path, default=EVIDENCE)
    args = parser.parse_args()
    if any(not re.fullmatch(r'[A-Za-z0-9_-]{1,64}', name) for name in args.tags + [args.phase]):
        raise ValueError('Use bounded file-name identifiers for tags and phase')
    if args.action == 'tests' and len(args.tags) > 1:
        raise ValueError('Select at most one source variant for tests')
    WORK, EVIDENCE = args.workspace.resolve(), args.evidence.resolve()
    if WORK == Path(r'D:\Workspaces\AI') or not WORK.is_relative_to(Path(r'D:\Workspaces\AI')):
        raise ValueError('Use a task subdirectory under D:/Workspaces/AI')
    if not EVIDENCE.is_relative_to(rig.REPO / 'Research') or EVIDENCE == rig.REPO / 'Research':
        raise ValueError('Use a dedicated Research subdirectory')
    WORK.mkdir(parents=True, exist_ok=True)
    (EVIDENCE / 'raw').mkdir(parents=True, exist_ok=True)
    rig.WORK, rig.EVIDENCE = WORK, EVIDENCE
    env = rig.environment()
    lock_path = Path.home() / '.codex/locks/performance-measurement.lock'
    # The original lock helper was removed during this session. Retain the same
    # existing byte-range lock, acquired non-blockingly; never create a replacement.
    with lock_path.open('r+b') as host_lock:
        host_lock.seek(0)
        msvcrt.locking(host_lock.fileno(), msvcrt.LK_NBLCK, 1)
        try:
            if args.action == 'screen':
                if (EVIDENCE / 'plan.json').exists():
                    raise ValueError('Immutable experiment plan exists; select a new evidence directory')
                rig.save(EVIDENCE / 'plan.json', dict(
                    sourceRevision=SOURCE_REVISION,
                    variants=['base', 'hot', 'keys', 'nomask'],
                    hypotheses=dict(hot='Flatten primary hit check to one cold exit; retain topology checks.',
                        keys='Store primary origin inline as native does; retain managed leaf buffer indirection.',
                        nomask='Use unconditional activity OR as selected native variant does.'),
                    alreadyPresent=['64-entry leaf cache', 'non-inlined miss paths', 'inlined primary hits'],
                    excluded=dict(bufferPointer='LeafManager swaps value arrays without topology changes; stale buffer risk.',
                        versionRemoval='Managed references require existing topology invalidation; earlier churn cost rejected callbacks.'),
                    threshold='Screen: >=5% throughput gain in either coherent read/write; other coherent metric <=3% time regression. Confirmation: >=3% gain and all mandatory guardrails <=5% time regression.',
                    guardrails=['random', 'partial', 'large', 'large-random', 'collisions', 'churn', 'full correctness suite', 'buffer swaps'],
                    budget='Screen 3 launches x 5 samples x 25,001,984 operations; 1500ms warmup, CPU8. Confirmation 5 launches; all samples retained. Churn 32768 operations.',
                    lock=str(lock_path), observer='No profiler in timing runs; disassembly separate. Shared-host load uncontrolled.',
                    statistics='Median of per-launch medians, descriptive comparisons; no independence/significance claim.'))
                rig.save(EVIDENCE / 'raw/environment.json', dict(platform=sys.version, cpu=os.environ.get('PROCESSOR_IDENTIFIER'),
                    dotnet=rig.run(['dotnet', '--info'], env, WORK / 'environment.log')))
                for tag in ('base', 'hot', 'keys', 'nomask'):
                    build(tag, env)
                for pattern in ('coherent', 'random'):
                    rig.measure(['base', 'hot', 'keys', 'nomask'], 'screen-' + pattern, 3, 5, 25_001_984, 1500, env, pattern)
            elif args.action == 'confirm':
                if (EVIDENCE / 'confirmation-plan.json').exists():
                    raise ValueError('Immutable confirmation plan exists; select a new evidence directory')
                rig.save(EVIDENCE / 'confirmation-plan.json', dict(
                    selected='hot: direct primary hit guard, topology invalidation moved into existing cold paths',
                    rejected=dict(keys='Coherent read +4.5%, random read +8.2% median time in screen.',
                        nomask='Dense write +7.6% time in the write-first screen pair; no consistent benefit.'),
                    screenCaveat='Synthesized and dynamic PGO callers observed in diagnostic runs; transient tiers may contaminate short warmup. Screen results exploratory, not adopted.',
                    prospectiveChange='Fresh normal builds, fixed 4000ms warmup per workload, no other runtime changes.',
                    coherent=dict(launches=5, samples=9, operations=50_003_968),
                    guardrails=dict(launches=3, samples=5, operations=25_001_984, churnOperations=32768,
                        patterns=['random', 'partial', 'large', 'large-random', 'collisions', 'churn']),
                    threshold='At least 3% coherent read or write throughput gain; other coherent and all guardrail median times no worse than 5%. Full suite and zero steady-state allocations required.',
                    exclusions='None. Retain complete fixed budgets. If primary gate fails, do not spend guardrail budget or adopt.',
                    statistics='Median of launch medians; complete launch ranges; descriptive, shared host.'))
                for tag in ('final_base', 'final_hot'):
                    build(tag, env)
                rig.measure(['final_base', 'final_hot'], 'confirmation-coherent', 5, 9, 50_003_968, 4000, env, 'coherent')
                summary = json.loads((EVIDENCE / 'confirmation-coherent-summary.json').read_text())
                ratios = [summary['final_hot'][k]['nsPerOperation'] / summary['final_base'][k]['nsPerOperation']
                    for k in summary['final_base']]
                if min(ratios) <= 1 / 1.03 and max(ratios) <= 1.05:
                    for pattern in ('random', 'partial', 'large', 'large-random', 'collisions', 'churn'):
                        rig.measure(['final_base', 'final_hot'], 'confirmation-' + pattern, 3, 5,
                            32768 if pattern == 'churn' else 25_001_984, 4000, env, pattern)
                else:
                    print('Primary adoption gate failed; no source candidate retained.', flush=True)
            elif args.action == 'build':
                for tag in args.tags:
                    build(tag, env)
            elif args.action == 'measure':
                rig.measure(args.tags, args.phase, args.launches, args.samples,
                    32768 if args.pattern == 'churn' else args.operations, args.warmup_ms, env, args.pattern)
            elif args.action == 'disasm':
                for tag in args.tags:
                    diagnostic = env.copy()
                    diagnostic['DOTNET_JitDisasm'] = '*Read* *Write* Tedd.OpenVDB.Tree.ValueAccessor`1:*'
                    diagnostic['DOTNET_JitStdOutFile'] = str(EVIDENCE / 'raw' / (tag + '-jit.txt'))
                    rig.run(rig.command(tag) + ['--operations', '1048576', '--samples', '1'], diagnostic, WORK / 'disasm.log')
            elif args.action == 'tests':
                test_source = rig.REPO
                if args.tags:
                    test_source = WORK / args.tags[0] / 'source'
                    if not (test_source / ACCESSOR).is_file():
                        raise ValueError('Build the test variant first')
                    for name in subprocess.check_output(['git', 'ls-files', 'tests'], cwd=rig.REPO, text=True).splitlines():
                        target = test_source / name
                        target.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copy2(rig.REPO / name, target)
                rig.run(['dotnet', 'test', str(test_source / 'tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj'),
                    '-c', 'Release', '--artifacts-path', str(WORK / 'tests'), '-p:GeneratePackageOnBuild=false',
                    '--results-directory', str(WORK / 'test-results')], env, EVIDENCE / 'raw/tests.log')
                print('Full suite passed', flush=True)
            else:
                inventory_evidence = rig.REPO / 'Research/2026-10-15 Exhaustive three-way benchmarks-1'
                rig.run(['dotnet', 'build', str(rig.REPO / 'benchmarks/ApiInventory/ApiInventory.csproj'),
                    '-c', 'Release', '--artifacts-path', str(WORK / 'inventory'), '-p:GeneratePackageOnBuild=false'],
                    env, inventory_evidence / 'raw/inventory-source-crosscheck-build.log')
                data = json.loads(rig.run(['dotnet', str(WORK / 'inventory/bin/ApiInventory/release/ApiInventory.dll'),
                    '--source-root', str(rig.REPO)], env, WORK / 'inventory.log'))
                rig.save(inventory_evidence / 'managed-api-inventory.json', data)
                print('Inventory verified', flush=True)
        finally:
            host_lock.seek(0)
            msvcrt.locking(host_lock.fileno(), msvcrt.LK_UNLCK, 1)


if __name__ == '__main__':
    main()
