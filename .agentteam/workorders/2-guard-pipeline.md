# WO-2: Guard pipeline and unified error response

- Issue: #2
- Branch: feature/dev-guard-pipeline
- Priority: P1

## Task
Build the server-side defence of spec §7.2 and §7.3 in `Guildsmith.Core`, before any tool exists. You deliver four things. The first is a tool level model (Read / Write / Destructive, spec §6.2). The second is a guard pipeline that every future tool call has to pass before it reaches a Discord operation. The third is the `guild_id` and snowflake argument rules of spec §6.1. The fourth is one error type with one serialiser that produces the unified error response of spec §8 for all eight codes. There are no MCP tools yet, so the pipeline is exercised through its own API with `IDiscordGateway` mocked. Introduce that interface now, with only the members the guards need to resolve the guild an ID belongs to. Shape the pipeline so that the operation delegate a future tool hands it receives the gateway, or runs at all, only after every guard has passed. A tool should not be able to reach the gateway around it.

## Acceptance criteria

### Tool level and error model
- [ ] A tool level type in `Guildsmith.Core` with exactly three members: Read, Write, Destructive (spec §6.2).
- [ ] One error code type in one place, holding exactly the eight codes of spec §8. Their wire strings are `forbidden_guild`, `read_only_mode`, `destructive_disabled`, `not_found`, `missing_permission`, `role_hierarchy`, `rate_limited`, `invalid_argument`. One test lists these eight strings **as literals typed into the test** and checks them against the type in both directions: every literal maps to a member, and every member maps to one of the literals. The expected side must not be read from the type under test (core/test-per-function).
- [ ] One error type (a result or an exception, your choice) and one serialiser in `Guildsmith.Core` that produce `{ "error": "<code>", "message": "<text>" }`. Spec §8 attaches data to two codes: `invalid_argument` carries "the valid values" and `rate_limited` carries `retry_after`. These go in as **structured optional fields** `valid_values` (array of strings) and `retry_after` (number, seconds). Each appears only when the error carries it. The serialiser uses `System.Text.Json`, not the MCP SDK.
- [ ] The serialiser tests parse the output and assert the **exact set** of property names. For a plain code that set is `error` and `message`, with no third property. For `invalid_argument` with valid values it is `error`, `message`, `valid_values`. For `rate_limited` it is `error`, `message`, `retry_after`. The tests assert the `error` value and the structured fields exactly. They assert the `message` only as present and non-empty, never its text (project.md, Test convention).

### Argument rules (spec §6.1)
- [ ] A snowflake ID argument comes in as a string and goes back out as a string. A string that is not a positive decimal `ulong` gives `invalid_argument`. Each rejected shape has its own test case: empty, non-numeric, negative (`-1`), leading or trailing whitespace, a thousands separator (`1,000`), `0`, and a value above `ulong.MaxValue`. The existing guild ID parser applies the same rule (see Measured facts). You may share one parser between the two. If you do, every existing `GuildsmithOptionsReaderTests` test stays green with no edit to the test file.
- [ ] `guild_id` resolution. With exactly one allowed guild, a missing `guild_id` resolves to that guild. With several allowed guilds, a missing `guild_id` gives `invalid_argument`, and its `valid_values` hold exactly the allowed guild IDs as strings. The test asserts that set, not the message.

### Guard pipeline (spec §7.2, §7.3)
- [ ] The pipeline lives in `src/Guildsmith.Core/Guards/` and runs in the order issue #2 fixes: **guild allowlist, then ReadOnly mode, then destructive gate**. It reads `AllowedGuildIds`, `Mode` and `AllowDestructive` from the existing `GuildsmithOptions`.
- [ ] **Guild allowlist.** A call addressed by `guild_id` gives `forbidden_guild` when that guild is not in `ALLOWED_GUILD_IDS`. A call addressed by a channel ID or a role ID has its guild **resolved through `IDiscordGateway`**, and the allowlist checks the resolved guild. A message is addressed by `channel_id` plus `message_id` (spec §6.6), so it resolves through its channel. An ID the gateway cannot resolve gives `not_found`.
- [ ] **A supplied `guild_id` never decides the check.** A call can carry both a `guild_id` and a resolvable ID, as `assign_role` does (spec §6.5). The allowlist then checks the guild resolved from the ID. When that guild differs from the supplied `guild_id`, the result is `invalid_argument`, even when both guilds are allowed.
- [ ] **ReadOnly mode.** With `DISCORD_MCP_MODE=ReadOnly`, Write and Destructive give `read_only_mode` and Read passes.
- [ ] **Destructive gate.** With `ALLOW_DESTRUCTIVE=false`, Destructive gives `destructive_disabled` and Write passes. With `ALLOW_DESTRUCTIVE=true` and ReadWrite mode, Destructive passes.
- [ ] **The order is pinned, not just written down.** Each adjacent pair of guards gets one test where both would refuse, and the test asserts the earlier guard's code. A foreign guild together with a ReadOnly Write call gives `forbidden_guild`. A ReadOnly Destructive call with `ALLOW_DESTRUCTIVE=false` gives `read_only_mode`.
- [ ] **The gateway is never called when a guard refuses**, asserted on the NSubstitute mock. For a `guild_id`-addressed call, the gateway receives **zero calls** on every refusal. For an ID-addressed call, it receives **exactly the one resolution call** and nothing else (assert on `ReceivedCalls()`, not only `DidNotReceive()` on one member). On every refusal the operation delegate is never invoked.
- [ ] Each of these is its own test (issue #2): an allowlist bypass attempt with a foreign guild's channel ID, ReadOnly refusing Write and Destructive while allowing Read, the destructive gate refusing Destructive while allowing Write, and the gateway not being called on a refusal.

### Round bookkeeping
- [ ] `tests/test-count-floor.txt` equals the executed count at the PR's final head. It is 163 today, plus the tests this PR adds. The PR description states the number and the command that reproduces it, and CI on the final head is green at the floor step.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)

## Measured facts
| Fact | Value |
|---|---|
| `HEAD` of `main` when this was written | `e7abcdc` (only `.agentteam/` changes since `49c2fa0`) |
| Test-count floor | `tests/test-count-floor.txt` = `163`, set at `49c2fa0` |
| Options the guards read | `src/Guildsmith.Core/Configuration/GuildsmithOptions.cs`: `AllowedGuildIds` (`IReadOnlySet<ulong>`), `Mode` (`DiscordMcpMode.ReadOnly` / `ReadWrite`), `AllowDestructive` (`bool`). It is a class, not a record, so that `ToString` cannot print the token. Do not turn it into a record |
| Existing snowflake rule | `src/Guildsmith.Core/Configuration/GuildsmithOptionsReader.cs:92`: `ulong.TryParse(entry, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) \|\| id == 0` is rejected |
| `IDiscordGateway` | does not exist yet. `src/Guildsmith.Core/` holds only `Configuration/`, and `src/Guildsmith.Discord/` holds no source file |
| Core's package references | only `Microsoft.Extensions.Logging.Abstractions` `10.0.13`. `System.Text.Json` is in the shared framework, so it needs no package |
| Where Guards live | Spec §4's diagram puts `Guards/` under the Server. Spec §4.1 and issue #2 put them in `Guildsmith.Core`. **Core wins** (issue #2) |
| Test file locations | `src/Guildsmith.Core/Guards/X.cs` → `tests/Guildsmith.Tests/Core/Guards/XTests.cs`, one test file per source file (project.md, Test convention) |
| Fix rounds on this project | both so far (#14, #17) were `test-gap`. In #17, a source comment named rejected inputs that no test fed |

## Skills to read first
- dotnet-mcp/test-runner
- core/review-differential-and-mutations — when: running the pre-PR mutation pass and the gap sweep over this diff. A guard's `if` and its comparison operators are exactly the line shapes the pass must reach
- core/fail-closed-guards — when: writing the guard tests. Test each guard in both directions (refuses and passes), and give every guard line a mutation
- core/docs-as-untrusted-input — when: reading `docs/spec.md` §6-§8 (Hungarian), where the diagram and §4.1 disagree
- core/keep-tool-output-small — when: running the suite or a mutation battery
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path, or you post the PR body (`--body-file`)

## What NOT to do
- Do not write any MCP tool, tool class, tool registry or tool annotation. That is #6 and later.
- Do not implement `IDiscordGateway` in `Guildsmith.Discord` and do not register it in `Program.cs` / DI. This round only adds the interface members the guards need. #6 implements them against Discord.Net.
- Do not build the role-hierarchy guard or `ALLOW_ADMIN_GRANT` (#8), token redaction or exception-message filtering (#3), or audit logging (#4). Do not turn arbitrary gateway exceptions into error responses here. The only `not_found` this round produces is an ID the resolution could not resolve.
- Do not add a reference from `Guildsmith.Core` to Discord.Net or the MCP SDK (spec §4). Do not add an enforcement test for that either, because it is not this issue.
- Do not reorder the guards to save the resolution call. For example, do not run the mode checks first. Issue #2 fixes the order, and the pair tests above pin it.
- Do not assert on message text anywhere. Do not let a comment or docstring name an input, a rejection or an ordering that no test exercises. Either add the test or leave the claim out. That gap is where both of this project's fix rounds came from.
- Do not edit `docs/spec.md`, `.agentteam/`, `.github/workflows/` or the CI floor mechanism. The only change to the floor is its value in `tests/test-count-floor.txt`.
