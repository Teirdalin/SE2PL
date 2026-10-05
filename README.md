# SE2PL

Adds a **Mods** menu to Space Engineers 2. Includes Better Grouping.

Requires SE2 **2.4.0.95**, Windows x64, Steam, and the **.NET 9 Desktop Runtime**.

1. Extract into the game's **Game2** folder, beside **SpaceEngineers2.exe**.
2. Launch **SE2PL.exe**. You can create a desktop shortcut to it.
3. Open **Mods**, enable your plugins, and select **Save & restart**.

Close the game before updating. Keep your `SE2PL/settings.json` and other installed plugins. Remove old `-plugins:` arguments from Steam's launch options.

Install plugins in `SE2PL/Plugins/<PluginName>`. Better Grouping adds a **Group** dropdown to the Control Panel.

## Create plugins

Use the [starter SDK](SDK/README.md) for a C# plugin template and build/package script. Download [SE2PL-SDK-0.2.2.zip](https://github.com/Teirdalin/SE2PL/releases/download/v0.2.2/SE2PL-SDK-0.2.2.zip), or use the SDK folder in this repository. Requires a .NET SDK supporting .NET 9 and a local SE2 2.4.0.95 installation.

## License

SE2PL, Better Grouping, and the SDK use [JDL-1](LICENSE) (LicenseRef-JDL-1). [SDK starter permission](SDK/SDK-PERMISSION.txt) allows template reuse in independently authored plugins. Third-party components retain their own licenses.
