# Forward instance and dependency-chain verification

On **2026-09-04**, `Build-InstanceReference.ps1 -SkipBuild` passed **179 managed tests** and **64 independent native reads of managed-written archives**, using pinned OpenVDB 13.0.1 revision `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`. The combined IO and mask-inheritance suites pass **465 tests** in Release. Native and managed toolchains and zlib/oneTBB dependency checksums are recorded in [compression provenance](COMPRESSION_VERIFICATION.md).

The reader parses every grid before connecting instance trees. Forward and backward references work on seekable and non-seekable input streams, with either native descriptor layout. Grid metadata and transforms remain independent while instances share only their tree. The supported grid identities are Int32, Int64, Float, Double, Bool, Mask, Vec3i, Vec3s and Vec3d.

## Native ordering and the optional extension

`VdbReadOptions.InstanceResolution` defaults to `VdbInstanceResolution.NativeOrder`: connect instances once in file descriptor order, matching native **`io::Stream`**. This matters for chains. For descriptors ordered `a -> b`, `b -> z`, `z -> stored tree`, native Stream attaches `a` to the initial empty tree of `b`, then attaches `b` to the stored tree of `z`. Consequently `a` stays empty. The managed default deliberately preserves this observed behavior. Direct forward references to non-instance grids resolve correctly.

Native `io::File` can instead connect in its name-map order, so its chain result and output grid order can differ from Stream. The unified managed reader preserves file descriptor order for both offset and streaming layouts; it does not claim to reproduce File's alternative name ordering.

Use the explicit managed extension to resolve every acyclic chain to its terminal non-instance tree:

```csharp
VdbArchive archive = VdbFile.ReadArchive(stream, options: new VdbReadOptions
{
    InstanceResolution = VdbInstanceResolution.ResolveDependencies
});
```

Both modes reject cyclic/self references, missing parents, duplicate unique descriptor names and parent/child grid-type mismatches. Repeated display names remain supported through native record-separator suffixes. Cycle and duplicate-name rejection strengthen validation: pinned native readers can silently produce empty or ambiguously connected trees for those inputs. Validation completes before any instance tree is assigned.

A child's half-storage flag may differ from its parent's, matching native `Grid::setTree`: an instance contains no value buffer of its own. Each grid still requires its own descriptor half suffix to agree with its metadata, and half encoding remains restricted to floating scalar/vector grids. On export, the writer follows native behavior and writes separate tree data when the half flags differ. This can intentionally break sharing across those differently encoded grids and applies the requested half quantization.

Resolution is iterative and linear in grid count. Pending descriptors, placeholder trees and parent/root/state arrays are charged to the allocation budget; graph arrays are bounded by `MaxGrids`. Actual stored tree nodes are charged once, irrespective of the number or depth of references. No recursion, repeated payload reads or whole-stream buffering is introduced. Tests include valid and cyclic 4,096-grid chains, limits on grids/bytes/allocation/nodes, deferred failures and stream ownership.

## Native corpus and reproduction

The source-controlled `instance-oracle.cpp` calls native protected `Archive::writeHeader`, `writeGrid` and `writeGridInstance` with explicit descriptors. This creates genuine native format225 files with graphs that ordinary native `Archive::write` does not produce: the ordinary writer only refers to an earlier matching tree and half flag. The oracle then reads the emitted files through native Stream. It also writes canonical native results and explicitly resolved native-tree results for the optional extension; the latter are not presented as native Stream's default semantics.

The corpus contains **42 explicitly constructed native archives** (21 cases in two descriptor layouts), of which 32 are valid positive profiles. It adds **64 native canonical archives**, for **106 committed files**, about 1.65 MB. Cases cover direct forward sharing, chains with different orderings, all supported value types, independent metadata/transforms, half flags in both directions, repeated names with unique suffixes, cycles, self references, missing names, type mismatches and duplicate descriptors. Managed reads are tested through both seekable and non-seekable stream implementations.

For each of the 32 positive profiles and both resolution modes, a managed-written archive is loaded by native OpenVDB. The native verifier compares every stored value/activity, topology, background, counts, file/grid metadata and sampled transforms. It checks tree sharing where equal half flags permit native instancing. The managed tests separately verify sharing before export across differing half flags and exercise shared MaskGrid mutations without exposing its Boolean backing tree.

```powershell
./reference/Build-InstanceReference.ps1 -OpenVdbSource D:/source/openvdb
```

`-SkipBuild` reuses the pinned native build; `-TbbRoot` selects an existing pinned oneTBB installation. Fixture copies are published through temporary files and rename. Native build artifacts remain ignored. The managed runtime requires no native dependency.

Source references: `io/Archive.cc:1099` (one-pass connection), `io/Stream.cc:125` (descriptor-order connections), `io/File.cc:477` (name-map connections), `io/Archive.cc:1334` (writer parent selection), and `Grid.h:1487` (tree-type validation and assignment). Earlier archive documents describe their historical milestone; this document supersedes their forward-reference exclusion and parent-half-equality restriction. Legacy versions, custom/point layouts, partial I/O and delayed loading remain outside the verified codec scope.
