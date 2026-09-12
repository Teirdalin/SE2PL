using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel.Views;
using Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.WorldObjects.CubeGrids;
using Keen.VRage.DCS.Components;

namespace BetterGrouping;

// These SE2 types are internal. Use exact, validated members instead of shipping
// modified/publicized game assemblies or pretending there is a public mod API.
internal sealed class PanelView
{
    private static readonly ConditionalWeakTable<object, PanelView> Views = new();
    private static readonly Type Type = typeof(ControlPanelLeftScreen).Assembly.GetType(
        "Keen.Game2.Client.UI.TerminalScreen.ControlPanel.ControlPanelViewModel", true)!;
    private readonly object instance;
    private PanelView(object instance) => this.instance = instance;
    internal static void Validate()
    {
        foreach (var name in new[] { "ControlPanelModel", "BlockTerminalSelection", "Blocks" })
            if (Type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is null)
                throw new MissingMemberException(Type.FullName, name);
        if (Type.GetMethod("UpdateSelection") is null) throw new MissingMethodException(Type.FullName, "UpdateSelection");
    }
    public static PanelView? From(object? value) => value is not null && Type.IsInstanceOfType(value)
        ? Views.GetValue(value, v => new(v)) : null;
    private object? Get(string name) => Type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance);
    public ControlPanelListModel? ControlPanelModel => (ControlPanelListModel?)Get("ControlPanelModel");
    public Keen.VRage.Core.Game.Systems.Session? Session => (Keen.VRage.Core.Game.Systems.Session?)Type.GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance);
    public IList<object> BlockTerminalSelection => (IList<object>)Get("BlockTerminalSelection")!;
    public IEnumerable<ControlPanelEntityViewModel> Blocks => (IEnumerable<ControlPanelEntityViewModel>)Get("Blocks")!;
    public void UpdateSelection(object sender, SelectionChangedEventArgs args)
        => Type.GetMethod("UpdateSelection")!.Invoke(instance, [sender, args]);
}

internal static class NativeAccess
{
    private static readonly Assembly Simulation = typeof(ControlPanelGridModel).Assembly;
    private static readonly Type GridServer = Simulation.GetType("Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel.ControlPanelGridModelServer", true)!;
    private static readonly Type BlockServer = Simulation.GetType("Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel.ControlPanelEntityModelServer", true)!;
    internal static void Validate()
    {
        PanelView.Validate();
        LocalModelBridge.Validate();
        if (Simulation.GetName().Version != new Version(2, 4, 0, 95)) throw new NotSupportedException("Unvalidated simulation assembly.");
        if (GridServer.GetProperty("Grid")?.PropertyType != typeof(CubeGridComponent) ||
            BlockServer.GetProperty("Entity")?.PropertyType != typeof(Entity))
            throw new MissingMemberException("SE2 server model contract changed.");
    }
    internal static bool IsServer(ControlPanelGridModel model) => GridServer.IsInstanceOfType(model);
    internal static Entity GridEntity(ControlPanelGridModel model)
        => ((CubeGridComponent)GridServer.GetProperty("Grid")!.GetValue(model)!).Entity;
    internal static Entity? BlockEntity(ControlPanelEntityModel model)
        => BlockServer.IsInstanceOfType(model) ? (Entity?)BlockServer.GetProperty("Entity")!.GetValue(model) : null;
}
