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
    if ($LASTEXITCODE -ne 0) { throw 'String attribute oracle configuration failed.' }
    & $CMake --build $build --config Release --target vdb-string-attribute-reference --parallel 2
    if ($LASTEXITCODE -ne 0) { throw 'String attribute oracle build failed.' }
}
$previousPath=$env:PATH
$previousExport=$env:VDB_STRING_ATTRIBUTE_EXPORT
try {
    $env:PATH="$TbbRoot/redist/intel64/vc14;$previousPath"
    $oracle=Join-Path $build 'Release/vdb-string-attribute-reference.exe'
    $generated=Join-Path $build 'string-attributes.json'
    & $oracle generate $generated
    if ($LASTEXITCODE -ne 0) { throw 'Native string attribute generation failed.' }
    $document=Get-Content -LiteralPath $generated -Raw -Encoding utf8 | ConvertFrom-Json
    $streams=@($document.cases | Where-Object kind -eq 'stream')
    if ($document.commit -ne $commit -or $document.cases.Count -ne 25 -or $streams.Count -ne 16) {
        throw 'Unexpected string attribute fixture provenance or case count.'
    }
    for ($id=0; $id -lt 16; ++$id) {
        if (@($streams | Where-Object id -eq $id).Count -ne 1) { throw "Missing string stream profile $id." }
    }

    $fixture=Join-Path $root 'tests/Tedd.OpenVDB.Tests/Fixtures/openvdb-13.0.1-string-attributes.json.gz'
    $inputStream=[IO.File]::OpenRead($generated)
    $outputStream=[IO.File]::Create("$fixture.tmp")
    $gzip=[IO.Compression.GZipStream]::new($outputStream,[IO.Compression.CompressionLevel]::SmallestSize)
    try { $inputStream.CopyTo($gzip) }
    finally { $gzip.Dispose(); $outputStream.Dispose(); $inputStream.Dispose() }
    Move-Item -LiteralPath "$fixture.tmp" -Destination $fixture -Force

    $env:VDB_STRING_ATTRIBUTE_EXPORT=Join-Path $build 'managed-string-streams'
    Push-Location $root
    try {
        & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter 'FullyQualifiedName~StringAttribute' --artifacts-path artifacts/string-attribute-reference --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Managed string attribute verification failed.' }
    } finally { Pop-Location }

    & $oracle verify $env:VDB_STRING_ATTRIBUTE_EXPORT
    if ($LASTEXITCODE -ne 0) { throw 'Native reading of managed string streams failed.' }
    $hash=(Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output "Passed: 25 native cases, 32 managed raw/Blosc streams independently read by OpenVDB; fixture SHA-256 $hash."
} finally {
    $env:PATH=$previousPath
    $env:VDB_STRING_ATTRIBUTE_EXPORT=$previousExport
}
