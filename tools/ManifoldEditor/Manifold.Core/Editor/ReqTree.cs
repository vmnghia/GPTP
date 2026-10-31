using System.Globalization;

namespace Manifold.Core.Editor;

/// <summary>A node of the Requirements page's tree: a group (no id) with children, or an id's leaf.</summary>
public sealed record ReqTreeNode(string Title, int? Id, IReadOnlyList<ReqTreeNode> Children);

/// <summary>
/// The Requirements page's tree (requirements UX spec §2): FireGraft's groups, the editor's
/// titles. Groups in the order an id first reaches them, leaves in id order, ids the list
/// lacks under "Unused".
/// </summary>
public static class ReqTree
{
    public const string Unused = "Unused";

    sealed class Group(string title)
    {
        public string Title { get; } = title;
        public List<Group> Groups { get; } = new();
        public List<object> Items { get; } = new();   // Group or leaf id, in order of appearance

        public Group Child(string title)
        {
            var found = Groups.FirstOrDefault(g => g.Title == title);
            if (found is not null) return found;
            var made = new Group(title);
            Groups.Add(made);
            Items.Add(made);
            return made;
        }
    }

    /// <param name="groups">FireGraft's list (<see cref="Data.FireGraftList.Parse"/>); null for the flat ID view.</param>
    /// <param name="query">A number keeps that id's leaf; other text keeps leaves whose title contains it (any case); empty keeps all.</param>
    public static IReadOnlyList<ReqTreeNode> Build(int idCount, IReadOnlyDictionary<int, IReadOnlyList<string>>? groups,
        Func<int, string> leafTitle, string query)
    {
        query = query.Trim();
        bool numeric = int.TryParse(query, NumberStyles.None, CultureInfo.InvariantCulture, out int queryId);
        // As the set list's search: a number matches that id, anything else the titles.
        bool Keep(int id) => query.Length == 0 || (numeric ? id == queryId : leafTitle(id).Contains(query, StringComparison.OrdinalIgnoreCase));

        var root = new Group("");
        var unlisted = new List<int>();
        for (int id = 0; id < idCount; id++)
        {
            if (groups is null) { root.Items.Add(id); continue; }
            if (!groups.TryGetValue(id, out var path)) { unlisted.Add(id); continue; }
            var group = root;
            foreach (var title in path) group = group.Child(title);
            group.Items.Add(id);
        }
        if (unlisted.Count > 0)
        {
            var unused = root.Child(Unused);
            foreach (int id in unlisted) unused.Items.Add(id);
        }
        return Nodes(root, Keep, leafTitle);
    }

    static IReadOnlyList<ReqTreeNode> Nodes(Group group, Func<int, bool> keep, Func<int, string> leafTitle)
    {
        var nodes = new List<ReqTreeNode>();
        foreach (var item in group.Items)
        {
            if (item is Group child)
            {
                var children = Nodes(child, keep, leafTitle);
                if (children.Count > 0) nodes.Add(new ReqTreeNode(child.Title, null, children));
            }
            else if (item is int id && keep(id))
                nodes.Add(new ReqTreeNode(leafTitle(id), id, []));
        }
        // Leaves in id order (the unlisted ids join Unused after its listed ones); groups keep their places.
        var leaves = new Queue<ReqTreeNode>(nodes.Where(n => n.Id is not null).OrderBy(n => n.Id));
        return nodes.Select(n => n.Id is null ? n : leaves.Dequeue()).ToArray();
    }
}
