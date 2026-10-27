namespace Guildsmith.Core.Configuration;

/// <summary>
/// One rejected configuration variable. <see cref="Message"/> never contains the value that was
/// supplied: the value could be a mistyped token, and this text goes to stderr.
/// </summary>
public sealed record ConfigurationError(string Variable, ConfigurationErrorKind Kind, string Message);
