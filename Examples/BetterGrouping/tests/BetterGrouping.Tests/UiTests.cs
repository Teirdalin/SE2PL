using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using BetterGrouping;
using BetterGrouping.Core;
using HarmonyLib;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel.Controls;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel.Views;
using Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel;
using Keen.VRage.UI.Shared;

internal static class UiTests
{
    private static readonly List<(EditKind Kind, ControlPanelGroupModel? Group, ControlPanelEntityModel[] Blocks)> Requests = [];
    private static IDataObject? draggedData;
    private static TaskCompletionSource<DragDropEffects>? pendingDrag;
    private static bool CaptureRequest(EditKind kind, ControlPanelGroupModel? group, ControlPanelEntityModel[] blocks)
    { Requests.Add((kind, group, blocks)); return false; }
    private static bool CaptureDrag(IDataObject __1, ref Task<DragDropEffects> __result)
    { draggedData = __1; __result = pendingDrag?.Task ?? Task.FromResult(DragDropEffects.Copy); return false; }
    private static bool Skip() => false;
    private static bool Initialize(ControlPanelLeftScreen __instance)
    {
        AccessTools.Field(typeof(ControlPanelLeftScreen), "PART_SearchBox").SetValue(__instance, new TextBox());
        AccessTools.Field(typeof(ControlPanelLeftScreen), "PART_GroupButton").SetValue(__instance, new Button());
        AccessTools.Field(typeof(ControlPanelLeftScreen), "PART_BlockListControl").SetValue(__instance, new BlockListControl());
        return false;
    }
    private static bool InitializeControls(BlockControlsScreen __instance)
    {
        var toolbarField = AccessTools.Field(typeof(BlockControlsScreen), "PART_ToolbarControl");
        toolbarField.SetValue(__instance, Activator.CreateInstance(toolbarField.FieldType));
        var power = new Avalonia.Controls.Primitives.ToggleButton { Content = "Turned On" };
        Grid.SetRow(power, 2);
        __instance.Content = new Grid { RowDefinitions = new("Auto,8,Auto"), Children = { power } };
        return false;
    }
    public static void Run()
    {
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        Application.Current!.Styles.Add(new Avalonia.Themes.Simple.SimpleTheme());
        var patcher = new Harmony("BetterGrouping.HeadlessUiFixture");
        // Supply a minimal view template and inert native VM lifecycle in the test process.
        // The plugin's menu, selection snapshot, and routed drag handlers remain unchanged.
        patcher.Patch(AccessTools.Method(typeof(ControlPanelLeftScreen), "InitializeComponent"), prefix: new HarmonyMethod(typeof(UiTests), nameof(Initialize)));
        patcher.Patch(AccessTools.Method(typeof(BlockControlsScreen), "InitializeComponent"), prefix: new HarmonyMethod(typeof(UiTests), nameof(InitializeControls)));
        patcher.Patch(AccessTools.Method(typeof(ControlPanelLeftScreen), "UpdateContext"), prefix: new HarmonyMethod(typeof(UiTests), nameof(Skip)));
        patcher.Patch(AccessTools.Method(typeof(EditQueue), "Submit"), prefix: new HarmonyMethod(typeof(UiTests), nameof(CaptureRequest)));
        patcher.Patch(AccessTools.Method(typeof(DragDrop), "DoDragDrop"), prefix: new HarmonyMethod(typeof(UiTests), nameof(CaptureDrag)) { priority = Priority.Last });
        using var liveHooks = new BetterGrouping.Plugin(null!);
        var window = new Window { Width = 700, Height = 500 };
        try
        {
            var panel = new ControlPanelLeftScreen();
            var context = new ControlPanelListModel();
            var serverType = typeof(ControlPanelGridModel).Assembly.GetType("Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel.ControlPanelGridModelServer", true)!;
            var grid = (ControlPanelGridModel)RuntimeHelpers.GetUninitializedObject(serverType);
            grid.RootGroups = new();
            grid.DisplayName = "Test grid";
            context.Grids.Add(grid);
            var group = new ControlPanelGroupModel { DisplayName = "Thrusters" };
            grid.RootGroups.Add(group); context.Groups.Add(group);
            var models = Enumerable.Range(0, 2).Select(_ => new ControlPanelEntityModel { GridModel = grid }).ToArray();
            foreach (var m in models) context.Blocks.Add(m);
            var vms = models.Select(m =>
            {
                var b = (ControlPanelEntityViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ControlPanelEntityViewModel));
                AccessTools.Field(typeof(ControlPanelEntityViewModel), "<Model>k__BackingField").SetValue(b, m);
                return b;
            }).ToArray();
            var vmType = typeof(ControlPanelLeftScreen).Assembly.GetType("Keen.Game2.Client.UI.TerminalScreen.ControlPanel.ControlPanelViewModel", true)!;
            var vm = RuntimeHelpers.GetUninitializedObject(vmType);
            AccessTools.Field(vmType, "_controlPanelModel").SetValue(vm, context);
            AccessTools.Field(vmType, "<BlockTerminalSelection>k__BackingField").SetValue(vm, new AvaloniaList<object>(vms));
            AccessTools.Field(vmType, "_blocks").SetValue(vm, new ReadOnlyObservableCollection<ControlPanelEntityViewModel>(new(vms)));
            panel.DataContext = vm;
            var wrapper = (ControlPanelGroupWrapper)RuntimeHelpers.GetUninitializedObject(typeof(ControlPanelGroupWrapper));
            AccessTools.Field(typeof(ControlPanelGroupWrapper), "_group").SetValue(wrapper, group);
            var blockRow = new ControlPanelItemEntry { DataContext = vms[0], Height = 30 };
            var groupRow = new ControlPanelItemEntry { DataContext = wrapper, Height = 30 };
            panel.Content = new StackPanel { Children = { blockRow, groupRow } };
            window.Content = panel;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            PanelExtension.Attach(panel);
            var selected = PanelView.From(vm)!.BlockTerminalSelection;
            var pointer = new Avalonia.Input.Pointer(1, PointerType.Mouse, true);
            var right = new PointerPressedEventArgs(blockRow, pointer, window, new Point(5, 5), 1,
                new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed), KeyModifiers.None);
            blockRow.RaiseEvent(right);
            Check.That(right.Handled && selected.Count == 2, "right-click preserves multiple selection");
            Check.That(blockRow.ContextMenu is { Items.Count: >= 3 }, "native row has supplemental context menu");
            var add = (MenuItem)blockRow.ContextMenu!.Items[0]!;
            Check.That((string)add.Header! == "Add to Group" && add.Items.Count == 1, "add submenu lists actual grid group");
            Check.That(((MenuItem)add.Items[0]!).Header!.ToString() == "Test grid / Thrusters", "group destination label");
            ((MenuItem)add.Items[0]!).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            Check.That(Requests.Count == 1 && Requests[0].Kind == EditKind.Add && Requests[0].Blocks.SequenceEqual(models) && Requests[0].Group == group,
                "context-menu click dispatches exact selection and target");
            blockRow.ContextMenu.Close();
            var left = new PointerPressedEventArgs(blockRow, pointer, window, new Point(5, 5), 2,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None);
            blockRow.RaiseEvent(left);
            var move = new PointerEventArgs(InputElement.PointerMovedEvent, blockRow, pointer, window, new Point(40, 40), 3,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None);
            AccessTools.DeclaredMethod(typeof(ControlPanelLeftScreen), "OnItemPointerMoved").Invoke(panel,
                [blockRow, new ControlPanelItemPointerEventArgs(vms[0], move)]);
            Check.That(draggedData is VRageDataObject && draggedData.Contains("ControlPanelEntityViewModel"), "native toolbar drag payload preserved");
            var captured = ((VRageDataObject)draggedData!).GetInternal("BetterGrouping.Selection.v1")!;
            Check.That(((ControlPanelEntityModel[])captured.GetType().GetProperty("Blocks")!.GetValue(captured)!).SequenceEqual(models), "drag start snapshots multiple selected components");
            var table = AccessTools.Field(typeof(PanelExtension), "Instances").GetValue(null)!;
            var lookup = table.GetType().GetMethod("TryGetValue")!;
            object?[] args = [panel, null]; lookup.Invoke(table, args);
            var extension = args[1]!;
            var payloadType = typeof(PanelExtension).GetNestedType("DragSelection", BindingFlags.NonPublic)!;
            var payload = Activator.CreateInstance(payloadType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [extension, PanelView.From(vm)!, context, models], null);
            var activeField = AccessTools.Field(typeof(PanelExtension), "activeDrag");
            activeField.SetValue(null, payload);
            var data = new VRageDataObject(); data.SetInternal("BetterGrouping.Selection.v1", payload!);
            var over = new DragEventArgs(DragDrop.DragOverEvent, data, groupRow, new Point(2, 2), KeyModifiers.None) { Source = groupRow };
            var enter = new DragEventArgs(DragDrop.DragEnterEvent, data, groupRow, new Point(2, 2), KeyModifiers.None) { Source = groupRow };
            groupRow.RaiseEvent(enter);
            Check.That(enter.Handled && enter.DragEffects == DragDropEffects.Copy && groupRow.Background is not null,
                "first drag-enter immediately accepts and highlights group without requiring another move");
            DragDrop.SetAllowDrop((StackPanel)panel.Content!, false);
            Check.That(DragDrop.GetAllowDrop(groupRow), "realized row remains droppable beneath a tree container that disables inherited dropping");
            groupRow.RaiseEvent(over);
            Check.That(over.Handled && over.DragEffects == DragDropEffects.Copy, "multi-component drag accepted for local group");
            Check.That(groupRow.Background is not null, "valid target receives highlight");
            var drop = new DragEventArgs(DragDrop.DropEvent, data, groupRow, new Point(2, 2), KeyModifiers.None) { Source = groupRow };
            groupRow.RaiseEvent(drop);
            Check.That(drop.Handled && Requests.Last().Kind == EditKind.Move && Requests.Last().Blocks.SequenceEqual(models), "multi-component drop dispatches one exact request");
            var single = Activator.CreateInstance(payloadType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, [extension, PanelView.From(vm)!, context, new[] { models[0] }], null);
            activeField.SetValue(null, single);
            groupRow.RaiseEvent(drop);
            Check.That(Requests.Last().Blocks.SequenceEqual([models[0]]), "single-component drop dispatches only dragged component");
            activeField.SetValue(null, payload);
            groupRow.RaiseEvent(new DragEventArgs(DragDrop.DragLeaveEvent, data, groupRow, new Point(), KeyModifiers.None) { Source = groupRow });
            Check.That(groupRow.Background is null, "drag-leave restores original highlight property");
            models[0].GroupModel = group;
            groupRow.RaiseEvent(over);
            Check.That(over.DragEffects == DragDropEffects.Move, "existing membership shows move effect");
            context.Blocks.Remove(models[1]);
            groupRow.RaiseEvent(over);
            Check.That(over.DragEffects == DragDropEffects.None && groupRow.Background is null, "stale dragged block invalidates drop");
            context.Blocks.Add(models[1]);
            models[1].GridModel = new ControlPanelGridModel();
            groupRow.RaiseEvent(over);
            Check.That(over.DragEffects == DragDropEffects.None, "cross-grid drop rejected");
            activeField.SetValue(null, null);
            models[1].GridModel = grid;
            pendingDrag = new TaskCompletionSource<DragDropEffects>();
            var nativeData = new VRageDataObject();
            nativeData.SetInternal("ControlPanelEntityViewModel", vms[0]);
            var nativeOperation = (Task<DragDropEffects>)AccessTools.Method(typeof(DragDrop), "DoDragDrop")
                .Invoke(null, [move, nativeData, DragDropEffects.Move])!;
            Check.That(nativeData.Contains("BetterGrouping.Selection.v1") && !nativeOperation.IsCompleted,
                "actual native drag entry point enriched even if supplemental pointer start was bypassed");
            var nativePayload = nativeData.GetInternal("BetterGrouping.Selection.v1")!;
            Check.That(((ControlPanelEntityModel[])nativePayload.GetType().GetProperty("Blocks")!.GetValue(nativePayload)!).SequenceEqual(models),
                "native fallback preserves both selected components");
            var nativeDrop = new DragEventArgs(DragDrop.DropEvent, nativeData, groupRow, new Point(2, 2), KeyModifiers.None) { Source = groupRow };
            groupRow.RaiseEvent(nativeDrop);
            Check.That(Requests.Last().Blocks.SequenceEqual(models), "native fallback drop dispatches complete selection");
            pendingDrag.SetResult(DragDropEffects.Move);
            Dispatcher.UIThread.RunJobs();
            Check.That(nativeOperation.IsCompletedSuccessfully && activeField.GetValue(null) is null,
                "native fallback state cleaned when platform drag operation completes");
            pendingDrag = null;
            DropdownTests(vm, models, group, grid, context);
            Check.Pass("headless native Avalonia controls: context menu, multi-selection, drag-over routing/highlight/effects, invalid targets (minimal fixture template)");
        }
        finally { PanelExtension.DetachAll(); window.Close(); patcher.UnpatchAll(patcher.Id); }
    }

    private static void DropdownTests(object vm, ControlPanelEntityModel[] models, ControlPanelGroupModel group,
        ControlPanelGridModel grid, ControlPanelListModel context)
    {
        var screen = new BlockControlsScreen { DataContext = vm };
        var dropdown = GroupDropdown.For(screen)!;
        Check.That(dropdown is not null, "native block-controls constructor attaches Group dropdown");
        var selection = PanelView.From(vm)!.BlockTerminalSelection;
        var vms = selection.ToArray();
        models[0].GroupModel = models[1].GroupModel = null;
        dropdown!.Refresh();
        Check.That(dropdown.Selector.IsEnabled && dropdown.Selector.SelectedItem!.ToString() == "None", "ungrouped multiselection shows None");
        int count = Requests.Count;
        dropdown.Selector.SelectedItem = dropdown.Selector.Items.Cast<GroupDropdown.Choice>().Single(c => c.Group == group);
        Check.That(Requests.Count == count + 1 && Requests.Last().Kind == EditKind.Move && Requests.Last().Blocks.SequenceEqual(models), "dropdown dispatches all selected blocks to existing group");
        // Captured submission substitutes simulation completion in this headless fixture.
        void Complete() => AccessTools.Field(typeof(GroupDropdown), "pending").SetValue(dropdown, false);
        Complete();
        models[0].GroupModel = group;
        dropdown.Refresh();
        Check.That(dropdown.Selector.SelectedItem!.ToString() == "Mixed", "different memberships show Mixed");
        dropdown.Selector.SelectedItem = dropdown.Selector.Items.Cast<GroupDropdown.Choice>().Single(c => c.Label == "None");
        Check.That(Requests.Last().Kind == EditKind.Ungroup && Requests.Last().Blocks.Length == 2, "None clears every selected block membership");
        Complete();
        models[1].GroupModel = group;
        dropdown.Refresh();
        Check.That(((GroupDropdown.Choice)dropdown.Selector.SelectedItem!).Group == group, "common membership reflects native model updates");
        count = Requests.Count;
        group.DisplayName = "Renamed thrusters";
        dropdown.Refresh();
        Check.That(dropdown.Selector.SelectedItem!.ToString() == "Renamed thrusters" && Requests.Count == count, "rename refresh makes no unsolicited edit");
        var nested = new ControlPanelGroupModel { DisplayName = "Nested" };
        group.SubGroups.Add(nested); context.Groups.Add(nested);
        dropdown.Refresh();
        Check.That(dropdown.Selector.Items.Cast<GroupDropdown.Choice>().Any(c => c.Label == "Renamed thrusters / Nested"), "nested group choices use paths");
        selection.Remove(vms[1]);
        dropdown.Refresh();
        dropdown.Selector.SelectedItem = dropdown.Selector.Items.Cast<GroupDropdown.Choice>().Single(c => c.Group == nested);
        Check.That(Requests.Last().Blocks.SequenceEqual([models[0]]) && Requests.Last().Group == nested, "single selection assigns only that block");
        Complete();
        selection.Add(vms[1]); dropdown.Refresh();
        // Selection changes while popup is open must never apply to a different selection.
        count = Requests.Count;
        selection.Remove(vms[1]);
        dropdown.Selector.SelectedItem = dropdown.Selector.Items.Cast<GroupDropdown.Choice>().Single(c => c.Label == "None");
        Check.That(Requests.Count == count, "changed popup selection cancels stale action");
        selection.Add(vms[1]); models[1].GridModel = new ControlPanelGridModel(); dropdown.Refresh();
        Check.That(!dropdown.Selector.IsEnabled, "multi-grid dropdown cannot move blocks across grids");
        models[1].GridModel = grid; context.Blocks.Remove(models[1]); dropdown.Refresh();
        Check.That(!dropdown.Selector.IsEnabled, "deleted selected block disables stale dropdown");
        context.Blocks.Add(models[1]); selection.Clear(); dropdown.Refresh();
        Check.That(!dropdown.Selector.IsEnabled, "empty selection disables dropdown");
        GroupDropdown.DetachAll();
        Check.That(((Grid)screen.Content!).Children.OfType<Avalonia.Controls.Primitives.ToggleButton>().Any(c => Grid.GetRow(c) == 2), "detach restores original native power control");
    }
}
