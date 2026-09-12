# Better Grouping

By **Teirdalin**. Version **0.1.3**, for Space Engineers 2 **2.4.0.95**.

Assign selected Control Panel blocks to existing groups with a **Group** dropdown.

## Install

Close SE2 and put the BetterGrouping folder inside `Game2/SE2PL/Plugins`. Start **SE2PL.exe**, enable **Better Grouping** in **Mods**, and restart if you changed its toggle. Keep the bundled DLLs together.

## Use

Select one or several blocks on the same grid in the Control Panel. Use **Group**, beside the power control, to choose an existing group.

- The dropdown shows the current group, **None**, or **Mixed** for different memberships.
- Choosing a group assigns every selected block to it. Existing members and the group name are preserved.
- Choosing **None** removes the selected blocks from their groups, including nested membership. SE2 removes empty groups.
- SE2 allows one direct group per block, so choosing another group transfers membership.

Right-click actions also support adding, removing, creating, renaming, and deleting groups. Drag-and-drop is experimental; use the dropdown.

## Notes

Supports local games. Remote multiplayer clients are unsupported. Save/reload and construct split/merge still need broader gameplay testing. Group edits use native game data.

Log: `%APPDATA%/SpaceEngineers2/BetterGrouping/BetterGrouping.log`.

## Permissions

Free to use. Redistributing, selling, rebranding, or publishing forks requires Teirdalin's written permission. The manifest may be adapted as a packaging example for your own plugins. See LICENSE; third-party components retain their own licenses.
