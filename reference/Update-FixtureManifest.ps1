[CmdletBinding()]
param([switch]$Verify)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifestPath=Join-Path $PSScriptRoot 'FIXTURE_MANIFEST.json'
$fixtureRoot=Join-Path $repo 'tests/Tedd.OpenVDB.Tests/Fixtures'
$paths=@(& rg --files $fixtureRoot)
if($LASTEXITCODE -ne 0) {throw 'Fixture enumeration failed'}
$entries=@(
    foreach($path in ($paths | Sort-Object)) {
        if($path -notmatch '\.(json|vdb|json\.gz)$') {continue}
        $item=Get-Item -LiteralPath $path
        [pscustomobject][ordered]@{
            path=[IO.Path]::GetRelativePath($repo,$item.FullName).Replace('\','/')
            bytes=$item.Length
            sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
) | Sort-Object { $_.path }
$manifest=[ordered]@{upstreamCommit='f42248c35a9f7bb30641a8a74d9075ce7d1ddcaf';files=@($entries)}
$json=ConvertTo-Json -InputObject $manifest -Depth 5
if($Verify) {
    $existing=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if($existing.upstreamCommit -ne $manifest.upstreamCommit -or $existing.files.Count -ne $entries.Count) {throw 'Fixture manifest provenance or count differs'}
    for($i=0;$i -lt $entries.Count;$i++) {
        $expected=$existing.files[$i];$actual=$entries[$i]
        if($expected.path -ne $actual.path -or $expected.bytes -ne $actual.bytes -or $expected.sha256 -ne $actual.sha256) {throw "Fixture differs from manifest: $($actual.path)"}
    }
} else {
    [IO.File]::WriteAllText($manifestPath,$json+[Environment]::NewLine)
}
[pscustomobject]@{Files=$entries.Count;Bytes=($entries | Measure-Object -Property bytes -Sum).Sum;Verified=[bool]$Verify}
