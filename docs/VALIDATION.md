# Validation record

Date: 2026-09-04. Platform: Windows x64. SDK: 11.0.100-preview.7.26381.103.
Upstream OpenVDB: f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf (13.0.1).
Original wrapper repository HEAD before replacement: f05f82318811b97e1591395df10979c6cacb5886.
The original main branch and history were preserved; managed work is on codex/net11-openvdb-port.

The managed suite contains 600 xUnit tests. Validation includes Release and scalar-fallback
execution, native-generated fixtures, independently modeled randomized operations,
public API argument/stream failures, compacting GC, source/destination aliasing, and
read-only multi-task accessor use. CI is configured for Windows/Linux but no remote CI
execution is claimed in this record.

Commands used for the final target-directory validation:

```powershell
dotnet test Tedd.OpenVDB.slnx -c Release --logger 'trx;LogFileName=release.trx' --results-directory artifacts/test-results
$env:DOTNET_EnableHWIntrinsic='0'
dotnet test Tedd.OpenVDB.slnx -c Release --no-build --logger 'trx;LogFileName=scalar.trx' --results-directory artifacts/test-results
Remove-Item Env:DOTNET_EnableHWIntrinsic
dotnet test Tedd.OpenVDB.slnx -c Debug --logger 'trx;LogFileName=debug.trx' --results-directory artifacts/test-results
dotnet publish samples/VoxelBlocks -c Release -r win-x64 -p:PublishAot=true -o artifacts/native-sample
./artifacts/native-sample/VoxelBlocks.exe
dotnet pack src/Tedd.OpenVDB -c Release -o artifacts/packages
```

Native comparisons: 222 sparse-tree scenarios, 1,235 mutation checkpoints and 99,434
value/activity/depth samples. Native `.vdb` validation reads seven upstream-written
files in the managed implementation and reads seven managed-written files in the real
OpenVDB library. The I/O comparison covers every stored tile and voxel rather than only
spot-checking a few coordinates. Reproduction instructions and hashes are in reference/.

The x64 NativeAOT executable successfully ran the voxel editing, dense-buffer transfer,
pruning and Int32 standard-VDB roundtrip example. This validates the exercised generic
instantiations; it is not an assertion that every type and operation has AOT coverage.
The local benchmark reports allocation and throughput measurements in
benchmark-baseline.json. There is no controlled cross-runtime/native speedup claim.

The complete upstream library and test suite are not ported. PORT_STATUS.md records
remaining components, API gaps, and deliberate managed behavior differences.
