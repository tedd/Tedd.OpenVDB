using System.Diagnostics;
using System.Numerics;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

int operations = IntArg("--operations", 50_003_968);
int samples = IntArg("--samples", 9);
int warmupMilliseconds = IntArg("--warmup-ms", 2000);
int cpu = IntArg("--cpu", 8);
string pattern = Array.IndexOf(args, "--pattern") is int patternIndex && patternIndex >= 0 ? args[patternIndex + 1] : "coherent";
if (pattern is not ("coherent" or "random" or "partial" or "churn" or "large" or "large-random" or "collisions")) throw new ArgumentException("Unknown access pattern.");
if (operations < 32768 || operations % 32768 != 0 || samples < 1 || warmupMilliseconds < 0 || cpu is < 0 or > 62)
    throw new ArgumentException("Operations must be a positive multiple of 32768; invalid sample, warmup or CPU argument.");
if (OperatingSystem.IsWindows()) Process.GetCurrentProcess().ProcessorAffinity = new IntPtr(1L << cpu);

if (pattern == "churn")
{
    var changingTree = new VdbTree<ushort>(0);
    var changingAccessor = changingTree.GetAccessor();
    long warmupStart = Stopwatch.GetTimestamp();
    do { Churn(changingTree, changingAccessor, 8192); }
    while (Stopwatch.GetElapsedTime(warmupStart).TotalMilliseconds < warmupMilliseconds);
    var elapsed = new double[samples];
    var allocated = new long[samples];
    for (int sample = 0; sample < samples; ++sample)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        Churn(changingTree, changingAccessor, operations);
        elapsed[sample] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        allocated[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
        if (changingAccessor.GetValue(new(1, 2, 3)) != (ushort)((operations - 1) & 65535)
            || changingTree.ActiveVoxelCount != 1 || changingTree.LeafCount != 1)
            throw new InvalidOperationException("Topology-churn validation failed.");
    }
    Console.WriteLine(JsonSerializer.Serialize(new { implementation = "managed", pattern, runtime = RuntimeInformation.FrameworkDescription,
        results = new[] { new { name = "accessor.topology.churn", operations, elapsedMilliseconds = elapsed, allocatedBytes = allocated } } }));
    return;
}

var coordinates = new Coord[32768];
var tree = new VdbTree<ushort>(0);
long accessorAllocation = GC.GetAllocatedBytesForCurrentThread();
var accessor = tree.GetAccessor();
accessorAllocation = GC.GetAllocatedBytesForCurrentThread() - accessorAllocation;
for (int i = 0; i < coordinates.Length; ++i)
    coordinates[i] = new((i >> 10) - 16, ((i >> 5) & 31) - 16, (i & 31) - 16);
if (pattern is "large" or "large-random")
    for (int i = 0; i < coordinates.Length; ++i)
    {
        int leaf = i >> 6, local = i & 63;
        coordinates[i] = new(((leaf >> 6) - 4) * 8, (((leaf >> 3) & 7) - 4) * 8 + (local >> 3),
            ((leaf & 7) - 4) * 8 + (local & 7));
    }
if (pattern == "collisions")
    for (int i = 0; i < coordinates.Length; ++i)
    {
        int leaf = i >> 9, local = i & 511;
        coordinates[i] = new(((leaf >> 4) - 2) * 32 + (local >> 6),
            (((leaf >> 2) & 3) - 2) * 32 + ((local >> 3) & 7), ((leaf & 3) - 2) * 32 + (local & 7));
    }
if (pattern is "random" or "large-random" or "collisions")
{
    uint seed = 7391;
    for (int i = coordinates.Length - 1; i > 0; --i)
    {
        seed = unchecked(seed * 1664525 + 1013904223);
        int j = (int)(seed % (uint)(i + 1));
        (coordinates[i], coordinates[j]) = (coordinates[j], coordinates[i]);
    }
}
long setupAllocation = GC.GetAllocatedBytesForCurrentThread();
long setupStart = Stopwatch.GetTimestamp();
for (int i = 0; i < coordinates.Length; ++i)
{
    if (pattern == "partial" && (i & 1) != 0) accessor.SetValueOff(coordinates[i], (ushort)(1 + (i & 7)));
    else accessor.SetValueOn(coordinates[i], (ushort)(1 + (i & 7)));
}
double setupMilliseconds = Stopwatch.GetElapsedTime(setupStart).TotalMilliseconds;
setupAllocation = GC.GetAllocatedBytesForCurrentThread() - setupAllocation;
ulong coordinateHash = 14695981039346656037UL;
foreach (var c in coordinates)
    foreach (int component in new[] { c.X, c.Y, c.Z }) coordinateHash = unchecked((coordinateHash ^ (uint)component) * 1099511628211UL);
Validate(0);
long sink = 0;
var results = new List<object>();
if (!args.Contains("--validate"))
{
    foreach (string workload in args.Contains("--write-first") ? new[] { "write", "read" } : new[] { "read", "write" })
    {
        // Every launch primes the same populated tree. Allocation/setup are outside timed access loops.
        long warmupStart = Stopwatch.GetTimestamp();
        do
        {
            if (workload == "read") sink = Read(accessor, coordinates, 1_048_576);
            else WriteFixture(1_048_576, 3);
        } while (Stopwatch.GetElapsedTime(warmupStart).TotalMilliseconds < warmupMilliseconds);
        WriteFixture(coordinates.Length, 0);
        Validate(0);
        var elapsed = new double[samples];
        var allocated = new long[samples];
        for (int sample = 0; sample < samples; ++sample)
        {
            long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            if (workload == "read") sink = Read(accessor, coordinates, operations);
            else WriteFixture(operations, sample + 1);
            long end = Stopwatch.GetTimestamp();
            allocated[sample] = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
            elapsed[sample] = (end - start) * 1000.0 / Stopwatch.Frequency;
            if (workload == "read" && sink != operations / 8L * 36) throw new InvalidOperationException("Read checksum mismatch.");
            Validate(workload == "read" ? 0 : sample + 1);
        }
        results.Add(new { name = "accessor." + workload + "." + pattern, operations, elapsedMilliseconds = elapsed, allocatedBytes = allocated });
        WriteFixture(coordinates.Length, 0);
    }
}
Validate(0);
Console.WriteLine(JsonSerializer.Serialize(new
{
    implementation = "managed", valueType = "UInt16", coordinates = coordinates.Length, pattern,
    setupMilliseconds, setupAllocatedBytes = setupAllocation, accessorAllocatedBytes = accessorAllocation,
    coordinateHash = coordinateHash.ToString("x16"), checksum = 147456,
    activeVoxelCount = tree.ActiveVoxelCount, leafCount = tree.LeafCount,
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    serverGc = GCSettings.IsServerGC, vectorBytes = Vector<byte>.Count,
    cpu, affinityMask = (1L << cpu).ToString("x"),
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
    tieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "default",
    warmupMilliseconds, sink, results
}));

int IntArg(string name, int fallback)
{
    int index = Array.IndexOf(args, name);
    return index < 0 ? fallback : int.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture);
}
void Validate(int phase)
{
    long checksum = 0;
    for (int i = 0; i < coordinates.Length; ++i)
    {
        bool active = pattern != "partial" || (i & 1) == 0;
        ushort expected = (ushort)(1 + ((i + (active ? phase : 0)) & 7));
        if (accessor.ProbeValue(coordinates[i], out ushort actual) != active || actual != expected)
            throw new InvalidOperationException("Voxel value/activity mismatch at " + i);
        checksum += actual;
    }
    long expectedChecksum = pattern == "partial" && (phase & 1) != 0 ? 163840 : 147456;
    if (checksum != expectedChecksum || tree.ActiveVoxelCount != (pattern == "partial" ? 16384UL : 32768UL)
        || tree.LeafCount != (pattern is "large" or "large-random" ? 512L : 64L))
        throw new InvalidOperationException("Tree topology or aggregate checksum mismatch.");
}
void WriteFixture(int count, int phase)
{
    if (pattern == "partial") WritePartial(accessor, coordinates, count, phase);
    else Write(accessor, coordinates, count, phase);
}
[MethodImpl(MethodImplOptions.NoInlining)]
static long Read(ValueAccessor<ushort> accessor, Coord[] coordinates, int count)
{
    long sum = 0;
    for (int i = 0; i < count; ++i) sum += accessor.GetValue(coordinates[i & 32767]);
    return sum;
}
[MethodImpl(MethodImplOptions.NoInlining)]
static void Write(ValueAccessor<ushort> accessor, Coord[] coordinates, int count, int phase)
{
    for (int i = 0; i < count; ++i)
        accessor.SetValueOn(coordinates[i & 32767], (ushort)(1 + ((i + phase) & 7)));
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void WritePartial(ValueAccessor<ushort> accessor, Coord[] coordinates, int count, int phase)
{
    for (int i = 0; i < count; ++i)
        accessor.SetValueOn(coordinates[(i & 16383) << 1], (ushort)(1 + (((i << 1) + phase) & 7)));
}

[MethodImpl(MethodImplOptions.NoInlining)]
static void Churn(VdbTree<ushort> tree, ValueAccessor<ushort> accessor, int count)
{
    var point = new Coord(1, 2, 3);
    for (int i = 0; i < count; ++i)
    {
        tree.AddTile(1, point, 0, false);
        accessor.SetValueOn(point, (ushort)(i & 65535));
    }
}
