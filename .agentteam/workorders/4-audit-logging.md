# WO-4: Audit logging of write and destructive operations

- Issue: #4
- Branch: feature/dev-audit-logging
- Priority: P1

## Task
Deliver spec §7.8: every Write and Destructive tool call is audited on stderr, and every Discord request such a call makes carries the audit-log reason `via Discord MCP`. The audit point is `GuardPipeline.RunAsync`, the single entry every tool goes through and the one place that already knows the call's `ToolLevel`. The pipeline writes **exactly one** audit entry per Write / Destructive call once its outcome is known: success, any refusal, `internal_error`, or cancellation. No tool has to remember to call anything. `RunAsync` does not know the tool's name today, so it gains a **required** `toolName` parameter, with no overload that omits it. There are still no MCP tools and no write operation on `IDiscordGateway`, so the reason cannot be asserted on a write call this round. Instead, this round delivers the one place the reason comes from, in Core and in `Guildsmith.Discord`. #7-#10 assert it on the mock for each write they add.

## Acceptance criteria

### The audit entry
- [ ] A pure function in `Guildsmith.Core` (suggested: `src/Guildsmith.Core/Audit/`) renders an audit entry from: the tool name, the level, the guild ID, the channel, role and message IDs, and the outcome. The outcome is `success`, the §8 wire string of the error code, or `cancelled`. An ID that is not known is rendered as absent, never as `0`. Its own tests pin the **exact** output string for: a success with only a guild, a success with channel + message, a refusal with an error code, `cancelled`, and a call where no ID is known. Type the expected strings into the test; do not build them from the constants under test (core/test-per-function).
- [ ] The entry carries IDs as parsed snowflakes only. A **raw argument string is never rendered**: there is no input to the function that could carry one, so the function takes `ulong?`, not `string?`.

### The audit point
- [ ] `GuardPipeline.RunAsync` takes a required `string toolName` and writes the entry through an `ILogger` under one fixed category (for example `Guildsmith.Audit`), at `Information` level. This category is **separate from the `GuardPipeline` logger** that carries the `internal_error` detail.
- [ ] A Write and a Destructive call each produce exactly one entry on success, and the entry names the tool, the level, the guild and the target IDs. Test both levels and assert the count, not just the presence.
- [ ] A `Read` call produces **zero** entries, both on success and on a refusal. Test both.
- [ ] Each refusal path of #2 is audited with its error code, one test each: `forbidden_guild` (supplied path and resolved path), `read_only_mode`, `destructive_disabled`, `not_found` (channel and role unresolved), and `invalid_argument` (malformed ID, empty supplied `guild_id`, and a supplied `guild_id` that mismatches the resolved one). The operation is not invoked in any of them; the existing tests already pin that and stay as they are.
- [ ] An `internal_error` is audited with outcome `internal_error`, in addition to the existing `Error`-level detail log. There is still exactly one audit entry.
- [ ] An `OperationCanceledException` that propagates (the token is cancelled) is still audited with outcome `cancelled`, and the exception still propagates. A cancelled write may already have reached Discord, so an audit log that drops it has a hole.
- [ ] On a refusal raised before resolution completes, the entry carries the IDs that parsed as snowflakes. Example: a `not_found` channel entry contains that channel's ID. An argument that failed to parse is absent.
- [ ] **What never reaches the entry**, each its own test that asserts the marker is absent **and** that an entry was written (otherwise the test passes vacuously):
  - a malformed `channel_id` whose text is a recognisable marker string (`invalid_argument` path);
  - the operation's return value, made a marker string;
  - the token, with a pipeline whose operation throws with `FAKE-TOKEN-…` in its message.
  The resolved guild ID **may** appear in the audit entry on the resolved `forbidden_guild` path. The audit entry is the operator's stderr, not the model's response, so WO-3's disclosure rule does not apply to it.
- [ ] The 48 existing `RunAsync` calls in `GuardPipelineTests.cs` gain a tool name argument and **nothing else changes in them**. The PR description says so in one line, so the reviewer can tell this mechanical edit from a weakened test.

### Reaching stderr
- [ ] The audit category is not silenced by `LOG_LEVEL`. `ServerStartup.ConfigureServices` adds a filter rule that keeps the audit category at `Information` whatever `SetMinimumLevel(options.LogLevel)` sets (Measured facts). A test over the built host, with `LOG_LEVEL` at `Error`, asserts that a logger for the audit category is enabled at `Information`, and that a logger for another category is not. The second assertion proves the filter is scoped to the audit category.
- [ ] One test through the real pipeline `ConfigureServices` builds writes an audit entry and finds it on the captured stderr, redacted, with zero bytes on stdout. The existing exact-provider-set test stays green with no edit: the audit goes through the one `RedactingLoggerProvider`, never through a provider of its own.

### The audit-log reason
- [ ] One constant in `Guildsmith.Core` holds `via Discord MCP`. A test pins it against the literal typed into the test. This is a deliberate literal, like `[REDACTED]`, and the PR description says so.
- [ ] One factory in `Guildsmith.Discord` returns the Discord.Net `RequestOptions` that a write request uses, with `AuditLogReason` set from that constant. Its test asserts the literal `via Discord MCP` on the returned options, and that each call returns a **fresh** instance. A shared mutable `RequestOptions` is a cross-call leak. This is the only source file this round adds to `Guildsmith.Discord`, and it does not implement `IDiscordGateway`.
- [ ] The XML doc on `IDiscordGateway` states the contract: every Write / Destructive method added to it carries the audit reason, and its test asserts this on the mock. The doc says no more than that; the enforcement lands with the first write method (#7).

### Round bookkeeping
- [ ] `tests/test-count-floor.txt` equals the executed count at the PR's final head. It is 378 today, plus the tests this PR adds. The PR description states the number and the command that reproduces it, and CI on the final head is green at the floor step.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)

## Measured facts
| Fact | Value |
|---|---|
| `HEAD` of `main` when this was written | `5e37fb7` |
| Test-count floor | `tests/test-count-floor.txt` = `378` |
| The audit point | `src/Guildsmith.Core/Guards/GuardPipeline.cs`: `RunAsync<T>(ToolLevel level, ToolTarget target, Func<IDiscordGateway, GuardedTarget, CancellationToken, Task<T>> operation, CancellationToken)`. It has no tool name. Outcome sources: `GuardAndRunAsync` (resolution failure, the guard chain, the operation) and the `catch … when (!(OperationCanceledException && IsCancellationRequested))` in `RunAsync` |
| Parsed IDs before a refusal | `ResolveAsync` / `ResolveChannelAsync` / `ResolveRoleAsync` / `ResolveMessageAsync` hold the parsed `ulong`s locally. On `not_found` and on a parse failure only a `ToolError` comes back today (`Resolution.Failed`) |
| Existing calls to update | `grep -c "RunAsync" tests/Guildsmith.Tests/Core/Guards/GuardPipelineTests.cs` = `48` |
| Log level | `src/Guildsmith.Server/ServerStartup.cs:108`: `builder.Logging.SetMinimumLevel(options.LogLevel)`. `LOG_LEVEL` defaults to `Information` (`GuildsmithOptionsReader.cs:48`) |
| The one provider | `ServerStartup.cs:107`: `AddProvider(new RedactingLoggerProvider(new TokenRedactor(options.DiscordToken), stderr))` |
| DI | `GuardPipeline` is not registered in DI. Registering it is not required this round |
| `Guildsmith.Discord` | no source file. It references `Discord.Net.Rest` `3.20.2` and Core; the test project already references it, so no lock file changes |
| Fix rounds on this project | all four (#14, #17, #24, #26) were `test-gap`: an input or a path that a comment or the code named, with no test feeding it |

## Skills to read first
- dotnet-mcp/test-runner
- core/review-differential-and-mutations — when: running the pre-PR mutation pass and the gap sweep. The line shapes it must reach: the `Read` skip, each outcome branch, the cancelled path, and the filter rule's category string
- core/clause-inventory-for-new-test-code — when: you add or extend a log-capturing helper under `tests/`
- core/docs-as-untrusted-input — when: reading `docs/spec.md` §7.8 (Hungarian)
- core/keep-tool-output-small — when: running the suite or a mutation battery
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path, or you post the PR body (`--body-file`)

## What NOT to do
- Do not write any MCP tool or tool class, do not implement `IDiscordGateway`, and do not add a method to it. Those are #6 and later.
- Do not add a file sink, a second logger provider or a console writer for the audit. Stderr through the existing `RedactingLoggerProvider` is the whole scope (issue #4, Out of scope).
- Do not change the guard order, any refusal's code or message, the `internal_error` behaviour or its `Error`-level detail log. This round only adds the audit entry next to them.
- Do not give `RunAsync` an overload or a default value that lets a call skip the tool name.
- Do not log any argument beyond the parsed IDs: not a name, a topic, message content or the operation's result.
- Do not assert on message text, except for the exact strings of the pure render function's own tests, the reason literal, and the absent markers above.
- Do not let a comment or docstring name an outcome, an input or a path that no test feeds. All four of this project's fix rounds came from that gap.
- Do not edit `docs/spec.md`, `.agentteam/`, `.github/workflows/` or the CI floor mechanism. The only change to the floor is its value in `tests/test-count-floor.txt`.
