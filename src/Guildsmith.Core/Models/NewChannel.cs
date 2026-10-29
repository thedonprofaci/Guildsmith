namespace Guildsmith.Core.Models;

/// <summary>
/// The channel <c>create_channel</c> asks the gateway to create. <see cref="ParentId"/>,
/// <see cref="Topic"/> and <see cref="Position"/> are <see langword="null"/> when the caller did not
/// give them, and the gateway then leaves them to Discord.
/// </summary>
public sealed record NewChannel(string Name, ChannelKind Kind, ulong? ParentId, string? Topic, int? Position);
