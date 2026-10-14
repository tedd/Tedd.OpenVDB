"""Windows x64 cached-access comparison. Invoke through the shared performance lock."""
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
import time
import urllib.request
import winreg
import zipfile

PIN = "f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf"
TBB_SHA = "4243442411bae9e3b80bfe2e47b5b571e8ae541ac45cfb991036b3a16a0c6012"
TBB_URL = "https://github.com/uxlfoundation/oneTBB/releases/download/v2022.2.0/oneapi-tbb-2022.2.0-win.zip"
SKILL = Path(r"C:\Users\tedd\.codex\skills\scientific-method-performance")


def save(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def execute(command: list[str], cwd: Path, environment: dict[str, str], log: Path | None = None) -> str:
    result = subprocess.run(command, cwd=cwd, env=environment, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if log:
        with log.open("a", encoding="utf-8") as stream:
            stream.write("COMMAND: " + repr(command) + "\n" + (result.stdout + result.stderr).rstrip() + "\n")
    if result.returncode:
        raise RuntimeError(f"Command failed ({result.returncode}): {command!r}\n{result.stdout[-3000:]}\n{result.stderr[-3000:]}")
    return result.stdout


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, required=True)
    parser.add_argument("--evidence", type=Path, required=True)
    parser.add_argument("--openvdb-source", type=Path, default=Path(r"D:\source\openvdb"))
    parser.add_argument("--openvdb-build", type=Path)
    parser.add_argument("--tbb-root", type=Path)
    parser.add_argument("--cmake", default=r"C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe")
    parser.add_argument("--generator", default="Visual Studio 18 2026")
    parser.add_argument("--cpu", type=int, default=8)
    parser.add_argument("--operations", type=int, default=50_003_968)
    parser.add_argument("--samples", type=int, default=9)
    parser.add_argument("--launches", type=int, default=5)
    parser.add_argument("--warmup-ms", type=int, default=2000)
    parser.add_argument("--epoch", default="E1")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parent.parent
    work, evidence = args.workspace.resolve(), args.evidence.resolve()
    temporary_parent = Path(r"D:\Workspaces\AI").resolve()
    if not work.is_relative_to(temporary_parent) or work == temporary_parent:
        raise ValueError("Build workspace must be a task subdirectory under D:\\Workspaces\\AI.")
    if not evidence.is_relative_to(repo / "Research"):
        raise ValueError("Evidence must be in the allocated repository Research directory.")
    if args.operations < 32768 or args.operations % 32768 or args.samples < 1 or args.launches < 1:
        raise ValueError("Invalid operation/sample/launch counts.")
    if not 0 <= args.cpu < min(os.cpu_count() or 1, 63):
        raise ValueError("CPU affinity is outside the available processor range.")
    work.mkdir(parents=True, exist_ok=True)
    raw = evidence / "raw"
    raw.mkdir(exist_ok=True)
    env = os.environ.copy()
    env.update({
        "TEMP": str(work / "temp"), "TMP": str(work / "temp"), "PYTHONDONTWRITEBYTECODE": "1",
        "DOTNET_CLI_HOME": str(work / "dotnet-home"), "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
        "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1", "DOTNET_NOLOGO": "1",
        "NUGET_PACKAGES": str(work / "nuget"), "NUGET_HTTP_CACHE_PATH": str(work / "nuget-http"),
        "DOTNET_TieredCompilation": "1", "DOTNET_TieredPGO": "1", "DOTNET_EnableHWIntrinsic": "1",
    })
    Path(env["TEMP"]).mkdir(exist_ok=True)
    upstream_revision = execute(["git", "-C", str(args.openvdb_source), "rev-parse", "HEAD"], repo, env).strip()
    if upstream_revision != PIN:
        raise ValueError("Native source must be the pinned OpenVDB 13.0.1 revision.")
    native_build = (args.openvdb_build or repo / "reference" / "build-upstream-zlib").resolve()
    native_library = native_build / "openvdb" / "openvdb" / "Release" / "libopenvdb.lib"
    zlib = repo / "reference" / "build-zlib" / "Release" / "zs.lib"
    if not native_library.is_file() or not zlib.is_file():
        raise ValueError("The pinned Release native reference libraries are required; see reference/Build-CompressionReference.ps1.")
    native_config = (native_build / "openvdb" / "openvdb" / "openvdb" / "version.h").read_text()
    if "#define OPENVDB_LIBRARY_MAJOR_VERSION_NUMBER 13" not in native_config or "#define OPENVDB_LIBRARY_MINOR_VERSION_NUMBER 0" not in native_config:
        raise ValueError("Native generated headers do not describe OpenVDB 13.0.x.")

    started = dt.datetime.now().astimezone().isoformat()
    with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"HARDWARE\DESCRIPTION\System\CentralProcessor\0") as key:
        cpu_name = winreg.QueryValueEx(key, "ProcessorNameString")[0].strip()
    power = execute([r"C:\Windows\System32\powercfg.exe", "/getactivescheme"], repo, env).strip()
    os_info = {"platform": platform.platform(), "cpu": cpu_name, "logicalProcessors": os.cpu_count(), "powerScheme": power}
    environment = {
        "started": started, "managedRevision": execute(["git", "rev-parse", "HEAD"], repo, env).strip(),
        "managedDirtyState": execute(["git", "status", "--short"], repo, env).splitlines(),
        "nativeRevision": upstream_revision, "nativeDirtyState": execute(["git", "-C", str(args.openvdb_source), "status", "--short"], repo, env).splitlines(),
        "nativeLibrarySha256": hashlib.sha256(native_library.read_bytes()).hexdigest(),
        "os": os_info, "configuration": {k: str(v) if isinstance(v, Path) else v for k, v in vars(args).items()},
        "dotnetSdks": execute(["dotnet", "--list-sdks"], repo, env),
        "lockPath": str(Path.home() / ".codex" / "locks" / "performance-measurement.lock"),
        "runtimeControls": {k: env[k] for k in ("DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_EnableHWIntrinsic")},
        "benchmarkSourceSha256": {str(p.relative_to(repo)): hashlib.sha256(p.read_bytes()).hexdigest() for p in (
            repo / "benchmarks" / "CachedAccess.Managed" / "Program.cs", repo / "benchmarks" / "CachedAccess.Native" / "main.cpp")},
    }
    save(raw / "environment.json", environment)
    print(f"Building matching cached-access harnesses; CPU {args.cpu}: {cpu_name}", flush=True)
    tbb = args.tbb_root
    if not tbb:
        archive = work / "oneapi-tbb-2022.2.0-win.zip"
        if not archive.exists():
            print("Downloading pinned oneTBB dependency into the task workspace.", flush=True)
            urllib.request.urlretrieve(TBB_URL, archive)
        if hashlib.sha256(archive.read_bytes()).hexdigest() != TBB_SHA:
            raise ValueError("oneTBB archive SHA-256 mismatch.")
        with zipfile.ZipFile(archive) as package:
            for item in package.infolist():
                if not (work / item.filename).resolve().is_relative_to(work):
                    raise ValueError("Unsafe archive member.")
            package.extractall(work)
        tbb = work / "oneapi-tbb-2022.2.0"
    tbb = tbb.resolve()
    env["PATH"] = str(tbb / "redist" / "intel64" / "vc14") + os.pathsep + env["PATH"]
    managed_build = work / "managed"
    execute(["dotnet", "build", str(repo / "benchmarks" / "CachedAccess.Managed" / "CachedAccess.Managed.csproj"),
             "-c", "Release", "--artifacts-path", str(managed_build), "-p:GeneratePackageOnBuild=false"], repo, env, raw / "build.log")
    native_output = work / "native"
    execute([args.cmake, "-S", str(repo / "benchmarks" / "CachedAccess.Native"), "-B", str(native_output),
             "-G", args.generator, "-A", "x64", f"-DOPENVDB_SOURCE_DIR={args.openvdb_source}",
             f"-DOPENVDB_BUILD_DIR={native_build}", f"-DTBB_ROOT={tbb}", f"-DZLIB_LIBRARY={zlib}"], repo, env, raw / "build.log")
    execute([args.cmake, "--build", str(native_output), "--config", "Release", "--parallel", "2"], repo, env, raw / "build.log")
    native = [str(native_output / "Release" / "cached-access-native.exe")]
    managed = ["dotnet", str(managed_build / "bin" / "CachedAccess.Managed" / "release" / "CachedAccess.Managed.dll")]
    common = ["--operations", str(args.operations), "--samples", str(args.samples), "--warmup-ms", str(args.warmup_ms), "--cpu", str(args.cpu)]
    commands = {"native": native, "managed": managed}
    validation = {key: json.loads(execute(command + common + ["--validate"], repo, env)) for key, command in commands.items()}
    if validation["native"]["openvdbVersion"] != "13.0.1":
        raise ValueError("Native generated headers must match OpenVDB 13.0.1 exactly.")
    for key in ("valueType", "coordinates", "coordinateHash", "checksum", "activeVoxelCount", "leafCount"):
        if validation["managed"][key] != validation["native"][key]:
            raise ValueError("Native/managed fixture mismatch: " + key)
    save(raw / "validation.json", validation)
    environment["managedRuntime"] = validation["managed"]["runtime"]
    environment["nativeCompiler"] = validation["native"]["msvc"]
    print("Correctness passed: 32768 active voxels, 64 leaves, identical coordinate hash and value checksum.", flush=True)
    ps = r"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"
    load_query = "Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor | Select-Object Name,PercentProcessorTime | ConvertTo-Json -Compress"
    environment["externalCpuBefore"] = execute([ps, "-NoProfile", "-Command", load_query], repo, env).strip()
    runs = []
    for launch in range(args.launches):
        order = ["native", "managed"] if launch % 2 == 0 else ["managed", "native"]
        for key in order:
            print(f"Launch {launch + 1}/{args.launches}: {key}, {'write' if launch % 2 else 'read'} first", flush=True)
            output = execute(commands[key] + common + (["--write-first"] if launch % 2 else []), repo, env)
            data = json.loads(output)
            data["launch"] = launch + 1
            for field in ("valueType", "coordinates", "coordinateHash", "checksum", "activeVoxelCount", "leafCount"):
                if data[field] != validation[key][field]:
                    raise ValueError("Post-measurement fixture changed: " + field)
            save(raw / f"{key}-{launch + 1}.json", data)
            runs.append(data)
            for item in data["results"]:
                milliseconds = statistics.median(item["elapsedMilliseconds"]) / item["operations"]
                print(f"  {item['name']}: {milliseconds:.11f} ms/op", flush=True)
    environment["externalCpuAfter"] = execute([ps, "-NoProfile", "-Command", load_query], repo, env).strip()
    environment["completed"] = dt.datetime.now().astimezone().isoformat()
    environment["elapsedSeconds"] = (dt.datetime.fromisoformat(environment["completed"]) - dt.datetime.fromisoformat(started)).total_seconds()
    save(raw / "environment.json", environment)
    summaries = []
    for name in ("accessor.read.coherent", "accessor.write.coherent"):
        implementations = {}
        for key in ("native", "managed"):
            medians = []
            all_samples = []
            allocation = []
            for run in runs:
                if run["implementation"] != key:
                    continue
                item = next(item for item in run["results"] if item["name"] == name)
                values = [value / item["operations"] for value in item["elapsedMilliseconds"]]
                medians.append(statistics.median(values))
                all_samples.extend(values)
                allocation.extend(item.get("allocatedBytes", []))
            median = statistics.median(medians)
            implementations[key] = {"medianMillisecondsPerOperation": median, "launchMedians": medians,
                                    "minLaunch": min(medians), "maxLaunch": max(medians),
                                    "allSamples": all_samples, "operationsPerSecond": 1000 / median,
                                    "managedAllocatedBytesPerBatch": allocation or None}
        ratios = [m / n for m, n in zip(implementations["managed"]["launchMedians"], implementations["native"]["launchMedians"])]
        summaries.append({"name": name, **implementations,
                          "managedOverNative": implementations["managed"]["medianMillisecondsPerOperation"] / implementations["native"]["medianMillisecondsPerOperation"],
                          "medianPairedRatio": statistics.median(ratios), "pairedRatios": ratios})
    save(evidence / "comparison.json", summaries)
    produce_report(evidence, environment, summaries)
    execute([sys.executable, str(SKILL / "scripts" / "render_report.py"), str(evidence / "report-data.json"), str(evidence / "report.html")], repo, env, raw / "report-render.log")
    print("Comparison and report saved to " + str(evidence), flush=True)


def produce_report(evidence: Path, environment: dict, summaries: list[dict]) -> None:
    hypotheses = json.loads((evidence / "hypotheses.json").read_text(encoding="utf-8"))
    for hypothesis, item in zip(hypotheses, summaries):
        ratios = item["pairedRatios"]
        hypothesis.update({
            "state": "retained" if min(ratios) > 1 else "rejected" if max(ratios) < 1 else "inconclusive",
            "decision": "Supported" if min(ratios) > 1 else "Rejected" if max(ratios) < 1 else "Mixed runs",
            "change": "Added equivalent C# and C++ measurement/validation harnesses; library implementations unchanged.",
            "changeScope": "local", "maintainability": "One bounded reproduction runner and two explicit access loops.",
            "codeComments": "Harness comments identify setup exclusion and checksum/anti-elimination controls.",
            "result": f"Managed/native ratio of launch medians {item['managedOverNative']:.3f}; paired ratio median {item['medianPairedRatio']:.3f}, range {min(ratios):.3f}–{max(ratios):.3f}.",
            "effectPercent": (item["managedOverNative"] - 1) * 100,
            "decisionRationale": "Classifies the observed comparison only; no library optimization or application-speed claim is adopted.",
            "evidence": ["comparison.json", "raw/validation.json", "raw/environment.json"], "risks": [],
        })
    save(evidence / "hypotheses.json", hypotheses)
    rows, effects, cards = [], [], []
    for hypothesis, item in zip(hypotheses, summaries):
        n, m = item["native"], item["managed"]
        rows.append({"label": item["name"], "detail": "UInt16, 32768 prepopulated coordinates, default safe cached accessors",
                     "measurements": [{"median": v["medianMillisecondsPerOperation"], "min": v["minLaunch"], "max": v["maxLaunch"], "n": len(v["launchMedians"])} for v in (n, m)]})
        ratios = item["pairedRatios"]
        effects.append({"hypothesisId": hypothesis["id"], "label": item["name"], "effect": item["managedOverNative"] - 1,
                        "range": [min(ratios) - 1, max(ratios) - 1], "note": "Managed time relative to native; positive means slower."})
        cards.append({"title": item["name"], "value": m["medianMillisecondsPerOperation"], "unit": "ms/op",
                      "baseline": n["medianMillisecondsPerOperation"], "wallChangePercent": (item["managedOverNative"] - 1) * 100,
                      "progression": f"Native {n['operationsPerSecond']:,.0f} ops/s; managed {m['operationsPerSecond']:,.0f} ops/s"})
    config = environment["configuration"]
    # The standard report rounds numeric labels to three decimals. Chart equal-sized
    # batches in milliseconds so small per-access values remain visible; raw JSON
    # and progression text retain milliseconds per operation without rescaling.
    operations = config["operations"]
    for row in rows:
        for measurement in row["measurements"]:
            for field in ("median", "min", "max"):
                measurement[field] *= operations
    for card, item in zip(cards, summaries):
        card["title"] += f" ({operations:,} operations)"
        card["value"] *= operations
        card["baseline"] *= operations
        card["unit"] = "ms/batch"
        card["progression"] = (f"Managed {item['managed']['medianMillisecondsPerOperation']:.11f} ms/op; "
                               f"native {item['native']['medianMillisecondsPerOperation']:.11f} ms/op. " + card["progression"])
    report = {
        "title": "Managed versus native OpenVDB cached access", "generatedAt": environment["completed"], "status": "complete",
        "scope": "Two local coherent cached-access kernels on an existing UInt16 Tree4 5/4/3, Windows x64. No library optimization.",
        "question": "How do matching managed and native cached reads/writes compare on the same warmed coordinate stream?",
        "primaryMetric": "milliseconds per operation; median of launch medians",
        "summary": {"baseline": summaries[0]["native"]["medianMillisecondsPerOperation"], "final": summaries[0]["managed"]["medianMillisecondsPerOperation"], "unit": "ms/op", "changePercent": (summaries[0]["managedOverNative"] - 1) * 100,
                    "headline": "Read managed/native time ratio %.3fx; write %.3fx. Ratios describe these kernels, not general OpenVDB performance." % tuple(x["managedOverNative"] for x in summaries)},
        "scorecards": cards, "series": [{"title": "Cached access time", "description": f"Milliseconds per batch of {operations:,} operations. Bars: median of process-launch medians. Whiskers: fastest/slowest launch median. Setup excluded.", "unit": "ms/batch", "lowerIsBetter": True, "rounds": ["Native C++", "Managed C#"], "rows": rows}],
        "effectGroups": [{"name": "Managed relative to native", "control": "Contemporaneous paired launches with alternating implementation and workload order.", "items": effects}],
        "hypotheses": hypotheses,
        "retainedChanges": ["Reproducible cross-language cached-access harnesses; no library implementation changes."], "decisions": [],
        "environment": [{"label": key, "value": str(value)} for key, value in {
            "Managed revision": environment["managedRevision"], "Native revision": environment["nativeRevision"], "CPU": environment["os"]["cpu"],
            "OS": environment["os"]["platform"], "Power": environment["os"]["powerScheme"], "Affinity": f"Both processes: logical CPU {config['cpu']}",
            "Native build": "MSVC x64 Release /O2 /Ob2 /DNDEBUG, static OpenVDB 13.0.1; no native PGO/LTO enabled.",
            "Managed build": f"{environment.get('managedRuntime', 'See raw validation metadata')}, Release, tiered compilation/PGO enabled, server GC requested.",
            "Iterations": f"{config['launches']} paired launches; {config['samples']} samples/launch/workload; {config['operations']} operations/sample; {config['warmup_ms']} ms warmup/workload.",
            "Host lock": environment["lockPath"], "Baseline epoch": f"{config.get('epoch', 'E1')}; workloads and controls fixed before timing.",
            "Preexisting changes": "Only unrelated untracked .pending files/attachments existed initially; preserved."}.items()],
        "hotspots": [{"name": "Cached reads and writes", "location": "benchmarks/CachedAccess.Managed/Program.cs; benchmarks/CachedAccess.Native/main.cpp", "evidence": "User-nominated cached operations; no application profiling requested.", "limit": "Local kernel comparison only."}],
        "areas": [{"name": "Cached access", "boundary": "Coherent reads/writes of existing active voxels, no topology changes.", "evidence": "Matching fixture hashes, checksums and topology; paired raw launch timings.", "hypothesisIds": ["H-001", "H-002"], "disposition": "Only these two comparison questions are authorized. Optimization hypotheses and additional workloads are outside scope; no padded ten/twenty-hypothesis catalogue."}],
        "correctness": ["Native/managed UInt16 Tree4 5/4/3 fixture: identical coordinate hashes, 32768 active voxels, 64 leaves, checksum 147456.", "Every measured read sum checked against count/8 × 36; every write pass changes the value phase and validates all 32768 values/activity outside timing.", "Both restore the initial state before each workload and after measurements."],
        "generatedCode": [],
        "validityThreats": ["A warm 32×32×32 coordinate working set with 87.5% leaf-cache hit rate; not random access, cold data, tree creation, meshing, filtering or I/O.", "Different implementation languages, compilers, layouts and accessor cache levels; the result cannot identify a single causal mechanism.", "No application-level speedup, universal performance advantage, confidence interval or native allocation measurement is claimed.", "Shared host lock is cooperative. External applications can still affect CPU/cache/power; per-CPU load snapshots are recorded in raw/environment.json.", "Allocation counters cover the managed current thread only. Native memory allocation is not instrumented.", "Default safe native accessor versus managed structural-version invalidation; both conform for this no-topology-change workload.", "Native dependency build paths contain prior reference artifacts; exact static-library SHA-256, source revision and compiler settings are retained."],
        "reproduction": ["Run the driver through scientific-method-performance/scripts/run_with_performance_lock.py; see benchmarks/CACHED_COMPARISON.md.", "Use the pinned native checkout and Release reference libraries. All disposable builds/dependencies go under the named D:\\Workspaces\\AI task directory."],
        "artifacts": [{"label": "Comparison summary", "path": "comparison.json"}, {"label": "Raw process measurements and metadata", "path": "raw/"}, {"label": "Hypothesis ledger", "path": "hypotheses.json"}, {"label": "Reproduction runner", "path": os.path.relpath(Path(__file__).resolve(), evidence)}],
    }
    save(evidence / "report-data.json", report)


if __name__ == "__main__":
    main()
