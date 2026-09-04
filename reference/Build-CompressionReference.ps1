[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$OpenVdbSource,
    [string]$CMake = 'cmake',
    [string]$Generator = 'Visual Studio 18 2026',
    [string]$DotNet = 'dotnet',
    [string]$TbbRoot,
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$referenceRoot = $PSScriptRoot
$expectedCommit = 'f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf'
if ((& git -C $OpenVdbSource rev-parse HEAD).Trim() -ne $expectedCommit) { throw 'Incorrect upstream OpenVDB revision' }
$dependencies = Join-Path $referenceRoot 'dependencies'
New-Item -ItemType Directory -Path $dependencies -Force | Out-Null
if (-not $TbbRoot) { $TbbRoot = Join-Path $dependencies 'oneapi-tbb-2022.2.0' }
if (-not (Test-Path -LiteralPath $TbbRoot)) {
    $archive = Join-Path $dependencies 'oneapi-tbb-2022.2.0-win.zip'
    Invoke-WebRequest -Uri 'https://github.com/uxlfoundation/oneTBB/releases/download/v2022.2.0/oneapi-tbb-2022.2.0-win.zip' -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '4243442411bae9e3b80bfe2e47b5b571e8ae541ac45cfb991036b3a16a0c6012') { throw 'oneTBB checksum mismatch' }
    Expand-Archive -LiteralPath $archive -DestinationPath $dependencies
}
$zlibArchive = Join-Path $dependencies 'zlib-1.3.2.tar.gz'
$zlibSource = Join-Path $dependencies 'zlib-1.3.2'
$zlibBuild = Join-Path $referenceRoot 'build-zlib'
$upstreamBuild = Join-Path $referenceRoot 'build-upstream-zlib'
$oracleBuild = Join-Path $referenceRoot 'build-oracle-zlib'
if (-not $SkipBuild) {
    if (-not (Test-Path -LiteralPath $zlibArchive)) {
        Invoke-WebRequest -Uri 'https://zlib.net/fossils/zlib-1.3.2.tar.gz' -OutFile $zlibArchive
    }
    if ((Get-FileHash -LiteralPath $zlibArchive -Algorithm SHA256).Hash -ne 'bb329a0a2cd0274d05519d61c667c062e06990d72e125ee2dfa8de64f0119d16') { throw 'zlib checksum mismatch' }
    if (-not (Test-Path -LiteralPath $zlibSource)) {
        & tar -xzf $zlibArchive -C $dependencies
        if ($LASTEXITCODE -ne 0) { throw 'zlib extraction failed' }
    }
    & $CMake -S $zlibSource -B $zlibBuild -G $Generator -A x64 -DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded -DZLIB_BUILD_SHARED=OFF -DZLIB_BUILD_STATIC=ON -DZLIB_BUILD_TESTING=OFF
    if ($LASTEXITCODE -ne 0) { throw 'zlib configuration failed' }
    & $CMake --build $zlibBuild --config Release --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'zlib build failed' }
    & $CMake -S $OpenVdbSource -B $upstreamBuild -G $Generator -A x64 "-DTBB_ROOT=$TbbRoot" "-DTBB_LIBRARYDIR=$TbbRoot/lib/intel64/vc14" "-DZLIB_INCLUDE_DIR=$zlibSource" "-DZLIB_LIBRARY=$zlibBuild/Release/zs.lib" -DOPENVDB_BUILD_BINARIES=OFF -DOPENVDB_BUILD_UNITTESTS=OFF -DOPENVDB_CORE_SHARED=OFF -DOPENVDB_CORE_STATIC=ON -DOPENVDB_USE_DELAYED_LOADING=OFF -DUSE_BLOSC=OFF -DUSE_ZLIB=ON -DUSE_IMATH_HALF=OFF -DUSE_EXPLICIT_INSTANTIATION=OFF -DUSE_CCACHE=OFF
    if ($LASTEXITCODE -ne 0) { throw 'OpenVDB configuration failed' }
    & $CMake --build $upstreamBuild --config Release --target openvdb_static --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'OpenVDB build failed' }
    & $CMake -S $referenceRoot -B $oracleBuild -G $Generator -A x64 "-DOPENVDB_SOURCE_DIR=$OpenVdbSource" "-DOPENVDB_BUILD_DIR=$upstreamBuild" "-DOPENVDB_ZLIB_BUILD_DIR=$upstreamBuild" "-DZLIB_LIBRARY=$zlibBuild/Release/zs.lib" "-DTBB_ROOT=$TbbRoot"
    if ($LASTEXITCODE -ne 0) { throw 'Compression oracle configuration failed' }
    & $CMake --build $oracleBuild --config Release --target vdb-compression-reference --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Compression oracle build failed' }
}
$oracle = Join-Path $oracleBuild 'Release\vdb-compression-reference.exe'
$nativeOutput = Join-Path $oracleBuild 'generated'
$managedOutput = Join-Path $oracleBuild 'managed'
$fixtureRoot = Join-Path $referenceRoot '..\tests\Tedd.OpenVDB.Tests\Fixtures\compression'
New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
$previousPath = $env:PATH
$previousExport = $env:TEDD_OPENVDB_COMPRESSION_OUTPUT_DIR
try {
    $env:PATH = "$TbbRoot\redist\intel64\vc14;$previousPath"
    & $oracle generate $nativeOutput
    if ($LASTEXITCODE -ne 0) { throw 'Native compression fixture generation failed' }
    foreach ($file in Get-ChildItem -LiteralPath $nativeOutput -Filter '*.vdb') {
        $destination = Join-Path $fixtureRoot $file.Name
        Copy-Item -LiteralPath $file.FullName -Destination "$destination.tmp" -Force
        Move-Item -LiteralPath "$destination.tmp" -Destination $destination -Force
    }
    $env:TEDD_OPENVDB_COMPRESSION_OUTPUT_DIR = $managedOutput
    Push-Location (Join-Path $referenceRoot '..')
    try {
        & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter FullyQualifiedName~VdbCompressionTests --logger 'console;verbosity=minimal'
        if ($LASTEXITCODE -ne 0) { throw 'Managed compression fixture verification failed' }
    } finally { Pop-Location }
    $verified = 0
    foreach ($scalar in @('int32','float','double')) {
        foreach ($half in @('full','half')) {
            if ($scalar -eq 'int32' -and $half -eq 'half') { continue }
            foreach ($flags in @(0,1,2,3)) {
                $key = "$scalar-$flags-$half"
                & $oracle verify (Join-Path $managedOutput "managed-$key.vdb") (Join-Path $fixtureRoot "native-$key.vdb") $scalar
                if ($LASTEXITCODE -ne 0) { throw "Independent native verification failed: $key" }
                ++$verified
            }
        }
    }
    Write-Output "Passed: $verified managed compressed/half files independently verified by native OpenVDB."
} finally {
    $env:PATH = $previousPath
    $env:TEDD_OPENVDB_COMPRESSION_OUTPUT_DIR = $previousExport
}
