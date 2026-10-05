param(
    [Parameter(Mandatory = $true)][string]$GameDir
)
$ErrorActionPreference = 'Stop'
$GameDir = (Resolve-Path -LiteralPath $GameDir).Path
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'plugin.json') -Raw | ConvertFrom-Json
if ($manifest.id -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,127}$') { throw 'Invalid plugin id.' }
if ($manifest.version -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]{0,63}$') { throw 'Use a filename-safe version.' }
if ($manifest.assembly -ne 'MyPlugin.dll' -or $manifest.entryPoint -ne 'MyPlugin.Plugin') {
    throw 'Keep the manifest assembly and entryPoint synchronized with this project.'
}
foreach ($file in @('VRage.Core.dll', 'Game2.Client.dll')) {
    $version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $GameDir $file)).Version.ToString()
    if ($version -ne '2.4.0.95' -or $manifest.gameVersion -ne $version) {
        throw "Unsupported game/manifest version: $file=$version; expected 2.4.0.95."
    }
}
$buildId = [Guid]::NewGuid().ToString('N')
$publish = Join-Path $PSScriptRoot "artifacts\$buildId"
& dotnet publish (Join-Path $PSScriptRoot 'MyPlugin.csproj') -c Release "-p:GameDir=$GameDir" "-p:Version=$($manifest.version)" --self-contained false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
# References supplied by the game must never enter a plugin release.
foreach ($dll in Get-ChildItem -LiteralPath $publish -Filter '*.dll' -Recurse) {
    if (Test-Path -LiteralPath (Join-Path $GameDir $dll.Name)) {
        throw "Game-supplied assembly in output: $($dll.Name). Set Private=false on that reference."
    }
}
$release = Join-Path $PSScriptRoot "dist\$($manifest.id)-$($manifest.version)-$buildId"
$plugin = Join-Path $release "SE2PL\Plugins\$($manifest.id)"
New-Item -ItemType Directory -Path $plugin -Force | Out-Null
Get-ChildItem -LiteralPath $publish | Where-Object { $_.Extension -ne '.pdb' } |
    Copy-Item -Destination $plugin -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'plugin.json') -Destination $plugin
foreach ($notice in @('LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
    $path = Join-Path $PSScriptRoot $notice
    if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $plugin }
}
Compress-Archive -LiteralPath (Join-Path $release 'SE2PL') -DestinationPath "$release.zip"
Write-Host "Plugin ZIP: $release.zip"

