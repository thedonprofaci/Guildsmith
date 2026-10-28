using Guildsmith.Core.Errors;

namespace Guildsmith.Core.Gateway;

/// <summary>
/// A Discord failure the gateway has already mapped to an error code of spec section 8. Core does
/// not reference Discord.Net (spec section 4), so the mapping happens in the gateway and crosses
/// into Core as this exception; <see cref="Guards.GuardPipeline"/> returns its <see cref="Error"/>
/// to the model instead of <c>internal_error</c>.
/// </summary>
public sealed class DiscordFailureException : Exception
{
    /// <summary>
    /// A failure carrying <paramref name="error"/>, with the Discord.Net exception it was mapped
    /// from as <paramref name="innerException"/>. An <see cref="ErrorCode.InternalError"/> error is
    /// rejected: that code belongs to the pipeline's own boundary, which logs it under a correlation id.
    /// </summary>
    public DiscordFailureException(ToolError error, Exception? innerException)
        : base(error?.Message, innerException)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (error.Code == ErrorCode.InternalError)
        {
            throw new ArgumentException("An internal_error is not a mapped Discord failure.", nameof(error));
        }

        Error = error;
    }

    /// <summary>The error the model receives.</summary>
    public ToolError Error { get; }
}
