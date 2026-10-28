using System.Buffers;
using System.Text;
using System.Text.Json;

using Guildsmith.Core.Arguments;

namespace Guildsmith.Core.Models;

/// <summary>One guild's overview, as <c>get_guild_info</c> returns it (spec section 6.3).</summary>
public sealed record GuildInfo(ulong Id, string Name, int MemberCount, int ChannelCount, int RoleCount)
{
    /// <summary>
    /// The compact JSON object
    /// <c>{"id":"&lt;id&gt;","name":…,"member_count":…,"channel_count":…,"role_count":…}</c>, the ID
    /// as a string (spec section 6.1).
    /// </summary>
    public string ToJson()
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("id", Snowflake.Format(Id));
            writer.WriteString("name", Name);
            writer.WriteNumber("member_count", MemberCount);
            writer.WriteNumber("channel_count", ChannelCount);
            writer.WriteNumber("role_count", RoleCount);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
