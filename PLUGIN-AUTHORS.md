# Plugin manifest

Target .NET 9 for Windows and SE2 **2.4.0.95**. Implement `Keen.VRage.Core.Plugins.IPlugin` from the installed `VRage.Core.dll`. Use a parameterless constructor or one taking `PluginHost`; implement `IDisposable` for cleanup.

Place your DLL, dependencies, and `plugin.json` in `Game2/SE2PL/Plugins/<PluginName>`:

```json
{
  "id": "YourName.YourPlugin",
  "name": "Your Plugin",
  "version": "1.0.0",
  "description": "What the plugin does.",
  "assembly": "YourPlugin.dll",
  "entryPoint": "YourPlugin.Plugin",
  "gameVersion": "2.4.0.95",
  "dependencies": []
}
```

`id` must be unique and stable; comparisons are case-insensitive. `assembly` is relative to the plugin folder. `entryPoint` is the full IPlugin class name. `dependencies` lists plugin IDs that must be enabled and load first.

See [Better Grouping's manifest](Examples/BetterGrouping/plugin.json) or [the JSON schema](plugin.schema.json). Enable the plugin in Mods and restart to test it. Loader errors are written to `%APPDATA%/SpaceEngineers2/PluginLoader/loader.log`.
