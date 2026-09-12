using System.Reflection;
using System.Runtime.CompilerServices;
using BetterGrouping;
using Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel;
using Keen.VRage.Multiplayer.Replications.InProcess;

internal static class BridgeTests
{
    public static void Run()
    {
        var replay = (ReplayContext)Activator.CreateInstance(typeof(ReplayContext), BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object?[] { null }, null)!;
        var ui = new ControlPanelListModel();
        var server = new ControlPanelListModel();
        var uiGrid = new ControlPanelGridModel();
        var gridType = typeof(ControlPanelGridModel).Assembly.GetType("Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel.ControlPanelGridModelServer", true)!;
        var serverGrid = (ControlPanelGridModel)RuntimeHelpers.GetUninitializedObject(gridType);
        serverGrid.RootGroups = new();
        ui.Grids.Add(uiGrid); server.Grids.Add(serverGrid);
        var uiGroup = new ControlPanelGroupModel { DisplayName = "Same name" };
        var serverGroup = new ControlPanelGroupModel { DisplayName = "Same name" };
        var unrelated = new ControlPanelGroupModel { DisplayName = "Same name" };
        serverGrid.RootGroups.Add(unrelated); serverGrid.RootGroups.Add(serverGroup);
        server.Groups.Add(unrelated); server.Groups.Add(serverGroup);
        ui.Groups.Add(uiGroup); uiGrid.RootGroups.Add(uiGroup);
        var uiBlocks = Enumerable.Range(0, 2).Select(_ => new ControlPanelEntityModel { GridModel = uiGrid }).ToArray();
        var serverBlocks = Enumerable.Range(0, 2).Select(_ => new ControlPanelEntityModel { GridModel = serverGrid }).ToArray();
        replay.AddObjectPairing(ui, server); replay.AddObjectPairing(uiGrid, serverGrid); replay.AddObjectPairing(uiGroup, serverGroup);
        for (int i = 0; i < 2; i++)
        { ui.Blocks.Add(uiBlocks[i]); server.Blocks.Add(serverBlocks[i]); replay.AddObjectPairing(uiBlocks[i], serverBlocks[i]); }
        var mapped = LocalModelBridge.Translate(replay, ui, uiGrid, uiGroup, uiBlocks);
        Check.That(mapped.Context == server && mapped.Grid == serverGrid, "real ReplayContext maps replicated UI context/grid to simulation models");
        Check.That(mapped.Group == serverGroup && mapped.Group != unrelated, "group identity survives duplicate names without matching by name");
        Check.That(mapped.Blocks.SequenceEqual(serverBlocks), "all selected blocks translate by native pairing");
        mapped = LocalModelBridge.Translate(replay, ui, uiGrid, null, [uiBlocks[0], uiBlocks[0]]);
        Check.That(mapped.Group is null && mapped.Blocks.Length == 1, "ungroup translation supports null target and deduplicates selection");
        void Rejected(string label)
        {
            bool rejected = false;
            try { LocalModelBridge.Translate(replay, ui, uiGrid, uiGroup, uiBlocks); } catch { rejected = true; }
            Check.That(rejected, label);
        }
        server.Blocks.Remove(serverBlocks[1]); Rejected("stale server selection rejected before any edit"); server.Blocks.Add(serverBlocks[1]);
        serverBlocks[1].GridModel = new ControlPanelGridModel(); Rejected("detached/split block on different server grid rejected"); serverBlocks[1].GridModel = serverGrid;
        server.Groups.Remove(serverGroup); Rejected("deleted server target rejected"); server.Groups.Add(serverGroup);
        replay.RemoveObjectPairing(uiGroup); Rejected("missing native pairing never falls back to group name");
        Check.Pass("shipped ReplayContext: distinct client/server models, exact group identity, stale pairings, cross-grid selection");
    }
}
