using LazyCrafter.Core.Model;

namespace LazyCrafter.Core;

/// <summary>
/// Rebuilds the ingredient tree the UI draws from the flat leaf list a <see cref="Tiering.Assess"/> walk produced
/// (Plan §Phase 4 task 3). The walk emits a chosen sub-craft's leaves immediately <b>before</b> the ingredient they
/// serve, one level deeper (<see cref="IngredientLeaf.Depth"/>), so a single pass with one pending list per depth
/// re-attaches children to their parent. Ingredients whose sub-craft was not walked (on hand, or a cheaper source
/// won) come back as childless nodes.
/// </summary>
public static class IngredientTree
{
    public sealed record Node(IngredientLeaf Leaf, IReadOnlyList<Node> Children);

    public static IReadOnlyList<Node> Build(IReadOnlyList<IngredientLeaf> leaves)
    {
        var pending = new List<List<Node>>();
        List<Node> At(int depth)
        {
            while (pending.Count <= depth) pending.Add(new List<Node>());
            return pending[depth];
        }

        foreach (var leaf in leaves)
        {
            var d = Math.Max(0, leaf.Depth);
            var children = pending.Count > d + 1 && pending[d + 1].Count > 0 ? pending[d + 1].ToArray() : Array.Empty<Node>();
            if (pending.Count > d + 1) pending[d + 1].Clear();
            At(d).Add(new Node(leaf, children));
        }
        return pending.Count == 0 ? Array.Empty<Node>() : pending[0].ToArray();
    }

    /// <summary>Every leaf of the tree in draw order, i.e. parents before their children (the inverse of the walk order).</summary>
    public static IEnumerable<(Node Node, int Depth)> Flatten(IReadOnlyList<Node> roots)
    {
        var stack = new Stack<(Node, int)>();
        for (var i = roots.Count - 1; i >= 0; i--) stack.Push((roots[i], 0));
        while (stack.Count > 0)
        {
            var (n, d) = stack.Pop();
            yield return (n, d);
            for (var i = n.Children.Count - 1; i >= 0; i--) stack.Push((n.Children[i], d + 1));
        }
    }
}
