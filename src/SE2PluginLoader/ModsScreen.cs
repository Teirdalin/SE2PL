using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Keen.VRage.UI.Screens;
using Keen.VRage.UI.AvaloniaInterface.Services;
using Keen.VRage.UI.Shared.Helpers;
using SE2PluginLoader.Core;

namespace SE2PluginLoader;

public sealed class ModsViewModel : ScreenViewModel
{
    public ModsViewModel()
    {
        KeepsOtherScreensVisible = false;
        AllowsInputBelowUI = false;
        AllowsInputFromLowerScreens = false;
        InitializeInputContext();
    }
}

[NeedsWindowStyles]
public sealed class ModsScreen : ScreenView
{
    public ModsScreen()
    {
        Content = new ModsPanel(Plugin.Session ?? throw new InvalidOperationException("Loader not initialized."), Dispose, MenuIntegration.Restart);
    }
}

// Kept separate from ScreenView so the real toggle and save behavior can be tested headlessly.
public sealed class ModsPanel : UserControl
{
    private readonly PluginSession session;
    private readonly HashSet<string> draft;
    private readonly Dictionary<PluginEntry, (CheckBox Toggle, TextBlock Status)> controls = [];
    private readonly TextBlock feedback = Text("Enable plugins for your next game launch. Changes require a restart.", 16);
    private readonly StackPanel list = new() { Spacing = 12 };
    private readonly TextBox search = new() { Watermark = "Search installed plugins", Margin = new Thickness(0, 0, 0, 12) };
    internal Button SaveButton { get; }
    internal Button RestartButton { get; }
    internal IReadOnlyDictionary<PluginEntry, (CheckBox Toggle, TextBlock Status)> Controls => controls;

    public ModsPanel(PluginSession session, Action close, Action restart)
    {
        this.session = session;
        draft = session.ReadEnabled();
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(36), MaxWidth = 1080, MaxHeight = 820 };
        var title = Text("MODS", 32);
        layout.Children.Add(title);
        var subtitle = Text("Installed plugins", 18); subtitle.Margin = new Thickness(0, 8, 0, 18);
        Grid.SetRow(subtitle, 1); layout.Children.Add(subtitle);
        Grid.SetRow(search, 2); layout.Children.Add(search);
        var scroll = new ScrollViewer { Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 3); layout.Children.Add(scroll);
        feedback.Margin = new Thickness(0, 16, 0, 16);
        Grid.SetRow(feedback, 4); layout.Children.Add(feedback);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        SaveButton = Button("Save changes", () => Save(false, restart));
        RestartButton = Button("Save & restart", () => Save(true, restart));
        actions.Children.Add(SaveButton); actions.Children.Add(RestartButton);
        actions.Children.Add(Button("Plugin folder", () => OpenFolder()));
        actions.Children.Add(Button("Back", close));
        Grid.SetRow(actions, 5); layout.Children.Add(actions);
        Content = new Border { Background = new SolidColorBrush(Color.Parse("#EE101923")), Child = layout };
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        search.TextChanged += (_, _) => Filter();
        BuildRows();
        if (session.SettingsError is not null)
        {
            feedback.Text = "Settings could not be read. No plugins loaded. " + session.SettingsError;
            SaveButton.IsEnabled = RestartButton.IsEnabled = false;
        }
        else if (session.SafeMode) feedback.Text = "Safe mode: plugins are inactive for this session. Saved choices apply on your next normal launch.";
    }

    private void BuildRows()
    {
        if (session.Entries.Count == 0)
            list.Children.Add(Text("No plugins installed. Open Plugin folder and add one subfolder per plugin, then reopen Mods.", 18));
        foreach (var entry in session.Entries)
        {
            var toggle = new CheckBox { Content = entry.Name, IsChecked = draft.Contains(entry.Id), FontSize = 22,
                IsEnabled = entry.Error is null && session.SettingsError is null };
            var status = Text(session.Status(entry, draft.Contains(entry.Id)), 15);
            var body = new StackPanel { Spacing = 5 };
            body.Children.Add(toggle);
            body.Children.Add(Text($"{entry.Version}  ·  {entry.Id}", 14));
            if (!string.IsNullOrWhiteSpace(entry.Description)) body.Children.Add(Text(entry.Description, 16));
            if (entry.Dependencies.Length > 0) body.Children.Add(Text("Requires: " + string.Join(", ", entry.Dependencies), 14));
            body.Children.Add(status);
            list.Children.Add(new Border { Child = body, Padding = new Thickness(18), Background = new SolidColorBrush(Color.Parse("#263746")), CornerRadius = new CornerRadius(4) });
            controls.Add(entry, (toggle, status));
            toggle.IsCheckedChanged += (_, _) =>
            {
                if (toggle.IsChecked == true) draft.Add(entry.Id); else draft.Remove(entry.Id);
                status.Text = session.Status(entry, toggle.IsChecked == true);
                feedback.Text = "Unsaved changes. Save to apply these choices on your next launch.";
            };
        }
    }

    private void Filter()
    {
        var query = search.Text?.Trim() ?? "";
        var i = 0;
        foreach (var entry in session.Entries)
            list.Children[i++].IsVisible = (entry.Name + " " + entry.Id + " " + entry.Description).Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void Save(bool restartNow, Action restart)
    {
        try
        {
            var missing = session.Entries.Where(e => draft.Contains(e.Id) && e.Error is null)
                .SelectMany(e => e.Dependencies.Where(d => !draft.Contains(d) || !session.Entries.Any(x => x.Id.Equals(d, StringComparison.OrdinalIgnoreCase) && x.Error is null))
                    .Select(d => $"{e.Name} requires {d}" )).ToArray();
            if (missing.Length > 0) { feedback.Text = string.Join("; ", missing); return; }
            session.SaveEnabled(draft);
            feedback.Text = "Saved. Restart Space Engineers 2 to apply your changes.";
            if (restartNow) restart();
        }
        catch (Exception ex) { feedback.Text = "Could not apply changes: " + ex.Message; Plugin.Log(ex.ToString()); }
    }

    private void OpenFolder()
    {
        try { Process.Start(new ProcessStartInfo(session.PluginDirectory) { UseShellExecute = true }); }
        catch (Exception ex) { feedback.Text = ex.Message; }
    }
    private static TextBlock Text(string text, double size) => new() { Text = text, FontSize = size, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
    private static Button Button(string text, Action action)
    {
        return new Button { Content = text, Padding = new Thickness(14, 8), MinHeight = 42, Command = SimpleCommand.Create(action) };
    }
}
