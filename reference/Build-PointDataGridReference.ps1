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
$build=Join-Path $PSScriptRoot 'build-point-data-grid'
& $CMake -S (Join-Path $PSScriptRoot 'point-data-grid') -B $build -G $Generator -A x64 "-DOPENVDB_SOURCE_DIR=$OpenVdbSource" "-DOPENVDB_BUILD_DIR=$OpenVdbBuild" "-DTBB_ROOT=$TbbRoot"
if ($LASTEXITCODE -ne 0) { throw 'PointDataGrid oracle configuration failed' }
& $CMake --build $build --config Release --parallel 2
if ($LASTEXITCODE -ne 0) { throw 'PointDataGrid oracle build failed' }
$fixturePath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-point-data-grid.json.gz'))
$temporaryPath="$fixturePath.json.tmp"
$previousPath=$env:PATH
try {
    $env:PATH=(Join-Path $TbbRoot 'redist/intel64/vc14')+';'+$previousPath
    & (Join-Path $build 'Release/point-data-grid-reference.exe') $temporaryPath
    if ($LASTEXITCODE -ne 0) { throw 'PointDataGrid oracle execution failed' }
} finally { $env:PATH=$previousPath }
$fixture=Get-Content -LiteralPath $temporaryPath -Raw | ConvertFrom-Json
if ($fixture.upstreamCommit -ne $expectedCommit) { throw 'Unexpected fixture provenance' }
if ($fixture.cases.Count -ne 12) { throw "Expected 12 cases, found $($fixture.cases.Count)" }
if (($fixture.cases.codec | Sort-Object -Unique) -join ',' -ne 'fxpt16,null') { throw 'Unexpected codec profiles' }
$compressedPath="$fixturePath.tmp"
$inputStream=[IO.File]::OpenRead($temporaryPath)
$outputStream=[IO.File]::Create($compressedPath)
$gzip=[IO.Compression.GZipStream]::new($outputStream,[IO.Compression.CompressionLevel]::SmallestSize)
try { $inputStream.CopyTo($gzip) } finally { $gzip.Dispose(); $outputStream.Dispose(); $inputStream.Dispose() }
Move-Item -LiteralPath $compressedPath -Destination $fixturePath -Force
Remove-Item -LiteralPath $temporaryPath
[pscustomobject]@{Cases=$fixture.cases.Count;Sha256=(Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash}
