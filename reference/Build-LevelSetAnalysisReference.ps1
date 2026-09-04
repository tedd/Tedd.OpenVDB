[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OpenVdbSource,
    [Parameter(Mandatory=$true)][string]$OpenVdbBuild,
    [Parameter(Mandatory=$true)][string]$TbbRoot,
    [string]$CMake='cmake',
    [string]$Generator='Visual Studio 18 2026'
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$expectedCommit='f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf'
$actualCommit=(& git -C $OpenVdbSource rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit) { throw "Expected OpenVDB revision $expectedCommit, found $actualCommit" }
$build=Join-Path $PSScriptRoot 'build-levelset-analysis'
& $CMake -S (Join-Path $PSScriptRoot 'levelset-analysis') -B $build -G $Generator -A x64 "-DOPENVDB_SOURCE_DIR=$OpenVdbSource" "-DOPENVDB_BUILD_DIR=$OpenVdbBuild" "-DTBB_ROOT=$TbbRoot"
if ($LASTEXITCODE -ne 0) { throw 'Level-set analysis oracle configuration failed' }
& $CMake --build $build --config Release --parallel 2
if ($LASTEXITCODE -ne 0) { throw 'Level-set analysis oracle build failed' }
$fixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-levelset-analysis.json'))
$temporaryPath="$fixturePath.tmp"
$previousPath=$env:PATH
try {
    $env:PATH=(Join-Path $TbbRoot 'redist/intel64/vc14')+';'+$previousPath
    & (Join-Path $build 'Release/levelset-analysis-reference.exe') | Set-Content -LiteralPath $temporaryPath
    if ($LASTEXITCODE -ne 0) { throw 'Level-set analysis oracle execution failed' }
} finally { $env:PATH=$previousPath }
$fixture=Get-Content -LiteralPath $temporaryPath -Raw | ConvertFrom-Json
if ($fixture.upstreamCommit -ne $expectedCommit) { throw 'Unexpected fixture provenance' }
Move-Item -LiteralPath $temporaryPath -Destination $fixturePath -Force
[pscustomobject]@{Cases=$fixture.cases.Count;Measurements=$fixture.measurements.Count;Sha256=(Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash}
