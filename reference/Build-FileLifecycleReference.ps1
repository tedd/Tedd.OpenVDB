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
$build=Join-Path $referenceRoot 'build-oracle-codecs'
if (-not $TbbRoot) { $TbbRoot=Join-Path $referenceRoot 'dependencies/oneapi-tbb-2022.2.0' }
$commit='f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf'
if ((& git -C $OpenVdbSource rev-parse HEAD).Trim() -ne $commit) { throw 'Incorrect OpenVDB revision.' }
if (-not $SkipBuild) {
    & "$referenceRoot/Build-CodecReference.ps1" -OpenVdbSource $OpenVdbSource -CMake $CMake -Generator $Generator -DotNet $DotNet -TbbRoot $TbbRoot
    & $CMake -S $referenceRoot -B $build
    if ($LASTEXITCODE -ne 0) { throw 'File lifecycle oracle configuration failed.' }
    & $CMake --build $build --config Release --target vdb-file-lifecycle-reference --parallel 2
    if ($LASTEXITCODE -ne 0) { throw 'File lifecycle oracle build failed.' }
}
$previousPath=$env:PATH
$previousOutput=$env:TEDD_OPENVDB_FILE_LIFECYCLE_OUTPUT_DIR
try {
    $env:PATH="$TbbRoot/redist/intel64/vc14;$previousPath"
    $oracle=Join-Path $build 'Release/vdb-file-lifecycle-reference.exe'
    $generated=Join-Path $build 'generated-file-lifecycle'
    New-Item -ItemType Directory -Force -Path $generated | Out-Null
    & $oracle generate $generated
    if ($LASTEXITCODE -ne 0) { throw 'Native File lifecycle generation failed.' }
    $observed=Get-Content -LiteralPath (Join-Path $generated 'native-file-lifecycle.json') -Raw | ConvertFrom-Json
    if ($observed.commit -ne $commit) { throw 'Unexpected native lifecycle provenance.' }
    $fixtures=Join-Path $root 'tests/Tedd.OpenVDB.Tests/Fixtures/file-lifecycle'
    New-Item -ItemType Directory -Force -Path $fixtures | Out-Null
    foreach($name in @('native-file-lifecycle.json','native-file-lifecycle.vdb')) {
        $source=Join-Path $generated $name; $target=Join-Path $fixtures $name
        Copy-Item -LiteralPath $source -Destination ($target+'.tmp') -Force
        Move-Item -LiteralPath ($target+'.tmp') -Destination $target -Force
    }
    $env:TEDD_OPENVDB_FILE_LIFECYCLE_OUTPUT_DIR=Join-Path $build 'managed-file-lifecycle'
    Push-Location $root
    try {
        & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter 'FullyQualifiedName~VdbFileLifecycleTests' --artifacts-path artifacts/file-lifecycle-reference --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Managed File lifecycle verification failed.' }
    } finally { Pop-Location }
    & $oracle verify "$env:TEDD_OPENVDB_FILE_LIFECYCLE_OUTPUT_DIR/managed-first.vdb" "$env:TEDD_OPENVDB_FILE_LIFECYCLE_OUTPUT_DIR/managed-second.vdb" (Join-Path $build 'native-managed-reopen.vdb')
    if ($LASTEXITCODE -ne 0) { throw 'Native readback of managed UUID lifecycle failed.' }
    Write-Output 'Passed: native File lifecycle observations and two managed archive UUIDs verified bidirectionally.'
} finally { $env:PATH=$previousPath; $env:TEDD_OPENVDB_FILE_LIFECYCLE_OUTPUT_DIR=$previousOutput }
