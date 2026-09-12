# SE2PL

Adds a **Mods** menu to Space Engineers 2. Includes Better Grouping.

Requires SE2 **2.4.0.95**, Windows x64, Steam, and the **.NET 9 Desktop Runtime**.

1. Extract into the game's **Game2** folder, beside **SpaceEngineers2.exe**.
2. Launch **SE2PL.exe**. You can create a desktop shortcut to it.
3. Open **Mods**, enable your plugins, and select **Save & restart**.

Close the game before updating. Keep your `SE2PL/settings.json` and other installed plugins. Remove old `-plugins:` arguments from Steam's launch options.

Install plugins in `SE2PL/Plugins/<PluginName>`. Better Grouping adds a **Group** dropdown to the Control Panel.

[Downloads and plugin documentation](https://github.com/Teirdalin/SE2PL)

## Build from source

Requires a .NET SDK supporting .NET 9 and an installed copy of SE2. From the repository root:

```powershell
./tools/Build.ps1 -GameDir 'D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2'
```

Builds and tests SE2PL and the [Better Grouping example](Examples/BetterGrouping/src). Output: `dist/SE2PL-0.2.2.zip`. Game assemblies are referenced from your installation and are not included.
