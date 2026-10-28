using Guildsmith.Core.Errors;

namespace Guildsmith.Core.Audit;

/// <summary>
/// How an audited tool call ended: <see cref="Success"/>, <see cref="Cancelled"/>, or the error
/// code it was refused or failed with. The only way to make one, so an outcome can never carry a
/// text the caller supplied.
/// </summary>
public sealed class AuditOutcome
{
    private AuditOutcome(string text)
    {
        Text = text;
    }

    /// <summary>The call ran its operation and returned a value.</summary>
    public static AuditOutcome Success { get; } = new("success");

    /// <summary>The caller's token was cancelled; the operation may already have reached Discord.</summary>
    public static AuditOutcome Cancelled { get; } = new("cancelled");

    /// <summary>The text an audit entry carries for this outcome.</summary>
    public string Text { get; }

    /// <summary>The call was refused or failed with <paramref name="code"/>; its text is the code's wire string.</summary>
    public static AuditOutcome Failed(ErrorCode code) => new(code.ToWireString());
}
