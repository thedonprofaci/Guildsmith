using Guildsmith.Core.Errors;

using ModelContextProtocol.Protocol;

namespace Guildsmith.Server.Tools;

/// <summary>Turns a service result into the MCP tool result the model reads.</summary>
public static class ToolResponse
{
    /// <summary>
    /// One text block: <paramref name="toJson"/> of the value on success; on failure the unified
    /// error response of spec section 8, with <c>isError</c> set.
    /// </summary>
    public static CallToolResult From<T>(ToolResult<T> result, Func<T, string> toJson)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(toJson);

        return result.IsSuccess
            ? new CallToolResult { Content = [new TextContentBlock { Text = toJson(result.Value!) }], IsError = false }
            : new CallToolResult { Content = [new TextContentBlock { Text = ToolErrorSerializer.Serialize(result.Error) }], IsError = true };
    }
}
