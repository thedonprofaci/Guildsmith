using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Guildsmith.Core.Errors;

/// <summary>
/// Writes a <see cref="ToolError"/> as <c>{ "error": "&lt;code&gt;", "message": "&lt;text&gt;" }</c>
/// (spec section 8), plus <c>valid_values</c> and <c>retry_after</c> only when the error carries them.
/// </summary>
public static class ToolErrorSerializer
{
    /// <summary>The JSON text of <paramref name="error"/>.</summary>
    public static string Serialize(ToolError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("error", error.Code.ToWireString());
            writer.WriteString("message", error.Message);

            if (error.ValidValues is not null)
            {
                writer.WriteStartArray("valid_values");
                foreach (string value in error.ValidValues)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
            }

            if (error.RetryAfterSeconds is double retryAfter)
            {
                writer.WriteNumber("retry_after", retryAfter);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
