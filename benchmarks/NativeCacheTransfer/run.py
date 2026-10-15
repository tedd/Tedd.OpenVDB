"""Windows native source-transfer experiment. Invoke through the shared performance lock."""
from __future__ import annotations
import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import statistics
import subprocess
import sys
import urllib.request
import winreg
import zipfile
from patch_header import patch

PIN = 'f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf'
TBB_SHA = '4243442411bae9e3b80bfe2e47b5b571e8ae541ac45cfb991036b3a16a0c6012'
SKILL = Path('C:/Users/tedd/.codex/skills/scientific-method-performance')
VARIANTS = ['stock', 'cache', 'mask', 'both']
PATTERNS = ['coherent64', 'random64', 'coherent512', 'random512', 'partial64', 'collision64']


def save(path, value):
    path.write_text(json.dumps(value, indent=2) + '\n', encoding='utf-8')


def main():
    global VARIANTS, PATTERNS
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--workspace', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--variants', nargs='+', default=VARIANTS)
    parser.add_argument('--patterns', nargs='+', default=PATTERNS)
    parser.add_argument('--small-control-operations', type=int, default=50_003_968)
    parser.add_argument('--large-control-operations', type=int, default=16_777_216)
    parser.add_argument('--epoch', default='E1')
    args = parser.parse_args()
    VARIANTS, PATTERNS = args.variants, args.patterns
    assert set(VARIANTS) <= {'stock','cache','mask','both','split','split_both','split_only'} and 'stock' in VARIANTS
    repo = Path(__file__).resolve().parents[2]
    work, evidence = args.workspace.resolve(), args.evidence.resolve()
    assert work.is_relative_to(Path('D:/Workspaces/AI')) and work != Path('D:/Workspaces/AI')
    assert evidence.is_relative_to(repo / 'Research')
    work.mkdir(parents=True, exist_ok=True)
    raw = evidence / 'raw'; raw.mkdir(exist_ok=True)
    env = os.environ.copy()
    env.update(TEMP=str(work/'temp'), TMP=str(work/'temp'), PYTHONDONTWRITEBYTECODE='1')
    (work/'temp').mkdir(exist_ok=True)

    def run(command, log=None):
        result = subprocess.run([str(v) for v in command], cwd=repo, env=env, capture_output=True,
                                text=True, encoding='utf-8', errors='replace')
        if log:
            with (raw/log).open('a', encoding='utf-8') as f:
                f.write(repr([str(v) for v in command])+'\n'+result.stdout+result.stderr+'\n')
        if result.returncode:
            raise RuntimeError(f'{command}: {result.stdout[-3000:]} {result.stderr[-3000:]}')
        return result.stdout

    source = Path('D:/source/openvdb')
    native_build = repo/'reference/build-upstream-zlib'
    assert run(['git','-C',source,'rev-parse','HEAD']).strip() == PIN
    assert not run(['git','-C',source,'status','--short']).strip()
    with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r'HARDWARE\DESCRIPTION\System\CentralProcessor\0') as key:
        cpu = winreg.QueryValueEx(key, 'ProcessorNameString')[0].strip()
    environment = dict(started=dt.datetime.now().astimezone().isoformat(), cpu=cpu, platform=platform.platform(),
        managedRevision=run(['git','rev-parse','HEAD']).strip(), dirtyState=run(['git','status','--short']).splitlines(),
        nativeRevision=PIN, affinityCpu=8, lockPath=str(Path.home()/'.codex/locks/performance-measurement.lock'),
        configuration={k:str(v) if isinstance(v,Path) else v for k,v in vars(args).items()},
        powerScheme=run(['powercfg','/getactivescheme']).strip(), protocol=args.epoch+'; fixed workload and variant orders before measurements',
        nativeLibrarySha256=hashlib.sha256((native_build/'openvdb/openvdb/Release/libopenvdb.lib').read_bytes()).hexdigest(),
        sourceHashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in Path(__file__).parent.iterdir() if p.is_file()})
    save(raw/'environment.json',environment)
    with zipfile.ZipFile(evidence/'harness-source.zip','w',zipfile.ZIP_DEFLATED) as z:
        for p in Path(__file__).parent.iterdir():
            if p.is_file(): z.write(p,p.name)
    # Copy the tree headers so their quoted sibling includes resolve to the overlay.
    # Stock uses the upstream tree directory, with no overlay on its include path.
    overlay = work/'overlay/openvdb/tree'
    shutil.copytree(source/'openvdb/openvdb/tree',overlay,dirs_exist_ok=True)
    patch(source,overlay,evidence)
    archive=work/'tbb.zip'
    urllib.request.urlretrieve('https://github.com/uxlfoundation/oneTBB/releases/download/v2022.2.0/oneapi-tbb-2022.2.0-win.zip',archive)
    assert hashlib.sha256(archive.read_bytes()).hexdigest() == TBB_SHA
    with zipfile.ZipFile(archive) as z:
        assert all((work/x.filename).resolve().is_relative_to(work) for x in z.infolist())
        z.extractall(work)
    tbb=work/'oneapi-tbb-2022.2.0'
    env['PATH']=str(tbb/'redist/intel64/vc14')+os.pathsep+env['PATH']
    cmake=Path('C:/Program Files/Microsoft Visual Studio/18/Insiders/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe')
    build=work/'build'
    print('Building native variants on '+cpu,flush=True)
    run([cmake,'-S',Path(__file__).parent,'-B',build,'-G','Visual Studio 18 2026','-A','x64',
         f'-DPATCH_ROOT={work/"overlay"}',f'-DOPENVDB_SOURCE_DIR={source}',f'-DOPENVDB_BUILD_DIR={native_build}',
         f'-DTBB_ROOT={tbb}',f'-DZLIB_LIBRARY={repo/"reference/build-zlib/Release/zs.lib"}'],'build.log')
    run([cmake,'--build',build,'--config','Release','--parallel','2'],'build.log')
    validation={v:json.loads(run([build/'Release'/f'{v}.exe','--pattern','validate'])) for v in VARIANTS}
    save(raw/'validation.json',validation)
    print('All selected variants passed native mutation, collision, copy, assignment and activity tests.',flush=True)
    ps='C:/Windows/System32/WindowsPowerShell/v1.0/powershell.exe'
    load='Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor | Select-Object Name,PercentProcessorTime | ConvertTo-Json -Compress'
    environment['cpuBefore']=json.loads(run([ps,'-NoProfile','-Command',load]))
    runs=[]
    for pattern in PATTERNS:
        launches=5 if pattern=='coherent64' else 3
        operations=50_003_968 if pattern=='coherent64' else 262144 if pattern=='churn' else args.small_control_operations if pattern in ['partial64','random64'] else args.large_control_operations
        samples=9 if pattern=='coherent64' else 5
        # Primary count also divides the 32768-coordinate working set exactly.
        for launch in range(launches):
            offset=launch % len(VARIANTS)
            order=VARIANTS[offset:]+VARIANTS[:offset]
            if launch % 2: order=list(reversed(order))
            for variant in order:
                print(f'{pattern} {launch+1}/{launches}: {variant}',flush=True)
                data=json.loads(run([build/'Release'/f'{variant}.exe','--pattern',pattern,'--operations',operations,
                     '--samples',samples,'--warmup-ms','1000' if pattern=='coherent64' else '350',
                     '--write-first',str(launch%2)]))
                data.update(variant=variant,launch=launch+1)
                save(raw/f'{pattern}-{variant}-{launch+1}.json',data)
                runs.append(data)
                print('  '+', '.join(f"{x['name']} {statistics.median(x['elapsedMilliseconds'])*1e6/operations:.3f} ns" for x in data['results']),flush=True)
    environment['cpuAfter']=json.loads(run([ps,'-NoProfile','-Command',load]))
    environment['completed']=dt.datetime.now().astimezone().isoformat()
    save(raw/'environment.json',environment)
    summaries=[]
    for pattern in PATTERNS:
        fixture=[r for r in runs if r['pattern']==pattern]
        for field in ['coordinates','coordinateHash','activeVoxelCount','leafCount']:
            assert len({r[field] for r in fixture})==1,(pattern,field)
        for workload in [x['name'] for x in fixture[0]['results']]:
            arms={}
            for variant in VARIANTS:
                selected=[r for r in fixture if r['variant']==variant]
                medians=[]; values=[]
                for r in selected:
                    x=next(x for x in r['results'] if x['name']==workload)
                    ns=[t*1e6/x['operations'] for t in x['elapsedMilliseconds']]
                    medians.append(statistics.median(ns));values.extend(ns)
                arms[variant]=dict(median=statistics.median(medians),min=min(medians),max=max(medians),
                                   launchMedians=medians,allSamples=values,n=len(medians))
            for variant in VARIANTS[1:]:
                arms[variant]['pairedRatios']=[x/y for x,y in zip(arms[variant]['launchMedians'],arms['stock']['launchMedians'])]
            summaries.append(dict(pattern=pattern,workload=workload,arms=arms))
    save(evidence/'comparison.json',summaries)
    # Preserve assembly listings as compact evidence, not disposable object files.
    with zipfile.ZipFile(evidence/'assembly.zip','w',zipfile.ZIP_DEFLATED) as z:
        for p in build.glob('*.asm'): z.write(p,p.name)
    report(evidence,environment,summaries,validation)
    run([sys.executable,SKILL/'scripts/render_report.py',evidence/'report-data.json',evidence/'report.html'],'report-render.log')
    print('Complete: '+str(evidence),flush=True)


def report(evidence,environment,summaries,validation):
    hypotheses=json.loads((evidence/'hypotheses.json').read_text())
    primary={x['workload']:x['arms'] for x in summaries if x['pattern']=='coherent64'}
    for index,h in enumerate(hypotheses):
        variant=h.get('variant',['cache','mask','both'][min(index,2)])
        workload=h.get('workload',['read','write','write'][min(index,2)])
        p=primary[workload];ratios=p[variant]['pairedRatios']
        h.update(state='retained' if max(ratios)<1 else 'rejected' if min(ratios)>1 else 'inconclusive',
            decision='Supported locally' if max(ratios)<1 else 'Not supported' if min(ratios)>1 else 'Mixed runs',
            result=f"{workload}: stock {p['stock']['median']:.6f}, {variant} {p[variant]['median']:.6f} ns/op; paired time ratio range {min(ratios):.4f} to {max(ratios):.4f}.",
            effectPercent=(p[variant]['median']/p['stock']['median']-1)*100,
            changeScope='local',maintainability='Experimental header overlay; generic library integration and additional platforms remain unvalidated.',
            codeComments='Patch comments state cache collision checks, invalidation and mask-word conditions.',
            decisionRationale='Classifies the bounded transfer experiment. Guardrail workload results determine upstream suitability; no production patch is installed.',
            evidence=['comparison.json','native-cache-transfer.patch','raw/validation.json'],risks=['One Windows x64 compiler and processor.'])
    save(evidence/'hypotheses.json',hypotheses)
    series=[]
    for pattern in PATTERNS:
        rows=[]
        for x in summaries:
            if x['pattern']!=pattern: continue
            rows.append(dict(label=x['workload'],measurements=[{k:x['arms'][v][k] for k in ['median','min','max','n']} for v in VARIANTS]))
        series.append(dict(title=pattern,description='Median of process-launch medians; whiskers span launch medians. Variants and read/write order alternate; setup and correctness checks excluded.',unit='ns/op',lowerIsBetter=True,rounds=VARIANTS,rows=rows))
    effects=[]
    for x in summaries:
        arms=x['arms']
        effects.append(dict(name=x['pattern']+' '+x['workload'],control='Paired unmodified native stock launches',items=[dict(label=v,effect=arms[v]['median']/arms['stock']['median']-1,range=[min(arms[v]['pairedRatios'])-1,max(arms[v]['pairedRatios'])-1]) for v in VARIANTS[1:]]))
    combined='split_only' if 'split_only' in VARIANTS else 'split_both' if 'split_both' in VARIANTS else 'both' if 'both' in VARIANTS else 'split'
    r=dict(title='Transferring managed cache choices to native OpenVDB',generatedAt=environment['completed'],status='complete',
        scope='Local C++ UInt16 Tree4 5/4/3 experiment. Stock upstream headers versus 64-leaf cache, saturated activity-word guard, and their combination.',
        question='Do the two managed implementation choices improve native C++ cached access, and where do they regress?',primaryMetric='ns/op; median of process-launch medians',
        summary=dict(baseline=primary['write']['stock']['median'],final=primary['write'][combined]['median'],unit='ns/op',changePercent=(primary['write'][combined]['median']/primary['write']['stock']['median']-1)*100,headline='Native source-transfer experiment; primary and counterexample workloads measured separately.'),
        scorecards=[dict(title='coherent64 '+w,value=primary[w][combined]['median'],baseline=primary[w]['stock']['median'],unit='ns/op',wallChangePercent=(primary[w][combined]['median']/primary[w]['stock']['median']-1)*100) for w in ['read','write']],
        environment=[dict(label=k,value=str(v)) for k,v in environment.items() if k not in ['cpuBefore','cpuAfter','dirtyState','sourceHashes']],
        hotspots=[dict(name='Native cached voxel access',location='openvdb/tree/ValueAccessor.h',evidence='User-requested local transfer experiment; no application hotspot or application speedup claimed.')],
        areas=[dict(name='Leaf location and activity writes',boundary='Only the two source-level choices discussed with the user and their combination.',evidence='Source comparison plus paired timing and independent tree validation.',hypothesisIds=[h['id'] for h in hypotheses],disposition='Three bounded questions; unrelated optimization catalogue categories excluded. No fabricated ten/twenty-hypothesis search.')],
        series=series,effectGroups=effects,hypotheses=hypotheses,retainedChanges=['Reproduction harness, experimental patch and evidence. Upstream checkout and managed library remain unchanged.'],decisions=[],
        correctness=['Each executable tests signed cache collisions, copying, assignment across trees, explicitly invalidated direct tile replacement, clearing, explicit cache erasure, mask reactivation and tree destruction.',
          'Every timed sample validates all voxel values/activity through tree lookups independent of the accessor; read checksums are consumed and verified.',
          'All variants have identical coordinate hashes, topology and active voxel counts for every workload.'],
        generatedCode=[dict(summary='MSVC Release source/assembly listings are preserved for each variant. Listings support subsequent inspection; no hardware-counter or branch-miss attribution is claimed.',artifact='assembly.zip')],
        validityThreats=['Only AMD Ryzen 9 5950X, Windows x64 and MSVC Release. No cross-machine claim.',
          'The 64-leaf fixture exactly fits the secondary cache; random512 and collision64 deliberately test losing distributions.',
          'The mask experiment modifies the native cached-buffer write path; it is not a general NodeMask API change.',
          'The cache overlay changes accessor object size and layout. It is a prototype, not an ABI-compatible upstream patch.',
          'Focused tests do not establish all OpenVDB types, APIs, threading configurations or full upstream-suite compatibility.',
          'The shared performance lock is cooperative; external CPU snapshots are retained in raw/environment.json.'],
        reproduction=['Set PYTHONDONTWRITEBYTECODE=1. Run through the shared performance lock: python benchmarks/NativeCacheTransfer/run.py --workspace D:/Workspaces/AI/openvdb-native-cache-transfer-20261015 --evidence "Research/2026-10-15 Native cache transfer-1"',
          'Primary: five launches, nine samples of 50,003,968 operations, 1000 ms warmup/workload. Controls: three launches, five samples, 350 ms warmup; operation counts in raw JSON.',
          'CMake uses unchanged upstream headers for stock and a copied tree-header overlay for other variants. Both experimental mechanisms are compile-time switches.'],
        artifacts=[dict(label='All measurements',path='comparison.json'),dict(label='Experimental header patch',path='native-cache-transfer.patch'),dict(label='Source and reproduction driver',path='../../benchmarks/NativeCacheTransfer/'),dict(label='Assembly listings',path='assembly.zip'),dict(label='Raw measurements, validation and environment',path='raw/')])
    save(evidence/'report-data.json',r)


if __name__=='__main__': main()
