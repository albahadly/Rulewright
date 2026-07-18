namespace Rulewright.Core;

/// <summary>
/// Per-evaluation options.
/// </summary>
public sealed class EvaluationOptions
{
    /// <summary>The default options: tracing off, evaluate all rules.</summary>
    public static EvaluationOptions Default { get; } = new EvaluationOptions();

    /// <summary>
    /// When true, the result carries an <see cref="EvaluationTrace"/> recording which
    /// rules fired and which condition nodes passed or failed. Off by default; the
    /// untraced fast path has no tracing overhead.
    /// </summary>
    public bool EnableTrace { get; set; }

    /// <summary>
    /// When true, evaluation stops after the first rule whose condition passes;
    /// remaining rules are reported as skipped in the trace.
    /// </summary>
    public bool StopOnFirstMatch { get; set; }
}
