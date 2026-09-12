param([Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package = Join-Path $root 'dist\BetterGrouping'
$version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $GameDir 'Game2.Client.dll')).Version.ToString()
if ($version -ne '2.4.0.95') { throw "Unsupported game version: $version" }
& dotnet build (Join-Path $root 'src\BetterGrouping\BetterGrouping.csproj') -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Better Grouping build failed.' }
& dotnet run --project (Join-Path $root 'tests\BetterGrouping.Tests') -c Release "-p:GameDir=$GameDir" -- $GameDir
if ($LASTEXITCODE -ne 0) { throw 'Better Grouping tests failed.' }
New-Item -ItemType Directory -Path $package -Force | Out-Null
$bin = Join-Path $root 'src\BetterGrouping\bin\Release\net9.0-windows'
foreach ($name in @('BetterGrouping.dll','BetterGrouping.Core.dll','BetterGrouping.deps.json','0Harmony.dll')) {
    Copy-Item -LiteralPath (Join-Path $bin $name) -Destination $package -Force
}
foreach ($name in @('plugin.json','LICENSE','THIRD-PARTY-NOTICES.txt')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $package -Force
}
