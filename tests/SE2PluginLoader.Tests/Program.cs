using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HarmonyLib;
using Keen.Game2.Client.UI.Menu;
using Keen.VRage.Core.Plugins;
using Keen.VRage.Library.Reflection;
using SE2PluginLoader;
using SE2PluginLoader.Core;

internal static class Program
{
    private static int count;
    private static int pushes;
    private static int pops;
    private static string fixture = "";
    private static string root = "";

    private static int Main(string[] args)
    {
        var game = args.ElementAtOrDefault(0) ?? @"D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2";
        fixture = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "SE2PluginLoader/tests/FixturePlugin/bin/Release/net9.0-windows/FixturePlugin.dll");
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(game, name.Name + ".dll"))
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, name.Name + ".dll")) : null;
        root = Path.Combine(Path.GetTempPath(), "SE2PluginLoader.Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { return Run(); }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            Environment.SetEnvironmentVariable("SE2PL_TEST_MARKER", null);
            // The unique, fixed-prefix directory created above is the only deletion target.
            if (Path.GetFileName(root).StartsWith("SE2PluginLoader.Tests-"))
            {
                try { Directory.Delete(root, true); }
                catch (UnauthorizedAccessException) { Console.WriteLine("Test fixture DLL locked until process exit: " + root); }
                catch (IOException) { Console.WriteLine("Test fixture DLL locked until process exit: " + root); }
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        var marker = Path.Combine(root, "executed.txt");
        Environment.SetEnvironmentVariable("SE2PL_TEST_MARKER", marker);
        var installed = Path.Combine(root, "installed");
        WritePlugin(installed, "Good", "Good", "GoodPlugin");
        WritePlugin(installed, "Broken", "Broken", "BrokenPlugin");
        WritePlugin(installed, "Later", "Later", "LaterPlugin", ["Good"]);
        var entries = Catalog.Scan(Path.Combine(installed, "Plugins"));
        Check(entries.Count == 3 && entries.All(e => e.Error is null), "manifest discovery: " + string.Join("; ", entries.Select(e => e.Id + ": " + e.Error)));
        Check(!File.Exists(marker), "discovery does not execute plugin module initializer");
        Check(Catalog.FindEntryPoints(fixture).Length == 3, "PE IPlugin interface discovery");
        var store = new SettingsStore(Path.Combine(installed, "settings.json"));
        Check(store.Read().EnabledPlugins.Length == 0, "new plugins default to disabled");
        store.SetEnabled(["Good", "Good", "Broken", "Later"]);
        Check(store.Read().EnabledPlugins.Length == 3, "enabled ids deduplicated");
        for (var i = 0; i < 80; i++)
        {
            store.SetEnabled(i % 2 == 0 ? ["Good"] : ["Good", "Later"]);
            Check(store.Read().EnabledPlugins.Length == (i % 2 == 0 ? 1 : 2), "atomic settings roundtrip " + i);
        }
        Check(File.Exists(store.Path + ".bak"), "previous settings backup");
        store.SetEnabled(["Later"]);
        var missing = new PluginSession(installed);
        Check(missing.StartupOrder().Count == 0 && missing.StartupStatus.ContainsKey("Later"), "disabled dependency blocks dependent");
        store.SetEnabled(["Later", "Good", "Broken"]);
        var session = new PluginSession(installed);
        var order = session.StartupOrder().Select(e => e.Id).ToArray();
        Check(Array.IndexOf(order, "Good") < Array.IndexOf(order, "Later"), "dependency load order");
        Check(new PluginSession(installed, true).StartupOrder().Count == 0, "safe mode skips all enabled plugins");

        var bad = Path.Combine(root, "bad");
        WritePlugin(bad, "One", "duplicate", "GoodPlugin");
        WritePlugin(bad, "Two", "DUPLICATE", "GoodPlugin");
        Check(Catalog.Scan(Path.Combine(bad, "Plugins")).All(e => e.Error!.Contains("Duplicate")), "duplicate ids fail closed");
        var traversal = Path.Combine(root, "traversal");
        WritePlugin(traversal, "Escape", "Escape", "GoodPlugin", assembly: "../../FixturePlugin.dll");
        Check(Catalog.Scan(Path.Combine(traversal, "Plugins")).Single().Error is not null, "manifest cannot escape plugin directory");
        var malformed = Path.Combine(root, "malformed");
        WritePlugin(malformed, "InvalidDependency", "InvalidDependency", "GoodPlugin", [null!]);
        Check(Catalog.Scan(Path.Combine(malformed, "Plugins")).Single().Error is not null, "malformed dependency rejected without breaking catalog");
        var lateCorruption = new PluginSession(installed);
        File.WriteAllText(store.Path, "{bad external edit");
        Check(lateCorruption.ReadEnabled().Count == 0 && lateCorruption.SettingsError is not null, "settings corrupted after startup still leave menu available");
        store.Save(new LoaderSettings { EnabledPlugins = ["Good", "Later", "Broken"] });
        var cycle = Path.Combine(root, "cycle");
        WritePlugin(cycle, "A", "A", "GoodPlugin", ["B"]); WritePlugin(cycle, "B", "B", "LaterPlugin", ["A"]);
        new SettingsStore(Path.Combine(cycle, "settings.json")).SetEnabled(["A", "B"]);
        Check(new PluginSession(cycle).StartupOrder().Count == 0, "dependency cycles blocked");
        var corrupted = Path.Combine(root, "corrupted"); Directory.CreateDirectory(corrupted);
        File.WriteAllText(Path.Combine(corrupted, "settings.json"), "{broken");
        var corrupt = new PluginSession(corrupted);
        Check(corrupt.SettingsError is not null && corrupt.StartupOrder().Count == 0, "corrupt config disables loading");
        try { corrupt.SaveEnabled(["Good"]); throw new Exception("Expected save failure"); } catch (InvalidDataException) { count++; }
        Check(File.ReadAllText(corrupt.Store.Path) == "{broken", "corrupt config preserved for recovery");

        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
        Application.Current!.Styles.Add(new Avalonia.Themes.Simple.SimpleTheme());
        UiTests(installed);
        NativeTests(installed, marker);
        Console.WriteLine($"PASS {count} assertions: catalog, settings, native host lifecycle, main-menu hook, headless Mods toggles.");
        return 0;
    }

    private static void UiTests(string installed)
    {
        MenuIntegration.Validate();
        var menu = new GameMenu();
        var buttons = new StackPanel();
        buttons.Children.Add(new Button { Name = "MenuSettings" });
        buttons.Children.Add(new Button { Name = "MenuQuit" });
        var vmType = typeof(GameMenu).Assembly.GetType("Keen.Game2.Client.UI.Menu.MainMenu.MainMenuScreenViewModel", true)!;
        menu.DataContext = RuntimeHelpers.GetUninitializedObject(vmType);
        AccessTools.Field(typeof(GameMenu), "_buttonsPanel").SetValue(menu, buttons);
        MenuIntegration.AfterUpdateButtons(menu); MenuIntegration.AfterUpdateButtons(menu);
        Check(buttons.Children.Count == 3 && buttons.Children[0].Name == MenuIntegration.ButtonName, "main menu Mods button inserted once before Settings");
        var modsButton = (Button)buttons.Children[0];
        Check(modsButton.Command is not null && modsButton.Command.CanExecute(null), "native menu button has an executable command so game styling does not disable it");
        var clicks = 0;
        var commandPanel = new StackPanel();
        MenuIntegration.InsertButton(commandPanel, () => clicks++);
        ((Button)commandPanel.Children[0]).Command!.Execute(null);
        Check(clicks == 1, "native Mods command invokes open action");
        var inGameType = typeof(GameMenu).Assembly.GetType("Keen.Game2.Client.UI.Menu.InGameMenu.InGameMenuScreenViewModel", true)!;
        Check(!MenuIntegration.IsMainMenu(RuntimeHelpers.GetUninitializedObject(inGameType)), "pause menu excluded");
        var session = new PluginSession(installed);
        session.Loaded.Add("Good");
        var panel = new ModsPanel(session, () => { }, () => { });
        var window = new Window { Width = 1050, Height = 800, Content = panel };
        window.Show(); Dispatcher.UIThread.RunJobs();
        Check(panel.Controls.Count == 3, "installed plugins rendered");
        var good = panel.Controls.Single(p => p.Key.Id == "Good").Value;
        good.Toggle.IsChecked = false;
        Check(good.Status.Text!.Contains("after restart"), "active state distinct from next-start toggle");
        Check(session.Store.Read().EnabledPlugins.Contains("Good"), "toggle not persisted before Save");
        var later = panel.Controls.Single(p => p.Key.Id == "Later").Value;
        later.Toggle.IsChecked = false;
        panel.SaveButton.Command!.Execute(null);
        Check(!session.Store.Read().EnabledPlugins.Contains("Good") && session.Loaded.Contains("Good"), "disable saved without runtime unloading");
        good.Toggle.IsChecked = true;
        later.Toggle.IsChecked = true;
        panel.SaveButton.Command!.Execute(null);
        Check(new PluginSession(installed).ReadEnabled().Contains("Later"), "enable survives fresh session");
        var restarts = 0;
        var restartPanel = new ModsPanel(new PluginSession(installed), () => { }, () => restarts++);
        restartPanel.RestartButton.Command!.Execute(null);
        Check(restarts == 1, "save and restart invokes helper callback after save");
        window.Close();
    }

    private static void NativeTests(string installed, string marker)
    {
        // Only replace MetadataManager's engine-owned registry. Keep actual PluginHost.Add,
        // reflection constructors, child-host ownership, and loader Harmony patch execution.
        var fixturePatches = new Harmony("SE2PluginLoader.Tests.Metadata");
        fixturePatches.Patch(AccessTools.Method(typeof(MetadataManager), "PushContext"), prefix: new HarmonyMethod(typeof(Program), nameof(Push)));
        fixturePatches.Patch(AccessTools.Method(typeof(MetadataManager), "PopContext"), prefix: new HarmonyMethod(typeof(Program), nameof(Pop)));
        try
        {
            var host = new PluginHost(["-noDevPlugins"]);
            using (var loader = new Plugin(host, installed))
            {
                Check(Plugin.Session!.Loaded.SetEquals(["Good", "Later"]), "actual native host loads valid plugins after constructor failure");
                Check(Plugin.Session.StartupStatus["Broken"].Contains("Expected fixture"), "constructor failure visible in menu status");
                Check(File.Exists(marker), "enabled plugin module initializer executes only during loading");
                Check(pushes - pops == 2, "failed constructor context balanced immediately");
                var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(GameMenu), "UpdateButtons"));
                Check(info!.Owners.Contains("SE2PluginLoader.MainMenu"), "native UpdateButtons patched");
            }
            Check(pushes == pops, "all metadata contexts balanced on shutdown");
            Check(File.ReadAllText(marker).Contains("good disposed") && File.ReadAllText(marker).Contains("later disposed"), "successful plugins disposed");
            Check(!Harmony.GetPatchInfo(AccessTools.Method(typeof(GameMenu), "UpdateButtons"))!.Owners.Contains("SE2PluginLoader.MainMenu"), "menu hook removed on shutdown");
            using var safe = new Plugin(new PluginHost(["-noDevPlugins", "-se2pl-safe-mode"]), installed);
            Check(Plugin.Session!.Loaded.Count == 0, "actual native loader safe mode skips plugins");
        }
        finally { fixturePatches.UnpatchAll(fixturePatches.Id); }
    }
    private static bool Push() { pushes++; return false; }
    private static bool Pop() { pops++; return false; }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; }
    private static void WritePlugin(string install, string folder, string id, string type, string[]? deps = null, string assembly = "FixturePlugin.dll")
    {
        var path = Path.Combine(install, "Plugins", folder); Directory.CreateDirectory(path);
        File.Copy(fixture, Path.Combine(path, "FixturePlugin.dll"));
        File.WriteAllText(Path.Combine(path, "plugin.json"), JsonSerializer.Serialize(new
        {
            id, name = id, version = "1.0", description = "Test fixture", assembly,
            entryPoint = "FixturePlugin." + type, gameVersion = Catalog.GameVersion, dependencies = deps ?? []
        }));
    }
}
