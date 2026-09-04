[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OpenVdbSource,
    [string]$CMake = 'cmake',
    [string]$Generator = 'Visual Studio 18 2026',
    [string]$DotNet = 'dotnet',
    [string]$TbbRoot = '',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$referenceRoot = $PSScriptRoot
$root = Split-Path -Parent $referenceRoot
$oracleBuild = Join-Path $referenceRoot 'build-oracle-codecs'
if (-not $TbbRoot) { $TbbRoot = Join-Path $referenceRoot 'dependencies/oneapi-tbb-2022.2.0' }
if ((& git -C $OpenVdbSource rev-parse HEAD).Trim() -ne 'f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf') { throw 'Incorrect OpenVDB revision.' }
if ((& git -C "$referenceRoot/dependencies/c-blosc" rev-parse HEAD).Trim() -ne '616f4b7343a8479f7e71dd3d7025bd92c9a6bbd0') { throw 'Incorrect c-blosc revision.' }
if (-not $SkipBuild) {
    & "$referenceRoot/Build-CodecReference.ps1" -OpenVdbSource $OpenVdbSource -CMake $CMake -Generator $Generator -DotNet $DotNet -TbbRoot $TbbRoot
    & $CMake --build $oracleBuild --config Release --target vdb-zstd-reference vdb-blosc-reference --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Zstd reference build failed.' }
}
$previousPath = $env:PATH
$previousOutput = $env:TEDD_OPENVDB_ZSTD_OUTPUT_DIR
try {
    $env:PATH = "$TbbRoot/redist/intel64/vc14;$previousPath"
    $oracle = Join-Path $oracleBuild 'Release/vdb-zstd-reference.exe'
    $vdbOracle = Join-Path $oracleBuild 'Release/vdb-blosc-reference.exe'
    $generated = Join-Path $oracleBuild 'generated-zstd'
    New-Item -ItemType Directory -Force -Path $generated | Out-Null
    & $oracle "$generated/native-zstd.json" "$referenceRoot/dependencies/c-blosc/compat"
    if ($LASTEXITCODE -ne 0) { throw 'Native Zstd fixture verification failed.' }
    & $vdbOracle generate-zstd $generated
    if ($LASTEXITCODE -ne 0) { throw 'Native .vdb Zstd fixture verification failed.' }
    $fixtures = Join-Path $root 'tests/Tedd.OpenVDB.Tests/Fixtures/zstd'
    New-Item -ItemType Directory -Force -Path $fixtures | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $generated -File) {
        if ($file.Extension -notin @('.vdb', '.json')) { continue }
        $target = Join-Path $fixtures $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination ($target + '.tmp') -Force
        Move-Item -LiteralPath ($target + '.tmp') -Destination $target -Force
    }
    $env:TEDD_OPENVDB_ZSTD_OUTPUT_DIR = Join-Path $oracleBuild 'managed-zstd'
    Push-Location $root
    try {
        & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter 'FullyQualifiedName~VdbZstdTests' --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Managed Zstd fixture verification failed.' }
    } finally { Pop-Location }
    $verified = 0
    foreach ($filter in 0..2) { foreach ($profile in 0..3) {
        $name = "zstd-$filter-$profile"
        & $vdbOracle verify "$env:TEDD_OPENVDB_ZSTD_OUTPUT_DIR/managed-$name.vdb" "$fixtures/expected-frame-$name.vdb" float
        if ($LASTEXITCODE -ne 0) { throw "Native managed-file verification failed: $name" }
        ++$verified
    } }
    Write-Output "Passed: $verified managed files independently verified by native OpenVDB, plus native Zstd frame fixtures."
} finally {
    $env:PATH = $previousPath
    $env:TEDD_OPENVDB_ZSTD_OUTPUT_DIR = $previousOutput
}
