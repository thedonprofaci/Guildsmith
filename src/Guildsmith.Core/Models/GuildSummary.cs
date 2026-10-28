using System.Buffers;
using System.Text;
using System.Text.Json;

using Guildsmith.Core.Arguments;

namespace Guildsmith.Core.Models;

/// <summary>A guild the bot has joined, as <c>list_guilds</c> returns it (spec section 6.3).</summary>
public sealed record GuildSummary(ulong Id, string Name)
{
    /// <summary>
    /// The compact JSON array of <paramref name="guilds"/>, in the given order:
    /// <c>[{"id":"&lt;id&gt;","name":"&lt;name&gt;"}]</c>, the ID as a string (spec section 6.1).
    /// </summary>
    public static string ToJson(IEnumerable<GuildSummary> guilds)
    {
        ArgumentNullException.ThrowIfNull(guilds);

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartArray();
            foreach (GuildSummary guild in guilds)
            {
                writer.WriteStartObject();
                writer.WriteString("id", Snowflake.Format(guild.Id));
                writer.WriteString("name", guild.Name);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
