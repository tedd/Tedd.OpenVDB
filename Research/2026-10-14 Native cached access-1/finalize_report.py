"""Rebuild the reviewed reports from retained measurements, without retiming."""
import json
import os
from pathlib import Path
import subprocess
import sys

run = Path(__file__).resolve().parent
repo = run.parent.parent
sys.path.insert(0, str(repo / "benchmarks"))
from run_cached_comparison import produce_report, save  # noqa: E402

renderer = Path(r"C:\Users\tedd\.codex\skills\scientific-method-performance\scripts\render_report.py")
epoch_hypotheses = {}
for epoch_name in ("E1", "E2"):
    epoch = run / "epochs" / epoch_name
    summaries = json.loads((epoch / "comparison.json").read_text(encoding="utf-8"))
    environment = json.loads((epoch / "raw" / "environment.json").read_text(encoding="utf-8"))
    validation = json.loads((epoch / "raw" / "validation.json").read_text(encoding="utf-8"))
    environment["configuration"]["epoch"] = epoch_name
    environment["managedRuntime"] = validation["managed"]["runtime"]
    environment["nativeCompiler"] = validation["native"]["msvc"]
    environment["analysisReview"] = "Runtime read from validation metadata; ratio of medians and median paired ratio retained separately; timing samples unchanged."
    save(epoch / "raw" / "environment.json", environment)
    for item in summaries:
        paired = item["pairedRatios"]
        item["medianPairedRatio"] = sorted(paired)[len(paired) // 2]
        item["managedOverNative"] = item["managed"]["medianMillisecondsPerOperation"] / item["native"]["medianMillisecondsPerOperation"]
        assert item["managed"]["managedAllocatedBytesPerBatch"] == [0] * 45
        assert all(value > 1 for value in paired)
        for implementation in ("native", "managed"):
            metric = item[implementation]
            assert len(metric["launchMedians"]) == 5 and len(metric["allSamples"]) == 45
            assert abs(metric["operationsPerSecond"] * metric["medianMillisecondsPerOperation"] - 1000) < 1e-8
    save(epoch / "comparison.json", summaries)
    produce_report(epoch, environment, summaries)
    subprocess.run([sys.executable, str(renderer), str(epoch / "report-data.json"), str(epoch / "report.html")], check=True)
    epoch_hypotheses[epoch_name] = json.loads((epoch / "hypotheses.json").read_text(encoding="utf-8"))

final_data = json.loads((run / "epochs" / "E2" / "report-data.json").read_text(encoding="utf-8"))
final_data["scope"] += " Final result: E2 on logical CPU 16; the more heavily contended E1 on CPU 8 remains in epochs/E1."
final_data["environment"].append({"label": "Epoch selection", "value": "E1 selected CPU 8: 22% before, 51% after; sibling CPU 9: 76% before. E2 selected CPU 16: 5% before, 0% after. The first complete run was retained, not discarded."})
final_data["validityThreats"].append("Native write launch medians in E2 vary from 0.00000440416 to 0.00000587976 ms/op. Background load and frequency drift remain; use approximate ratios rather than a precise guarantee.")
final_data["artifacts"] = [
    {"label": "Final comparison summary", "path": "comparison.json"},
    {"label": "Final E2 raw measurements", "path": "epochs/E2/raw/"},
    {"label": "Retained first E1 report and measurements", "path": "epochs/E1/report.html"},
    {"label": "Complete hypothesis ledger", "path": "hypotheses.json"},
    {"label": "Reproduction instructions", "path": "../../benchmarks/CACHED_COMPARISON.md"},
]
for item in final_data["hypotheses"]:
    item["evidence"] = ["comparison.json", "epochs/E2/raw/validation.json", "epochs/E2/raw/environment.json"]
    item["epochs"] = [{"epoch": name, "result": next(h for h in entries if h["id"] == item["id"])["result"],
                       "state": next(h for h in entries if h["id"] == item["id"])["state"]} for name, entries in epoch_hypotheses.items()]
save(run / "hypotheses.json", final_data["hypotheses"])
save(run / "report-data.json", final_data)
save(run / "comparison.json", json.loads((run / "epochs" / "E2" / "comparison.json").read_text(encoding="utf-8")))
subprocess.run([sys.executable, str(renderer), str(run / "report-data.json"), str(run / "report.html")], check=True)
print("Reviewed both epochs: 180 measured samples per implementation, all managed allocation samples zero; reports rebuilt.")
