param([string]$PackageRoot, [string]$GameDir = 'D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2')
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('SE2PL-LaunchTest-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    # Exercise the same copy-only installation as extracting the release ZIP.
    Get-ChildItem -LiteralPath $PackageRoot | Copy-Item -Destination $testRoot -Recurse
    Copy-Item -LiteralPath (Join-Path $GameDir 'Game2.Client.dll') -Destination $testRoot
    Set-Content -LiteralPath (Join-Path $testRoot 'SpaceEngineers2.exe') -Value 'Do not launch this test fixture'
    if (Test-Path -LiteralPath (Join-Path $testRoot 'SE2PL\files.json')) { throw 'Release still contains files.json.' }
    if (Get-ChildItem -LiteralPath $testRoot -Recurse -File | Where-Object Extension -eq '.ps1') { throw 'Release contains scripts.' }
    $launcher = Join-Path $testRoot 'SE2PL.exe'
    $result = Start-Process -FilePath $launcher -ArgumentList '--check' -WindowStyle Hidden -Wait -PassThru
    if ($result.ExitCode -ne 0) { throw 'Extracted launcher compatibility check failed.' }
    $check = Get-Content -LiteralPath (Join-Path $testRoot 'SE2PL\check-result.json') -Raw | ConvertFrom-Json
    if (-not $check.compatible -or $check.gameDirectory -ne $testRoot) { throw 'Launcher did not use adjacent game.' }
    Copy-Item -LiteralPath (Join-Path $testRoot 'SE2PL\SE2PluginLoader.Core.dll') -Destination (Join-Path $testRoot 'Game2.Client.dll') -Force
    $result = Start-Process -FilePath $launcher -ArgumentList '--check' -WindowStyle Hidden -Wait -PassThru
    if ($result.ExitCode -ne 1) { throw 'Unsupported game version was accepted.' }
    Write-Output 'PASS extracted launcher: no installer/manifest required, adjacent game resolved, unsupported version rejected. No game launched.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $boundary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
    if (-not $resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path $resolved -Leaf) -notlike 'SE2PL-LaunchTest-*') { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
