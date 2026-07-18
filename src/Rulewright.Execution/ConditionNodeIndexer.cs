using System.Collections.Generic;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// Assigns every node in a condition tree a stable pre-order index. The compiled
/// traced delegate, the interpreter, and the trace builder all use the same indexing,
/// so per-node pass/fail results line up across execution modes.
/// </summary>
internal static class ConditionNodeIndexer
{
    internal static Dictionary<ConditionNode, int> BuildIndexMap(ConditionNode root, out int nodeCount)
    {
        var map = new Dictionary<ConditionNode, int>();
        Visit(root, map);
        nodeCount = map.Count;
        return map;
    }

    private static void Visit(ConditionNode node, Dictionary<ConditionNode, int> map)
    {
        map.Add(node, map.Count);
        if (node is ConditionGroup group)
        {
            foreach (ConditionNode child in group.Children)
            {
                Visit(child, map);
            }
        }
    }
}
