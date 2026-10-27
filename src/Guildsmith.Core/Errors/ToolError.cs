namespace Guildsmith.Core.Errors;

/// <summary>
/// One error returned to the model instead of a tool result. <see cref="ToolErrorSerializer"/>
/// turns it into the unified error response of spec section 8.
/// </summary>
public sealed class ToolError
{
    /// <summary>An error that carries only its code and its message.</summary>
    public ToolError(ErrorCode code, string message)
        : this(code, message, null, null)
    {
    }

    private ToolError(ErrorCode code, string message, IReadOnlyList<string>? validValues, double? retryAfterSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
        ValidValues = validValues;
        RetryAfterSeconds = retryAfterSeconds;
    }

    /// <summary>The error code.</summary>
    public ErrorCode Code { get; }

    /// <summary>Human-readable text for the model. Never a contract: callers branch on <see cref="Code"/>.</summary>
    public string Message { get; }

    /// <summary>The accepted values of an <see cref="ErrorCode.InvalidArgument"/> error, when it has a closed set.</summary>
    public IReadOnlyList<string>? ValidValues { get; }

    /// <summary>How many seconds a <see cref="ErrorCode.RateLimited"/> caller should wait.</summary>
    public double? RetryAfterSeconds { get; }

    /// <summary>An <see cref="ErrorCode.InvalidArgument"/> error listing the values the argument accepts.</summary>
    public static ToolError InvalidArgument(string message, IEnumerable<string> validValues)
    {
        ArgumentNullException.ThrowIfNull(validValues);
        return new ToolError(ErrorCode.InvalidArgument, message, validValues.ToArray(), null);
    }

    /// <summary>A <see cref="ErrorCode.RateLimited"/> error with the wait in seconds.</summary>
    public static ToolError RateLimited(string message, double retryAfterSeconds)
    {
        // A NaN or infinite wait cannot be written as a JSON number.
        if (!double.IsFinite(retryAfterSeconds) || retryAfterSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAfterSeconds), retryAfterSeconds,
                "The wait must be a finite, non-negative number of seconds.");
        }

        return new ToolError(ErrorCode.RateLimited, message, null, retryAfterSeconds);
    }
}
