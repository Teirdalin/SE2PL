using System.Reflection;
using BetterGrouping.Core;
using HarmonyLib;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Collections;

internal static class NativeTests
{
    private static int signals;
    private static bool SignalStub() { signals++; return false; }
    private sealed class NativeStore : IGroupStore<Entity, GridGroup>
    {
        public readonly CubeGridGroupsComponent Native = new();
        public NativeStore()
        {
            typeof(Component).GetProperty("Entity")!.SetValue(Native, Activator.CreateInstance(typeof(Entity), true));
        }
        public IEnumerable<GridGroup> Roots => Native.RootGroups;
        public IEnumerable<GridGroup> Children(GridGroup g) => g.SubGroups;
        public IEnumerable<Entity> Blocks(GridGroup g) => g.Blocks;
        public GridGroup? Parent(GridGroup g) => g.ParentGroup;
        public bool IsValid(Entity b) => true; // Offline entities have no Scene.
        public void Add(Entity b, GridGroup g) => Native.AddToGroup(b, g);
        public void Pop(Entity b, GridGroup g) => Native.MoveToParentOrPop(b, g);
        public void Delete(GridGroup g) => Native.RemoveGroup(g);
        public void Rename(GridGroup g, string name) => Native.SetDisplayName(g, name);
        public GridGroup Create(IReadOnlyList<Entity> blocks, string name)
        {
            var g = new GridGroup { DisplayName = name, Blocks = blocks.ToList() };
            Native.AddNewGroup(g); return g;
        }
    }

    public static void Run()
    {
        var patcher = new Harmony("BetterGrouping.OfflineTests");
        var type = typeof(CubeGridGroupsComponent);
        // Execute shipped membership methods, but replace DCS signal delivery because
        // there is no running scene. This deliberately does not test live replication.
        foreach (var name in new[] { "OnBlockAddedToGroup", "OnBlockRemovedFromGroup", "OnGroupAdded", "OnGroupRemoved", "OnGroupNameChanged" })
            patcher.Patch(AccessTools.DeclaredMethod(type, name), prefix: new HarmonyMethod(typeof(NativeTests), nameof(SignalStub)));
        try
        {
            Entity Make() => (Entity)Activator.CreateInstance(typeof(Entity), true)!;
            var blocks = Enumerable.Range(0, 6).Select(_ => Make()).ToArray();
            var s = new NativeStore();
            var g = s.Create([blocks[0]], "Thrusters");
            var other = s.Create([blocks[5]], "Other");
            EditResult Edit(EditKind kind, GridGroup target, params Entity[] entities) => GroupEditor.Apply(s, kind, target, entities);
            Check.That(Edit(EditKind.Add, g, blocks[1]).Changed == 1, "native add one");
            Check.That(Edit(EditKind.Add, g, blocks[2], blocks[3], blocks[2]).Changed == 2, "native add multiple");
            Check.That(Edit(EditKind.Add, g, blocks[1]).Changed == 0, "native duplicate no-op");
            Check.That(Edit(EditKind.Remove, g, blocks[1]).Changed == 1, "native remove one");
            Check.That(Edit(EditKind.Remove, g, blocks[2], blocks[3]).Changed == 2, "native remove multiple");
            Check.That(g.Blocks.SequenceEqual([blocks[0]]) && other.Blocks.SequenceEqual([blocks[5]]), "native exact retained membership");
            for (int i = 0; i < 500; i++)
            {
                Edit(EditKind.Move, g, blocks[1], blocks[2]);
                Edit(EditKind.Move, other, blocks[1], blocks[2]);
                Edit(EditKind.Remove, other, blocks[1], blocks[2]);
            }
            var registered = (HashSet<Entity>)AccessTools.Field(type, "_containedBlocks").GetValue(s.Native)!;
            Check.That(registered.SetEquals([blocks[0], blocks[5]]), "native registration remains consistent after 1500 edits");
            Check.That(Edit(EditKind.Remove, g, blocks[0]).Changed == 1 && !s.Native.RootGroups.Contains(g), "native empty group cleanup");
            var groupModelType = typeof(GridGroup).Assembly.GetType("Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel.ControlPanelGroupModelServer", true)!;
            var groupModel = (Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel.ControlPanelGroupModel)Activator.CreateInstance(groupModelType, s.Native, other)!;
            groupModel.DisplayName = "Renamed Other";
            Check.That(other.DisplayName == "Renamed Other", "native UI model rename persists in original group");
            ((IDisposable)groupModel).Dispose();
            var doomed = s.Create([blocks[4]], "Removed block group");
            var blockComponent = new CubeBlockComponent();
            typeof(Component).GetProperty("Entity")!.SetValue(blockComponent, blocks[4]);
            using var removed = new PooledList<CubeBlockComponent>();
            removed.Add(blockComponent);
            var removal = new CubeGridComponent.BlocksChangedArgs { RemovedBlocks = removed };
            AccessTools.DeclaredMethod(type, "OnBlockRemovedFromGrid").Invoke(s.Native, [removal]);
            Check.That(!s.Native.RootGroups.Contains(doomed) && !registered.Contains(blocks[4]), "native deleted/detached block callback cleans group and registry");
            Check.That(signals > 1000, "native notification triggers exercised");
            Check.Pass("shipped SE2 methods: create/add/remove/move/rename/idempotence/deleted-block callback/1500 edits; scene signal delivery stubbed");
            ContractTests();
        }
        finally { patcher.UnpatchAll(patcher.Id); }
    }

    private static void ContractTests()
    {
        var client = Assembly.Load("Game2.Client");
        var vm = client.GetType("Keen.Game2.Client.UI.TerminalScreen.ControlPanel.ControlPanelViewModel", true)!;
        foreach (var name in new[] { "ControlPanelModel", "BlockTerminalSelection", "Blocks" })
            Check.That(vm.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is not null, "viewmodel contract " + name);
        var left = client.GetType("Keen.Game2.Client.UI.TerminalScreen.ControlPanel.Views.ControlPanelLeftScreen", true)!;
        foreach (var name in new[] { "OnLoaded", "OnItemPointerMoved" })
            Check.That(AccessTools.DeclaredMethod(left, name) is not null, "hook contract " + name);
        var sim = typeof(GridGroup).Assembly;
        foreach (var pair in new[] { ("ControlPanelGridModelServer", "Grid"), ("ControlPanelEntityModelServer", "Entity") })
            Check.That(sim.GetType("Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel." + pair.Item1, true)!.GetProperty(pair.Item2) is not null, "server contract " + pair.Item1);
        // Verify Harmony can install and remove the complete plugin's hooks against the
        // installed runtime, without starting or modifying the user's game process.
        using var plugin = new BetterGrouping.Plugin(null!);
        Check.That(Harmony.HasAnyPatches("BetterGrouping.SE2"), "plugin installs native hooks");
        Check.Pass("installed-assembly reflection contracts and plugin hook initialization");
    }
}
