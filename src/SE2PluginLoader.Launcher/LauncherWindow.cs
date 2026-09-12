using System.Diagnostics;
using SE2PluginLoader.Core;

namespace SE2PluginLoader.Launcher;

internal sealed class LauncherWindow : Form
{
    private readonly TextBox gamePath = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(650, 0), Margin = new Padding(0, 16, 0, 8) };

    public LauncherWindow()
    {
        Text = "SE2PL — Settings";
        ClientSize = new Size(720, 430);
        MinimumSize = new Size(660, 440);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(28), ColumnCount = 1, RowCount = 8 };
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "SE2PL — Space Engineers 2 Plugin Loader", AutoSize = true, Font = new Font("Segoe UI", 17, FontStyle.Bold), Margin = new Padding(0, 0, 0, 12) });
        layout.Controls.Add(new Label { Text = Catalog.Description, AutoSize = true, MaximumSize = new Size(650, 0), Margin = new Padding(0, 0, 0, 20) });
        layout.Controls.Add(new Label { Text = "Space Engineers 2 — Game2 folder", AutoSize = true });
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(gamePath, 0, 0);
        pathRow.Controls.Add(MakeButton("Browse…", Browse), 1, 0);
        layout.Controls.Add(pathRow);
        gamePath.Text = Program.ResolveGame();
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 20, 0, 0) };
        actions.Controls.Add(MakeButton("Launch SE2", () => Start(false)));
        actions.Controls.Add(MakeButton("Launch in safe mode", () => Start(true)));
        layout.Controls.Add(actions);
        var tools = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        tools.Controls.Add(MakeButton("Open Plugins folder", () =>
        {
            var directory = Path.Combine(Program.Root, "Plugins"); Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }));
        tools.Controls.Add(MakeButton("Copy Steam launch option", () =>
        {
            Clipboard.SetText(Program.LaunchArgument());
            status.Text = "Copied. In Steam → SE2 → Properties → Launch Options, replace the old -plugins option with this one. Then Steam Play loads Mods automatically.";
        }));
        layout.Controls.Add(tools);
        status.Text = "After launching, choose Mods on SE2's main menu. Toggle plugins, then Save & restart.\nSafe mode opens Mods with all managed plugins inactive.";
        layout.Controls.Add(status);
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Choose SpaceEngineers2\\Game2", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) gamePath.Text = dialog.SelectedPath;
    }
    private void Start(bool safe)
    {
        Program.Launch(safe, gamePath.Text.Trim());
        status.Text = safe ? "Starting SE2 in safe mode…" : "Starting SE2 with the Mods menu…";
    }
    private Button MakeButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 4, 8, 4), Margin = new Padding(0, 0, 10, 8) };
        button.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        return button;
    }
}
