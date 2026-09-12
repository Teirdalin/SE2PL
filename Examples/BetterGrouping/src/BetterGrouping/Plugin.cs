using System.Reflection;
using Avalonia.Input;
using HarmonyLib;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel.Controls;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel.Views;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.Core.Plugins;

namespace BetterGrouping;

public sealed class Plugin : IPlugin, IDisposable
{
    private readonly Harmony harmony = new("BetterGrouping.SE2");
    private static readonly object LogLock = new();
    public Plugin(PluginHost host)
    {
        try
        {
            var version = typeof(ControlPanelLeftScreen).Assembly.GetName().Version;
            if (version != new Version(2, 4, 0, 95))
                throw new NotSupportedException($"SE2 {version} is not validated. Expected 2.4.0.95; plugin disabled.");
            NativeAccess.Validate();
            Patch(typeof(ControlPanelLeftScreen), "OnLoaded", nameof(Loaded), false);
            harmony.Patch(AccessTools.Constructor(typeof(BlockControlsScreen)), postfix: new HarmonyMethod(typeof(Plugin), nameof(ControlsCreated)));
            Patch(typeof(ControlPanelLeftScreen), "OnItemPointerMoved", nameof(PointerMoved), true);
            harmony.Patch(AccessTools.DeclaredMethod(typeof(DragDrop), "DoDragDrop"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(StartingDrag)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(StartedDrag)));
            Patch(typeof(Session), "Update", nameof(BeforeUpdate), true);
            EditQueue.Enabled = true;
            Log("Better Grouping 0.1.3 loaded for SE2 " + version);
        }
        catch (Exception ex) { harmony.UnpatchAll(harmony.Id); Log(ex.ToString()); }
    }
    private void Patch(Type type, string name, string handler, bool prefix)
    {
        var method = AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);
        var patch = new HarmonyMethod(typeof(Plugin).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static)!);
        harmony.Patch(method, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
    }
    private static void Loaded(ControlPanelLeftScreen __instance) => PanelExtension.Attach(__instance);
    private static void ControlsCreated(BlockControlsScreen __instance)
    {
        try { GroupDropdown.Attach(__instance); } catch (Exception ex) { Log("Group dropdown attachment failed: " + ex); }
    }
    private static bool PointerMoved(ControlPanelLeftScreen __instance, ControlPanelItemPointerEventArgs e)
        => PanelExtension.HandleNativeDrag(__instance, e);
    private static void StartingDrag(PointerEventArgs triggerEvent, IDataObject data, ref DragDropEffects allowedEffects, out object? __state)
        => __state = PanelExtension.EnrichNativeDrag(triggerEvent, data, ref allowedEffects);
    private static void StartedDrag(object? __state, ref Task<DragDropEffects> __result)
    {
        if (__state is not null) __result = PanelExtension.CompleteNativeDrag(__state, __result);
    }
    private static void BeforeUpdate(Session __instance) => EditQueue.Drain(__instance);
    public void Dispose()
    {
        EditQueue.Enabled = false;
        PanelExtension.DetachAll();
        GroupDropdown.DetachAll();
        harmony.UnpatchAll(harmony.Id);
    }
    internal static void Log(string message)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpaceEngineers2", "BetterGrouping");
            Directory.CreateDirectory(folder);
            lock (LogLock) File.AppendAllText(Path.Combine(folder, "BetterGrouping.log"), $"{DateTimeOffset.Now:O} [{Environment.ProcessId}:{System.Diagnostics.Process.GetCurrentProcess().ProcessName}] {message}{Environment.NewLine}");
        }
        catch { /* A log failure must not crash the game. */ }
    }
}
