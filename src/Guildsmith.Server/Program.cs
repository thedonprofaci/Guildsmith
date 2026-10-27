// Skeleton only. The stdio MCP host is issue #1.
// In stdio mode stdout belongs to the MCP protocol: nothing may ever be written to it
// except protocol messages, so even this placeholder writes to stderr.
Console.Error.WriteLine("Guildsmith: the MCP host is not implemented yet.");
return 1;
