# WO-6: Server tools end to end - template for the tool issues

- Issue: #6
- Branch: feature/dev-server-tools-template
- Priority: P1

## Task
Build the two server tools of spec §6.3, `list_guilds` and `get_guild_info`, through every layer of spec §4: MCP tool class → `GuardPipeline` → service → `IDiscordGateway` → its first Discord.Net REST implementation. This is the vertical slice for #7-#10 (`core/vertical-slice-before-repetition`): the two tools are the payload, and the shape they prove is the point. That shape covers the file layout, the gateway test seam, the Discord failure mapping, the production construction of the pipeline, the registry instrument and the Inspector smoke. Every later tool round copies it. The round ends with the amendment pass over #7-#10, recorded in the PR.

## Acceptance criteria

### The two tools
- [ ] `list_guilds` (Read, no parameters) returns the guilds that are **both** in `ALLOWED_GUILD_IDS` **and** joined by the bot, as compact JSON `{ id, name }` with the ID as a string. Tests: a joined guild that is not allowed is absent; an allowed guild the bot has not joined is absent; a guild that is both is present; the order of the output is pinned by a test, whichever order you choose.
- [ ] `get_guild_info` (Read, `guild_id`) returns name, member count, channel count and role count, with the ID as a string. `guild_id` goes through `GuildArgument` (optional when exactly one guild is allowed, spec §6.1) and the allowlist guard: a non-allowed `guild_id` is `forbidden_guild` with **no** gateway call, tested.
- [ ] Responses follow spec §6.1: IDs as strings, compact objects, never a serialised Discord.Net entity. A test pins each tool's exact response JSON for one input, with the expected JSON typed as a literal in the test.
- [ ] A Discord call that fails with a mapped code (below) reaches the model as that §8 error code and not as `internal_error`. Test this for both tools with `not_found`, through the real `GuardPipeline`.

### Layers (spec §4)
- [ ] The tool classes are in `src/Guildsmith.Server/Tools/`. They are thin: they parse nothing beyond the arguments they hand over, and they call one service method. The service is in `src/Guildsmith.Core/Services/`, the gateway methods are on `IDiscordGateway`, and their implementation is in `src/Guildsmith.Discord/`. Every new source file has its own test file under the mirrored path (project.md, Test convention).
- [ ] **`list_guilds` goes through `GuardPipeline` like every other tool.** It has no guild, so the pipeline gains a form for a call that is not scoped to one guild. That form keeps the same level gate, `internal_error` boundary, Discord failure mapping and audit rule as the guild-scoped form. You choose the shape, but `GuardedTarget.GuildId` stays a non-nullable `ulong`: a guild-scoped tool must not be able to receive an unscoped target. The allowlist is applied as a filter in the service, and the tests above pin it.
- [ ] **The Discord gateway is testable without the network.** `Guildsmith.Discord`'s `IDiscordGateway` implementation has unit tests that run without a token or a socket. A suggested seam is to implement it over Discord.Net's own interfaces (`IDiscordClient`, `IGuild`), mocked with NSubstitute. That seam is where #7-#10 will assert the audit reason on the `RequestOptions` that each write call receives. The PR names the seam in one sentence.
- [ ] **Login is deferred to the first Discord call.** The server starts, answers `initialize` and `tools/list`, and exits cleanly on closed stdin with a fake token and no network. The existing run smoke (project.md, Commands) stays green with zero bytes on stdout, and a test pins that building and starting the host makes no Discord call.

### One mapping from Discord.Net failures to §8 codes
- [ ] The mapping is one function in `Guildsmith.Discord` that is tested on the mapping itself with constructed exceptions and no network (`Discord.Net.HttpException` has a public constructor; see Measured facts). It maps 404 → `not_found`; 403 or a missing permission → `missing_permission`, naming the permission where Discord reports it; and a rate limit → `rate_limited` with `retry_after` in seconds (spec §7.6). One test per mapped case. An unmapped failure (for example a 401 or a 500) stays an exception and becomes `internal_error` at the pipeline boundary, tested once.
- [ ] Core does not reference Discord.Net (spec §4). The mapped result must therefore cross into Core as a Core type, for example an exception type in `Guildsmith.Core` that carries a `ToolError`. **`GuardPipeline` returns that `ToolError` instead of `internal_error`.** That is a deliberate, narrow change to the boundary #3 built. It gets its own `GuardPipelineTests` case, and the audit entry of a Write / Destructive call ending this way carries the mapped code (one test, with a fake Write operation). Every other exception still becomes `internal_error` exactly as before, and the existing tests for that stay unedited.
- [ ] `retry_after`: `Discord.Net.RateLimitedException` carries no retry-after value (Measured facts). Find where Discord.Net 3.20.2 exposes one to the gateway, for example `IRateLimitInfo.RetryAfter` through `RequestOptions.RatelimitCallback`, and also check what the client's retry mode does with a 429 by default. Measure both, and state the result in the PR. **If no retry-after value reaches the gateway, do not invent a number.** Ship the other two mappings, and raise `rate_limited` as an open question in the PR.

### Production construction of the pipeline
- [ ] `GuardPipeline` is registered in DI in `ServerStartup.ConfigureServices`, built with the **`ILoggerFactory` constructor** (`GuardPipeline.cs:42`). The two-argument constructor (`:32`) logs nothing, so a production pipeline built with it silently drops every audit entry and every `internal_error` detail (Tester's note on PR #30). A test over the host `ConfigureServices` builds does three things. It resolves the pipeline and runs a fake **Write** operation through it. It finds the audit entry on the captured stderr. It asserts zero bytes on stdout. The same test with the two-argument constructor substituted must go red, and the PR shows that run.
- [ ] The tool classes, the service and the gateway are resolved from DI, and none of them builds a `GuardPipeline` itself. A grep for `new GuardPipeline(` over `src/` returns only the DI registration. Paste the command and its hit count in the PR.

### MCP surface and the instrument for the tools that follow
- [ ] Both tools declare their level (Read / Write / Destructive) in one place, carry a short description, and set `ReadOnly`, `Destructive` **and** `Idempotent` explicitly on the MCP attribute. The SDK's defaults are not safe: unset `Destructive` means `true` and unset `ReadOnly` means `false` (Measured facts). Spec §6.1 says the description states when a tool is destructive.
- [ ] **The registry instrument:** a test that enumerates every MCP tool registered in `Guildsmith.Server` (from the built host's registrations, not from a hand-written list) and fails when a tool:
  - has no declared level;
  - has `readOnlyHint` / `destructiveHint` disagreeing with its level (Read → `true`/`false`, Write → `false`/`false`, Destructive → `false`/`true`);
  - leaves any of the three hints unset;
  - has a name that is not `snake_case`.
  It also asserts the registered count, so a tool that silently fails to register is seen. **Each failure condition goes red on a deliberately wrong fixture tool.** The fixture lives in the test project and never reaches the server's registrations. The PR shows each red run, one line per condition.
- [ ] Per tool, a test runs it with `DISCORD_MCP_MODE=ReadOnly` and succeeds. This pins the level the tool actually **hands the pipeline**, and the registry instrument cannot see that level.
- [ ] The MCP Inspector (`npx @modelcontextprotocol/inspector`) lists both tools with their schemas and annotations, with a fake token and no network. The exact command and its output go in the PR. The skill `dotnet-mcp/test-runner` marks the Inspector's argument form as `VERIFY: not run yet`, so state the form that worked, for example whether the CLI mode or `--` was needed.

### Round bookkeeping
- [ ] **Amendment pass:** before the PR is marked ready, re-read #7, #8, #9 and #10 against what this slice taught, including the file layout, the test shape, the gateway seam, the failure mapping and anything a criterion there now gets wrong. The PR description lists the proposed change per issue, or says "no change". The Manager applies the edits to the issues at merge time. The audit-reason criterion in all four already reads "asserted at the `Guildsmith.Discord` gateway seam #6 establishes" (Manager, 2026-10-28). Your amendment fills in the seam's concrete name.
- [ ] Update `IDiscordGateway`'s XML doc so it says where the audit reason is asserted (the gateway seam above), not "on the mocked request". A mock of `IDiscordGateway` cannot see `RequestOptions`.
- [ ] `tests/test-count-floor.txt` equals the executed count at the PR's final head. It is 434 today, plus the tests this PR adds. The PR states the number and the command that reproduces it, and CI on the final head is green at the floor step.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)

## Measured facts
| Fact | Value |
|---|---|
| `HEAD` of `main` when this was written | `22f916e` |
| Test-count floor | `tests/test-count-floor.txt` = `434` |
| Pipeline entry | `src/Guildsmith.Core/Guards/GuardPipeline.cs:66` `RunAsync<T>(string toolName, ToolLevel level, ToolTarget target, Func<IDiscordGateway, GuardedTarget, CancellationToken, Task<T>> operation, CancellationToken)` |
| Constructors | `GuardPipeline.cs:32` `(options, gateway)` → `NullLoggerFactory`, logs nothing; `:42` `(options, gateway, ILoggerFactory)` → `GuardPipeline` category + `AuditEntry.Category` (`"Guildsmith.Audit"`, `src/Guildsmith.Core/Audit/AuditEntry.cs:18`) |
| DI today | `ServerStartup.ConfigureServices` (`src/Guildsmith.Server/ServerStartup.cs` ~`:103-123`) registers only the options, the one `RedactingLoggerProvider`, the audit filter, and `AddMcpServer(server => server.ToolCollection = [])`. No `GuardPipeline`, no gateway, no tools |
| Target kinds | `ToolTargetKind` = `Guild`, `Channel`, `Role`, `Message`. There is none for a call with no guild. `GuardedTarget(ulong GuildId, ulong? ChannelId, ulong? RoleId, ulong? MessageId)` |
| `guild_id` optional | `src/Guildsmith.Core/Arguments/GuildArgument.cs:18` `Resolve(string?, IReadOnlySet<ulong>)` uses the only allowed guild when omitted |
| `IDiscordGateway` today | `src/Guildsmith.Core/Gateway/IDiscordGateway.cs`: `GetChannelGuildIdAsync`, `GetRoleGuildIdAsync`. There is no implementation yet |
| `Guildsmith.Discord` today | one file, `WriteRequestOptions.cs` (`Create()` → fresh `RequestOptions { AuditLogReason = AuditReason.Value }`). `Discord.Net.Rest` `3.20.2` |
| MCP attribute defaults (SDK `2.3.0`, `ModelContextProtocol.Core.xml`) | `McpServerToolAttribute.ReadOnly` default `false`; `Destructive` default **`true`**; `Idempotent` default `false`; `OpenWorld` default `true` |
| Discord.Net failures (`Discord.Net.Core.xml` 3.20.2) | `HttpException(HttpStatusCode, IRequest, DiscordErrorCode?, string reason, DiscordJsonError[])`, with `HttpCode`, `DiscordCode`, `Reason`. `RateLimitedException(IRequest)` exposes only `Request`, so it has **no retry-after**. `IRateLimitInfo.RetryAfter` exists and `RequestOptions.RatelimitCallback` delivers it. `RetryMode` = `AlwaysFail` / `RetryTimeouts` / `RetryRatelimit` / `Retry502` / `AlwaysRetry`; the client default is not measured |
| Discord.Net interfaces | `IDiscordClient.GetGuildsAsync(CacheMode, RequestOptions)`, `GetGuildAsync(ulong, CacheMode, RequestOptions)`; `IGuild.GetChannelsAsync(CacheMode, RequestOptions)`, `IGuild.Roles`, `IGuild.ApproximateMemberCount`. Unverified: whether `ApproximateMemberCount` is populated without `DiscordRestClient.GetGuildAsync(id, withCounts: true)` |
| Fix rounds on this project | all five (#14, #17, #24, #26, #31) were `test-gap`: an input, a path or an ordering that the code or a comment named, with no test feeding it |

## Skills to read first
- dotnet-mcp/test-runner — the suite command, and the MCP Inspector check
- dotnet-mcp/project-bootstrap — when: adding a project reference or a package (stdout belongs to the protocol)
- core/vertical-slice-before-repetition — the amendment pass at the end of the round
- core/review-differential-and-mutations — when: running the pre-PR mutation pass and the gap sweep. The line shapes it must reach: the allowlist filter's two conditions in `list_guilds`, each mapping branch, the pipeline's mapped-error-versus-`internal_error` branch, and the DI constructor choice
- core/clause-inventory-for-new-test-code — when: you write the registry instrument, its fixture tool, or any fake Discord client or capture helper under `tests/`
- core/coverage-stops-at-the-host-language — when: deciding what pins the tool schemas the client sees (the Inspector, not the unit suite)
- core/docs-as-untrusted-input — when: reading `docs/spec.md` (Hungarian)
- core/keep-tool-output-small — when: running the suite, a mutation battery, or the Inspector
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path, or you post the PR body (`--body-file`)

## What NOT to do
- Do not write any tool beyond `list_guilds` and `get_guild_info`, and do not add a Write / Destructive method to `IDiscordGateway`. Those are #7-#10. The audit wiring is proven here with a fake Write operation in a test, not with a real write tool.
- Do not edit issues #7-#10 yourself. Propose the amendments in the PR, and the Manager applies them.
- Do not change the guard order, any existing refusal's code or message, the audit entry's format, or `AuditEntry.Render`. The only change to the `internal_error` boundary is the mapped-Discord-failure pass-through above.
- Do not let Core reference Discord.Net or the MCP SDK, and do not let a tool class call `IDiscordGateway` directly.
- Do not log in to Discord at startup, and do not make any test reach the network.
- Do not remove the two-argument `GuardPipeline` constructor. The tests use it. The rule is only that production does not.
- Do not invent a `retry_after` value or a permission name that Discord did not report.
- Do not let a comment or docstring name an outcome, an input or a path that no test feeds. All five of this project's fix rounds came from that gap.
- Do not edit `docs/spec.md`, `.agentteam/`, `.github/workflows/` or the CI floor mechanism. The only change to the floor is its value in `tests/test-count-floor.txt`.
