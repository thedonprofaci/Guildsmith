using System.Buffers;
using System.Text;
using System.Text.Json;

using Guildsmith.Core.Arguments;

namespace Guildsmith.Core.Models;

/// <summary>
/// One guild channel as the channel tools see it. <see cref="Kind"/> is <see langword="null"/> for a
/// channel type outside the five of spec section 6.4 (a stage channel, a thread, a media channel, …).
/// <see cref="ParentId"/> is the category the channel sits under, <see langword="null"/> when it sits
/// under none.
/// </summary>
public sealed record ChannelInfo(ulong Id, ulong GuildId, string Name, ChannelKind? Kind, ulong? ParentId, int Position)
{
    /// <summary>
    /// The compact JSON object
    /// <c>{"id":"&lt;id&gt;","name":…,"type":…,"parent_id":"&lt;id&gt;"|null,"position":…}</c>, the IDs as
    /// strings (spec section 6.1); <c>type</c> is <c>null</c> for a channel <see cref="Kind"/> does not name.
    /// </summary>
    public string ToJson() => Write(writer => WriteObject(writer, this));

    /// <summary>The compact JSON array of <paramref name="channels"/>, each as <see cref="ToJson()"/>, in the given order.</summary>
    public static string ToJson(IEnumerable<ChannelInfo> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        return Write(writer =>
        {
            writer.WriteStartArray();
            foreach (ChannelInfo channel in channels)
            {
                WriteObject(writer, channel);
            }

            writer.WriteEndArray();
        });
    }

    private static string Write(Action<Utf8JsonWriter> write)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            write(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteObject(Utf8JsonWriter writer, ChannelInfo channel)
    {
        writer.WriteStartObject();
        writer.WriteString("id", Snowflake.Format(channel.Id));
        writer.WriteString("name", channel.Name);
        if (channel.Kind is ChannelKind kind)
        {
            writer.WriteString("type", ChannelTypeArgument.Format(kind));
        }
        else
        {
            writer.WriteNull("type");
        }

        if (channel.ParentId is ulong parent)
        {
            writer.WriteString("parent_id", Snowflake.Format(parent));
        }
        else
        {
            writer.WriteNull("parent_id");
        }

        writer.WriteNumber("position", channel.Position);
        writer.WriteEndObject();
    }
}
