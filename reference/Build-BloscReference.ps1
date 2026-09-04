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
$expectedOpenVdb = 'f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf'
$expectedBlosc = '616f4b7343a8479f7e71dd3d7025bd92c9a6bbd0'
if ((& git -C $OpenVdbSource rev-parse HEAD).Trim() -ne $expectedOpenVdb) { throw 'Incorrect upstream OpenVDB revision.' }
$dependencies = Join-Path $referenceRoot 'dependencies'
$bloscSource = Join-Path $dependencies 'c-blosc'
$bloscBuild = Join-Path $referenceRoot 'build-blosc'
$zlibSource = Join-Path $dependencies 'zlib-1.3.2'
$zlibLibrary = Join-Path $referenceRoot 'build-zlib/Release/zs.lib'
$upstreamBuild = Join-Path $referenceRoot 'build-upstream-blosc'
$oracleBuild = Join-Path $referenceRoot 'build-oracle-blosc'
if (-not $TbbRoot) { $TbbRoot = Join-Path $dependencies 'oneapi-tbb-2022.2.0' }
if (-not $SkipBuild) {
    & "$referenceRoot/Build-CompressionReference.ps1" -OpenVdbSource $OpenVdbSource -CMake $CMake -Generator $Generator -DotNet $DotNet -TbbRoot $TbbRoot
    if (-not (Test-Path -LiteralPath $bloscSource)) {
        & git clone --depth 1 --branch v1.21.6 https://github.com/Blosc/c-blosc.git $bloscSource
        if ($LASTEXITCODE -ne 0) { throw 'c-blosc checkout failed.' }
    }
    if ((& git -C $bloscSource rev-parse HEAD).Trim() -ne $expectedBlosc) { throw 'Incorrect c-blosc revision.' }
    & $CMake -S $bloscSource -B $bloscBuild -G $Generator -A x64 -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DBUILD_BENCHMARKS=OFF -DBUILD_FUZZERS=OFF -DBUILD_SHARED=OFF -DBUILD_STATIC=ON -DBUILD_TESTS=OFF -DPREFER_EXTERNAL_ZLIB=ON "-DZLIB_INCLUDE_DIR=$zlibSource" "-DZLIB_LIBRARY=$zlibLibrary"
    if ($LASTEXITCODE -ne 0) { throw 'c-blosc configuration failed.' }
    & $CMake --build $bloscBuild --config Release --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'c-blosc build failed.' }
    & $CMake -S $OpenVdbSource -B $upstreamBuild -G $Generator -A x64 "-DTBB_ROOT=$TbbRoot" "-DTBB_LIBRARYDIR=$TbbRoot/lib/intel64/vc14" "-DZLIB_INCLUDE_DIR=$zlibSource" "-DZLIB_LIBRARY=$zlibLibrary" "-DBlosc_INCLUDE_DIR=$bloscSource/blosc" "-DBlosc_LIBRARY_RELEASE=$bloscBuild/blosc/Release/libblosc.lib" -DBlosc_USE_STATIC_LIBS=ON -DOPENVDB_BUILD_BINARIES=OFF -DOPENVDB_BUILD_UNITTESTS=OFF -DOPENVDB_CORE_SHARED=OFF -DOPENVDB_CORE_STATIC=ON -DOPENVDB_USE_DELAYED_LOADING=OFF -DUSE_BLOSC=ON -DUSE_ZLIB=ON -DUSE_IMATH_HALF=OFF -DUSE_EXPLICIT_INSTANTIATION=OFF -DUSE_CCACHE=OFF
    if ($LASTEXITCODE -ne 0) { throw 'OpenVDB Blosc configuration failed.' }
    & $CMake --build $upstreamBuild --config Release --target openvdb_static --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'OpenVDB Blosc build failed.' }
    & $CMake -S $referenceRoot -B $oracleBuild -G $Generator -A x64 "-DOPENVDB_SOURCE_DIR=$OpenVdbSource" "-DOPENVDB_BUILD_DIR=$upstreamBuild" "-DOPENVDB_BLOSC_BUILD_DIR=$upstreamBuild" "-DBLOSC_LIBRARY=$bloscBuild/blosc/Release/libblosc.lib" "-DBLOSC_INCLUDE_DIR=$bloscSource/blosc" "-DZLIB_LIBRARY=$zlibLibrary" "-DTBB_ROOT=$TbbRoot"
    if ($LASTEXITCODE -ne 0) { throw 'Blosc oracle configuration failed.' }
    & $CMake --build $oracleBuild --config Release --target vdb-blosc-reference --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Blosc oracle build failed.' }
}
if ((& git -C $bloscSource rev-parse HEAD).Trim() -ne $expectedBlosc) { throw 'Incorrect c-blosc revision.' }
$previousPath = $env:PATH
$previousExport = $env:TEDD_OPENVDB_BLOSC_OUTPUT_DIR
try {
    $env:PATH = "$TbbRoot/redist/intel64/vc14;$previousPath"
    $oracle = Join-Path $oracleBuild 'Release/vdb-blosc-reference.exe'
    $generated = Join-Path $oracleBuild 'generated'
    & $oracle generate $generated
    if ($LASTEXITCODE -ne 0) { throw 'Native Blosc fixture generation failed.' }
    $fixtures = Join-Path $root 'tests/Tedd.OpenVDB.Tests/Fixtures/blosc'
    New-Item -ItemType Directory -Force -Path $fixtures | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $generated -Filter '*.vdb') {
        $target = Join-Path $fixtures $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination ($target + '.tmp') -Force
        Move-Item -LiteralPath ($target + '.tmp') -Destination $target -Force
    }
    $env:TEDD_OPENVDB_BLOSC_OUTPUT_DIR = Join-Path $oracleBuild 'managed'
    Push-Location $root
    try {
        & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter 'FullyQualifiedName~VdbBloscTests' --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Managed Blosc fixture verification failed.' }
    } finally { Pop-Location }
    $verified = 0
    foreach ($type in @('int32','float','double','int64','bool','mask','vec3i','vec3s','vec3d')) {
        foreach ($precision in @('full','half')) {
            if ($precision -eq 'half' -and $type -notin @('float','double','vec3s','vec3d')) { continue }
            foreach ($flags in 4..7) {
                $name = "$type-$flags-$precision.vdb"
                & $oracle verify "$env:TEDD_OPENVDB_BLOSC_OUTPUT_DIR/managed-$name" "$fixtures/native-$name" $type
                if ($LASTEXITCODE -ne 0) { throw "Native Blosc verification failed: $name" }
                ++$verified
            }
        }
    }
    Write-Output "Passed: $verified managed Blosc files independently verified by native OpenVDB."
} finally {
    $env:PATH = $previousPath
    $env:TEDD_OPENVDB_BLOSC_OUTPUT_DIR = $previousExport
}
