namespace Guildsmith.Core.Models;

/// <summary>
/// The changes <c>edit_channel</c> asks the gateway to make. A <see langword="null"/> field was not
/// given, and the gateway leaves it unchanged on Discord.
/// </summary>
public sealed record ChannelEdit(string? Name, string? Topic, ulong? ParentId, int? Position, int? SlowModeSeconds, bool? Nsfw);
