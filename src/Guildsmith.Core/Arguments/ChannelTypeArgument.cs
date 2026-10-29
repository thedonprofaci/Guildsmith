using Guildsmith.Core.Errors;
using Guildsmith.Core.Models;

namespace Guildsmith.Core.Arguments;

/// <summary>The <c>type</c> argument of <c>list_channels</c> and <c>create_channel</c> (spec section 6.4).</summary>
public static class ChannelTypeArgument
{
    /// <summary>The argument's name as a tool declares it.</summary>
    public const string Name = "type";

    /// <summary>The wire name of every <see cref="ChannelKind"/>, in the order spec section 6.4 lists them.</summary>
    public static IReadOnlyList<string> ValidValues { get; } = ["text", "voice", "category", "announcement", "forum"];

    /// <summary>
    /// The channel type <paramref name="value"/> names, matched exactly (lower case, no whitespace), or
    /// <see cref="ErrorCode.InvalidArgument"/> listing the five valid values.
    /// </summary>
    public static ToolResult<ChannelKind> Parse(string? value) => value switch
    {
        "text" => ToolResult<ChannelKind>.Success(ChannelKind.Text),
        "voice" => ToolResult<ChannelKind>.Success(ChannelKind.Voice),
        "category" => ToolResult<ChannelKind>.Success(ChannelKind.Category),
        "announcement" => ToolResult<ChannelKind>.Success(ChannelKind.Announcement),
        "forum" => ToolResult<ChannelKind>.Success(ChannelKind.Forum),
        _ => ToolResult<ChannelKind>.Failure(ToolError.InvalidArgument($"{Name} must be one of the channel types listed.", ValidValues)),
    };

    /// <summary>The wire name of <paramref name="kind"/>, the form a tool returns it in.</summary>
    public static string Format(ChannelKind kind) => kind switch
    {
        ChannelKind.Text => "text",
        ChannelKind.Voice => "voice",
        ChannelKind.Category => "category",
        ChannelKind.Announcement => "announcement",
        ChannelKind.Forum => "forum",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a defined channel kind."),
    };
}
