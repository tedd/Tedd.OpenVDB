using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;
using Tedd.OpenVDB.Util;

// A reproducible smoke benchmark, not a statistically rigorous cross-runtime comparison.
const int count = 1_000_000;
var tree = new VdbTree<ushort>();
var accessor = tree.GetAccessor();
var coords = new Coord[32768];
for (int i = 0; i < coords.Length; i++)
{
    coords[i] = new((i >> 10) - 16, ((i >> 5) & 31) - 16, (i & 31) - 16);
    accessor.SetValueOn(coords[i], (ushort)(1 + (i & 7)));
}
long sink = 0;
var maskA = new NodeMask(5, true);
var maskB = new NodeMask(5);
for (int i = 0; i < maskB.Size; i += 3) maskB.SetOn(i);
var results = new List<object>();
Measure("tree.read.coherent", count, () =>
{
    long sum = 0;
    var local = coords;
    for (int i = 0; i < count; i++) sum += tree.GetValue(local[i & (local.Length - 1)]);
    sink = sum;
});
Measure("accessor.read.coherent", count, () =>
{
    long sum = 0;
    var local = coords;
    for (int i = 0; i < count; i++) sum += accessor.GetValue(local[i & (local.Length - 1)]);
    sink = sum;
});
Measure("accessor.write.coherent", count, () =>
{
    var local = coords;
    for (int i = 0; i < count; i++) accessor.SetValueOn(local[i & (local.Length - 1)], (ushort)(1 + (i & 7)));
});
Measure("mask.xor.32768bits", 20000, () =>
{
    for (int i = 0; i < 20000; i++) maskA.XorWith(maskB);
    sink = maskA.CountOn();
});
Measure("fill.uniform.4096cubed", 1000, () =>
{
    var box = new CoordBBox(default, new(4095, 4095, 4095));
    for (int i = 0; i < 1000; i++)
    {
        var volume = new VdbTree<ushort>();
        volume.Fill(box, 7);
        sink = (long)volume.ActiveVoxelCount;
    }
});
Console.WriteLine(JsonSerializer.Serialize(new
{
    runtime = RuntimeInformation.FrameworkDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    serverGc = GCSettings.IsServerGC,
    processorCount = Environment.ProcessorCount,
    vectorAccelerated = System.Numerics.Vector.IsHardwareAccelerated,
    vectorBytes = System.Numerics.Vector<byte>.Count,
    tieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "runtime default",
    generatedUtc = DateTimeOffset.UtcNow,
    note = "Five warmups, seven measurements; median. Stopwatch microbenchmark; no cross-runtime speedup claims.",
    results,
    sink
}, new JsonSerializerOptions { WriteIndented = true }));

void Measure(string name, int operations, Action action)
{
    for (int i = 0; i < 5; i++) action();
    var samples = new (double ns, long allocated)[7];
    for (int i = 0; i < samples.Length; i++)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        action();
        long end = Stopwatch.GetTimestamp();
        samples[i] = ((end - start) * (1_000_000_000.0 / Stopwatch.Frequency) / operations,
            GC.GetAllocatedBytesForCurrentThread() - allocated);
    }
    Array.Sort(samples, (a, b) => a.ns.CompareTo(b.ns));
    var median = samples[samples.Length / 2];
    results.Add(new { name, operations, nanosecondsPerOperation = median.ns, allocatedBytes = median.allocated });
}
