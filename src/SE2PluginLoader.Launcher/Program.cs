using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SE2PluginLoader.Core;

namespace SE2PluginLoader.Launcher;

internal static class Program
{
    internal static readonly string GameRoot = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    internal static readonly string Root = Path.Combine(GameRoot, "SE2PL");
    internal static readonly SettingsStore Store = new(Path.Combine(Root, "settings.json"));

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Contains("--check"))
            {
                var game = ResolveGame();
                string? error = null;
                try { ValidateGame(game); _ = LaunchArgument(); } catch (Exception ex) { error = ex.Message; }
                File.WriteAllText(Path.Combine(Root, "check-result.json"), System.Text.Json.JsonSerializer.Serialize(new
                { compatible = error is null, gameDirectory = game, pluginDirectory = Path.Combine(Root, "Plugins"), expectedVersion = Catalog.GameVersion, error }, Catalog.JsonOptions));
                return error is null ? 0 : 1;
            }
            if (args.Contains("--print-launch-argument"))
            {
                var output = LaunchArgument();
                File.WriteAllText(Path.Combine(Root, "steam-launch-option.txt"), output);
                return 0;
            }
            if (args.Contains("--restart-after-pid"))
            {
                var pid = int.Parse(Value(args, "--restart-after-pid")!);
                var expectedStart = long.Parse(Value(args, "--started-at")!);
                Process? oldGame = null;
                try { oldGame = Process.GetProcessById(pid); } catch (ArgumentException) { }
                using (oldGame)
                {
                    if (oldGame is not null && oldGame.StartTime.ToUniversalTime().Ticks == expectedStart && !oldGame.WaitForExit(120000))
                        throw new TimeoutException("SE2 has not closed. Relaunch it once shutdown has finished.");
                }
                // Steam needs a moment to observe the process exit before another app launch.
                Thread.Sleep(2500);
                Launch(false, ResolveGame());
                return 0;
            }
            if (!args.Contains("--settings"))
            {
                Launch(args.Contains("--safe-mode"), Value(args, "--game-dir") ?? ResolveGame());
                return 0;
            }
            Application.Run(new LauncherWindow());
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message + "\n\nUse SE2PL.exe --settings to open launcher settings.", "SE2PL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static string? Value(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    internal static string LaunchArgument()
    {
        var dll = Path.Combine(Root, "SE2PluginLoader.dll");
        if (!File.Exists(dll)) throw new FileNotFoundException("Missing SE2PL\\SE2PluginLoader.dll. Copy SE2PL.exe and the SE2PL folder into the game's Game2 folder together.");
        if (dll.Contains(';') || dll.Contains('"')) throw new InvalidOperationException("Loader path cannot contain a semicolon or quotation mark.");
        return "\"-plugins:" + dll + "\"";
    }

    internal static string? ReadGameDirectory()
    {
        try { return Store.Read().GameDirectory; } catch { return null; }
    }

    internal static string ResolveGame() => File.Exists(Path.Combine(GameRoot, "Game2.Client.dll"))
        ? GameRoot : ReadGameDirectory() ?? DiscoverGame() ?? "";

    internal static void ValidateGame(string game)
    {
        var client = Path.Combine(game, "Game2.Client.dll");
        if (!File.Exists(client)) throw new DirectoryNotFoundException("Choose SE2's Game2 folder first.");
        var version = AssemblyName.GetAssemblyName(client).Version?.ToString();
        if (version != Catalog.GameVersion) throw new NotSupportedException($"This loader supports SE2 {Catalog.GameVersion}. Installed: {version}.");
    }

    internal static void Launch(bool safeMode, string game)
    {
        ValidateGame(game);
        if (Process.GetProcessesByName("SpaceEngineers2").Any()) throw new InvalidOperationException("SE2 is already running. Save and exit before launching with the loader.");
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        var steamExe = Path.Combine(steam ?? "", "steam.exe");
        if (!File.Exists(steamExe)) throw new FileNotFoundException("Steam was not found. Install/sign in to Steam first.");
        var argument = LaunchArgument();
        if (!safeMode) Store.Save(Store.Read() with { GameDirectory = Path.GetFullPath(game) });
        _ = Process.Start(new ProcessStartInfo(steamExe)
        {
            Arguments = "-applaunch 1133870 " + argument + (safeMode ? " -se2pl-safe-mode" : ""),
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Steam could not be started.");
    }

    internal static string? DiscoverGame()
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (steam is null) return null;
        var libraries = new List<string> { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            libraries.AddRange(Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value.Replace("\\\\", "\\")));
        return libraries.Select(p => Path.Combine(p, "steamapps", "common", "SpaceEngineers2", "Game2"))
            .FirstOrDefault(p => File.Exists(Path.Combine(p, "Game2.Client.dll")));
    }
}
