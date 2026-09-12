using BetterGrouping.Core;
using Keen.Game2.Simulation.WorldObjects.CubeBlocks;
using Keen.Game2.Simulation.StreamedUI.Terminal.ControlPanel;
using Keen.VRage.Core.Game.Systems;
using Keen.VRage.DCS.Components;

namespace BetterGrouping;

internal sealed class NativeGroupStore(Session session, Entity grid, CubeGridGroupsComponent groups, ControlPanelGroupModel? targetModel)
    : IGroupStore<Entity, GridGroup>
{
    public IEnumerable<GridGroup> Roots => groups.RootGroups;
    public IEnumerable<GridGroup> Children(GridGroup group) => group.SubGroups;
    public IEnumerable<Entity> Blocks(GridGroup group) => group.Blocks;
    public GridGroup? Parent(GridGroup group) => group.ParentGroup;
    public bool IsValid(Entity block) => session.IsEntityInScene(block) && block.Scene.IsEntityAlive(block.DEntity)
        && block.TryGet<CubeBlockComponent>()?.Grid?.Entity == grid;
    public void Add(Entity block, GridGroup group) => groups.AddToGroup(block, group);
    public void Pop(Entity block, GridGroup group) => groups.MoveToParentOrPop(block, group);
    public void Delete(GridGroup group) => groups.RemoveGroup(group);
    public void Rename(GridGroup group, string name)
    {
        if (targetModel is null || !ReferenceEquals(targetModel.Group, group))
            throw new InvalidOperationException("The group name model is no longer attached.");
        // The server model's change handler forwards to SetDisplayName. Calling the
        // component first would let the model's reverse binding restore the old name.
        targetModel.DisplayName = name;
    }
    public GridGroup Create(IReadOnlyList<Entity> blocks, string name)
    {
        var group = new GridGroup { DisplayName = name, Blocks = blocks.ToList() };
        groups.AddNewGroup(group);
        return group;
    }
}
