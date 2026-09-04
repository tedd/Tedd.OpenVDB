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
$build=Join-Path $PSScriptRoot 'build-background-review'
& $CMake -S (Join-Path $PSScriptRoot 'background-review') -B $build -G $Generator -A x64 "-DOPENVDB_SOURCE_DIR=$OpenVdbSource" "-DOPENVDB_BUILD_DIR=$OpenVdbBuild" "-DTBB_ROOT=$TbbRoot"
if ($LASTEXITCODE -ne 0) { throw 'Background review oracle configuration failed' }
& $CMake --build $build --config Release --parallel 2
if ($LASTEXITCODE -ne 0) { throw 'Background review oracle build failed' }
$fixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-background-review.json'))
$temporaryPath="$fixturePath.tmp"
$previousPath=$env:PATH
try {
    $env:PATH=(Join-Path $TbbRoot 'redist/intel64/vc14')+';'+$previousPath
    & (Join-Path $build 'Release/background-review-reference.exe') | Set-Content -LiteralPath $temporaryPath
    if ($LASTEXITCODE -ne 0) { throw 'Background review oracle execution failed' }
} finally { $env:PATH=$previousPath }
$fixture=Get-Content -LiteralPath $temporaryPath -Raw | ConvertFrom-Json
if ($fixture.upstreamCommit -ne $expectedCommit) { throw 'Unexpected fixture provenance' }
Move-Item -LiteralPath $temporaryPath -Destination $fixturePath -Force
[pscustomobject]@{Cases=$fixture.cases.Count;Sha256=(Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash}
$halfFixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-half-filter-review.json'))
$halfTemporaryPath="$halfFixturePath.tmp"
$previousPath=$env:PATH
try {
    $env:PATH=(Join-Path $TbbRoot 'redist/intel64/vc14')+';'+$previousPath
    & (Join-Path $build 'Release/background-half-filter-reference.exe') | Set-Content -LiteralPath $halfTemporaryPath
    if ($LASTEXITCODE -ne 0) { throw 'Half filter review oracle execution failed' }
} finally { $env:PATH=$previousPath }
$halfFixture=Get-Content -LiteralPath $halfTemporaryPath -Raw | ConvertFrom-Json
if ($halfFixture.upstreamCommit -ne $expectedCommit) { throw 'Unexpected Half filter fixture provenance' }
Move-Item -LiteralPath $halfTemporaryPath -Destination $halfFixturePath -Force
[pscustomobject]@{Cases=$halfFixture.cases.Count;Sha256=(Get-FileHash -LiteralPath $halfFixturePath -Algorithm SHA256).Hash}
