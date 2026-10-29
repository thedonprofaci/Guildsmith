# WO-7: Channel tools

- Issue: #7
- Branch: feature/dev-channel-tools
- Priority: P1

## Task
Build the four channel tools of spec §6.4 (`list_channels`, `create_channel`, `edit_channel`, `delete_channel`) on the template PR #32 established for #6: tool class → service → real `GuardPipeline` → real `DiscordGateway`, with only `IDiscordRestApi` substituted in tests. This round also owns two pieces of shared infrastructure that #9 and #10 reuse: channel resolution (`DiscordGateway.GetChannelGuildIdAsync`, which throws `NotSupportedException` today) and `WriteRequestOptions.Create(CancellationToken)`, which replaces the parameterless form. These are the first Write and Destructive tools of the project, so they are the first real carriers of the audit reason and the cancellation token on a Discord request. Issue #7's body carries the amendment pass of PR #32; this work order restates its criteria and adds the decisions the issue left open.

## Acceptance criteria

### Shared infrastructure (#9 and #10 depend on it)
- [ ] **Channel resolution.** `DiscordGateway.GetChannelGuildIdAsync` goes through a new `IDiscordRestApi.GetChannelAsync` and returns the channel's guild ID. A missing channel (Discord.Net returns `null` on 404) and a non-guild channel (DM, group DM) each return `null`, so the pipeline answers `not_found`. One test per case, plus one for the guild channel, plus one asserting the cancellation token the gateway method received is the one handed to the `IDiscordRestApi` call.
- [ ] **`WriteRequestOptions.Create(CancellationToken)`** sets `AuditLogReason = AuditReason.Value` **and** `CancelToken`. The parameterless `Create()` is removed, so no write call can omit the token. `WriteRequestOptionsTests` pins both properties with literals (the reason as the literal `via Discord MCP`, the token as a token the test created) and keeps the fresh-instance-per-call test.
- [ ] Every Write / Destructive gateway method in this round builds its `RequestOptions` with `WriteRequestOptions.Create(cancellationToken)`, and `DiscordGatewayTests` asserts, per method, on the `RequestOptions` the substituted Discord.Net entity method received (for example `IGuild.CreateTextChannelAsync(…, RequestOptions)`, `IGuildChannel.ModifyAsync(…, RequestOptions)`, `IDeletable.DeleteAsync(RequestOptions)`): the audit reason is the literal `via Discord MCP`, and the `CancelToken` is the very token the tool received. Not on an `IDiscordGateway` mock, which cannot see them.

### The four tools
- [ ] `list_channels` (Read): `guild_id` (through `GuildArgument`, optional when exactly one guild is allowed), optional `type` filter over the same five values as `create_channel`. An unknown `type` value is `invalid_argument` listing the five valid values, with no gateway call. A test pins the exact response JSON for one input, with the expected JSON typed as a literal, IDs as strings (spec §6.1); the output order is pinned by a test, whichever order you choose. A channel whose Discord type is outside the five (stage, thread, …) is either listed under a stated type name or left out — your choice, stated in the PR, and tested once.
- [ ] `create_channel` (Write): `guild_id`, `name`, `type` (`text` / `voice` / `category` / `announcement` / `forum`), optional `parent_id`, `topic`, `position`. Each of the five types reaches its own Discord.Net creation call (`CreateTextChannelAsync`, `CreateVoiceChannelAsync`, `CreateCategoryAsync`, `CreateNewsChannelAsync`, `CreateForumChannelAsync`): one test per type asserting which call was made. An unknown `type` is `invalid_argument` listing the five valid values, with no gateway call.
- [ ] `edit_channel` (Write): `channel_id`, optional `name`, `topic`, `parent_id`, `position`, `slowmode_seconds`, `nsfw`. **Only the fields given are changed**: a test per field asserts that the given field is set on the properties object the substituted `ModifyAsync` callback receives, and that the fields not given stay unspecified (Discord.Net's `Optional<T>` `IsSpecified == false`). A call with no optional field at all is `invalid_argument` with no Discord request. A field the channel's type cannot carry (for example `topic` on a category) is `invalid_argument` naming the field, with no Discord request; which fields each type accepts you read from Discord.Net's property classes, and the PR lists the table. One test per rejected field/type pair in that table.
- [ ] `delete_channel` (Destructive): `channel_id`. It passes through the destructive gate of #2; a test with the gate closed gets its refusal code with no Discord request.
- [ ] **Channel-addressed tools resolve the guild.** `edit_channel` and `delete_channel` go through `ToolTarget.Channel(…)`, so the pipeline resolves the channel's guild and applies the allowlist: a test per tool with a channel of a non-allowed guild gets `forbidden_guild` and no write request; a test per tool with a missing channel gets `not_found`.
- [ ] **`parent_id` is a category in the same guild**, for `create_channel` and `edit_channel`: a `parent_id` that is not a category, a category of **another allowed guild**, and a missing channel are each `invalid_argument`, with no write request. Each of those three cases is a separate test per tool (six tests), so neither of the two conditions (is a category; same guild) can be deleted with the suite green.
- [ ] **The §12 scenario** as a service-level test with only `IDiscordRestApi` substituted: create a *Projektek* category, then `general` and `dev` text channels under it, asserting each creation call's name, type and parent ID.
- [ ] **MCP hints**: `list_channels` Read; `create_channel` `Idempotent = false`; `edit_channel` `Idempotent = true`; `delete_channel` your choice, stated in the PR with one sentence of reasoning. Every hint is set explicitly, and each description states when the tool is destructive (spec §6.1).
- [ ] One `DISCORD_MCP_MODE=ReadOnly` test per tool: `list_channels` succeeds, the three write tools are refused with no Discord request.
- [ ] A Discord failure with a mapped code (`not_found`, `missing_permission`) reaches the model as that code for one write tool, through the real `GuardPipeline`; the audit entry of that Write call carries the mapped code.

### Registry and round bookkeeping
- [ ] The three tests listing the registered tool names grow to six tools and are renamed so their names no longer say "two": `ToolRegistryTests.The_host_registers_exactly_the_two_server_tools`, `ServerStartupTests.ConfigureServices_serves_exactly_the_two_server_tools`, `ProgramTests.RunAsync_lists_the_server_tools_to_an_sdk_stdio_client_without_logging_in_to_discord`, plus the tool count in `ProgramTests.RunAsync_writes_only_json_rpc_messages_to_stdout_during_a_session`. The registry instrument passes for all six tools.
- [ ] The MCP Inspector command of `project.md` (Commands), run on the final head, lists all six tools with their hints; the command and its output summary go in the PR.
- [ ] `IDiscordGateway`'s XML doc stays true: `GetChannelGuildIdAsync`'s summary says what a DM / group DM returns. The `DiscordGateway` doc comment saying channel resolution arrives with #7 is removed; the role one (#8) stays.
- [ ] `tests/test-count-floor.txt` equals the executed count at the PR's final head (559 today, plus this PR's tests). The PR states the number and the command that reproduces it; CI on the final head is green at the floor step.
- [ ] The PR's pre-PR mutation pass reaches, at minimum, these line shapes (all six of this project's fix rounds were `test-gap`): every cancellation-token forward (gateway → `IDiscordRestApi`, gateway → `WriteRequestOptions.Create`, service lambda → gateway, whatever the lambda parameter is named); each of the five `type` arms in creation and in the filter; each of the two `parent_id` conditions; each "field given?" branch in `edit_channel`; the non-guild-channel branch of resolution.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)

## Measured facts
| Fact | Value |
|---|---|
| `HEAD` of `main` when this was written | `d4aa40b` |
| Test-count floor | `tests/test-count-floor.txt` = `559` |
| Channel resolution today | `src/Guildsmith.Discord/DiscordGateway.cs:26-28` throws `NotSupportedException`; `:30-32` the role one (#8) — leave it |
| `WriteRequestOptions` today | `src/Guildsmith.Discord/WriteRequestOptions.cs:15` `Create() => new() { AuditLogReason = AuditReason.Value }`; used in `src/` nowhere, in tests only by `tests/Guildsmith.Tests/Discord/WriteRequestOptionsTests.cs:12,20,23` |
| `IDiscordRestApi` today | `src/Guildsmith.Discord/IDiscordRestApi.cs`: `GetGuildSummariesAsync(RequestOptions)`, `GetGuildAsync(ulong, RequestOptions)` |
| Channel target | `src/Guildsmith.Core/Guards/ToolTarget.cs:38` `ToolTarget.Channel(string? channelId, string? guildId = null)` |
| Service template | `src/Guildsmith.Core/Services/GuildService.cs` (`GetGuildInfoAsync` at `:42`, lambda `(gateway, target, token) => …`) |
| Name-listing tests to grow | `tests/Guildsmith.Tests/Server/Tools/ToolRegistryTests.cs:34`, `tests/Guildsmith.Tests/Server/ServerStartupTests.cs:186`, `tests/Guildsmith.Tests/Server/ProgramTests.cs:32` and `:71` |
| Discord.Net 3.20.2 creation calls (`Discord.Net.Core.xml`) | `IGuild.CreateTextChannelAsync(string, Action<TextChannelProperties>, RequestOptions)`, `CreateNewsChannelAsync(string, Action<TextChannelProperties>, RequestOptions)`, `CreateVoiceChannelAsync(string, Action<VoiceChannelProperties>, RequestOptions)`, `CreateCategoryAsync(string, Action<GuildChannelProperties>, RequestOptions)`, `CreateForumChannelAsync(string, Action<ForumChannelProperties>, RequestOptions)` |
| Discord.Net 3.20.2 edit / delete / lookup | `IGuildChannel.ModifyAsync(Action<GuildChannelProperties>, RequestOptions)`, `ITextChannel.ModifyAsync(Action<TextChannelProperties>, RequestOptions)`, `IDeletable.DeleteAsync(RequestOptions)`, `IDiscordClient.GetChannelAsync(ulong, CacheMode, RequestOptions)`, `IGuild.GetChannelAsync(ulong, CacheMode, RequestOptions)` |
| Property classes | `GuildChannelProperties`: `Name`, `Position`, `CategoryId`, `PermissionOverwrites`, `Flags`; `TextChannelProperties` adds `Topic`, `IsNsfw`, `SlowModeInterval`, … (all `Optional<T>`) |
| Fix rounds on this project | six, all `test-gap` (#14, #17, #24, #26, #31, #33): an input, a path or a token forward that the code or a comment named, with no test feeding it. #33 was a cancellation-token forward |

## Skills to read first
- dotnet-mcp/test-runner — the suite command, and the MCP Inspector check
- core/review-differential-and-mutations — the pre-PR mutation pass and gap sweep; the line shapes it must reach are in the last criteria group above
- core/clause-inventory-for-new-test-code — when: you add or change a fake Discord entity, capture helper or fixture under `tests/`
- core/coverage-stops-at-the-host-language — when: deciding what pins the six tools' schemas as a client sees them (the Inspector, not the unit suite)
- core/docs-as-untrusted-input — when: reading `docs/spec.md` §6.4 / §12 (Hungarian)
- dotnet-mcp/project-bootstrap — when: adding a package or a project reference (stdout belongs to the protocol)
- core/keep-tool-output-small — when: running the suite, a mutation battery, or the Inspector
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path, or you post the PR body (`--body-file`)

## What NOT to do
- Do not implement channel permission overwrites (#9), role resolution (`GetRoleGuildIdAsync` stays `NotSupportedException`, #8), or any message tool (#10). Do not set `PermissionOverwrites` or `Flags` in `create_channel` / `edit_channel`.
- Do not keep the parameterless `WriteRequestOptions.Create()` next to the new overload "for compatibility" — the point is that a write cannot omit the token.
- Do not change the guard order, any existing refusal's code or message, the audit entry format, `AuditEntry.Render`, or `DiscordFailureMapping`'s mapping. No `rate_limited` mapping (project.md, "Deviations from the spec").
- Do not invent a permission name for `missing_permission`. Look `parent_id` up through the channel lookup this round adds; do not list another guild's channels to find it.
- Do not let a tool class call `IDiscordGateway` directly, do not let Core reference Discord.Net or the MCP SDK, and do not log in to Discord at startup or let any test reach the network.
- Do not let a comment or docstring name an outcome, an input or a path that no test feeds — every fix round of this project came from that gap.
- Do not edit issues #8-#10; if this round teaches something they need, propose it in the PR.
- Do not edit `docs/spec.md`, `.agentteam/`, `.github/workflows/` or the CI floor mechanism. The only change to the floor is its value in `tests/test-count-floor.txt`.
