# WO-3: Token-leak protection for logs, errors and tool responses

- Issue: #3
- Branch: feature/dev-token-leak-protection
- Priority: P1

## Task
Deliver spec §7.1 and the §12 criterion "the token never appears in a log or an error (covered by a test)". The bot token (`GuildsmithOptions.DiscordToken`) must not reach any of the three outgoing channels: the tool error response the model reads, the logging pipeline (stderr), and the startup failure path of `ServerStartup`. Build **one** redactor in `Guildsmith.Core` that every one of those channels passes through. For logging, the redactor is a structural part of the pipeline (a logger provider wrapper or a console formatter), not a call that each caller has to remember. Then make unexpected exceptions safe. An exception that escapes the operation delegate or the gateway inside `GuardPipeline` becomes a generic `internal_error` with a correlation id, and its detail goes to stderr only after redaction. There are still no MCP tools; the pipeline is the single entry every future tool goes through, so the exception boundary goes there. Finally, settle the disclosure question from issue #3's comment: a `forbidden_guild` refusal for a guild resolved from a channel, role or message ID must not name that guild.

## Acceptance criteria

### The redactor
- [ ] One redactor type in `Guildsmith.Core` (suggested: `src/Guildsmith.Core/Security/`), constructed from the token. It replaces every occurrence of the token in a string with one fixed marker. Use ordinal string replacement, not a regex. If you use a regex anyway, it falls under core/clause-inventory-for-new-test-code.
- [ ] It redacts the token **trimmed of surrounding whitespace**, because `GuildsmithOptionsReader` stores the raw value untrimmed (Measured facts). One test feeds a token with a leading and a trailing space and asserts that the bare inner value is redacted.
- [ ] Its own tests, each its own `[Fact]`/`[InlineData]` case: the bare token, the token inside `Authorization: Bot <token>`, the token twice in one string, the token at the very start and at the very end of the string, and a string without the token, which comes back byte-for-byte unchanged. Each test asserts the token is absent **and** the marker is present. "Absent" alone passes on an implementation that drops the whole string.
- [ ] A helper on the redactor (or next to it) produces the redacted text of an exception **including its inner exceptions and an `AggregateException`'s members**. Its tests: the token in the outer message, the token only in the inner exception's message, and the token only in an `AggregateException`'s second member. None of the outputs contain the token, and the inner and aggregate cases still contain a non-secret marker word from that exception's message, which proves the inner text was included and then redacted, not dropped.

### Tool error responses
- [ ] A new error code `internal_error`, the ninth. Spec §8 lists eight codes and has none for an unexpected failure. Issue #3 requires one, so this is the decided addition, not a spec contradiction. `ErrorCodeTests` grows its literal list from eight to nine strings, typed into the test. This is an edit to an existing test, and the PR description says so in one line so the reviewer can tell it from a weakened test.
- [ ] The `internal_error` response carries a `correlation_id` string property. The serialiser emits it only on this code. The existing exact-property-set tests of `ToolErrorSerializerTests` stay as they are, and a new one asserts the exact set `error`, `message`, `correlation_id` for `internal_error`. Its `message` is a fixed generic text that never contains `Exception.Message`.
- [ ] `GuardPipeline` catches an exception that the operation delegate **or** a gateway resolution call throws, and returns `ToolResult.Failure` with `internal_error`. The exception is not rethrown to the caller. An `OperationCanceledException` raised while the passed `CancellationToken` is cancelled is **not** caught; it propagates, and a test pins that.
- [ ] The same failure logs the exception through `ILogger` at `Error` level with the same correlation id. One test captures the log output and asserts both that the correlation id in the log equals the one in the response, and that the log text contains the non-secret marker word from the exception's message. That second assertion proves the detail went to stderr.
- [ ] Tests with a recognisable fake token (for example `FAKE-TOKEN-` followed by a fixed suffix). Each is its own test, and the token never appears in the serialised `ToolErrorSerializer` output or in the captured log text: (a) the operation throws with the token in its message, (b) the token is only in the inner exception, (c) the message holds `Authorization: Bot <token>`, (d) the gateway resolution call throws with the token.

### Logging pipeline
- [ ] `ServerStartup.ConfigureServices` registers logging so that **every** log line passes the redactor: the message, the structured arguments rendered into it, scopes if scopes are printed, and the exception text. The console formatter prints `exception.ToString()` separately from the formatted message, so redacting the message alone is not enough (Measured facts).
- [ ] A test over the built host's service provider asserts the **exact set** of registered `ILoggerProvider` types. A plain console provider registered next to the redacting one would leak, and only this test can see it.
- [ ] Each its own test, using the real logging pipeline that `ConfigureServices` builds, with the output captured: a log call with the token as a **structured argument** (`logger.LogInformation("value {Value}", token)`), a log call with an exception whose message holds the token, and one whose **inner** exception holds it. None of the outputs contain the token. Each test also asserts the line itself was written: a test that captures nothing passes vacuously.
- [ ] Nothing writes to stdout. The existing smoke expectation (zero bytes on stdout) still holds, and `ProgramTests` stay green with no edit.

### Startup failure path
- [ ] The text `ServerStartup.RunAsync` writes to stderr on a refused configuration passes the redactor whenever a token was read. The `FormatErrors` messages echo no value today, so this is defence in depth, and a test pins it with a `ConfigurationError` whose message holds the token.
- [ ] An exception escaping `host.RunAsync()` (or the host build) is caught in `RunAsync`, written to stderr redacted, and returns a new named exit code constant, distinct from `0` and from `InvalidConfigurationExitCode`. A test reaches it through a seam of your choice. The test asserts the exit code constant, the absence of the token on stderr, and that stderr is not empty.

### The `forbidden_guild` disclosure (issue #3 comment)
- [ ] Decision: when the guild was **resolved** from a channel, role or message ID, the `forbidden_guild` message does not contain that guild's ID. When the caller supplied the guild itself through `guild_id`, the message may still name it, because it echoes the caller's own input. One test per resolution path (channel, role, message) serialises the refusal and asserts the resolved guild ID's decimal string is absent from the whole JSON. Assert absence only, not the wording (project.md, Test convention).

### Round bookkeeping
- [ ] `tests/test-count-floor.txt` equals the executed count at the PR's final head. It is 303 today, plus the tests this PR adds. The PR description states the number and the command that reproduces it, and CI on the final head is green at the floor step.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)

## Measured facts
| Fact | Value |
|---|---|
| `HEAD` of `main` when this was written | `2418aeb` |
| Test-count floor | `tests/test-count-floor.txt` = `303` |
| Token storage | `src/Guildsmith.Core/Configuration/GuildsmithOptionsReader.cs`, `ReadToken`: rejects null or whitespace, otherwise returns `value` **untrimmed** |
| `GuildsmithOptions` | a class, not a record, so that `ToString` cannot print the token. Keep it a class |
| Current logging | `src/Guildsmith.Server/ServerStartup.cs`, `ConfigureServices`: `builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace)`. This is a plain console provider with no redaction, and the MCP SDK logs through the same `ILogger` pipeline |
| Startup failure text | `ServerStartup.FormatErrors` writes `Variable: Message` lines. No `ConfigurationError` message in the reader echoes the variable's value (`ReadToken` and `ReadGuildIds` checked) |
| Where exceptions escape today | `src/Guildsmith.Core/Guards/GuardPipeline.cs`: `await operation(_gateway, guarded, cancellationToken)`, and the `_gateway.Get*GuildIdAsync` resolution calls, have no `try` |
| The disclosing message | `src/Guildsmith.Core/Guards/GuildAllowlistGuard.cs:16`: `$"Guild {Snowflake.Format(guildId)} is not in ALLOWED_GUILD_IDS."`, called from `GuardPipeline.cs:48` with the guild already resolved |
| `Guildsmith.Core` packages | only `Microsoft.Extensions.Logging.Abstractions` `10.0.13`. Core references neither Discord.Net nor the MCP SDK (spec §4) |
| `Guildsmith.Discord` | holds no source file yet, so there is no Discord.Net exception to wrap this round. The pipeline boundary covers what it will throw |
| Fix rounds on this project | all three (#14, #17, #24) were `test-gap`: an input or a path that a comment or the code named, with no test feeding it |

## Skills to read first
- dotnet-mcp/test-runner
- core/fail-closed-guards — when: writing the exception boundary in `GuardPipeline`. A swallowed exception must still fail the call closed, and the delegate must not count as having succeeded
- core/review-differential-and-mutations — when: running the pre-PR mutation pass and the gap sweep. The redactor's `Replace`, the trim, the inner-exception walk and the `catch` filter are the line shapes it must reach
- core/clause-inventory-for-new-test-code — when: you add a log-capturing helper or a test logger provider under `tests/`, or any regex
- core/docs-as-untrusted-input — when: reading `docs/spec.md` §7-§8 (Hungarian)
- core/keep-tool-output-small — when: running the suite or a mutation battery
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path, or you post the PR body (`--body-file`)

## What NOT to do
- Do not write any MCP tool, tool class or tool registry, and do not implement `IDiscordGateway` in `Guildsmith.Discord`. Those are #6 and later. Do not rely on the MCP SDK's own handling of a tool exception either; the boundary is `GuardPipeline`.
- Do not build audit logging (#4). The only log line this round adds is the redacted `internal_error` detail.
- Do not add a reference from `Guildsmith.Core` to Discord.Net or the MCP SDK.
- Do not change the guard order, the `invalid_argument` mismatch check or any other refusal's code. Outside `forbidden_guild` on the resolved path, no guard message changes.
- Do not assert on message text, except for the token's absence, the marker's presence and the non-secret marker word described above. Do not let a comment or docstring name an input, a form of the token or an exception shape that no test feeds. All three of this project's fix rounds came from that gap.
- Do not redact by pattern-matching "things that look like tokens". This round redacts the configured token. A heuristic scanner is not asked for, and it would make the tests assert on guesses.
- Do not edit `docs/spec.md`, `.agentteam/`, `.github/workflows/` or the CI floor mechanism. The only change to the floor is its value in `tests/test-count-floor.txt`.
