using System.Globalization;

using Guildsmith.Core.Errors;

namespace Guildsmith.Core.Arguments;

/// <summary>
/// Discord snowflake IDs as they cross the tool boundary: a string in, a string out (spec
/// section 6.1), because a snowflake exceeds the integer precision of a JavaScript client.
/// </summary>
public static class Snowflake
{
    /// <summary>
    /// Whether <paramref name="text"/> is a positive decimal <see cref="ulong"/>. Digits only: a
    /// sign, a thousands separator or surrounding whitespace rejects the value rather than being
    /// read as a different ID.
    /// </summary>
    public static bool TryParse(string? text, out ulong id) =>
        ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0;

    /// <summary>The decimal string of <paramref name="id"/>, the form a tool returns it in.</summary>
    public static string Format(ulong id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses the tool argument <paramref name="argumentName"/>, or gives
    /// <see cref="ErrorCode.InvalidArgument"/> when it is not a snowflake.
    /// </summary>
    public static ToolResult<ulong> ParseArgument(string? value, string argumentName)
    {
        if (TryParse(value, out ulong id))
        {
            return ToolResult<ulong>.Success(id);
        }

        return ToolResult<ulong>.Failure(new ToolError(ErrorCode.InvalidArgument,
            $"{argumentName} must be a Discord ID: a positive whole number written as a string of digits."));
    }
}
