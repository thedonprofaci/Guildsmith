namespace Guildsmith.TestCountFloor;

/// <summary>An input the floor check cannot read; it fails the check, never reads as zero or as no floor.</summary>
public sealed class FloorCheckException(string message) : Exception(message);
