namespace Guildsmith.Core.Configuration;

/// <summary>
/// The outcome of reading the configuration: either the bound options and no errors, or no
/// options and every error found.
/// </summary>
public sealed class ConfigurationResult
{
    internal ConfigurationResult(GuildsmithOptions? options, IReadOnlyList<ConfigurationError> errors)
    {
        Options = options;
        Errors = errors;
    }

    /// <summary>The validated options; <see langword="null"/> when any variable was rejected.</summary>
    public GuildsmithOptions? Options { get; }

    /// <summary>Every rejected variable, in the order the variables are read.</summary>
    public IReadOnlyList<ConfigurationError> Errors { get; }
}
