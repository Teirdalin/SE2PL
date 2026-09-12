using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using HarmonyLib;
using Keen.Game2.Client.UI.Menu;
using Keen.Game2.Client.UI.Library;
using Keen.VRage.Core;
using Keen.VRage.DCS.Components;
using Keen.VRage.Library.Utils;
using Keen.VRage.UI.Shared.Helpers;

namespace SE2PluginLoader;

internal static class MenuIntegration
{
    internal const string ButtonName = "SE2PluginLoaderMods";
    private static readonly FieldInfo Buttons = AccessTools.Field(typeof(GameMenu), "_buttonsPanel");
    private static readonly Type AppType = Assembly.Load("SpaceEngineers2").GetType("Keen.Game2.GameAppComponent", true)!;
    private static readonly MethodInfo GetSharedUi = AccessTools.DeclaredMethod(AppType, "GetSharedUI");
    private static readonly MethodInfo GetApp = typeof(Entity).GetMethods().Single(m => m.Name == "Get" && m.IsGenericMethodDefinition).MakeGenericMethod(AppType);
    private static bool screenOpen;
    internal static void Validate()
    {
        if (Buttons?.FieldType != typeof(StackPanel) || GetSharedUi?.ReturnType != typeof(SharedUIComponent) || AccessTools.DeclaredMethod(typeof(GameMenu), "UpdateButtons") is null)
            throw new MissingMemberException("SE2 GameMenu contract changed.");
    }

    internal static bool IsMainMenu(object? context) => context?.GetType().FullName == "Keen.Game2.Client.UI.Menu.MainMenu.MainMenuScreenViewModel";

    internal static void AfterUpdateButtons(GameMenu __instance)
    {
        try
        {
            if (!IsMainMenu(__instance.DataContext) || Buttons.GetValue(__instance) is not StackPanel panel) return;
            InsertButton(panel, Open);
        }
        catch (Exception ex) { Plugin.Log("Mods menu attachment failed: " + ex); }
    }

    internal static void InsertButton(StackPanel panel, Action open)
    {
        if (panel.Children.Any(c => c.Name == ButtonName)) return;
        // Match the native GameMenu.CreateButton style without inventing a localization key.
        var button = new Button { Name = ButtonName, Content = "Mods", Classes = { "Menu" }, Command = SimpleCommand.Create(open) };
        var settings = panel.Children.FirstOrDefault(c => c.Name == "MenuSettings");
        panel.Children.Insert(settings is null ? Math.Max(0, panel.Children.Count - 1) : panel.Children.IndexOf(settings), button);
        Plugin.Log("Mods button attached to main menu.");
    }

    private static void Open()
    {
        try
        {
            if (Plugin.Session is null || screenOpen) return;
            Plugin.Session.Refresh();
            var app = GetApp.Invoke(Singleton<VRageCore>.Instance.Engine, [default(StringId)]);
            var ui = (SharedUIComponent?)GetSharedUi.Invoke(app, null)
                ?? throw new InvalidOperationException("Main menu UI is not available.");
            var handle = ui.CreateScreen<ModsScreen>(new ModsViewModel(), showCursor: true);
            screenOpen = true;
            handle.OnDisposed += _ => screenOpen = false;
            Plugin.Log("Mods screen opened.");
        }
        catch (Exception ex) { Plugin.Log("Cannot open Mods: " + ex); }
    }

    internal static void Restart()
    {
        var exe = Path.GetFullPath(Path.Combine(Plugin.Root, "..", "SE2PL.exe"));
        if (!File.Exists(exe)) throw new FileNotFoundException("Launcher missing. Save your changes, then exit and relaunch SE2.", exe);
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Plugin.Root };
        start.ArgumentList.Add("--restart-after-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        start.ArgumentList.Add("--started-at");
        start.ArgumentList.Add(Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks.ToString());
        _ = Process.Start(start) ?? throw new InvalidOperationException("Could not start restart helper.");
        Singleton<VRageCore>.Instance.Exit();
    }
}
