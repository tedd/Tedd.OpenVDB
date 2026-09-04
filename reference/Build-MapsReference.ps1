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
$build = Join-Path $referenceRoot 'build-oracle-codecs'
if (-not $TbbRoot) { $TbbRoot = Join-Path $referenceRoot 'dependencies/oneapi-tbb-2022.2.0' }
if ((& git -C $OpenVdbSource rev-parse HEAD).Trim() -ne 'f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf') { throw 'Incorrect OpenVDB revision.' }
if (-not $SkipBuild) {
    & "$referenceRoot/Build-CodecReference.ps1" -OpenVdbSource $OpenVdbSource -CMake $CMake -Generator $Generator -DotNet $DotNet -TbbRoot $TbbRoot
    & $CMake --build $build --config Release --target vdb-maps-reference --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Map oracle build failed.' }
}
$previousPath = $env:PATH
$previousOutput = $env:TEDD_OPENVDB_MAP_OUTPUT_DIR
try {
    $env:PATH = "$TbbRoot/redist/intel64/vc14;$previousPath"
    $oracle = Join-Path $build 'Release/vdb-maps-reference.exe'
    $generated = Join-Path $build 'generated-maps'
    & $oracle generate $generated
    if ($LASTEXITCODE -ne 0) { throw 'Native map generation failed.' }
    & $oracle extreme (Join-Path $generated 'native-maps-extreme.json')
    if ($LASTEXITCODE -ne 0) { throw 'Native extreme-scale generation failed.' }
    $fixtures = Join-Path $root 'tests/Tedd.OpenVDB.Tests/Fixtures/maps'
    New-Item -ItemType Directory -Force -Path $fixtures | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $generated -File) {
        if ($file.Extension -notin @('.json', '.vdb')) { continue }
        $target = Join-Path $fixtures $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination ($target + '.tmp') -Force
        Move-Item -LiteralPath ($target + '.tmp') -Destination $target -Force
    }
    $env:TEDD_OPENVDB_MAP_OUTPUT_DIR = Join-Path $build 'managed-maps'
    Push-Location $root
    try {
        & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter 'FullyQualifiedName~VdbMap' --artifacts-path artifacts/maps-reference --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Managed map verification failed.' }
    } finally { Pop-Location }
    foreach ($compression in @(0,4,6)) {
        & $oracle verify "$env:TEDD_OPENVDB_MAP_OUTPUT_DIR/managed-maps-$compression.vdb" "$fixtures/native-maps-$compression.vdb"
        if ($LASTEXITCODE -ne 0) { throw 'Native map readback failed.' }
    }
    Write-Output 'Passed: all 60 grids in 3 managed archives independently verified by native OpenVDB.'
} finally {
    $env:PATH = $previousPath
    $env:TEDD_OPENVDB_MAP_OUTPUT_DIR = $previousOutput
}
