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
$build=Join-Path $PSScriptRoot 'build-math'
& $CMake -S (Join-Path $PSScriptRoot 'math') -B $build -G $Generator -A x64 "-DOPENVDB_SOURCE_DIR=$OpenVdbSource" "-DOPENVDB_BUILD_DIR=$OpenVdbBuild" "-DTBB_ROOT=$TbbRoot"
if($LASTEXITCODE -ne 0){throw 'Math oracle configuration failed'}
& $CMake --build $build --config Release --parallel 2
if($LASTEXITCODE -ne 0){throw 'Math oracle build failed'}
$fixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tests\Tedd.OpenVDB.Tests\Fixtures\openvdb-13.0.1-math.json'))
$previousPath=$env:PATH
try {
    $env:PATH=(Join-Path $TbbRoot 'redist\intel64\vc14')+';'+$previousPath
    $output=& (Join-Path $build 'Release\math-reference.exe')
    if($LASTEXITCODE -ne 0){throw 'Math oracle execution failed'}
} finally { $env:PATH=$previousPath }
$json=$output -join [Environment]::NewLine
$fixture=$json | ConvertFrom-Json
if($fixture.upstreamCommit -ne $expectedCommit){throw 'Unexpected math fixture provenance'}
Set-Content -LiteralPath $fixturePath -Value $json
[pscustomobject]@{ Cases=$fixture.cases.Count; Sha256=(Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash }


$finiteFixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\tests\Tedd.OpenVDB.Tests\Fixtures\openvdb-13.0.1-finite-difference.json'))
$previousPath=$env:PATH
try {
    $env:PATH=(Join-Path $TbbRoot 'redist\intel64\vc14')+';'+$previousPath
    $output=& (Join-Path $build 'Release\finite-difference-reference.exe')
    if($LASTEXITCODE -ne 0){throw 'Finite-difference oracle execution failed'}
} finally { $env:PATH=$previousPath }
$json=$output -join [Environment]::NewLine
$fixture=$json | ConvertFrom-Json
if($fixture.upstreamCommit -ne $expectedCommit){throw 'Unexpected finite-difference fixture provenance'}
Set-Content -LiteralPath $finiteFixturePath -Value $json
[pscustomobject]@{ Cases=$fixture.cases.Count; Sha256=(Get-FileHash -LiteralPath $finiteFixturePath -Algorithm SHA256).Hash }

foreach($operatorName in @('operators','grid-operators')) {
    $operatorFixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot ("..\tests\Tedd.OpenVDB.Tests\Fixtures\openvdb-13.0.1-"+$operatorName+".json")))
    $operatorPreviousPath=$env:PATH
    try {
        $env:PATH=(Join-Path $TbbRoot 'redist\intel64\vc14')+';'+$operatorPreviousPath
        $operatorOutput=& (Join-Path $build ("Release\"+$operatorName+"-reference.exe"))
        if($LASTEXITCODE -ne 0){throw "$operatorName oracle execution failed"}
    } finally { $env:PATH=$operatorPreviousPath }
    $operatorJson=$operatorOutput -join [Environment]::NewLine
    $operatorFixture=$operatorJson | ConvertFrom-Json
    if($operatorFixture.upstreamCommit -ne $expectedCommit){throw "Unexpected $operatorName fixture provenance"}
    Set-Content -LiteralPath $operatorFixturePath -Value $operatorJson
    [pscustomobject]@{ Oracle=$operatorName; Cases=$operatorFixture.cases.Count; Sha256=(Get-FileHash -LiteralPath $operatorFixturePath -Algorithm SHA256).Hash }
}
