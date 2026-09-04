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
$build=Join-Path $PSScriptRoot 'build-volume-tools'
& $CMake -S (Join-Path $PSScriptRoot 'volume-tools') -B $build -G $Generator -A x64 "-DOPENVDB_SOURCE_DIR=$OpenVdbSource" "-DOPENVDB_BUILD_DIR=$OpenVdbBuild" "-DTBB_ROOT=$TbbRoot"
if ($LASTEXITCODE -ne 0) { throw 'Volume tools oracle configuration failed' }
& $CMake --build $build --config Release --parallel 2
if ($LASTEXITCODE -ne 0) { throw 'Volume tools oracle build failed' }
$fixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tests\Tedd.OpenVDB.Tests\Fixtures\openvdb-13.0.1-volume-tools.json'))
$temporaryPath="$fixturePath.tmp"
$previousPath=$env:PATH
try {
    $env:PATH=(Join-Path $TbbRoot 'redist\intel64\vc14')+';'+$previousPath
    & (Join-Path $build 'Release\volume-tools-reference.exe') | Set-Content -LiteralPath $temporaryPath
    if ($LASTEXITCODE -ne 0) { throw 'Volume tools oracle execution failed' }
} finally { $env:PATH=$previousPath }
$fixture=Get-Content -LiteralPath $temporaryPath -Raw | ConvertFrom-Json
if ($fixture.upstreamCommit -ne $expectedCommit) { throw 'Unexpected fixture provenance' }
Move-Item -LiteralPath $temporaryPath -Destination $fixturePath -Force
[pscustomobject]@{
    Cases=$fixture.cases.Count
    Samples=($fixture.cases.samples | Measure-Object).Count
    Sha256=(Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash
}
$sphereFixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tests\Tedd.OpenVDB.Tests\Fixtures\openvdb-13.0.1-levelset-sphere.json'))
$sphereTemporaryPath="$sphereFixturePath.tmp"
$previousPath=$env:PATH
try {
    $env:PATH=(Join-Path $TbbRoot 'redist\intel64\vc14')+';'+$previousPath
    & (Join-Path $build 'Release\levelset-sphere-reference.exe') | Set-Content -LiteralPath $sphereTemporaryPath
    if ($LASTEXITCODE -ne 0) { throw 'Sphere oracle execution failed' }
} finally { $env:PATH=$previousPath }
$sphereFixture=Get-Content -LiteralPath $sphereTemporaryPath -Raw | ConvertFrom-Json
if ($sphereFixture.upstreamCommit -ne $expectedCommit) { throw 'Unexpected sphere fixture provenance' }
Move-Item -LiteralPath $sphereTemporaryPath -Destination $sphereFixturePath -Force
[pscustomobject]@{
    Cases=$sphereFixture.cases.Count
    Samples=($sphereFixture.cases.samples | Measure-Object).Count
    Sha256=(Get-FileHash -LiteralPath $sphereFixturePath -Algorithm SHA256).Hash
}
$csgFixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tests\Tedd.OpenVDB.Tests\Fixtures\openvdb-13.0.1-csg.json'))
$csgTemporaryPath="$csgFixturePath.tmp"
$previousPath=$env:PATH
try {
    $env:PATH=(Join-Path $TbbRoot 'redist\intel64\vc14')+';'+$previousPath
    & (Join-Path $build 'Release\csg-reference.exe') | Set-Content -LiteralPath $csgTemporaryPath
    if ($LASTEXITCODE -ne 0) { throw 'CSG oracle execution failed' }
} finally { $env:PATH=$previousPath }
$csgFixture=Get-Content -LiteralPath $csgTemporaryPath -Raw | ConvertFrom-Json
if ($csgFixture.upstreamCommit -ne $expectedCommit) { throw 'Unexpected CSG fixture provenance' }
Move-Item -LiteralPath $csgTemporaryPath -Destination $csgFixturePath -Force
[pscustomobject]@{
    Cases=$csgFixture.cases.Count
    Samples=(($csgFixture.cases.resultA.samples | Measure-Object).Count + ($csgFixture.cases.resultB.samples | Measure-Object).Count)
    Sha256=(Get-FileHash -LiteralPath $csgFixturePath -Algorithm SHA256).Hash
}
$vectorFixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tests\Tedd.OpenVDB.Tests\Fixtures\openvdb-13.0.1-vector-composite.json'))
$vectorTemporaryPath="$vectorFixturePath.tmp"
$previousPath=$env:PATH
try {
    $env:PATH=(Join-Path $TbbRoot 'redist\intel64\vc14')+';'+$previousPath
    & (Join-Path $build 'Release\vector-composite-reference.exe') | Set-Content -LiteralPath $vectorTemporaryPath
    if ($LASTEXITCODE -ne 0) { throw 'Vector composite oracle execution failed' }
} finally { $env:PATH=$previousPath }
$vectorFixture=Get-Content -LiteralPath $vectorTemporaryPath -Raw | ConvertFrom-Json
if ($vectorFixture.upstreamCommit -ne $expectedCommit) { throw 'Unexpected vector composite fixture provenance' }
Move-Item -LiteralPath $vectorTemporaryPath -Destination $vectorFixturePath -Force
[pscustomobject]@{
    Cases=$vectorFixture.cases.Count
    Sha256=(Get-FileHash -LiteralPath $vectorFixturePath -Algorithm SHA256).Hash
}
