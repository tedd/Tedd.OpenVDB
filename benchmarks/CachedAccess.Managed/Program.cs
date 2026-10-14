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
if (operations < 32768 || operations % 32768 != 0 || samples < 1 || warmupMilliseconds < 0 || cpu is < 0 or > 62)
    throw new ArgumentException("Operations must be a positive multiple of 32768; invalid sample, warmup or CPU argument.");
if (OperatingSystem.IsWindows()) Process.GetCurrentProcess().ProcessorAffinity = new IntPtr(1L << cpu);

var coordinates = new Coord[32768];
var tree = new VdbTree<ushort>(0);
var accessor = tree.GetAccessor();
for (int i = 0; i < coordinates.Length; ++i)
{
    coordinates[i] = new((i >> 10) - 16, ((i >> 5) & 31) - 16, (i & 31) - 16);
    accessor.SetValueOn(coordinates[i], (ushort)(1 + (i & 7)));
}
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
            else Write(accessor, coordinates, 1_048_576, 3);
        } while (Stopwatch.GetElapsedTime(warmupStart).TotalMilliseconds < warmupMilliseconds);
        Write(accessor, coordinates, coordinates.Length, 0);
        Validate(0);
        var elapsed = new double[samples];
        var allocated = new long[samples];
        for (int sample = 0; sample < samples; ++sample)
        {
            long beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            if (workload == "read") sink = Read(accessor, coordinates, operations);
            else Write(accessor, coordinates, operations, sample + 1);
            long end = Stopwatch.GetTimestamp();
            allocated[sample] = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
            elapsed[sample] = (end - start) * 1000.0 / Stopwatch.Frequency;
            if (workload == "read" && sink != operations / 8L * 36) throw new InvalidOperationException("Read checksum mismatch.");
            Validate(workload == "read" ? 0 : sample + 1);
        }
        results.Add(new { name = "accessor." + workload + ".coherent", operations, elapsedMilliseconds = elapsed, allocatedBytes = allocated });
        Write(accessor, coordinates, coordinates.Length, 0);
    }
}
Validate(0);
Console.WriteLine(JsonSerializer.Serialize(new
{
    implementation = "managed", valueType = "UInt16", coordinates = coordinates.Length,
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
        ushort expected = (ushort)(1 + ((i + phase) & 7));
        if (!accessor.ProbeValue(coordinates[i], out ushort actual) || actual != expected)
            throw new InvalidOperationException("Voxel value/activity mismatch at " + i);
        checksum += actual;
    }
    if (checksum != 147456 || tree.ActiveVoxelCount != 32768 || tree.LeafCount != 64)
        throw new InvalidOperationException("Tree topology or aggregate checksum mismatch.");
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
