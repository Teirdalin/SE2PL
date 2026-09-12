# Making a plugin for SE2PL

Target the installed SE2 **2.4.0.95** assemblies and **.NET 9 for Windows**. Implement `Keen.VRage.Core.Plugins.IPlugin` from `VRage.Core.dll`. The loader supports a constructor taking `PluginHost` or a parameterless constructor, plus `IDisposable` for cleanup. SE1 plugin APIs are not compatible.

## Folder structure

```text
Game2/SE2PL/Plugins/YourPlugin/
  plugin.json
  YourPlugin.dll
  dependencies and assets
```

Copy `plugin.example.json` to your plugin folder as `plugin.json` and edit it:

```json
{
  "id": "YourName.YourPlugin",
  "name": "Your Plugin",
  "version": "1.0.0",
  "description": "What your plugin does.",
  "assembly": "YourPlugin.dll",
  "entryPoint": "YourPlugin.Plugin",
  "gameVersion": "2.4.0.95",
  "dependencies": []
}
```

- Keep `id` stable across updates. IDs are case-insensitive and must be unique.
- `assembly` is a DLL path relative to your plugin folder; `entryPoint` is the full name of its concrete IPlugin class.
- `gameVersion` declares the exact SE2 version you support.
- `dependencies` lists other plugin IDs. They must be enabled and load successfully first. Missing dependencies and cycles block loading.
- See `plugin.schema.json` for manifest validation. A manifest is recommended even though simple DLL-only discovery is supported.

## Test and distribute

Place your built folder in Plugins, launch SE2PL, enable it in Mods, and restart. Check the loader log if it fails to load. Loading successfully does not prove gameplay features work; test them in a disposable world.

Plugins run inside the game process. Release resources and remove hooks during disposal. Keep DLL dependencies beside your plugin; conflicting dependency versions can cause problems. Do not redistribute SE2 game assemblies or bundle the SE2PL launcher with your plugin. Point players to the official loader download.

The bundled `Plugins/BetterGrouping` folder is a working example of packaging and metadata. Its **Group** dropdown edits existing Control Panel groups. Follow its own LICENSE when using its files.

Your independently authored plugin can use your own license. You may adapt the supplied manifest/schema and examples for your plugin; see the exception in SE2PL's LICENSE.
