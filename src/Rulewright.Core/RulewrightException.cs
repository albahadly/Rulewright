using System;

namespace Rulewright.Core;

/// <summary>
/// Base type for all exceptions thrown by Rulewright libraries.
/// </summary>
public class RulewrightException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The error message.</param>
    public RulewrightException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and inner exception.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying cause.</param>
    public RulewrightException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
