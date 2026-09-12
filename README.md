# SE2PL — Space Engineers 2 Plugin Loader

By **Teirdalin**. [Download the latest release](https://github.com/Teirdalin/SE2PL/releases/latest).

A simple plugin loader that adds a **Mods** menu to activate or deactivate installed plugins. Launch the game directly through **SE2PL.exe**.

## Requirements

Windows x64, Steam, Space Engineers 2 **2.4.0.95**, and the **.NET 9 Desktop Runtime**. SE2PL checks the installed game version before launching.

## Install

1. In Steam, right-click Space Engineers 2 → **Manage → Browse local files**.
2. Open **Game2**, beside **SpaceEngineers2.exe**.
3. Extract the ZIP there, keeping **SE2PL.exe** and the **SE2PL** folder together.
4. Create a desktop shortcut to **SE2PL.exe** and use it to launch the game.
5. Open **Mods** on the main menu, choose your plugins, and select **Save & restart**.

Close the game before updating. Back up your existing SE2PL folder and keep your `settings.json` and other installed plugins. Remove any old `-plugins:` argument from Steam's launch options when switching to SE2PL.

## Plugins

Put each plugin in its own folder under `Game2/SE2PL/Plugins`. Open Mods to see installed plugins; new plugins start disabled. Toggle changes take effect after restarting the game.

**Better Grouping 0.1.3** is included as the example plugin. Enable it in Mods, then select blocks in the Control Panel and use the **Group** dropdown to assign them to a group or choose **None**. Multiple selected blocks are supported. Drag-and-drop is experimental; use the dropdown.

Making a plugin? See [PLUGIN-AUTHORS.md](PLUGIN-AUTHORS.md) and `plugin.example.json`.

## Help

- `SE2PL.exe --safe-mode` starts with plugins disabled for that launch.
- `SE2PL.exe --settings` opens launcher settings.
- Log: `%APPDATA%/SpaceEngineers2/PluginLoader/loader.log`.
- Plugin settings: `Game2/SE2PL/settings.json`. Keep this folder writable.

## Permissions

SE2PL is free to use. Redistributing, selling, rebranding, or publishing forks requires Teirdalin's written permission. Independent plugins keep their own licenses. See [LICENSE](LICENSE); third-party components retain their own terms.


