using System.Text.Json;
using BetterGrouping.Core;

internal sealed class Block(string name) { public string Name = name; public bool Valid = true; }
internal sealed class Group(string name)
{
    public string Name = name;
    public Group? Parent;
    public List<Group> Children = [];
    public List<Block> Blocks = [];
}
internal sealed class Store : IGroupStore<Block, Group>
{
    public List<Group> RootList = [];
    public IEnumerable<Group> Roots => RootList;
    public IEnumerable<Group> Children(Group g) => g.Children;
    public IEnumerable<Block> Blocks(Group g) => g.Blocks;
    public Group? Parent(Group g) => g.Parent;
    public bool IsValid(Block b) => b.Valid;
    public void Add(Block b, Group g) => g.Blocks.Add(b);
    public void Pop(Block b, Group g)
    {
        if (!g.Blocks.Remove(b)) return;
        g.Parent?.Blocks.Add(b);
        if (g.Blocks.Count == 0 && g.Children.Count == 0) Delete(g);
    }
    public void Delete(Group g)
    {
        (g.Parent?.Children ?? RootList).Remove(g);
        if (g.Parent is { } p && p.Blocks.Count == 0 && p.Children.Count == 0) Delete(p);
    }
    public void Rename(Group g, string name) => g.Name = name;
    public Group Create(IReadOnlyList<Block> blocks, string name)
    { var g = new Group(name); g.Blocks.AddRange(blocks); RootList.Add(g); return g; }
}

internal static class PolicyTests
{
    public static void Run()
    {
        var s = new Store();
        var a = new Block("a"); var b = new Block("b"); var c = new Block("c"); var d = new Block("d");
        var g = s.Create([a], "Thrusters"); var other = s.Create([d], "Other");
        EditResult Edit(EditKind kind, Group target, params Block[] blocks) => GroupEditor.Apply(s, kind, target, blocks);
        Check.That(Edit(EditKind.Add, g, b).Changed == 1 && g.Blocks.SequenceEqual([a, b]), "add one preserves members");
        Check.That(Edit(EditKind.Add, g, b, c, c).Changed == 1 && g.Blocks.Count == 3, "add many and deduplicate input");
        Check.That(Edit(EditKind.Add, g, c).Changed == 0 && g.Blocks.Count == 3, "already grouped is idempotent");
        Check.That(other.Blocks.SequenceEqual([d]) && g.Name == "Thrusters", "unrelated group and name preserved");
        Check.Pass("add one, add several, duplicates, unrelated groups");
        Check.That(Edit(EditKind.Remove, g, b).Changed == 1 && g.Blocks.SequenceEqual([a, c]), "remove one");
        Check.That(Edit(EditKind.Remove, g, a, c).Changed == 2 && !s.RootList.Contains(g), "remove many cleans empty group");
        Check.That(Edit(EditKind.Add, g, b).Changed == 0, "deleted destination rejected");
        Check.Pass("remove one, remove several, empty and stale groups");
        g = s.Create([a], "Thrusters");
        Check.That(Edit(EditKind.Add, g, d).Skipped == 1 && other.Blocks.Contains(d), "safe add skips other membership");
        Check.That(Edit(EditKind.Move, g, d).Changed == 1 && !s.RootList.Contains(other), "explicit move obeys one membership");
        Check.That(Edit(EditKind.Move, g, b, c, c).Changed == 2 && g.Blocks.Count == 4, "multi-drag edit payload semantics");
        Check.Pass("single and multiple move payloads; one-membership restriction");
        var child = new Group("Child") { Parent = g, Blocks = [b] };
        g.Blocks.Remove(b); g.Children.Add(child);
        Check.That(Edit(EditKind.Move, g, b).Changed == 1 && s.RootList.Contains(g) && !g.Children.Contains(child), "move child to parent preserves destination");
        child = new Group("Child") { Parent = g, Blocks = [b] }; g.Blocks.Remove(b); g.Children.Add(child);
        Check.That(Edit(EditKind.Remove, child, b).Changed == 1 && g.Blocks.Contains(b), "remove subgroup member returns to parent");
        child = new Group("Child") { Parent = g, Blocks = [b] }; g.Blocks.Remove(b); g.Children.Add(child);
        Check.That(Edit(EditKind.Remove, g, b).Changed == 1 && !g.Blocks.Contains(b), "remove parent includes nested membership");
        Check.Pass("nested groups, parent transfers, subtree removal");
        var clearStore = new Store();
        var anchor = new Block("keep");
        var parent = clearStore.Create([anchor], "Parent");
        var nestedClear = new Group("Nested") { Parent = parent, Blocks = [a] };
        parent.Children.Add(nestedClear);
        var another = clearStore.Create([c], "Another");
        var cleared = GroupEditor.Apply(clearStore, EditKind.Ungroup, null, new[] { a, c, c, b });
        Check.That(cleared.Changed == 2 && cleared.Unchanged == 1 && parent.Blocks.SequenceEqual([anchor]) && parent.Children.Count == 0,
            "None removes nested and mixed memberships completely while preserving other members");
        Check.That(!clearStore.RootList.Contains(another) && GroupEditor.Apply(clearStore, EditKind.Ungroup, null, new[] { a, c }).Changed == 0,
            "None removes empty groups and repeated use is idempotent");
        b.Valid = false;
        Check.That(Edit(EditKind.Add, g, b).Skipped == 1, "deleted/detached/foreign-grid block ignored");
        b.Valid = true;
        var stale = new Group("split destination");
        Check.That(Edit(EditKind.Move, stale, a).Changed == 0 && g.Blocks.Contains(a), "missing destination after split does not remove source");
        Check.That(GroupEditor.Apply(s, EditKind.Rename, g, [], " ").Changed == 0, "blank name rejected");
        GroupEditor.Apply(s, EditKind.Rename, g, [], "Main Thrusters");
        Check.That(g.Name == "Main Thrusters", "rename in place");
        Check.Pass("invalid selection policy, stale topology, rename");
        var rng = new Random(7409);
        var blocks = Enumerable.Range(0, 24).Select(i => new Block(i.ToString())).ToArray();
        var stress = new Store();
        var left = stress.Create([new Block("anchorL")], "Left");
        var right = stress.Create([new Block("anchorR")], "Right");
        for (int i = 0; i < 10000; i++)
        {
            var target = rng.Next(2) == 0 ? left : right;
            var selected = new[] { blocks[rng.Next(blocks.Length)], blocks[rng.Next(blocks.Length)] };
            GroupEditor.Apply(stress, (EditKind)rng.Next(3), target, selected);
            var all = GroupEditor.Walk(stress).SelectMany(x => x.Blocks).ToArray();
            Check.That(all.Length == all.Distinct().Count(), "stress invariant: exactly one direct membership");
            Check.That(stress.RootList.Count == 2, "stress anchors preserved");
        }
        Check.Pass("10,000 repeated mixed edits without duplicate membership or anchor loss");
        // Only a policy fixture round-trip; not a claim of an SE2 world save/load.
        var before = stress.RootList.Select(x => new Saved(x.Name, x.Blocks.Select(b => b.Name).ToArray())).ToArray();
        var json = JsonSerializer.Serialize(before);
        Check.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<Saved[]>(json)) == json, "policy fixture roundtrip");
        Check.Pass("policy fixture JSON roundtrip (not an SE2 save/load test)");
    }
    private sealed record Saved(string Name, string[] Blocks);
}
