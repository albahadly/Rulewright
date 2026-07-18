using System.Collections.Generic;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// Materializes a <see cref="ConditionTraceNode"/> tree from the per-node pass/fail
/// results recorded during a traced evaluation. Nodes never reached because of
/// short-circuiting keep a null result slot and report <c>Passed == null</c>.
/// </summary>
internal static class ConditionTraceBuilder
{
    internal static ConditionTraceNode Build(
        ConditionNode node,
        Dictionary<ConditionNode, int> nodeIndex,
        bool?[] results)
    {
        ConditionTraceNode[]? children = null;
        if (node is ConditionGroup group)
        {
            children = new ConditionTraceNode[group.Children.Count];
            for (int i = 0; i < group.Children.Count; i++)
            {
                children[i] = Build(group.Children[i], nodeIndex, results);
            }
        }

        return new ConditionTraceNode(ConditionDescriber.Describe(node), results[nodeIndex[node]], children);
    }
}
