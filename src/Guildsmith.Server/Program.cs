// In stdio mode stdout belongs to the MCP protocol: nothing may ever be written to it except
// protocol messages. Startup errors and logs go to stderr (ServerStartup).
return await Guildsmith.Server.ServerStartup.RunAsync(Console.Error);
