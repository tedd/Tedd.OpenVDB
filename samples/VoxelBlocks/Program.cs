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

// Native .vdb interchange currently supports Int32/Float/Double grids.
var persisted = new Tedd.OpenVDB.Grid<int> { Name = "block-demo" };
persisted.Tree.AddTile(1, new(-1,-1,-1), 3, true);
persisted.Tree.SetValueOff(new(-2,-2,-2), 0);
using var stream = new MemoryStream();
Tedd.OpenVDB.IO.VdbFile.Write(stream, persisted);
stream.Position = 0;
var loaded = Tedd.OpenVDB.IO.VdbFile.Read<int>(stream);
if (!loaded.Tree.HasSameTopology(persisted.Tree) || loaded.Tree.GetValue(new(-2,-2,-2)) != 0)
    throw new InvalidOperationException("VDB roundtrip changed voxel data.");
Console.WriteLine($"VDB roundtrip: {loaded.ActiveVoxelCount:N0} blocks, {stream.Length:N0} bytes, grid '{loaded.Name}'.");
