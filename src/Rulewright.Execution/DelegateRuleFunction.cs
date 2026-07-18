using System;
using Rulewright.Core;

namespace Rulewright.Execution;

/// <summary>
/// Wraps a delegate registered via <see cref="RulewrightBuilder.RegisterFunction(string, Func{object?, object?, bool})"/>
/// as an <see cref="IRuleFunction"/>.
/// </summary>
internal sealed class DelegateRuleFunction : IRuleFunction
{
    private readonly Func<object?, object?, bool> _function;

    internal DelegateRuleFunction(string name, Func<object?, object?, bool> function)
    {
        Name = name;
        _function = function;
    }

    public string Name { get; }

    public bool Evaluate(object? fieldValue, object? value) => _function(fieldValue, value);
}
