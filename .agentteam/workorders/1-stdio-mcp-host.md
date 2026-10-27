# WO-1: Stdio MCP host with startup configuration validation

- Issue: #1
- Branch: feature/dev-stdio-mcp-host
- Priority: P1

## Task
Replace the placeholder `src/Guildsmith.Server/Program.cs` with a Generic Host that serves MCP over stdio through `ModelContextProtocol` 2.3.0 and registers no tools yet. Before the host starts, every setting of spec §5 (plus `ALLOW_ADMIN_GRANT`, spec §7.4) is read from configuration, validated and bound once into a single options type in `Guildsmith.Core`, which the guards of #2 will consume. An invalid configuration writes a message naming the offending variable to stderr and exits non-zero, writing nothing to stdout. Startup does not touch the network or Discord.Net at all. This is the first round of the project: the suite is empty today, so the tests you add are also the first floor the CI of #5 will enforce.

## Acceptance criteria
- [ ] `Program.cs` builds a Generic Host serving MCP over stdio (`AddMcpServer(...).WithStdioServerTransport()`), with no tool registered. A test starts the **built** server as a child process through the SDK client (`StdioClientTransport` + `McpClient.CreateAsync`), completes `initialize`, calls `ListToolsAsync`, and asserts the tool list is empty.
- [ ] Startup needs no network access: no Discord client is constructed, logged in or registered as an eagerly-resolved service at startup. The test above runs with a fake token and no network.
- [ ] Configuration sources: environment variables, plus user secrets **only** when the host environment is `Development` (the csproj gets a `UserSecretsId`). No file committed to the repository holds a token value; no `appsettings*.json` with a `DISCORD_TOKEN` key is added.
- [ ] One options type in `Guildsmith.Core` (name and folder yours, e.g. `src/Guildsmith.Core/Configuration/`) holding: `DISCORD_TOKEN` (required), `ALLOWED_GUILD_IDS` (required, a set of `ulong`), `DISCORD_MCP_MODE` (`ReadOnly` / `ReadWrite`, default `ReadWrite`), `ALLOW_DESTRUCTIVE` (default `false`), `ALLOW_ADMIN_GRANT` (default `false`), `MAX_MESSAGES_PER_READ` (default `50`), `MCP_TRANSPORT` (default `stdio`), `LOG_LEVEL` (default `Information`). A test pins every default on a configuration that sets only the two required variables.
- [ ] Validation, each case with its own test asserting the variable name in the error and a failed result: missing or blank `DISCORD_TOKEN`; missing, empty or unparseable `ALLOWED_GUILD_IDS`; unknown `DISCORD_MCP_MODE`; non-boolean `ALLOW_DESTRUCTIVE`; non-boolean `ALLOW_ADMIN_GRANT`; `MAX_MESSAGES_PER_READ` that is non-numeric, `0` or `101` (and `1` and `100` accepted — both boundaries pinned); `MCP_TRANSPORT=http` rejected as not supported until phase 3; an unknown `MCP_TRANSPORT` value rejected.
  - Interpretations (the spec is silent; fail closed, change only on a user decision): an out-of-range `MAX_MESSAGES_PER_READ` fails startup, it is not clamped. `ALLOWED_GUILD_IDS` entries are comma-separated and trimmed; an empty entry (`1,,2`, a trailing comma), a non-numeric entry, or `0` fails startup. Booleans accept `true` / `false` case-insensitively and nothing else (`1`, `yes` fail). An unknown `LOG_LEVEL` fails startup the same way.
  - When several variables are invalid, all of them are reported, not only the first (one test with two invalid variables asserting both names).
- [ ] The process-level failure path, tested by starting the built server as a process with an invalid configuration: exit code non-zero, stderr names the variable, stdout holds **zero bytes**. One such process test is enough; the per-case coverage lives at the validator level above.
- [ ] All logging goes to stderr: console logger with `LogToStandardErrorThreshold = LogLevel.Trace`, minimum level taken from the validated `LOG_LEVEL`. The session test asserts that every line the server wrote to stdout parses as a JSON-RPC message (`"jsonrpc":"2.0"`).
- [ ] The fake token never appears: use a recognisable fake (e.g. `FAKE-TOKEN-guildsmith-test-0001`) in the process tests and assert it occurs in neither stderr nor stdout, on the failing path (with a different variable invalid) and on the successful session path. Also assert that no validation error message produced by the validator contains the token value.
- [ ] `.env.example` at the repository root lists all eight variables with placeholders only (no value that looks like a real token or guild ID).
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)

## Measured facts
| Fact | Value |
|---|---|
| Server registration API (ModelContextProtocol 2.3.0 XML docs) | `services.AddMcpServer(Action<McpServerOptions>)`, `.WithStdioServerTransport()`; `.WithToolsFromAssembly(...)` exists — do not call it in this task |
| Client API for the session test (ModelContextProtocol.Core 2.3.0) | `ModelContextProtocol.Client.StdioClientTransport(StdioClientTransportOptions)`, `McpClient.CreateAsync(IClientTransport, McpClientOptions, ILoggerFactory, CancellationToken)`, `McpClient.ListToolsAsync(RequestOptions, CancellationToken)` |
| `StdioClientTransportOptions` properties | `Command`, `Arguments`, `WorkingDirectory`, `InheritEnvironmentVariables`, `EnvironmentVariables`, `ShutdownTimeout`, `StandardErrorLines` |
| Extensions band already resolved for Server | `Microsoft.Extensions.Hosting` 10.0.13 pulls `Configuration.EnvironmentVariables`, `Configuration.UserSecrets`, `Logging.Console`, `Options`, `Configuration.Binder` — all 10.0.13 (`src/Guildsmith.Server/packages.lock.json`). If Core needs an abstractions package, pin it to 10.0.13 exactly and commit the updated `src/Guildsmith.Core/packages.lock.json` |
| Test project already references Server | `tests/Guildsmith.Tests/Guildsmith.Tests.csproj` has a `ProjectReference` to `Guildsmith.Server` |
| Installed SDK | `9.0.318` only; `global.json` pins `9.0.300` with `latestFeature`; projects target `net8.0` |
| Suite today | zero tests; `dotnet test` exits 0 with no summary line, so read the `Passed!` line and its `Total:` |
| Current `HEAD` of `main` | `70830e9` |

## Skills to read first
- dotnet-mcp/project-bootstrap — stdout belongs to the protocol; the Extensions band
- dotnet-mcp/test-runner — the quiet test command; stdout hygiene as a process test
- core/fail-closed-guards — when: writing the validator; every unparseable value is a failure, never a silent default
- core/clause-inventory-for-new-test-code — when: you add a process-runner or environment helper under `tests/` for the child-process tests; it is code of this round with no test of its own otherwise
- core/docs-as-untrusted-input — when: reading `docs/spec.md` §3-§5, §7 (Hungarian; it names the projects `DiscordMcp.*`, the repo uses `Guildsmith.*`)
- core/keep-tool-output-small — when: running the suite or a mutation battery
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path or you post the PR body

## What NOT to do
- Do not register, write or stub any MCP tool, and do not implement `IDiscordGateway` or anything in `src/Guildsmith.Discord/` (that is #6). Do not add a Discord.Net or MCP package reference to `Guildsmith.Core`.
- Do not build guards (#2), a redaction layer (#3 — here only the startup path must not echo the token), audit logging (#4), CI workflows (#5) or the README (#11).
- Do not implement or stub an HTTP transport; `http` is a validation failure.
- **Process tests must not inherit the developer's environment**: set `InheritEnvironmentVariables = false` (or the `Process` equivalent) and pass every variable explicitly, and run the child with `DOTNET_ENVIRONMENT=Production` — otherwise a developer machine with a real `DISCORD_TOKEN` in its environment or in user secrets leaks it into a test run and its output.
- Do not parse the mode or `LOG_LEVEL` with a bare `Enum.TryParse`: it accepts numeric strings (`"1"`, `"99"`) as valid enum values. Reject anything that is not a defined name.
- Nothing writes to stdout except the MCP transport — not a banner, not a validation message, not `Console.WriteLine` in a test helper inside the server.
- Do not touch `.agentteam/`, `docs/spec.md`, `Directory.Build.props` or `global.json`.
