[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OpenVdbSource,
    [string]$CMake = 'cmake',
    [string]$Generator = 'Visual Studio 18 2026',
    [string]$DotNet = 'dotnet',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$referenceRoot = $PSScriptRoot
if (-not $SkipBuild) {
    & (Join-Path $referenceRoot 'Build-Reference.ps1') -OpenVdbSource $OpenVdbSource -CMake $CMake -Generator $Generator
}
$expectedCommit = 'f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf'
if ((& git -C $OpenVdbSource rev-parse HEAD).Trim() -ne $expectedCommit) { throw 'Incorrect upstream revision' }
$oracle = Join-Path $referenceRoot 'build-oracle\Release\vdb-io-reference.exe'
$tbbRoot = Join-Path $referenceRoot 'dependencies\oneapi-tbb-2022.2.0'
$nativeOutput = Join-Path $referenceRoot 'build-oracle\native-io'
$managedOutput = Join-Path $referenceRoot 'build-oracle\managed-io'
$fixtureRoot = Join-Path $referenceRoot '..\tests\Tedd.OpenVDB.Tests\Fixtures'
$previousPath = $env:PATH
$previousExport = $env:TEDD_OPENVDB_NATIVE_OUTPUT_DIR
try {
    $env:PATH = "$tbbRoot\redist\intel64\vc14;$previousPath"
    & $oracle generate $nativeOutput
    if ($LASTEXITCODE -ne 0) { throw 'Native fixture generation failed' }
    foreach ($nativeFile in Get-ChildItem -LiteralPath $nativeOutput -Filter '*.vdb') {
        $destination = Join-Path $fixtureRoot $nativeFile.Name
        Copy-Item -LiteralPath $nativeFile.FullName -Destination "$destination.tmp" -Force
        Move-Item -LiteralPath "$destination.tmp" -Destination $destination -Force
    }
    $env:TEDD_OPENVDB_NATIVE_OUTPUT_DIR = $managedOutput
    Push-Location (Join-Path $referenceRoot '..')
    try {
        & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter 'FullyQualifiedName~VdbFileTests|FullyQualifiedName~VdbFileReviewTests' --logger 'console;verbosity=minimal'
        if ($LASTEXITCODE -ne 0) { throw 'Managed native-fixture/read-write tests failed' }
    } finally { Pop-Location }
    foreach ($scalar in @('int32','float','double')) {
        foreach ($scenario in @('empty','mixed')) {
            & $oracle verify (Join-Path $managedOutput "managed-$scalar-$scenario.vdb") $scalar $scenario
            if ($LASTEXITCODE -ne 0) { throw "Native verification failed: $scalar $scenario" }
        }
    }
    & $oracle verify (Join-Path $managedOutput 'managed-int32-mixed-stream.vdb') int32 mixed
    if ($LASTEXITCODE -ne 0) { throw 'Native verification failed: streaming grid' }
    Write-Output 'Passed: 7 native-written fixtures read by managed tests, and 7 managed-written files independently verified by native OpenVDB.'
} finally {
    $env:PATH = $previousPath
    $env:TEDD_OPENVDB_NATIVE_OUTPUT_DIR = $previousExport
}
