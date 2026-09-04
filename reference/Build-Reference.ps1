[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OpenVdbSource,
    [string]$CMake = 'cmake',
    [string]$Generator = 'Visual Studio 18 2026'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$expectedCommit = 'f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf'
$actualCommit = (& git -C $OpenVdbSource rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $expectedCommit) {
    throw "OpenVDB checkout must be at $expectedCommit; found $actualCommit"
}
$referenceRoot = $PSScriptRoot
$tbbVersion = 'oneapi-tbb-2022.2.0'
$tbbRoot = Join-Path $referenceRoot "dependencies\$tbbVersion"
$archive = Join-Path $referenceRoot "$tbbVersion-win.zip"
$checksum = '4243442411bae9e3b80bfe2e47b5b571e8ae541ac45cfb991036b3a16a0c6012'
if (-not (Test-Path -LiteralPath $archive)) {
    Invoke-WebRequest -Uri "https://github.com/uxlfoundation/oneTBB/releases/download/v2022.2.0/$tbbVersion-win.zip" -OutFile $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $checksum) { throw 'oneTBB archive checksum mismatch' }
if (-not (Test-Path -LiteralPath $tbbRoot)) {
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $referenceRoot 'dependencies')
}
$upstreamBuild = Join-Path $referenceRoot 'build-upstream'
$oracleBuild = Join-Path $referenceRoot 'build-oracle'
& $CMake -S $OpenVdbSource -B $upstreamBuild -G $Generator -A x64 "-DTBB_ROOT=$tbbRoot" "-DTBB_LIBRARYDIR=$tbbRoot/lib/intel64/vc14" -DOPENVDB_BUILD_BINARIES=OFF -DOPENVDB_BUILD_UNITTESTS=OFF -DOPENVDB_CORE_SHARED=OFF -DOPENVDB_CORE_STATIC=ON -DOPENVDB_USE_DELAYED_LOADING=OFF -DUSE_BLOSC=OFF -DUSE_ZLIB=OFF -DUSE_IMATH_HALF=OFF -DUSE_EXPLICIT_INSTANTIATION=OFF -DUSE_CCACHE=OFF
if ($LASTEXITCODE -ne 0) { throw 'Upstream CMake configuration failed' }
& $CMake --build $upstreamBuild --config Release --target openvdb_static --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Upstream build failed' }
& $CMake -S $referenceRoot -B $oracleBuild -G $Generator -A x64 "-DOPENVDB_SOURCE_DIR=$OpenVdbSource" "-DOPENVDB_BUILD_DIR=$upstreamBuild" "-DTBB_ROOT=$tbbRoot"
if ($LASTEXITCODE -ne 0) { throw 'Oracle CMake configuration failed' }
& $CMake --build $oracleBuild --config Release --parallel 4
if ($LASTEXITCODE -ne 0) { throw 'Oracle build failed' }
$fixtureDirectory = [IO.Path]::GetFullPath((Join-Path $referenceRoot '..\tests\Tedd.OpenVDB.Tests\Fixtures'))
New-Item -ItemType Directory -Path $fixtureDirectory -Force | Out-Null
$fixturePath = Join-Path $fixtureDirectory 'openvdb-13.0.1-int32.json'
$temporaryPath = "$fixturePath.tmp"
$previousPath = $env:PATH
try {
    $env:PATH = "$tbbRoot\redist\intel64\vc14;$previousPath"
    & (Join-Path $oracleBuild 'Release\vdb-reference.exe') > $temporaryPath
    if ($LASTEXITCODE -ne 0) { throw 'Native oracle execution failed' }
} finally { $env:PATH = $previousPath }
# A reader must never see a partially emitted JSON document.
$fixture = Get-Content -LiteralPath $temporaryPath -Raw | ConvertFrom-Json
if ($fixture.upstreamCommit -ne $expectedCommit) { throw 'Unexpected fixture provenance' }
Move-Item -LiteralPath $temporaryPath -Destination $fixturePath -Force
[pscustomobject]@{
    Cases = $fixture.cases.Count
    Checkpoints = ($fixture.cases.steps | Measure-Object).Count
    Samples = ($fixture.cases.steps.samples | Measure-Object).Count
    Sha256 = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash
}
