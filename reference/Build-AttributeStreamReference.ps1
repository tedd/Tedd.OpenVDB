[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OpenVdbSource,
    [string]$CMake='cmake',
    [string]$Generator='Visual Studio 18 2026',
    [string]$DotNet='dotnet',
    [string]$TbbRoot='',
    [switch]$SkipBuild
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$referenceRoot=$PSScriptRoot
$root=Split-Path -Parent $referenceRoot
$build=Join-Path $referenceRoot 'build-attribute-stream'
if (-not $TbbRoot) { $TbbRoot=Join-Path $referenceRoot 'dependencies/oneapi-tbb-2022.2.0' }
$commit='f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf'
if ((& git -C $OpenVdbSource rev-parse HEAD).Trim() -ne $commit) { throw 'Incorrect OpenVDB revision.' }
if (-not $SkipBuild) {
    & "$referenceRoot/Build-CodecReference.ps1" -OpenVdbSource $OpenVdbSource -CMake $CMake -Generator $Generator -DotNet $DotNet -TbbRoot $TbbRoot
    & $CMake -S (Join-Path $referenceRoot 'attribute-stream') -B $build -G $Generator -A x64 "-DOPENVDB_SOURCE_DIR=$OpenVdbSource" "-DOPENVDB_BUILD_DIR=$referenceRoot/build-upstream-blosc" "-DTBB_ROOT=$TbbRoot" "-DBLOSC_LIBRARY=$referenceRoot/build-blosc-all/blosc/Release/libblosc.lib" "-DSNAPPY_LIBRARY=$referenceRoot/dependencies/snappy-install/lib/snappy.lib" "-DZLIB_LIBRARY=$referenceRoot/build-zlib/Release/zs.lib"
    if ($LASTEXITCODE -ne 0) { throw 'Attribute stream oracle configuration failed.' }
    & $CMake --build $build --config Release --parallel 2
    if ($LASTEXITCODE -ne 0) { throw 'Attribute stream oracle build failed.' }
}
$previousPath=$env:PATH
$previousExport=$env:VDB_ATTRIBUTE_STREAM_EXPORT
try {
    $env:PATH="$TbbRoot/redist/intel64/vc14;$previousPath"
    $oracle=Join-Path $build 'Release/vdb-attribute-stream-reference.exe'
    $generated=Join-Path $build 'attribute-stream.json'
    & $oracle generate $generated
    if ($LASTEXITCODE -ne 0) { throw 'Native attribute stream generation failed.' }
    $document=Get-Content -LiteralPath $generated -Raw | ConvertFrom-Json
    if ($document.commit -ne $commit -or $document.cases.Count -ne 500) { throw 'Unexpected fixture provenance or case count.' }
    $fixture=Join-Path $root 'tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-attribute-stream.json.gz'
    $inputStream=[IO.File]::OpenRead($generated)
    $outputStream=[IO.File]::Create("$fixture.tmp")
    $gzip=[IO.Compression.GZipStream]::new($outputStream,[IO.Compression.CompressionLevel]::SmallestSize)
    try { $inputStream.CopyTo($gzip) } finally { $gzip.Dispose(); $outputStream.Dispose(); $inputStream.Dispose() }
    Move-Item -LiteralPath "$fixture.tmp" -Destination $fixture -Force
    $env:VDB_ATTRIBUTE_STREAM_EXPORT=Join-Path $build 'managed-streams'
    Push-Location $root
    try {
        & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter 'FullyQualifiedName~AttributeStreamNativeTests' --artifacts-path artifacts/attribute-stream-reference --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Managed attribute stream verification failed.' }
    } finally { Pop-Location }
    & $oracle verify $env:VDB_ATTRIBUTE_STREAM_EXPORT
    if ($LASTEXITCODE -ne 0) { throw 'Native reading of managed attribute streams failed.' }
    Write-Output 'Passed: 500 native attribute profiles, 1,000 managed raw/Blosc exports independently read by OpenVDB.'
} finally { $env:PATH=$previousPath; $env:VDB_ATTRIBUTE_STREAM_EXPORT=$previousExport }
