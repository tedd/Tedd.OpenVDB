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
    & $CMake --build $build --config Release --target vdb-file-partial-reference --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'File oracle build failed.' }
}
$previousPath = $env:PATH
try {
    $env:PATH = "$TbbRoot/redist/intel64/vc14;$previousPath"
    $oracle = Join-Path $build 'Release/vdb-file-partial-reference.exe'
    $generated = Join-Path $build 'generated-file-partial'
    & $oracle generate $generated
    if ($LASTEXITCODE -ne 0) { throw 'Native File generation failed.' }
    $fixtures = Join-Path $root 'tests/Tedd.OpenVDB.Tests/Fixtures/file-partial'
    New-Item -ItemType Directory -Force -Path $fixtures | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $generated -File) {
        if ($file.Extension -notin @('.json', '.vdb')) { continue }
        $target = Join-Path $fixtures $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination ($target + '.tmp') -Force
        Move-Item -LiteralPath ($target + '.tmp') -Destination $target -Force
    }
    Push-Location $root
    try {
        & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter 'FullyQualifiedName~VdbFileReader' --artifacts-path artifacts/file-partial-reference --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Managed File verification failed.' }
    } finally { Pop-Location }
    Write-Output 'Passed: native File descriptor, metadata, selected-grid, world-box and streaming behavior verified against six native archives and the native skipped-buffer corruption fixture.'
} finally {
    $env:PATH = $previousPath
}
