"""Isolated .NET 11 cached-access experiments; invoke through the shared host lock."""
import argparse
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import statistics
import subprocess
import sys
import time
import urllib.request
import zipfile
sys.dont_write_bytecode = True

REPO = Path(__file__).resolve().parent.parent
WORK = Path(r"D:\Workspaces\AI\openvdb-cached-optimize")
EVIDENCE = REPO / "Research" / "2026-10-15 Cached access optimization"


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def environment():
    env = os.environ.copy()
    for key in list(env):
        if key.startswith("TEDD_OPENVDB_") or key.startswith("DOTNET_Jit") or key.startswith("COMPlus_"):
            del env[key]
    env.update(TEMP=str(WORK / "temp"), TMP=str(WORK / "temp"),
               DOTNET_CLI_HOME=str(WORK / "dotnet-home"), NUGET_PACKAGES=str(WORK / "nuget"),
               NUGET_HTTP_CACHE_PATH=str(WORK / "nuget-http"), DOTNET_CLI_TELEMETRY_OPTOUT="1",
               DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", DOTNET_NOLOGO="1", PYTHONDONTWRITEBYTECODE="1",
               DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_ADD_GLOBAL_TOOLS_TO_PATH="false",
               DOTNET_TieredCompilation="1", DOTNET_TieredPGO="1", DOTNET_EnableHWIntrinsic="1")
    Path(env["TEMP"]).mkdir(parents=True, exist_ok=True)
    return env


def run(command, env, log):
    result = subprocess.run(command, cwd=REPO, env=env, capture_output=True, text=True,
                            encoding="utf-8", errors="replace")
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open("a", encoding="utf-8") as output:
        output.write(repr(command) + "\n" + result.stdout + result.stderr + "\n")
    if result.returncode:
        raise RuntimeError(result.stdout[-4000:] + result.stderr[-4000:])
    return result.stdout


def build(tag, env, library_source=None, revision=None):
    source = WORK / tag / "source"
    if source.exists() or (EVIDENCE / "raw" / (tag + "-source.json")).exists():
        raise ValueError("Immutable experiment tag already exists: " + tag)
    tracked = subprocess.check_output(["git", "ls-files", "src", "benchmarks/CachedAccess.Managed",
                                       "Directory.Build.props", "global.json"], cwd=REPO, text=True).splitlines()
    hashes = {}
    for name in tracked:
        target = source / name
        target.parent.mkdir(parents=True, exist_ok=True)
        if revision and name.startswith("src/"):
            target.write_bytes(subprocess.check_output(["git", "show", revision + ":" + name], cwd=REPO))
        else:
            origin = WORK / library_source / "source" / name if library_source and name.startswith("src/") else REPO / name
            shutil.copy2(origin, target)
        hashes[name] = hashlib.sha256(target.read_bytes()).hexdigest()
    save(EVIDENCE / "raw" / (tag + "-source.json"), hashes)
    run(["dotnet", "build", str(source / "benchmarks/CachedAccess.Managed/CachedAccess.Managed.csproj"),
         "-c", "Release", "--artifacts-path", str(WORK / tag / "artifacts"),
         "-p:GeneratePackageOnBuild=false"], env, EVIDENCE / "raw" / (tag + "-build.log"))
    command = ["dotnet", str(WORK / tag / "artifacts/bin/CachedAccess.Managed/release/CachedAccess.Managed.dll")]
    validated = json.loads(run(command + ["--validate"], env, EVIDENCE / "raw" / (tag + "-validate.log")))
    save(EVIDENCE / "raw" / (tag + "-validation.json"), validated)
    print("Built and validated " + tag, flush=True)


def command(tag):
    if tag == "native":
        return [str(WORK / "native/Release/cached-access-native.exe")]
    return ["dotnet", str(WORK / tag / "artifacts/bin/CachedAccess.Managed/release/CachedAccess.Managed.dll")]


def build_native(env):
    from run_cached_comparison import TBB_SHA, TBB_URL, PIN
    upstream = Path(r"D:\source\openvdb")
    revision = subprocess.check_output(["git", "-C", str(upstream), "rev-parse", "HEAD"], text=True).strip()
    if revision != PIN:
        raise ValueError("Unexpected native source revision")
    archive = WORK / "tbb.zip"
    urllib.request.urlretrieve(TBB_URL, archive)
    if hashlib.sha256(archive.read_bytes()).hexdigest() != TBB_SHA:
        raise ValueError("oneTBB hash mismatch")
    with zipfile.ZipFile(archive) as package:
        for member in package.infolist():
            if not (WORK / member.filename).resolve().is_relative_to(WORK):
                raise ValueError("Unsafe archive member")
        package.extractall(WORK)
    tbb = WORK / "oneapi-tbb-2022.2.0"
    cmake = r"C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
    native_build = REPO / "reference/build-upstream-zlib"
    run([cmake, "-S", str(REPO / "benchmarks/CachedAccess.Native"), "-B", str(WORK / "native"),
         "-G", "Visual Studio 18 2026", "-A", "x64", f"-DOPENVDB_SOURCE_DIR={upstream}",
         f"-DOPENVDB_BUILD_DIR={native_build}", f"-DTBB_ROOT={tbb}",
         f"-DZLIB_LIBRARY={REPO / 'reference/build-zlib/Release/zs.lib'}"], env, EVIDENCE / "raw/native-build.log")
    run([cmake, "--build", str(WORK / "native"), "--config", "Release", "--parallel", "2"], env, EVIDENCE / "raw/native-build.log")
    save(EVIDENCE / "raw/native-identity.json", dict(revision=revision, tbbSha256=TBB_SHA,
         librarySha256=hashlib.sha256((native_build / "openvdb/openvdb/Release/libopenvdb.lib").read_bytes()).hexdigest()))
    print("Native build complete", flush=True)


def measure(tags, phase, launches, samples, operations, warmup, env, pattern):
    # Fixed complete budget: do not replace slow sessions or discard samples.
    for tag in tags:
        if not Path(command(tag)[-1]).is_file():
            raise ValueError("Build must complete before measuring: " + tag)
    protocol_path = EVIDENCE / "raw" / (phase + "-protocol.json")
    if protocol_path.exists():
        raise ValueError("Evidence phase already exists; choose a new phase name")
    protocol = dict(tags=tags, pattern=pattern, launches=launches, samples=samples, operations=operations,
                    warmupMilliseconds=warmup, cpu=8, order="rotate arms; alternate read/write first",
                    estimator="median of per-launch medians; descriptive paired ratios",
                    independence="not asserted; shared-host sequential sessions", exclusions="none",
                    retainedThreshold=("confirmation-plan-leaf.json" if phase.startswith("acceptance_leaf") else
                                       "confirmation-plan.json" if phase.startswith("confirmation") else
                                       "exploratory screen: all coherent launch pairs improve >=10%; no random regression >5%"),
                    runtimeControls={k: env[k] for k in ("DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_EnableHWIntrinsic")})
    save(protocol_path, protocol)
    results = {tag: [] for tag in tags}
    for launch in range(launches):
        order = tags[launch % len(tags):] + tags[:launch % len(tags)]
        for tag in order:
            args = ["--operations", str(operations), "--samples", str(samples), "--warmup-ms", str(warmup), "--cpu", "8"]
            if launch % 2:
                args.append("--write-first")
            if pattern != "coherent":
                args.extend(["--pattern", pattern])
            print(f"{phase}: launch {launch + 1}/{launches}, {tag}", flush=True)
            data = json.loads(run(command(tag) + args, env, WORK / "measurement.log"))
            if any(item["operations"] != operations for item in data["results"]):
                raise ValueError("Completed operation count differs from the frozen plan")
            save(EVIDENCE / "raw" / f"{phase}-{tag}-{launch + 1}.json", data)
            results[tag].append(data)
            print({x["name"]: round(statistics.median(x["elapsedMilliseconds"]) * 1e6 / x["operations"], 3)
                   for x in data["results"]}, flush=True)
    summary = {}
    for tag, runs in results.items():
        summary[tag] = {}
        for name in (x["name"] for x in runs[0]["results"]):
            values = [statistics.median(next(x for x in r["results"] if x["name"] == name)["elapsedMilliseconds"]) * 1e6 / operations for r in runs]
            summary[tag][name] = dict(nsPerOperation=statistics.median(values), launchMedians=values,
                                      minimum=min(values), maximum=max(values))
    save(EVIDENCE / (phase + "-summary.json"), summary)
    print(json.dumps(summary, indent=2), flush=True)


def main():
    global WORK, EVIDENCE
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["build", "native", "measure", "disasm", "tests"])
    parser.add_argument("tags", nargs="+")
    parser.add_argument("--phase", default="exploration")
    parser.add_argument("--launches", type=int, default=3)
    parser.add_argument("--samples", type=int, default=5)
    parser.add_argument("--operations", type=int, default=25_001_984)
    parser.add_argument("--warmup-ms", type=int, default=2000)
    parser.add_argument("--library-source")
    parser.add_argument("--revision", help="Library revision; the current common benchmark caller is retained")
    parser.add_argument("--pattern", choices=["coherent", "random", "partial", "churn"], default="coherent")
    parser.add_argument("--filter", help="Optional correctness test filter")
    parser.add_argument("--workspace", type=Path, default=WORK)
    parser.add_argument("--evidence", type=Path, default=EVIDENCE)
    args = parser.parse_args()
    if any(not re.fullmatch(r"[A-Za-z0-9_-]{1,64}", name) for name in args.tags + [args.phase] + ([args.library_source] if args.library_source else [])):
        raise ValueError("Tags and phases must be bounded file-name identifiers")
    WORK, EVIDENCE = args.workspace.resolve(), args.evidence.resolve()
    temporary_parent = Path(r"D:\Workspaces\AI").resolve()
    research_parent = (REPO / "Research").resolve()
    if WORK == temporary_parent or not WORK.is_relative_to(temporary_parent):
        raise ValueError("Workspace must be a task subdirectory of D:\\Workspaces\\AI")
    if EVIDENCE == research_parent or not EVIDENCE.is_relative_to(research_parent):
        raise ValueError("Evidence must be a dedicated repository Research subdirectory")
    env = environment()
    env["PATH"] = str(WORK / "oneapi-tbb-2022.2.0/redist/intel64/vc14") + os.pathsep + env["PATH"]
    if args.action == "build":
        for tag in args.tags:
            build(tag, env, args.library_source, args.revision)
    elif args.action == "native":
        build_native(env)
    elif args.action == "measure":
        measure(args.tags, args.phase, args.launches, args.samples, args.operations, args.warmup_ms, env, args.pattern)
    elif args.action == "disasm":
        for tag in args.tags:
            diagnostic = env.copy()
            diagnostic["DOTNET_JitDisasm"] = "*Read* *Write* Tedd.OpenVDB.Tree.ValueAccessor`1:*"
            diagnostic["DOTNET_JitStdOutFile"] = str(EVIDENCE / "raw" / (tag + "-jit.txt"))
            run(command(tag) + ["--operations", "1048576", "--samples", "1"], diagnostic, WORK / "disasm.log")
    else:
        test_command = ["dotnet", "test", str(REPO / "tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj"),
             "-c", "Release", "--artifacts-path", str(WORK / "tests"), "-p:GeneratePackageOnBuild=false",
             "--results-directory", str(WORK / "test-results")]
        if args.filter:
            test_command.extend(["--filter", args.filter])
        run(test_command, env, EVIDENCE / "raw" / "tests.log")
        print("Test suite passed", flush=True)


if __name__ == "__main__":
    main()
