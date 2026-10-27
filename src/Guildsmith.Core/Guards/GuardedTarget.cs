namespace Guildsmith.Core.Guards;

/// <summary>
/// The parsed IDs of a call that passed every guard. <see cref="GuildId"/> is the guild the
/// allowlist checked: for an ID-addressed call, the one resolved from that ID.
/// </summary>
public sealed record GuardedTarget(ulong GuildId, ulong? ChannelId, ulong? RoleId, ulong? MessageId);
