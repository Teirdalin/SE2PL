param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'dist' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$archive = Join-Path $OutputDirectory 'SE2PL-SDK-0.2.2.zip'
if (Test-Path -LiteralPath $archive) { throw "Package already exists: $archive. Choose another OutputDirectory." }
$staging = Join-Path $root ('artifacts\sdk-package-' + [Guid]::NewGuid().ToString('N'))
$package = Join-Path $staging 'SE2PL-SDK-0.2.2'
New-Item -ItemType Directory -Path $package -Force | Out-Null
$files = @(
    'README.md', 'SDK-PERMISSION.txt',
    'templates/MyPlugin/.template.config/template.json',
    'templates/MyPlugin/.gitignore',
    'templates/MyPlugin/Directory.Build.props',
    'templates/MyPlugin/MyPlugin.csproj',
    'templates/MyPlugin/Plugin.cs',
    'templates/MyPlugin/plugin.json',
    'templates/MyPlugin/Build.ps1'
)
foreach ($file in $files) {
    $destination = Join-Path $package $file
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path (Join-Path $root 'SDK') $file) -Destination $destination
}
foreach ($file in @('LICENSE', 'PLUGIN-AUTHORS.md', 'plugin.schema.json')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $package
}
$authors = Join-Path $package 'PLUGIN-AUTHORS.md'
(Get-Content -LiteralPath $authors -Raw).Replace('(SDK/README.md)', '(README.md)') |
    Set-Content -LiteralPath $authors -Encoding UTF8
# ZipFile preserves dotfiles required by dotnet new.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($staging, $archive)
Write-Host "SDK ZIP: $archive"
