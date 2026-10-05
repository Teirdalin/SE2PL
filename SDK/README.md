# SE2PL SDK 0.2.2

Build plugins for SE2PL 0.2.2 and Space Engineers 2 **2.4.0.95**.
Requires Windows x64, the .NET 9 SDK (or a newer SDK with .NET 9 targeting support),
and a local game installation. Download SE2PL separately from
https://github.com/Teirdalin/SE2PL.

## Quick start

Copy `templates/MyPlugin` to your development folder and edit `plugin.json`.
Build it from PowerShell:

```powershell
.\Build.ps1 -GameDir 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers2\Game2'
```

The script prints the release ZIP path. Extract that ZIP into **Game2** with the
game closed, launch **SE2PL.exe**, enable the plugin in **Mods**, and save/restart.
Builds do not install anything or change loader settings.

For a named project, install the included template directory:

```powershell
dotnet new install .\templates
dotnet new se2pl-plugin -n MyFirstPlugin -o .\MyFirstPlugin
.\MyFirstPlugin\Build.ps1 -GameDir 'C:\Program Files (x86)\Steam\steamapps\common\SpaceEngineers2\Game2'
```

Use a C# identifier for the project name. Set a unique, stable manifest `id`
before release. Template installation is optional; copying the folder works too.

## API and lifecycle

The game owns `Keen.VRage.Core.Plugins.IPlugin` in `VRage.Core.dll`.
Implement it directly on your public, concrete entry point. SE2PL discovers
plugins by reading assembly metadata. No SE2PL runtime DLL reference is needed.

The native host prefers a public constructor taking `PluginHost`, with a
parameterless fallback. The template subscribes to
`OnBeforeEngineInstantiated` and `OnBeforeProjectsLoaded`, and unsubscribes
in `IDisposable.Dispose`. These are startup events, not update or world-ready
callbacks. The constructor runs before a world or UI is necessarily available.
Use verified game lifecycle hooks for simulation/UI work and respect thread ownership.

The example writes lifecycle messages to
`%APPDATA%/SpaceEngineers2/Plugins/MyPlugin/plugin.log`.
Loader errors appear in
`%APPDATA%/SpaceEngineers2/PluginLoader/loader.log`.
Use a unique Harmony ID if you add patches, and undo only your own patches at shutdown.

## References and packaging

`GameDir` must point to **Game2**. The build checks the installed core/client
assembly versions. Add references to installed assemblies with `Private="false"`;
do not ship game or Avalonia DLLs. Add third-party package references only as
needed and include their required notices. The template has no third-party packages.

`Build.ps1` publishes to a fresh directory under `artifacts` and creates
`dist/<id>-<version>-<build>/SE2PL/Plugins/<id>` plus a ZIP. Own managed
dependencies from project/package references are included. Review additional
native assets and third-party licenses before release.

Manifest `dependencies` are other plugin IDs, not DLL names or NuGet packages.
See `plugin.schema.json` and `PLUGIN-AUTHORS.md` for the loader manifest contract.
SE2PL requires restart after enable/disable; it does not provide hot reload.
Recheck internal game APIs after updates. A successful SDK build is not an in-game test.

## Permissions

See `LICENSE` for SE2PL terms and `SDK-PERMISSION.txt` for permission to
adapt and distribute the starter code and build script in your own plugins.
Game assemblies and loader binaries are not included.

