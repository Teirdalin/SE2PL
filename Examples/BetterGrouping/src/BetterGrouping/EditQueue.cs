using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using BetterGrouping.Core;
using Keen.Game2.Client.UI.TerminalScreen.ControlPanel;
using Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.VRage.Core.Game.Components;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;

namespace BetterGrouping;

internal static class EditQueue
{
    private sealed record Request(PanelView View, ControlPanelListModel UiContext, ControlPanelListModel Context,
        ControlPanelGridModel Grid, ControlPanelGroupModel? Group,
        ControlPanelEntityModel[] Blocks, EditKind Kind, string? Name, Action<string> Report, long Created, CancellationToken Lifetime);
    private static readonly ConditionalWeakTable<Session, ConcurrentQueue<Request>> Queues = new();
    internal static bool Enabled { get; set; }

    internal static bool IsLocal(ControlPanelGridModel grid) => NativeAccess.IsServer(grid);
    internal static bool CanEdit(PanelView view, ControlPanelGridModel grid) => IsLocal(grid) || LocalModelBridge.Available(view);

    internal static void Submit(PanelView view, ControlPanelGridModel grid,
        ControlPanelGroupModel? group, ControlPanelEntityModel[] blocks,
        EditKind kind, string? name, Action<string> report, CancellationToken lifetime)
    {
        if (!Enabled || !CanEdit(view, grid) || view.ControlPanelModel is not { } context)
        { report("Editing requires an attached local simulation. Remote client models are unsupported."); return; }
        var created = Environment.TickCount64;
        void Feedback(string message) => Dispatcher.UIThread.Post(() => { if (!lifetime.IsCancellationRequested) report(message); });
        void Enqueue(Session session, ControlPanelListModel serverContext, ControlPanelGridModel serverGrid,
            ControlPanelGroupModel? serverGroup, ControlPanelEntityModel[] serverBlocks)
        {
            if (lifetime.IsCancellationRequested || !Enabled) return;
            var queue = Queues.GetOrCreateValue(session);
            if (queue.Count >= 32) { Feedback("Too many pending edits. Wait for the simulation."); return; }
            queue.Enqueue(new(view, context, serverContext, serverGrid, serverGroup, serverBlocks, kind, name, report, created, lifetime));
        }
        if (IsLocal(grid)) Enqueue(EntityMarshal.GetSessionUnsafe(NativeAccess.GridEntity(grid)), context, grid, group, blocks);
        else LocalModelBridge.Resolve(view, context, grid, group, blocks, Enqueue, Feedback);
    }

    // Called by the Session.Update prefix, before Scene.Tick starts its jobs.
    internal static void Drain(Session session)
    {
        if (!Enabled || !Queues.TryGetValue(session, out var queue)) return;
        int budget = 32;
        while (budget-- > 0 && queue.TryDequeue(out var request))
        {
            string message;
            try
            {
                var grid = NativeAccess.GridEntity(request.Grid);
                if (request.Lifetime.IsCancellationRequested || Environment.TickCount64 - request.Created > 15000 ||
                    !ReferenceEquals(request.View.ControlPanelModel, request.UiContext) ||
                    !request.Context.Grids.Contains(request.Grid) || !session.IsEntityInScene(grid) ||
                    !grid.Scene.IsEntityAlive(grid.DEntity))
                    message = "Edit canceled: the Control Panel or grid changed. Select the blocks again.";
                else
                {
                    var store = new NativeGroupStore(session, grid, grid.Get<CubeGridGroupsComponent>(), request.Group);
                    var validModels = request.Blocks.Where(b => request.Context.Blocks.Contains(b) &&
                        ReferenceEquals(b.GridModel, request.Grid)).Select(NativeAccess.BlockEntity).OfType<Entity>().ToArray();
                    var result = GroupEditor.Apply(store, request.Kind, request.Group?.Group,
                        validModels, request.Name);
                    message = result.Message;
                    if (validModels.Length != request.Blocks.Length)
                        message += $" {request.Blocks.Length - validModels.Length} stale or foreign-grid selections ignored.";
                }
                Plugin.Log(message);
            }
            catch (Exception ex)
            {
                Plugin.Log(ex.ToString());
                message = "Edit interrupted. Reopen the Control Panel and inspect membership; see BetterGrouping.log.";
            }
            try
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (request.Lifetime.IsCancellationRequested) return;
                    try { request.Report(message); } catch (Exception ex) { Plugin.Log("Feedback failed: " + ex); }
                });
            }
            catch (Exception ex) { Plugin.Log("Feedback dispatch failed: " + ex); }
        }
    }
}
