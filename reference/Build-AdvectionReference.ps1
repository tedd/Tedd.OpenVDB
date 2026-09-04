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
if($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit){throw "Expected OpenVDB revision $expectedCommit, found $actualCommit"}
$build=Join-Path $PSScriptRoot 'build-levelset-advection'
& $CMake -S (Join-Path $PSScriptRoot 'levelset-advection') -B $build -G $Generator -A x64 "-DOPENVDB_SOURCE_DIR=$OpenVdbSource" "-DOPENVDB_BUILD_DIR=$OpenVdbBuild" "-DTBB_ROOT=$TbbRoot"
if($LASTEXITCODE -ne 0){throw 'Advection oracle configuration failed'}
& $CMake --build $build --config Release --parallel 1
if($LASTEXITCODE -ne 0){throw 'Advection oracle build failed'}
$previousPath=$env:PATH
try {
    $env:PATH=(Join-Path $TbbRoot 'redist\intel64\vc14')+';'+$previousPath
    $output=& (Join-Path $build 'Release\levelset-advection-reference.exe')
    if($LASTEXITCODE -ne 0){throw 'Advection oracle execution failed'}
} finally { $env:PATH=$previousPath }
$json=$output -join [Environment]::NewLine
$fixture=$json | ConvertFrom-Json
if($fixture.upstreamCommit -ne $expectedCommit){throw 'Unexpected advection fixture provenance'}
$fixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tests\Tedd.OpenVDB.Tests\Fixtures\openvdb-13.0.1-levelset-advection.json'))
Set-Content -LiteralPath $fixturePath -Value $json
[pscustomobject]@{ Cases=$fixture.cases.Count; Sha256=(Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash }
