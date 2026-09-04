using Tedd.OpenVDB;
using Tedd.OpenVDB.IO;
using Tedd.OpenVDB.Tools;
using Tedd.OpenVDB.Math;
using Tedd.OpenVDB.Tree;

// One ushort material id per voxel. Zero is air; activity is separately controlled.
var blocks = new VdbTree<ushort>(background: 0);
var chunk = new CoordBBox(new(-32, -32, -32), new(-1, -1, -1));
blocks.Fill(chunk, 1); // Uniform 8³ regions remain compact tiles.
Console.WriteLine($"Solid chunk: {blocks.ActiveVoxelCount:N0} blocks, {blocks.LeafCount} leaves.");
var edit = blocks.GetAccessor(); // Reuse for coherent voxel access; one accessor per thread.
edit.SetValueOff(new(-16, -16, -16), 0); // Carve air and mark it inactive.
edit.SetValueOn(new(-15, -16, -16), 2);  // A different material.
Console.WriteLine($"Edited chunk: {blocks.ActiveVoxelCount:N0} blocks, {blocks.LeafCount} leaves.");
ushort[] renderBuffer = new ushort[(int)chunk.Volume];
blocks.CopyToDense(chunk, renderBuffer); // X, then Y, then contiguous Z.
Console.WriteLine($"Renderer buffer: {renderBuffer.Length:N0} material ids.");
edit.SetValueOn(new(-16, -16, -16), 1);
edit.SetValueOn(new(-15, -16, -16), 1);
blocks.Prune(); // Collapse uniform leaves; cached accessors invalidate automatically.
Console.WriteLine($"Restored chunk: {blocks.ActiveVoxelCount:N0} blocks, {blocks.LeafCount} leaves.");

// Standard .vdb interchange supports scalar, vector, Boolean and topology-mask grids.
var persisted = new Tedd.OpenVDB.Grid<int> { Name = "block-demo" };
persisted.Tree.AddTile(1, new(-1, -1, -1), 3, true);
persisted.Tree.SetValueOff(new(-2, -2, -2), 0);
using var stream = new MemoryStream();
Tedd.OpenVDB.IO.VdbFile.Write(stream, persisted);
stream.Position = 0;
var loaded = Tedd.OpenVDB.IO.VdbFile.Read<int>(stream);
if (!loaded.Tree.HasSameTopology(persisted.Tree) || loaded.Tree.GetValue(new(-2, -2, -2)) != 0)
    throw new InvalidOperationException("VDB roundtrip changed voxel data.");
Console.WriteLine($"VDB roundtrip: {loaded.ActiveVoxelCount:N0} blocks, {stream.Length:N0} bytes, grid '{loaded.Name}'.");


// Persist mixed voxel-game data in one compressed archive, preserving shared tree identity.
var identifiers = new Grid<long> { Name = "entity-ids" };
identifiers.Tree.SetValueOn(new(1, 2, 3), 9_007_199_254_740_993L);
var velocity = new Grid<Vec3f> { Name = "velocity" };
velocity.Tree.SetValueOn(new(1, 2, 3), new(1.5f, -2.25f, 3));
var flags = new Grid<bool> { Name = "flags" };
flags.Tree.SetValueOn(new(1, 2, 3), false); // Independent Boolean value and active state.
var occupancy = new MaskGrid { Name = "occupancy" };
occupancy.SetValueOn(new(1, 2, 3));
var sphere = LevelSetSphere.Create<float>(4, Vec3d.Zero, 1, 3);
sphere.Name = "terrain-distance";
sphere.SetMetadata("is_saved_as_half_float", true);
var secondSphere = LevelSetSphere.Create<float>(3, new(3, 0, 0), 1, 3);
Composite.CsgUnion(sphere, secondSphere);
var alias = new Grid<int>(persisted.Tree) { Name = "block-instance" };
var archive = new VdbArchive([persisted, alias, identifiers, velocity, flags, occupancy, sphere],
    new Dictionary<string, MetadataValue> { ["save-version"] = new(1) });
using var compressed = new MemoryStream();
VdbFile.WriteArchive(compressed, archive, options: new() { Compression = VdbCompression.Blosc | VdbCompression.ActiveMask });
compressed.Position = 0;
var restored = VdbFile.ReadArchive(compressed);
if (restored.Grids.Count != 7 ||
    !ReferenceEquals(((Grid<int>)restored.Grids[0]).Tree, ((Grid<int>)restored.Grids[1]).Tree) ||
    ((Grid<long>)restored.Grids[2]).Tree.GetValue(new(1, 2, 3)) != 9_007_199_254_740_993L ||
    ((Grid<Vec3f>)restored.Grids[3]).Tree.GetValue(new(1, 2, 3)) != new Vec3f(1.5f, -2.25f, 3) ||
    ((Grid<bool>)restored.Grids[4]).Tree.GetValue(new(1, 2, 3)) ||
    !((Grid<bool>)restored.Grids[4]).Tree.IsValueOn(new(1, 2, 3)) ||
    !((MaskGrid)restored.Grids[5]).GetValue(new(1, 2, 3)) ||
    restored.Grids[6].ActiveVoxelCount != sphere.ActiveVoxelCount)
    throw new InvalidOperationException("Compressed archive changed stored voxel data.");
var statistics = Statistics.Compute(((Grid<float>)restored.Grids[6]).Tree);
Console.WriteLine($"Compressed archive: {restored.Grids.Count} grids, {compressed.Length:N0} bytes, {statistics.Count:N0} level-set samples.");

// Exercise manager reductions, typed sampling and differential tools in NativeAOT as well.
var manager = new NodeManager<int>(loaded.Tree);
long allocatedNodes = manager.ReduceBottomUp(() => 0L, (count, node, index) => count + 1, (a, b) => a + b, parallel: true);
if (allocatedNodes != loaded.Tree.NodeCount().Sum()) throw new InvalidOperationException("Node-manager reduction lost nodes.");
var vectorSampler = new GridSampler<Vec3f>(velocity.Tree.GetAccessor(), velocity.Transform);
if (vectorSampler.IndexSample(new Vec3d(1, 2, 3)) != new Vec3f(1.5f, -2.25f, 3)) throw new InvalidOperationException("Vector sampling changed a voxel.");
var halfSampler = new GridSampler<Half>(new VdbTree<Half>((Half)1.5f), new Transform(), SamplingOrder.Quadratic);
if (halfSampler.IndexSample(new Vec3d(0.25, 0.5, 0.75)) != (Half)1.5f) throw new InvalidOperationException("Half sampling changed a constant field.");
var gradient = GridOperators.Gradient(sphere, options: new() { Threaded = false });
var measurement = new LevelSetMeasure<float>(sphere);
if (gradient.ActiveVoxelCount != sphere.ActiveVoxelCount || !double.IsFinite(measurement.Area()) || measurement.Volume() <= 0)
    throw new InvalidOperationException("Level-set analysis failed.");
Console.WriteLine($"Managers and sampling: {allocatedNodes} nodes; level-set area {measurement.Area():F3}, volume {measurement.Volume():F3}.");

// Sparse volume intervals skip empty leaves; surface intersection refines a signed-distance crossing.
var volumeRays = new VolumeRayIntersector<int>(loaded);
if (!volumeRays.SetIndexRay(new Rayd(new(-20, -3.5, -3.5), Vec3d.UnitX)) || !volumeRays.March(out double entry, out double exit) || exit <= entry)
    throw new InvalidOperationException("Sparse-volume ray missed occupied blocks.");
var surfaceRays = new LevelSetRayIntersector<float>(sphere, iterations: 2);
if (!surfaceRays.IntersectsWorld(new Rayd(new(-20, 0.125, 0.375), Vec3d.UnitX), out var surfacePoint, out var surfaceNormal, out double surfaceTime) || !surfacePoint.IsFinite || !surfaceNormal.IsFinite)
    throw new InvalidOperationException("Level-set ray did not produce a finite surface hit.");
Console.WriteLine($"Ray queries: occupied interval [{entry:F3}, {exit:F3}], surface distance {surfaceTime:F3}.");

// Exercise both geometry directions with compiled float/double generic kernels.
var box = MeshToVolume.CreateLevelSetBox<double>(new BBoxd(new Vec3d(-2), new Vec3d(2)), new Transform());
var mesher = new VolumeToMesh(adaptivity: 0.1);
mesher.Extract(box);
if (box.Tree.GetValue(Coord.Zero) >= 0 || mesher.Points.IsEmpty || mesher.PolygonPools.Count == 0)
    throw new InvalidOperationException("NativeAOT mesh conversion failed.");
Console.WriteLine($"Mesh conversion: {box.ActiveVoxelCount:N0} distance samples, {mesher.Points.Length:N0} surface points.");
