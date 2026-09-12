using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using BetterGrouping.Core;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel.Views;
using Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel;

namespace BetterGrouping;

internal sealed class GroupDropdown
{
    internal sealed record Choice(string Label, ControlPanelGroupModel? Group = null, bool Mixed = false)
    { public override string ToString() => Label; }
    private static readonly ConditionalWeakTable<BlockControlsScreen, GroupDropdown> Instances = new();
    private static readonly List<WeakReference<GroupDropdown>> Live = [];
    private readonly BlockControlsScreen screen;
    private readonly Grid root;
    private readonly Control power;
    private readonly Grid header = new() { ColumnDefinitions = new("*,*") };
    private readonly StackPanel field = new() { Spacing = 2, Margin = new(16, 0, 8, 0) };
    internal readonly ComboBox Selector = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 36 };
    private readonly DispatcherTimer timer;
    private CancellationTokenSource lifetime = new();
    private long submittedAt;
    private ControlPanelEntityModel[] displayedBlocks = [];
    private Choice[] choices = [];
    private bool refreshing;
    private bool pending;

    internal static void Attach(BlockControlsScreen screen)
    {
        if (Instances.TryGetValue(screen, out _)) return;
        // Verified SE2 2.4.0.95 compiled XAML: power toggle at root (row 2, column 0).
        if (screen.Content is not Grid root || root.Children.OfType<ToggleButton>()
            .FirstOrDefault(c => Grid.GetRow(c) == 2 && Grid.GetColumn(c) == 0) is not { } power)
        { Plugin.Log("Group dropdown: block controls layout not recognized."); return; }
        var instance = new GroupDropdown(screen, root, power);
        Instances.Add(screen, instance);
        Live.RemoveAll(w => !w.TryGetTarget(out _));
        Live.Add(new(instance));
        Plugin.Log("Group dropdown attached to native block controls.");
    }

    private GroupDropdown(BlockControlsScreen screen, Grid root, Control power)
    {
        this.screen = screen; this.root = root; this.power = power;
        Selector.Name = "BetterGrouping_GroupSelector";
        Selector.Classes.Add("Bevelled");
        ToolTip.SetTip(Selector, "Assign selected blocks to a group. None removes their membership. SE2 supports one group per block.");
        field.Children.Add(new TextBlock { Text = "Group", FontSize = 16 });
        field.Children.Add(Selector);
        Grid.SetColumn(field, 1);
        root.Children.Remove(power);
        Grid.SetRow(power, 0);
        header.Children.Add(power);
        header.Children.Add(field);
        Grid.SetRow(header, 2);
        root.Children.Add(header);
        Selector.SelectionChanged += Changed;
        screen.Loaded += Loaded;
        screen.DetachedFromVisualTree += Detached;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += Tick;
        Refresh();
    }
    private void Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    { if (lifetime.IsCancellationRequested) lifetime = new(); timer.Start(); Refresh(); }
    private void Detached(object? sender, VisualTreeAttachmentEventArgs e)
    { timer.Stop(); lifetime.Cancel(); pending = false; }
    private void Tick(object? sender, EventArgs e)
    {
        if (pending && Environment.TickCount64 - submittedAt > 16000)
        { pending = false; ToolTip.SetTip(Selector, "Edit timed out. Check membership and try again."); }
        try { Refresh(); } catch (Exception ex) { timer.Stop(); Selector.IsEnabled = false; Plugin.Log("Group dropdown refresh failed: " + ex); }
    }
    private ControlPanelEntityModel[] Selected(PanelView? view) => view?.BlockTerminalSelection?
        .OfType<ControlPanelEntityViewModel>().Select(v => v.Model).Distinct().ToArray() ?? [];

    internal static Choice[] ListGroups(ControlPanelGridModel grid)
    {
        var result = new List<Choice> { new("None") };
        var seen = new HashSet<ControlPanelGroupModel>();
        void Add(IEnumerable<ControlPanelGroupModel> groups, string prefix)
        {
            foreach (var group in groups)
            {
                if (!seen.Add(group)) continue;
                var name = prefix + group.DisplayName;
                result.Add(new(name, group));
                Add(group.SubGroups, name + " / ");
            }
        }
        Add(grid.RootGroups, "");
        return result.ToArray();
    }

    internal void Refresh()
    {
        if (refreshing || pending || Selector.IsDropDownOpen) return;
        var view = PanelView.From(screen.DataContext);
        var blocks = Selected(view);
        field.IsVisible = blocks.Length > 0;
        // Hide empty column with the field, keeping normal full-width power control.
        header.ColumnDefinitions[1].Width = blocks.Length > 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        var grid = blocks.FirstOrDefault()?.GridModel;
        bool valid = view?.ControlPanelModel is { } context && grid is not null &&
            context.Grids.Contains(grid) && blocks.All(b => ReferenceEquals(b.GridModel, grid) && context.Blocks.Contains(b));
        var next = valid ? ListGroups(grid!) : [new Choice("Select blocks on one grid", Mixed: true)];
        var owners = blocks.Select(b => b.GroupModel).Distinct().ToArray();
        bool mixed = owners.Length > 1;
        if (mixed && valid) next = [new("Mixed", Mixed: true), .. next];
        var current = mixed || !valid ? next[0] : next.FirstOrDefault(c => ReferenceEquals(c.Group, owners.FirstOrDefault()));
        refreshing = true;
        try
        {
            displayedBlocks = blocks;
            if (!choices.SequenceEqual(next)) { choices = next; Selector.ItemsSource = choices; }
            Selector.SelectedIndex = current is null ? -1 : Array.IndexOf(choices, current);
            Selector.IsEnabled = valid && EditQueue.CanEdit(view!, grid!);
        }
        finally { refreshing = false; }
    }
    private void Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (refreshing || pending || Selector.SelectedItem is not Choice choice || choice.Mixed) return;
        try
        {
            var view = PanelView.From(screen.DataContext);
            var blocks = Selected(view);
            var grid = blocks.FirstOrDefault()?.GridModel;
            if (view?.ControlPanelModel is not { } context || grid is null || blocks.Length == 0 ||
                !blocks.SequenceEqual(displayedBlocks) || !context.Grids.Contains(grid) ||
                blocks.Any(b => !ReferenceEquals(b.GridModel, grid) || !context.Blocks.Contains(b)) ||
                (choice.Group is not null && !ListGroups(grid).Any(c => ReferenceEquals(c.Group, choice.Group))))
            { Selector.IsDropDownOpen = false; Refresh(); return; }
            Selector.IsDropDownOpen = false;
            pending = true;
            submittedAt = Environment.TickCount64;
            Selector.IsEnabled = false;
            EditQueue.Submit(view, grid, choice.Group, blocks, choice.Group is null ? EditKind.Ungroup : EditKind.Move,
                null, message => { pending = false; ToolTip.SetTip(Selector, message); Refresh(); }, lifetime.Token);
        }
        catch (Exception ex) { pending = false; Plugin.Log("Group dropdown edit failed: " + ex); Refresh(); }
    }
    internal static GroupDropdown? For(BlockControlsScreen screen) => Instances.TryGetValue(screen, out var value) ? value : null;
    internal static void DetachAll()
    {
        foreach (var weak in Live)
        {
            if (!weak.TryGetTarget(out var item)) continue;
            item.lifetime.Cancel(); item.timer.Stop();
            item.screen.Loaded -= item.Loaded;
            item.screen.DetachedFromVisualTree -= item.Detached;
            item.Selector.SelectionChanged -= item.Changed;
            item.root.Children.Remove(item.header);
            item.header.Children.Remove(item.power);
            Grid.SetRow(item.power, 2);
            item.root.Children.Add(item.power);
            Instances.Remove(item.screen);
        }
        Live.Clear();
    }
}
