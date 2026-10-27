namespace Guildsmith.Core.Errors;

/// <summary>
/// One error returned to the model instead of a tool result. <see cref="ToolErrorSerializer"/>
/// turns it into the unified error response of spec section 8.
/// </summary>
public sealed class ToolError
{
    /// <summary>The fixed message of every <see cref="ErrorCode.InternalError"/> error.</summary>
    public const string InternalErrorMessage =
        "The tool call failed with an unexpected error. The server log holds the detail under correlation_id.";

    /// <summary>
    /// An error that carries only its code and its message. Not for
    /// <see cref="ErrorCode.InternalError"/>, which always carries a correlation id: use <see cref="Internal"/>.
    /// </summary>
    public ToolError(ErrorCode code, string message)
        : this(code, message, null, null, null)
    {
        if (code == ErrorCode.InternalError)
        {
            throw new ArgumentException("An internal_error is built with ToolError.Internal.", nameof(code));
        }
    }

    private ToolError(ErrorCode code, string message, IReadOnlyList<string>? validValues, double? retryAfterSeconds, string? correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
        ValidValues = validValues;
        RetryAfterSeconds = retryAfterSeconds;
        CorrelationId = correlationId;
    }

    /// <summary>The error code.</summary>
    public ErrorCode Code { get; }

    /// <summary>Human-readable text for the model. Never a contract: callers branch on <see cref="Code"/>.</summary>
    public string Message { get; }

    /// <summary>The accepted values of an <see cref="ErrorCode.InvalidArgument"/> error, when it has a closed set.</summary>
    public IReadOnlyList<string>? ValidValues { get; }

    /// <summary>How many seconds a <see cref="ErrorCode.RateLimited"/> caller should wait.</summary>
    public double? RetryAfterSeconds { get; }

    /// <summary>The id an <see cref="ErrorCode.InternalError"/> shares with its log line; <see langword="null"/> on every other code.</summary>
    public string? CorrelationId { get; }

    /// <summary>An <see cref="ErrorCode.InvalidArgument"/> error listing the values the argument accepts.</summary>
    public static ToolError InvalidArgument(string message, IEnumerable<string> validValues)
    {
        return new ToolError(ErrorCode.InvalidArgument, message, validValues.ToArray(), null, null);
    }

    /// <summary>A <see cref="ErrorCode.RateLimited"/> error with the wait in seconds.</summary>
    public static ToolError RateLimited(string message, double retryAfterSeconds)
    {
        if (!double.IsFinite(retryAfterSeconds) || retryAfterSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryAfterSeconds), retryAfterSeconds,
                "The wait must be a finite, non-negative number of seconds.");
        }

        return new ToolError(ErrorCode.RateLimited, message, null, retryAfterSeconds, null);
    }

    /// <summary>
    /// An <see cref="ErrorCode.InternalError"/> error. Its message is always <see cref="InternalErrorMessage"/>:
    /// the exception's own text goes to the log, never to the model.
    /// </summary>
    public static ToolError Internal(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return new ToolError(ErrorCode.InternalError, InternalErrorMessage, null, null, correlationId);
    }
}
