param([Parameter(Mandatory = $true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $root ('artifacts/sdk-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
& (Join-Path $root 'tools/Build-SDK.ps1') -OutputDirectory $testRoot
$archive = Join-Path $testRoot 'SE2PL-SDK-0.2.2.zip'
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $testRoot 'unpacked')
$sdk = Join-Path $testRoot 'unpacked/SE2PL-SDK-0.2.2'
$files = @(Get-ChildItem -LiteralPath $sdk -Recurse -File -Force)
if ($files.Count -ne 12) { throw "Unexpected SDK file count: $($files.Count)" }
if ($files | Where-Object { $_.Extension -in @('.dll', '.exe', '.pdb') }) { throw 'SDK contains binaries.' }
$hive = Join-Path $testRoot 'template-hive'
& dotnet new install (Join-Path $sdk 'templates') --debug:custom-hive $hive
if ($LASTEXITCODE -ne 0) { throw 'Template installation failed.' }
$project = Join-Path $testRoot 'SDKSmokePlugin'
& dotnet new se2pl-plugin -n SDKSmokePlugin -o $project --debug:custom-hive $hive
if ($LASTEXITCODE -ne 0) { throw 'Template generation failed.' }
& (Join-Path $project 'Build.ps1') -GameDir $GameDir
$releaseZip = @(Get-ChildItem -LiteralPath (Join-Path $project 'dist') -Filter '*.zip')
if ($releaseZip.Count -ne 1) { throw 'Expected one plugin release ZIP.' }
$release = Join-Path $testRoot 'release'
Expand-Archive -LiteralPath $releaseZip[0].FullName -DestinationPath $release
$pluginFiles = @(Get-ChildItem -LiteralPath $release -Recurse -File)
$expected = @('SDKSmokePlugin.dll', 'SDKSmokePlugin.deps.json', 'plugin.json')
if ($pluginFiles.Count -ne $expected.Count -or ($pluginFiles | Where-Object Name -NotIn $expected)) {
    throw 'Unexpected plugin ZIP contents.'
}
& dotnet run --project (Join-Path $PSScriptRoot 'SDK.Tests') -c Release -- $GameDir (Join-Path $release 'SE2PL/Plugins')
if ($LASTEXITCODE -ne 0) { throw 'SDK native contract checks failed.' }
$failedAsExpected = $false
try { & (Join-Path $project 'Build.ps1') -GameDir $testRoot }
catch { $failedAsExpected = $true }
if (-not $failedAsExpected) { throw 'Invalid GameDir unexpectedly accepted.' }
Write-Host 'PASS SDK archive, isolated template install/rename, release ZIP contents and invalid GameDir rejection.'
