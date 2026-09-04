# File lifecycle and UUID verification

This family adapts OpenVDB 13.0.1 `io/File.h`, `io/File.cc`, and the header identity behavior in `io/Archive.cc` at commit `f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf`.

`VdbFileHandle` is a reusable path-based reader. `Open()` returns true on its first successful read or when the 36-byte archive UUID differs from the last successfully read UUID. `Close()` is idempotent and permits reopening. `Copy()` preserves path, resource options, version fields and last UUID, but returns closed so two objects never share an input stream. `IsIdentical` follows native case-sensitive, nonempty string comparison. Disposal is terminal under normal .NET ownership conventions.

The native C++ oracle writes an actual scalar `.vdb`, then observes first open, repeated-open rejection, copied-open identity, closed-operation rejection, double close, same-file reopen, fixed UUID patching, copying after a UUID change, and rewrite-generated UUID change. The native fixture contains 15 explicit outcomes, tag strings, file size, file metadata and a grid payload.

Managed tests repeat lifecycle operations against the final native file. They additionally verify transactionality: a failed full-index open leaves the handle closed and retains the prior successful identity. The complete archive reader is constructed from the same open stream used to inspect the UUID, preventing a path replacement race between identity and descriptor parsing. Missing paths, closed operations, terminal disposal, exact UUID case, version reporting and distinct UUID generation for successive managed writes are covered.

The test export harness produces two managed archives with distinct UUIDs. Native OpenVDB independently opens the first, copies its `io::File`, confirms unchanged identity on the copied open, replaces the path with the second archive, detects the UUID change and verifies the changed voxel. This is native readback of managed output rather than a managed-only test.

Mapped files, delayed loading, delayed-load copy thresholds and notifiers are outside this family. `VdbFileHandle` delegates tree decoding and regional selection to the existing eager/seekable `VdbFileReader`; it does not claim lazy grid storage. Native updates its internal UUID as soon as `readHeader` succeeds even if later metadata parsing fails. Managed state is deliberately transactional and commits the UUID only after the bounded descriptor index succeeds, avoiding a malformed file becoming the baseline identity.

## Reproduction

```powershell
./reference/Build-FileLifecycleReference.ps1 -OpenVdbSource D:/source/openvdb -CMake 'C:/Program Files/Microsoft Visual Studio/18/Insiders/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe' -TbbRoot C:/Users/tedd/AppData/Local/Temp/Tedd.OpenVDB-net11-port/managed/reference/dependencies/oneapi-tbb-2022.2.0
```

Use `-SkipBuild` to reuse native binaries. Native build products and temporary managed archives remain in ignored reference build/artifact directories. The script publishes `.json` and `.vdb` fixtures through temporary paths, runs focused managed tests and then invokes the native readback verifier.

Executed fixture hashes:

- `native-file-lifecycle.json`: `4445725fd3e238b410efaaa51ad02f8a14d4471176d491339709958a68c0f35a`
- `native-file-lifecycle.vdb`: `1d5f3deff2cfc40acbfb31f7a23dd9c8d01d6eebaed22532c7c1622ef126b970`

The focused lifecycle suite passed 5/5. The broader `VdbFile` Release regression group passed 802/802 after activation.
