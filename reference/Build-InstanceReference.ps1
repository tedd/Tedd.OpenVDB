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
$root=Split-Path -Parent $PSScriptRoot
if ((& git -C $OpenVdbSource rev-parse HEAD).Trim() -ne 'f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf') { throw 'Incorrect upstream OpenVDB revision.' }
if (-not $SkipBuild) {
    & "$PSScriptRoot/Build-CompressionReference.ps1" -OpenVdbSource $OpenVdbSource -CMake $CMake -Generator $Generator -DotNet $DotNet -TbbRoot $TbbRoot
    & $CMake --build "$PSScriptRoot/build-oracle-zlib" --config Release --target vdb-instance-reference --parallel 2
    if($LASTEXITCODE -ne 0){throw 'Instance oracle build failed.'}
}
if (-not $TbbRoot) { $TbbRoot="$PSScriptRoot/dependencies/oneapi-tbb-2022.2.0" }
$oldPath=$env:PATH
$oldOutput=$env:TEDD_OPENVDB_INSTANCE_OUTPUT_DIR
try {
    $env:PATH="$TbbRoot/redist/intel64/vc14;$env:PATH"
    $oracle="$PSScriptRoot/build-oracle-zlib/Release/vdb-instance-reference.exe"
    $generated="$PSScriptRoot/build-oracle-zlib/instances"
    & $oracle generate $generated
    if($LASTEXITCODE -ne 0){throw 'Native instance fixture generation failed.'}
    $fixtures="$root/tests/Tedd.OpenVDB.Tests/Fixtures/instances"
    New-Item -ItemType Directory -Force -Path $fixtures | Out-Null
    Get-ChildItem -LiteralPath $generated -Filter '*.vdb' | ForEach-Object {
        $target=Join-Path $fixtures $_.Name
        Copy-Item -LiteralPath $_.FullName -Destination ($target+'.tmp') -Force
        Move-Item -LiteralPath ($target+'.tmp') -Destination $target -Force
    }
    $env:TEDD_OPENVDB_INSTANCE_OUTPUT_DIR="$PSScriptRoot/build-oracle-zlib/managed-instances"
    Push-Location $root
    try {
        & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter 'FullyQualifiedName~VdbInstanceTests' --nologo
        if($LASTEXITCODE -ne 0){throw 'Managed instance tests failed.'}
    } finally { Pop-Location }
    $verified=0
    foreach($expected in Get-ChildItem -LiteralPath $fixtures -Filter 'expected-*.vdb') {
        $key=$expected.Name.Substring('expected-'.Length)
        & $oracle verify "$env:TEDD_OPENVDB_INSTANCE_OUTPUT_DIR/managed-$key" $expected.FullName
        if($LASTEXITCODE -ne 0){throw "Native instance verification failed: $key"}
        ++$verified
    }
    Write-Output "Passed: $verified managed instance archives independently verified by native OpenVDB."
} finally {
    $env:PATH=$oldPath
    $env:TEDD_OPENVDB_INSTANCE_OUTPUT_DIR=$oldOutput
}
