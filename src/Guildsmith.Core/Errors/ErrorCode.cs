namespace Guildsmith.Core.Errors;

/// <summary>
/// The error codes of the unified error response: the eight of spec section 8, plus
/// <see cref="InternalError"/> for an unexpected failure (issue #3).
/// </summary>
public enum ErrorCode
{
    ForbiddenGuild,
    ReadOnlyMode,
    DestructiveDisabled,
    NotFound,
    MissingPermission,
    RoleHierarchy,
    RateLimited,
    InvalidArgument,
    InternalError,
}

/// <summary>The wire strings of <see cref="ErrorCode"/>: the only place they are spelled.</summary>
public static class ErrorCodeExtensions
{
    /// <summary>The string the <c>error</c> property carries for <paramref name="code"/>.</summary>
    public static string ToWireString(this ErrorCode code) => code switch
    {
        ErrorCode.ForbiddenGuild => "forbidden_guild",
        ErrorCode.ReadOnlyMode => "read_only_mode",
        ErrorCode.DestructiveDisabled => "destructive_disabled",
        ErrorCode.NotFound => "not_found",
        ErrorCode.MissingPermission => "missing_permission",
        ErrorCode.RoleHierarchy => "role_hierarchy",
        ErrorCode.RateLimited => "rate_limited",
        ErrorCode.InvalidArgument => "invalid_argument",
        ErrorCode.InternalError => "internal_error",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Not a defined error code."),
    };
}
