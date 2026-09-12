using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BetterGrouping.Core;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel.Controls;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel.Views;
using Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel;
using Keen.VRage.UI.Shared;

namespace BetterGrouping;

internal sealed class PanelExtension
{
    private const string DragKey = "BetterGrouping.Selection.v1";
    private static readonly ConditionalWeakTable<ControlPanelLeftScreen, PanelExtension> Instances = new();
    private static readonly List<WeakReference<PanelExtension>> Live = new();
    private readonly ControlPanelLeftScreen panel;
    private readonly CancellationTokenSource lifetime = new();
    private PanelView? View => PanelView.From(panel.DataContext);
    private ControlPanelEntityViewModel? pressed;
    private ControlPanelEntityViewModel[] pressedSelection = [];
    private Point origin;
    private bool dragging;
    private ControlPanelItemEntry? highlighted;
    private IDisposable? highlightBinding;
    private readonly Dictionary<ControlPanelItemEntry, IDisposable?> dropBindings = [];
    private string? lastDragDiagnostic;
    private static DragSelection? activeDrag;
    private sealed record DragSelection(PanelExtension Owner, PanelView View,
        ControlPanelListModel Context, ControlPanelEntityModel[] Blocks);

    internal static void Attach(ControlPanelLeftScreen panel)
    {
        if (Instances.TryGetValue(panel, out _)) return;
        try
        {
            var extension = new PanelExtension(panel);
            Instances.Add(panel, extension);
            Live.RemoveAll(w => !w.TryGetTarget(out _));
            Live.Add(new(extension));
            Plugin.Log("Control Panel group editing attached.");
        }
        catch (Exception ex) { Plugin.Log("UI attachment failed: " + ex); }
    }

    private PanelExtension(ControlPanelLeftScreen panel)
    {
        this.panel = panel;
        panel.AddHandler(InputElement.PointerPressedEvent, Pressed, RoutingStrategies.Tunnel, true);
        panel.AddHandler(InputElement.PointerReleasedEvent, Released, RoutingStrategies.Tunnel, true);
        panel.AddHandler(DragDrop.DragOverEvent, DragOver, RoutingStrategies.Bubble, true);
        panel.AddHandler(DragDrop.DragEnterEvent, DragOver, RoutingStrategies.Bubble, true);
        panel.AddHandler(DragDrop.DropEvent, Drop, RoutingStrategies.Bubble, true);
        panel.AddHandler(DragDrop.DragLeaveEvent, DragLeave, RoutingStrategies.Bubble, true);
        panel.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel, true);
        DragDrop.SetAllowDrop(panel, true);
        panel.DetachedFromVisualTree += Detached;
        panel.LayoutUpdated += LayoutUpdated;
        RefreshDropTargets();
    }

    internal static void DetachAll()
    {
        foreach (var weak in Live.ToArray()) if (weak.TryGetTarget(out var item)) item.Detach();
        Live.Clear();
    }
    private void Detached(object? sender, VisualTreeAttachmentEventArgs args) => Detach();
    private void Detach()
    {
        lifetime.Cancel();
        ClearHighlight();
        pressed = null;
        if (activeDrag?.Owner == this) activeDrag = null;
        panel.RemoveHandler(InputElement.PointerPressedEvent, Pressed);
        panel.RemoveHandler(InputElement.PointerReleasedEvent, Released);
        panel.RemoveHandler(DragDrop.DragOverEvent, DragOver);
        panel.RemoveHandler(DragDrop.DragEnterEvent, DragOver);
        panel.RemoveHandler(DragDrop.DropEvent, Drop);
        panel.RemoveHandler(DragDrop.DragLeaveEvent, DragLeave);
        panel.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        panel.DetachedFromVisualTree -= Detached;
        panel.LayoutUpdated -= LayoutUpdated;
        foreach (var binding in dropBindings.Values) binding?.Dispose();
        dropBindings.Clear();
        panel.ClearValue(DragDrop.AllowDropProperty);
        Instances.Remove(panel);
    }

    private static ControlPanelItemEntry? Entry(object? source)
        => (source as Visual)?.GetSelfAndVisualAncestors().OfType<ControlPanelItemEntry>().FirstOrDefault();

    private void LayoutUpdated(object? sender, EventArgs e) => RefreshDropTargets();
    private void RefreshDropTargets()
    {
        var rows = panel.GetVisualDescendants().OfType<ControlPanelItemEntry>().ToHashSet();
        foreach (var old in dropBindings.Keys.Where(row => !rows.Contains(row)).ToArray())
        { dropBindings[old]?.Dispose(); dropBindings.Remove(old); }
        foreach (var row in rows)
            if (!dropBindings.ContainsKey(row))
                dropBindings[row] = row.SetValue(DragDrop.AllowDropProperty, true, Avalonia.Data.BindingPriority.Animation);
    }

    // Native SE2 drag paths can bypass the supplemental pointer-start handler. Enrich the
    // real drag operation at its boundary, retaining its overlay, toolbar data and lifetime.
    internal static object? EnrichNativeDrag(PointerEventArgs trigger, IDataObject data, ref DragDropEffects effects)
    {
        if (data.Contains(DragKey) || data is not VRageDataObject native ||
            !data.Contains("ControlPanelEntityViewModel") ||
            data.GetInternal("ControlPanelEntityViewModel") is not ControlPanelEntityViewModel block) return null;
        var candidates = Live.Select(w => w.TryGetTarget(out var item) ? item : null)
            .Where(item => item?.View?.Blocks.Contains(block) == true && !item.lifetime.IsCancellationRequested).ToArray();
        if (candidates.Length != 1 || candidates[0] is not { } owner || owner.View?.ControlPanelModel is not { } context) return null;
        var selected = owner.pressedSelection.Contains(block) ? owner.pressedSelection : owner.Selected(block);
        var payload = new DragSelection(owner, owner.View, context, selected.Select(v => v.Model).Distinct().ToArray());
        native.SetInternal(DragKey, payload);
        activeDrag = payload;
        effects |= DragDropEffects.Copy;
        owner.lastDragDiagnostic = null;
        owner.RefreshDropTargets();
        Plugin.Log($"Native component drag captured: {payload.Blocks.Length} selected; grid model {block.Model.GridModel?.GetType().Name}.");
        return payload;
    }
    internal static async Task<DragDropEffects> CompleteNativeDrag(object state, Task<DragDropEffects> operation)
    {
        var payload = (DragSelection)state;
        try { return await operation; }
        finally
        {
            if (ReferenceEquals(activeDrag, payload)) activeDrag = null;
            payload.Owner.ClearHighlight();
            payload.Owner.pressedSelection = [];
        }
    }

    private ControlPanelEntityViewModel[] Selected(ControlPanelEntityViewModel? fallback = null)
    {
        var selected = View?.BlockTerminalSelection?.OfType<ControlPanelEntityViewModel>().ToArray() ?? [];
        return fallback is not null && !selected.Contains(fallback) ? [fallback] : selected;
    }

    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            var row = Entry(e.Source);
            if (row is null || View is null || row.IsEditing) return;
            var point = e.GetCurrentPoint(panel);
            if (point.Properties.IsRightButtonPressed)
            {
                // Freeze before TreeView's right-click selection can discard Ctrl/Shift selection.
                var selection = Selected(row.DataContext as ControlPanelEntityViewModel);
                ShowMenu(row, selection);
                e.Handled = true;
                pressed = null;
            }
            else if (point.Properties.IsLeftButtonPressed &&
                     row.DataContext is ControlPanelEntityViewModel block)
            {
                pressed = block;
                pressedSelection = Selected(block);
                origin = point.Position;
            }
            else pressed = null;
        }
        catch (Exception ex) { Plugin.Log(ex.ToString()); }
    }
    private void Released(object? sender, PointerReleasedEventArgs e) => pressed = null;
    private void KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { pressed = null; ClearHighlight(); }
    }

    internal static bool HandleNativeDrag(ControlPanelLeftScreen panel, ControlPanelItemPointerEventArgs args)
    {
        if (!Instances.TryGetValue(panel, out var extension) || args.Item is not ControlPanelEntityViewModel)
            return true;
        _ = extension.TryStartDrag(args.PointerArgs);
        return false;
    }

    private async Task TryStartDrag(PointerEventArgs e)
    {
        if (pressed is null || dragging || View?.ControlPanelModel is not { } context) return;
        var point = e.GetCurrentPoint(panel);
        if (!point.Properties.IsLeftButtonPressed) { pressed = null; return; }
        var delta = point.Position - origin;
        if (delta.X * delta.X + delta.Y * delta.Y < 36) return;
        var block = pressed;
        pressed = null;
        dragging = true;
        var payload = new DragSelection(this, View, context, pressedSelection.Select(v => v.Model).Distinct().ToArray());
        activeDrag = payload;
        lastDragDiagnostic = null;
        RefreshDropTargets();
        Plugin.Log($"Component drag started: {payload.Blocks.Length} selected; grid model {block.Model.GridModel?.GetType().Name}.");
        try
        {
            var data = new VRageDataObject();
            // Retain SE2's payload so dragging to native toolbar slots still works.
            data.SetInternal(((IDraggableControlPanelItem)block).DragDataKey, block);
            data.SetInternal(DragKey, payload);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy | DragDropEffects.Move);
        }
        catch (Exception ex) { Plugin.Log("Drag failed: " + ex); }
        finally
        {
            if (activeDrag == payload) activeDrag = null;
            dragging = false;
            ClearHighlight();
        }
    }

    private bool TryDestination(DragEventArgs e, out ControlPanelItemEntry? row,
        out ControlPanelGroupWrapper? target, out DragSelection? payload)
    {
        row = Entry(e.Source) ?? Entry(panel.InputHitTest(e.GetPosition(panel)));
        target = row?.DataContext as ControlPanelGroupWrapper;
        payload = activeDrag;
        if (target is null || payload is null || !e.Data.Contains(DragKey) ||
            !ReferenceEquals(payload.View, View) || !ReferenceEquals(payload.Context, View?.ControlPanelModel)) return false;
        var grid = GridOf(target.Model);
        var snapshot = payload;
        return grid is not null && View is { } view && EditQueue.CanEdit(view, grid) && snapshot.Blocks.Length > 0 &&
            snapshot.Blocks.All(b => ReferenceEquals(b.GridModel, grid) && snapshot.Context.Blocks.Contains(b));
    }
    private void DragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DragKey)) return;
        try
        {
        ClearHighlight();
        if (TryDestination(e, out var row, out _, out var payload))
        {
            e.DragEffects = payload!.Blocks.Any(b => b.GroupModel is not null) ? DragDropEffects.Move : DragDropEffects.Copy;
            highlighted = row;
            var brush = new SolidColorBrush(Color.FromArgb(150, 25, 150, 190));
            var area = row!.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_DoubleClickArea");
            highlightBinding = area is not null
                ? area.SetValue(Border.BackgroundProperty, brush, Avalonia.Data.BindingPriority.Animation)
                : row!.SetValue(TemplatedControl.BackgroundProperty, brush, Avalonia.Data.BindingPriority.Animation);
            DiagnoseDrag("Drop target accepted: " + ((ControlPanelGroupWrapper)row!.DataContext!).Model.DisplayName);
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
            DiagnoseDrag("Drop target rejected: source=" + e.Source?.GetType().Name + "; row=" + Entry(e.Source)?.DataContext?.GetType().Name);
        }
        // Only own the component tree; native toolbar handlers remain outside this panel.
        e.Handled = true;
        }
        catch (Exception ex) { e.DragEffects = DragDropEffects.None; e.Handled = true; DiagnoseDrag("Drag target error: " + ex); }
    }
    private void DiagnoseDrag(string message)
    {
        if (lastDragDiagnostic == message) return;
        lastDragDiagnostic = message;
        Plugin.Log(message);
    }
    private void Drop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DragKey)) return;
        try
        {
        if (TryDestination(e, out _, out var target, out var payload))
        {
            Plugin.Log($"Drop submitted: {payload!.Blocks.Length} components to {target!.Model.DisplayName}.");
            Submit(EditKind.Move, target!.Model, payload!.Blocks);
        }
        }
        catch (Exception ex) { Plugin.Log("Drop failed: " + ex); }
        ClearHighlight();
        e.Handled = true;
    }
    private void DragLeave(object? sender, DragEventArgs e) => ClearHighlight();
    private void ClearHighlight() { highlightBinding?.Dispose(); highlightBinding = null; highlighted = null; }

    private ControlPanelGridModel? GridOf(ControlPanelGroupModel group)
    {
        static bool Contains(IEnumerable<ControlPanelGroupModel> items, ControlPanelGroupModel find)
            => items.Any(g => ReferenceEquals(g, find) || Contains(g.SubGroups, find));
        return View?.ControlPanelModel?.Grids.FirstOrDefault(g => Contains(g.RootGroups, group));
    }

    private void Submit(EditKind kind, ControlPanelGroupModel? group, ControlPanelEntityModel[] selection, string? name = null)
    {
        if (View is not { } view) return;
        var grid = group is null ? selection.FirstOrDefault()?.GridModel : GridOf(group);
        if (grid is null) { Report("The grid or group no longer exists."); return; }
        EditQueue.Submit(view, grid, group, selection, kind, name, Report, lifetime.Token);
    }

    private static MenuItem Action(string label, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = label, IsEnabled = enabled };
        item.Click += (_, e) =>
        {
            e.Handled = true;
            try { action(); } catch (Exception ex) { Plugin.Log("Menu action failed: " + ex); }
        };
        return item;
    }

    private void ShowMenu(ControlPanelItemEntry row, ControlPanelEntityViewModel[] selection)
    {
        var models = selection.Select(b => b.Model).Distinct().ToArray();
        var menu = new ContextMenu();
        MenuItem Targets(string label, EditKind kind)
        {
            var item = new MenuItem { Header = label };
            foreach (var grid in View!.ControlPanelModel?.Grids ?? [])
            {
                void AddGroups(IEnumerable<ControlPanelGroupModel> groups, string prefix)
                {
                    foreach (var group in groups)
                    {
                        var path = prefix + group.DisplayName;
                        bool eligible = models.Any(b => ReferenceEquals(b.GridModel, grid));
                        item.Items.Add(Action(path, () => Submit(kind, group, models), eligible && EditQueue.CanEdit(View!, grid)));
                        AddGroups(group.SubGroups, path + " / ");
                    }
                }
                AddGroups(grid.RootGroups, grid.DisplayName + " / ");
            }
            item.IsEnabled = models.Length > 0 && item.Items.Count > 0;
            return item;
        }
        menu.Items.Add(Targets("Add to Group", EditKind.Add));
        menu.Items.Add(Targets("Remove from Group", EditKind.Remove));
        if (models.Any(b => b.GroupModel is not null)) menu.Items.Add(Targets("Move to Group (one membership in SE2)", EditKind.Move));
        bool canCreate = models.Count(b => b.GroupModel is null) >= 2 && models.Select(b => b.GridModel).Distinct().Count() == 1;
        menu.Items.Add(Action("Create Group from Selection…", () => PromptName("Create group", "New group", name => Submit(EditKind.Create, null, models, name)), canCreate));
        if (row.DataContext is ControlPanelGroupWrapper groupRow)
        {
            var target = groupRow.Model;
            menu.Items.Add(new Separator());
            menu.Items.Add(Action("Add Selected to This Group", () => Submit(EditKind.Add, target, models), models.Length > 0));
            menu.Items.Add(Action("Remove Selected from This Group", () => Submit(EditKind.Remove, target, models), models.Length > 0));
            menu.Items.Add(Action("Select All Components", () => SelectMembers(target)));
            menu.Items.Add(Action("Rename Group…", () => PromptName("Rename group", target.DisplayName, name => Submit(EditKind.Rename, target, [], name))));
            var delete = new MenuItem { Header = "Delete Group…" };
            delete.Items.Add(Action("Delete group and subgroups; keep blocks", () => Submit(EditKind.Delete, target, [])));
            menu.Items.Add(delete);
        }
        row.ContextMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(row.ContextMenu, menu)) row.ContextMenu = null; };
        menu.Open(row);
    }

    private void SelectMembers(ControlPanelGroupModel target)
    {
        if (View is not { } view) return;
        var ids = new HashSet<Keen.VRage.Multiplayer.SecureIds.EntityId>();
        void Collect(ControlPanelGroupModel g) { ids.UnionWith(g.BlockIds); foreach (var child in g.SubGroups) Collect(child); }
        Collect(target);
        view.BlockTerminalSelection.Clear();
        foreach (var block in view.Blocks.Where(b => ids.Contains(b.Model.EntityId))) view.BlockTerminalSelection.Add(block);
        view.UpdateSelection(panel, new SelectionChangedEventArgs(SelectingItemsControl.SelectionChangedEvent, Array.Empty<object>(), view.BlockTerminalSelection.ToArray()));
    }

    private void PromptName(string title, string initial, Action<string> apply)
    {
        var input = new TextBox { Text = initial, MinWidth = 280, MaxLength = 512 };
        var save = new Button { Content = "Save", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = title }, input, save } };
        var flyout = new Flyout { Content = content };
        save.Click += (_, _) => { var name = input.Text ?? ""; flyout.Hide(); apply(name); };
        flyout.ShowAt(panel);
        Dispatcher.UIThread.Post(() => { input.Focus(); input.SelectAll(); });
    }
    private void Report(string message)
    {
        if (lifetime.IsCancellationRequested || !panel.IsAttachedToVisualTree()) return;
        var flyout = new Flyout { Content = new TextBlock { Text = message, MaxWidth = 380, TextWrapping = TextWrapping.Wrap } };
        flyout.ShowAt(panel);
    }
}
