using System.Linq.Expressions;
using System.Reflection;
using Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.Multiplayer.Replications.InProcess;

namespace BetterGrouping;

// SE2 uses distinct UI and simulation models even in single player. Its in-process
// PostInvoke synchronizes both scenes, then ReplayContext resolves exact object pairs.
internal static class LocalModelBridge
{
    private static readonly Type ComponentType = typeof(ReplayContext).Assembly.GetType(
        "Keen.VRage.Multiplayer.Replications.InProcess.InProcessReplicationSessionComponent", true)!;
    private static readonly MethodInfo PostInvoke = ComponentType.GetMethod("PostInvoke")!;
    private static readonly Type SceneType = PostInvoke.GetParameters()[0].ParameterType.GenericTypeArguments[0];
    private static object? Component(Session? session) => session?.SessionComponents?.Components
        .FirstOrDefault(c => ComponentType.IsInstanceOfType(c));
    internal static bool Available(PanelView view) => Component(view.Session) is { } component &&
        !(bool)ComponentType.GetProperty("IsServer")!.GetValue(component)! &&
        !(bool)ComponentType.GetProperty("IsUnloading")!.GetValue(component)!;

    internal static void Validate()
    {
        if (SceneType.GetProperty("ReplayContext")?.PropertyType != typeof(ReplayContext) ||
            SceneType.GetField("SessionComponent", BindingFlags.NonPublic | BindingFlags.Instance) is null ||
            ComponentType.GetProperty("Session")?.PropertyType != typeof(Session))
            throw new MissingMemberException("SE2 local scene bridge contract changed.");
    }

    internal static void Resolve(PanelView view, ControlPanelListModel context, ControlPanelGridModel grid,
        ControlPanelGroupModel? group, ControlPanelEntityModel[] blocks,
        Action<Session, ControlPanelListModel, ControlPanelGridModel, ControlPanelGroupModel?, ControlPanelEntityModel[]> resolved,
        Action<string> failed)
    {
        var component = Component(view.Session);
        if (component is null || !Available(view)) { failed("Group editing requires a local game session."); return; }
        void Receive(object scene)
        {
            try
            {
                if (!(bool)SceneType.GetProperty("IsServer")!.GetValue(scene)!) throw new InvalidOperationException("Destination is not the local simulation.");
                var server = SceneType.GetField("SessionComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene)!;
                var session = (Session)ComponentType.GetProperty("Session")!.GetValue(server)!;
                var replay = (ReplayContext)SceneType.GetProperty("ReplayContext")!.GetValue(scene)!;
                var translated = Translate(replay, context, grid, group, blocks);
                resolved(session, translated.Context, translated.Grid, translated.Group, translated.Blocks);
            }
            catch (Exception ex) { Plugin.Log("Local model translation failed: " + ex); failed("The selection or group changed. Select the blocks again."); }
        }
        var parameter = Expression.Parameter(SceneType);
        Action<object> receiver = Receive;
        var callback = Expression.Lambda(PostInvoke.GetParameters()[0].ParameterType,
            Expression.Invoke(Expression.Constant(receiver), Expression.Convert(parameter, typeof(object))), parameter).Compile();
        PostInvoke.Invoke(component, [callback]);
    }

    internal static (ControlPanelListModel Context, ControlPanelGridModel Grid, ControlPanelGroupModel? Group, ControlPanelEntityModel[] Blocks)
        Translate(ReplayContext replay, ControlPanelListModel context, ControlPanelGridModel grid,
            ControlPanelGroupModel? group, ControlPanelEntityModel[] blocks)
    {
        T Map<T>(T value) where T : class => replay.TranslateObject(value) as T ?? throw new InvalidOperationException("Detached model.");
        var serverContext = Map(context);
        var serverGrid = Map(grid);
        var serverGroup = group is null ? null : Map(group);
        var serverBlocks = blocks.Select(Map).Distinct().ToArray();
        if (!NativeAccess.IsServer(serverGrid) || !serverContext.Grids.Contains(serverGrid) ||
            (serverGroup is not null && !serverContext.Groups.Contains(serverGroup)) ||
            serverBlocks.Any(b => !serverContext.Blocks.Contains(b) || !ReferenceEquals(b.GridModel, serverGrid)))
            throw new InvalidOperationException("Translated models no longer belong to this Control Panel/grid.");
        return (serverContext, serverGrid, serverGroup, serverBlocks);
    }
}
