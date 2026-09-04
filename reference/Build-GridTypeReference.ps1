param(
    [Parameter(Mandatory=$true)][string]$OpenVdbSource,
    [string]$CMake='cmake',
    [string]$Generator='Visual Studio 18 2026',
    [string]$DotNet='dotnet',
    [string]$TbbRoot='',
    [switch]$SkipBuild
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if ((& git -C $OpenVdbSource rev-parse HEAD).Trim() -ne 'f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf') { throw 'Incorrect upstream OpenVDB revision.' }
if (-not $SkipBuild) {
    & "$PSScriptRoot/Build-CompressionReference.ps1" -OpenVdbSource $OpenVdbSource -CMake $CMake -Generator $Generator -DotNet $DotNet -TbbRoot $TbbRoot
    & $CMake --build "$PSScriptRoot/build-oracle-zlib" --config Release --target vdb-gridtypes-reference
    if($LASTEXITCODE -ne 0){throw 'Grid type oracle build failed.'}
}
if (-not $TbbRoot) { $TbbRoot="$PSScriptRoot/dependencies/oneapi-tbb-2022.2.0" }
$oldPath=$env:PATH
$oldOutput=$env:TEDD_OPENVDB_GRIDTYPES_OUTPUT_DIR
try {
    $env:PATH="$TbbRoot/redist/intel64/vc14;$env:PATH"
    $oracle="$PSScriptRoot/build-oracle-zlib/Release/vdb-gridtypes-reference.exe"
    $generated="$PSScriptRoot/build-oracle-zlib/gridtypes"
    & $oracle generate $generated
    if($LASTEXITCODE -ne 0){throw 'Native grid type fixture generation failed.'}
    $fixtures="$root/tests/Tedd.OpenVDB.Tests/Fixtures/gridtypes"
    New-Item -ItemType Directory -Force -Path $fixtures | Out-Null
    Get-ChildItem -LiteralPath $generated -Filter '*.vdb' | ForEach-Object {
        $target=Join-Path $fixtures $_.Name
        Copy-Item -LiteralPath $_.FullName -Destination ($target+'.tmp')
        Move-Item -LiteralPath ($target+'.tmp') -Destination $target -Force
    }
    & $oracle mask-inheritance "$fixtures/mask-inheritance.json.tmp"
    if($LASTEXITCODE -ne 0){throw 'Native mask inheritance fixture generation failed.'}
    Move-Item -LiteralPath "$fixtures/mask-inheritance.json.tmp" -Destination "$fixtures/mask-inheritance.json" -Force
    $env:TEDD_OPENVDB_GRIDTYPES_OUTPUT_DIR="$PSScriptRoot/build-oracle-zlib/managed-gridtypes"
    Push-Location $root
    try { & $DotNet test tests/Tedd.OpenVDB.Tests/Tedd.OpenVDB.Tests.csproj -c Release --filter 'FullyQualifiedName~VdbGridTypeTests|FullyQualifiedName~MaskGridInheritanceTests' --nologo }
    finally { Pop-Location }
    if($LASTEXITCODE -ne 0){throw 'Managed grid type tests failed.'}
    foreach($type in @('int64','bool','mask','vec3i','vec3s','vec3d')) { foreach($precision in @('full','half')) {
        if($precision -eq 'half' -and $type -notin @('vec3s','vec3d')) { continue }
        foreach($flags in 0..3) {
            $name="$type-$flags-$precision.vdb"
            & $oracle verify "$env:TEDD_OPENVDB_GRIDTYPES_OUTPUT_DIR/managed-$name" "$fixtures/native-$name" $type
            if($LASTEXITCODE -ne 0){throw "Native grid type verification failed: $name"}
        }
    } }
    Write-Output 'Passed: 32 managed extended grid type files independently verified by native OpenVDB.'} finally {
    $env:PATH=$oldPath
    $env:TEDD_OPENVDB_GRIDTYPES_OUTPUT_DIR=$oldOutput
}
