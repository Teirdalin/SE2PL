param(
    [Parameter(Mandatory=$true)][string]$GameDir
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $projectRoot 'artifacts'
$package = Join-Path $projectRoot 'dist\SE2PL'
$support = Join-Path $package 'SE2PL'
& (Join-Path $projectRoot 'Examples\BetterGrouping\tools\Build.ps1') -GameDir $GameDir
# Rebuild only the known generated package/source directories; never include stale user settings.
foreach ($generated in @($package)) {
    $resolved = [IO.Path]::GetFullPath($generated)
    $boundary = [IO.Path]::GetFullPath((Join-Path $projectRoot 'dist')).TrimEnd('\')+'\'
    if (-not $resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)) { throw 'Generated directory outside dist.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $artifacts,$package,$support | Out-Null
$version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $GameDir 'Game2.Client.dll')).Version.ToString()
if ($version -ne '2.4.0.95') { throw "Unvalidated game version: $version" }
foreach ($name in @('SE2PluginLoader')) {
    & dotnet build (Join-Path $projectRoot "src\$name\$name.csproj") -c Release "-p:GameDir=$GameDir" 2>&1 | Tee-Object -FilePath (Join-Path $artifacts "$name-build.txt")
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
}
$publish = Join-Path $artifacts 'launcher-publish'
& dotnet publish (Join-Path $projectRoot 'src\SE2PluginLoader.Launcher') -c Release -o $publish 2>&1 | Tee-Object -FilePath (Join-Path $artifacts 'SE2PL-publish.txt')
if ($LASTEXITCODE -ne 0) { throw 'Single-file launcher publish failed.' }
$fixture = Join-Path $projectRoot 'tests\FixturePlugin\bin\Release\net9.0-windows\FixturePlugin.dll'
& dotnet run --project (Join-Path $projectRoot 'tests\SE2PluginLoader.Tests') -c Release "-p:GameDir=$GameDir" -- $GameDir $fixture 2>&1 | Tee-Object -FilePath (Join-Path $artifacts 'tests.txt')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
$bin = Join-Path $projectRoot 'src\SE2PluginLoader\bin\Release\net9.0-windows'
foreach ($file in @('SE2PluginLoader.dll','SE2PluginLoader.Core.dll','SE2PluginLoader.deps.json','0Harmony.dll')) {
    Copy-Item -LiteralPath (Join-Path $bin $file) -Destination $support -Force
}
Copy-Item -LiteralPath (Join-Path $publish 'SE2PL.exe') -Destination $package -Force
foreach ($file in @('README.md','LICENSE','THIRD-PARTY-NOTICES.txt')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $support -Force
}
# Plugin author reference material lives on GitHub, not in the installed game folder.
New-Item -ItemType Directory -Force -Path (Join-Path $support 'Plugins') | Out-Null
& {
    $grouping = Join-Path $projectRoot 'Examples\BetterGrouping\dist\BetterGrouping'
    if (-not (Test-Path -LiteralPath (Join-Path $grouping 'BetterGrouping.dll'))) { throw 'Build the sibling Better Grouping release first.' }
    $destination = Join-Path $support 'Plugins\BetterGrouping'
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    foreach ($file in @('BetterGrouping.dll','BetterGrouping.Core.dll','BetterGrouping.deps.json','0Harmony.dll','plugin.json')) {
        Copy-Item -LiteralPath (Join-Path $grouping $file) -Destination $destination -Force
    }
    foreach ($file in @('LICENSE','THIRD-PARTY-NOTICES.txt')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot ('Examples\BetterGrouping\'+$file)) -Destination $destination -Force
    }
    $manifestFile = Join-Path $destination 'plugin.json'
    $manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
    $manifest | Add-Member -NotePropertyName description -NotePropertyValue 'Assign selected Control Panel blocks to existing groups using a Group dropdown.' -Force
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestFile -Encoding utf8
}
$evidence = [ordered]@{ checkedAtUtc=[DateTime]::UtcNow.ToString('o'); gameVersion=$version; gameDirectory=$GameDir; assemblies=@() }
foreach ($file in @('SpaceEngineers2.dll','Game2.Client.dll','VRage.Core.dll','VRage.UI.dll','Avalonia.Controls.dll')) {
    $evidence.assemblies += [ordered]@{ name=$file; sha256=(Get-FileHash -LiteralPath (Join-Path $GameDir $file)).Hash }
}
$evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifacts 'environment.json') -Encoding utf8
# Investigation notes and validation evidence stay local, outside the player release.
$hashes = Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object Name -ne 'files.json' | ForEach-Object {
    [ordered]@{ path=$_.FullName.Substring($package.Length+1); sha256=(Get-FileHash -LiteralPath $_.FullName).Hash }
}
$hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'package-hashes.json') -Encoding utf8
& (Join-Path $projectRoot 'tests\Launcher.Tests.ps1') -PackageRoot $package -GameDir $GameDir | Tee-Object -FilePath (Join-Path $artifacts 'launcher-tests.txt')
$releaseVersion = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
Compress-Archive -Path (Join-Path $package '*') -DestinationPath (Join-Path $projectRoot "dist\SE2PL-$releaseVersion.zip") -Force
# Source is available in the repository; the player ZIP contains runtime files only.
Write-Output "Ready: $package"
