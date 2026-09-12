namespace BetterGrouping.Core;

public enum EditKind { Add, Move, Remove, Rename, Delete, Create, Ungroup }

/// <summary>The adapter owns native notifications, registration, and empty-group cleanup.</summary>
public interface IGroupStore<TBlock, TGroup> where TBlock : class where TGroup : class
{
    IEnumerable<TGroup> Roots { get; }
    IEnumerable<TGroup> Children(TGroup group);
    IEnumerable<TBlock> Blocks(TGroup group);
    TGroup? Parent(TGroup group);
    bool IsValid(TBlock block);
    void Add(TBlock block, TGroup group);
    void Pop(TBlock block, TGroup group);
    void Delete(TGroup group);
    void Rename(TGroup group, string name);
    TGroup Create(IReadOnlyList<TBlock> blocks, string name);
}

public sealed record EditResult(int Changed, int Unchanged, int Skipped, string Message);

public static class GroupEditor
{
    public static IEnumerable<TGroup> Walk<TBlock, TGroup>(IGroupStore<TBlock, TGroup> store)
        where TBlock : class where TGroup : class
    {
        var seen = new HashSet<TGroup>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<TGroup>(store.Roots.Reverse());
        while (stack.TryPop(out var group))
        {
            if (!seen.Add(group)) continue;
            yield return group;
            foreach (var child in store.Children(group).Reverse()) stack.Push(child);
        }
    }

    public static EditResult Apply<TBlock, TGroup>(IGroupStore<TBlock, TGroup> store,
        EditKind kind, TGroup? target, IEnumerable<TBlock> selection, string? name = null)
        where TBlock : class where TGroup : class
    {
        var groups = Walk(store).ToArray();
        if (kind is not (EditKind.Create or EditKind.Ungroup) && (target is null || !groups.Contains(target, ReferenceEqualityComparer.Instance)))
            return new(0, 0, 0, "The group no longer exists. Reopen the menu.");
        var blocks = selection.Distinct<TBlock>(ReferenceEqualityComparer.Instance).ToArray();
        int changed = 0, unchanged = 0, skipped = 0;
        if (kind is EditKind.Rename or EditKind.Create)
        {
            name = name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 512 || name.Any(char.IsControl))
                return new(0, 0, blocks.Length, "Use a nonempty group name of at most 512 characters.");
        }
        if (kind == EditKind.Rename)
        {
            store.Rename(target!, name!);
            return new(1, 0, 0, "Group renamed.");
        }
        if (kind == EditKind.Delete)
        {
            store.Delete(target!);
            return new(1, 0, 0, "Group deleted; blocks retained.");
        }
        TGroup? Owner(TBlock b) => Walk(store).FirstOrDefault(g => store.Blocks(g).Contains(b, ReferenceEqualityComparer.Instance));
        if (kind == EditKind.Create)
        {
            var valid = blocks.Where(b => store.IsValid(b) && Owner(b) is null).ToArray();
            // Match the native Control Panel's minimum, without ungrouping existing blocks.
            if (valid.Length < 2) return new(0, 0, blocks.Length, "Select at least two ungrouped blocks on one grid.");
            store.Create(valid, name!);
            return new(valid.Length, 0, blocks.Length - valid.Length, "Group created from ungrouped selection.");
        }
        foreach (var block in blocks)
        {
            if (!store.IsValid(block)) { skipped++; continue; }
            var owner = Owner(block);
            if (kind is EditKind.Add or EditKind.Move)
            {
                if (ReferenceEquals(owner, target)) { unchanged++; continue; }
                if (kind == EditKind.Add && owner is not null) { skipped++; continue; }
                // SE2 permits one direct group membership. Pop through the native hierarchy;
                // stopping at the target prevents deleting/recreating an ancestor destination.
                var visited = new HashSet<TGroup>(ReferenceEqualityComparer.Instance);
                while (owner is not null && !ReferenceEquals(owner, target))
                {
                    if (!visited.Add(owner)) throw new InvalidOperationException("Cyclic group hierarchy.");
                    store.Pop(block, owner);
                    owner = Owner(block);
                }
                if (owner is null) store.Add(block, target!);
                changed++;
            }
            else if (kind is EditKind.Remove or EditKind.Ungroup)
            {
                bool InTarget(TGroup? g)
                {
                    if (kind == EditKind.Ungroup) return g is not null;
                    var visited = new HashSet<TGroup>(ReferenceEqualityComparer.Instance);
                    while (g is not null && visited.Add(g))
                    {
                        if (ReferenceEquals(g, target)) return true;
                        g = store.Parent(g);
                    }
                    return false;
                }
                if (!InTarget(owner)) { unchanged++; continue; }
                var visited = new HashSet<TGroup>(ReferenceEqualityComparer.Instance);
                while (owner is not null && InTarget(owner))
                {
                    if (!visited.Add(owner)) throw new InvalidOperationException("Cyclic group hierarchy.");
                    store.Pop(block, owner);
                    owner = Owner(block);
                }
                changed++;
            }
        }
        return new(changed, unchanged, skipped,
            $"{changed} changed; {unchanged} already satisfied; {skipped} skipped." +
            (kind == EditKind.Add && skipped > 0 ? " Use Move to Group for blocks already in another group." : ""));
    }
}
