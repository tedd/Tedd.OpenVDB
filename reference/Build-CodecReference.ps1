[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OpenVdbSource,
    [string]$CMake = 'cmake',
    [string]$Generator = 'Visual Studio 18 2026',
    [string]$DotNet = 'dotnet',
    [string]$TbbRoot = '',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$referenceRoot = $PSScriptRoot
$root = Split-Path -Parent $referenceRoot
$dependencies = Join-Path $referenceRoot 'dependencies'
$bloscSource = Join-Path $dependencies 'c-blosc'
$bloscBuild = Join-Path $referenceRoot 'build-blosc-all'
$snappySource = Join-Path $dependencies 'snappy'
$snappyBuild = Join-Path $referenceRoot 'build-snappy'
$snappyInstall = Join-Path $dependencies 'snappy-install'
$snappyLibrary = Join-Path $snappyInstall 'lib/snappy.lib'
$zlibSource = Join-Path $dependencies 'zlib-1.3.2'
$zlibLibrary = Join-Path $referenceRoot 'build-zlib/Release/zs.lib'
$upstreamBuild = Join-Path $referenceRoot 'build-upstream-blosc'
$oracleBuild = Join-Path $referenceRoot 'build-oracle-codecs'
if (-not $TbbRoot) { $TbbRoot = Join-Path $dependencies 'oneapi-tbb-2022.2.0' }
if ((& git -C $OpenVdbSource rev-parse HEAD).Trim() -ne 'f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf') { throw 'Incorrect upstream OpenVDB revision.' }
if (-not $SkipBuild) {
    if (-not (Test-Path -LiteralPath "$upstreamBuild/openvdb/openvdb/Release/libopenvdb.lib")) {
        & "$referenceRoot/Build-BloscReference.ps1" -OpenVdbSource $OpenVdbSource -CMake $CMake -Generator $Generator -DotNet $DotNet -TbbRoot $TbbRoot
    }
    if (-not (Test-Path -LiteralPath $snappySource)) {
        & git clone --depth 1 --branch 1.2.2 https://github.com/google/snappy.git $snappySource
        if ($LASTEXITCODE -ne 0) { throw 'Snappy checkout failed.' }
    }
    if ((& git -C $snappySource rev-parse HEAD).Trim() -ne '6af9287fbdb913f0794d0148c6aa43b58e63c8e3') { throw 'Incorrect Snappy revision.' }
    & $CMake -S $snappySource -B $snappyBuild -G $Generator -A x64 '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW' -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DSNAPPY_BUILD_TESTS=OFF -DSNAPPY_BUILD_BENCHMARKS=OFF -DBUILD_SHARED_LIBS=OFF "-DCMAKE_INSTALL_PREFIX=$snappyInstall"
    if ($LASTEXITCODE -ne 0) { throw 'Snappy configuration failed.' }
    & $CMake --build $snappyBuild --config Release --target install --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Snappy build failed.' }
    & $CMake -S $bloscSource -B $bloscBuild -G $Generator -A x64 '-DCMAKE_POLICY_VERSION_MINIMUM=3.5' '-DCMAKE_POLICY_DEFAULT_CMP0091=NEW' -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DBUILD_BENCHMARKS=OFF -DBUILD_FUZZERS=OFF -DBUILD_SHARED=OFF -DBUILD_STATIC=ON -DBUILD_TESTS=OFF -DDEACTIVATE_SNAPPY=OFF "-DSNAPPY_INCLUDE_DIR=$snappyInstall/include" "-DSNAPPY_LIBRARY=$snappyLibrary" -DPREFER_EXTERNAL_ZLIB=ON "-DZLIB_INCLUDE_DIR=$zlibSource" "-DZLIB_LIBRARY=$zlibLibrary"
    if ($LASTEXITCODE -ne 0) { throw 'All-codec Blosc configuration failed.' }
    & $CMake --build $bloscBuild --config Release --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'All-codec Blosc build failed.' }
    & $CMake -S $referenceRoot -B $oracleBuild -G $Generator -A x64 "-DOPENVDB_SOURCE_DIR=$OpenVdbSource" "-DOPENVDB_BUILD_DIR=$upstreamBuild" "-DOPENVDB_BLOSC_BUILD_DIR=$upstreamBuild" "-DBLOSC_LIBRARY=$bloscBuild/blosc/Release/libblosc.lib" "-DBLOSC_INCLUDE_DIR=$bloscSource/blosc" "-DSNAPPY_LIBRARY=$snappyLibrary" "-DSNAPPY_INCLUDE_DIR=$snappyInstall/include" "-DZLIB_LIBRARY=$zlibLibrary" "-DTBB_ROOT=$TbbRoot"
    if ($LASTEXITCODE -ne 0) { throw 'Codec oracle configuration failed.' }
    & $CMake --build $oracleBuild --config Release --target vdb-codec-reference vdb-blosc-reference --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Codec oracle build failed.' }
}
if ((& git -C $bloscSource rev-parse HEAD).Trim() -ne '616f4b7343a8479f7e71dd3d7025bd92c9a6bbd0') { throw 'Incorrect c-blosc revision.' }
if ((& git -C $snappySource rev-parse HEAD).Trim() -ne '6af9287fbdb913f0794d0148c6aa43b58e63c8e3') { throw 'Incorrect Snappy revision.' }
$previousPath = $env:PATH
$previousOutput = $env:TEDD_OPENVDB_CODEC_OUTPUT_DIR
try {
    $env:PATH = "$TbbRoot/redist/intel64/vc14;$previousPath"
    $oracle = Join-Path $oracleBuild 'Release/vdb-codec-reference.exe'
    $vdbOracle = Join-Path $oracleBuild 'Release/vdb-blosc-reference.exe'
    $generated = Join-Path $oracleBuild 'generated'
    New-Item -ItemType Directory -Force -Path $generated | Out-Null
    & $oracle "$generated/native-codecs.json" "$bloscSource/compat"
    if ($LASTEXITCODE -ne 0) { throw 'Native codec fixture verification failed.' }
    & $vdbOracle generate-codecs $generated
    if ($LASTEXITCODE -ne 0) { throw 'Native .vdb codec fixture verification failed.' }
    $fixtures = Join-Path $root 'tests/Tedd.OpenVDB.Tests/Fixtures/codecs'
    New-Item -ItemType Directory -Force -Path $fixtures | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $generated -File) {
        if ($file.Extension -notin @('.vdb', '.json')) { continue }
        $target = Join-Path $fixtures $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination ($target + '.tmp') -Force
        Move-Item -LiteralPath ($target + '.tmp') -Destination $target -Force
    }
    $env:TEDD_OPENVDB_CODEC_OUTPUT_DIR = Join-Path $oracleBuild 'managed'
    Push-Location $root
    try {
        & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter 'FullyQualifiedName~VdbCodecTests|FullyQualifiedName~VdbBloscTests' --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Managed codec fixture verification failed.' }
    } finally { Pop-Location }
    $verified = 0
    foreach ($codec in @('blosclz', 'snappy')) { foreach ($filter in 0..2) { foreach ($profile in 0..3) {
        $name = "$codec-$filter-$profile"
        & $vdbOracle verify "$env:TEDD_OPENVDB_CODEC_OUTPUT_DIR/managed-$name.vdb" "$fixtures/expected-frame-$name.vdb" float
        if ($LASTEXITCODE -ne 0) { throw "Native managed-file verification failed: $name" }
        ++$verified
    } } }
    Write-Output "Passed: $verified managed files independently verified by native OpenVDB, plus native raw/frame/filter fixtures."
} finally {
    $env:PATH = $previousPath
    $env:TEDD_OPENVDB_CODEC_OUTPUT_DIR = $previousOutput
}
