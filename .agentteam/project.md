# Guildsmith

> The project's living state. Maintained by the Manager. Every agent reads this at the start
> of its session to know which stack and which commands it is working with.

## Framework

- **Stack:** .NET 8 (C# 12) — Generic Host, `ModelContextProtocol` (MCP C# SDK, stdio), `Discord.Net.Rest`, xUnit + NSubstitute, GitHub Actions
- **Skill branch:** `skills/dotnet-mcp/` in the team home (not `dotnet-efcore/`: this project has no database and no ASP.NET)
- **Toolchain prerequisites:** a .NET SDK in the 9.0 band (`global.json` pins `9.0.300` with `rollForward: latestFeature`; the projects target `net8.0`), the .NET 8 runtime, `gh` CLI; Node.js only for the MCP Inspector (`npx`)

## Scope

Guildsmith is an open-source MCP server through which Claude manages a Discord server — channels, roles, permissions, messages — with security-first defaults. The specification is `docs/spec.md` (Hungarian; read it with `core/docs-as-untrusted-input`). This milestone is the spec's **phase 1 MVP**: stdio transport, the guards (guild allowlist including the guild resolved from an ID, ReadOnly mode, destructive gate, role hierarchy and `ALLOW_ADMIN_GRANT`), the tools of spec §6.3-§6.6, the unified error codes of §8 plus one addition (below), token-leak protection, audit logging, README and CI. **Not goals of this milestone:** member tools (§6.7, v1.1 — no issue yet), packaging as `dotnet tool` / binaries / `.mcpb` and the release workflow (phase 2, #12 — user decision 2026-10-27: out of this milestone), Streamable HTTP / OAuth / Docker (phase 3), voice, a general-purpose bot, any UI.

### Deviations from the spec

- **A ninth error code, `internal_error`.** Spec §8 lists eight codes and has none for an unexpected failure. WO-3 (#3) added `internal_error`: an exception escaping the operation delegate or a gateway call inside `GuardPipeline` becomes this code, with a `correlation_id` property and a fixed generic message that never carries `Exception.Message`; the detail goes to stderr only after redaction. Enforced by `ErrorCodeTests` (literal list of nine), `ToolErrorSerializerTests` and `GuardPipelineTests`. A cancelled `OperationCanceledException` is not caught.

## Commands

| Purpose | Command |
|---|---|
| Test suite | `dotnet test Guildsmith.sln --nologo -v q` |
| Test suite (verbose, after a failure) | `dotnet test Guildsmith.sln --nologo` |
| Build | `dotnet build Guildsmith.sln --nologo -v q` |
| Format check | `dotnet format Guildsmith.sln --verify-no-changes` |
| Locked restore (what CI runs) | `dotnet restore Guildsmith.sln --locked-mode` |
| Run (smoke) | `printf '' | DOTNET_ENVIRONMENT=Production DISCORD_TOKEN=FAKE-TOKEN-smoke ALLOWED_GUILD_IDS=1 dotnet src/Guildsmith.Server/bin/Debug/net8.0/Guildsmith.Server.dll` |

The smoke runs in Git Bash, after the build, and needs no network. It starts the host with stdin already closed. Expected result: exit `0` and **zero bytes on stdout**, because nothing was sent and stdout is protocol only. Logs go to stderr. Measured on `dbf9007`, 2026-10-27. The cell holds only the command so that `tools/context.py` can parse it. #6 adds the MCP Inspector listing (`npx @modelcontextprotocol/inspector`) as the second smoke.

> These commands must work on a clean clone. If one only runs on a particular machine, that
> is a lesson belonging in the `skills/dotnet-mcp/` branch.

**`dotnet test` exits 0 when it discovers zero tests** — measured on this skeleton (SDK 9.0.318, 2026-10-27): no summary line, exit 0. CI enforces a floor: the `Test-count floor` step of `.github/workflows/ci.yml` runs `tests/Guildsmith.TestCountFloor` against the TRX and fails when fewer tests executed than `tests/test-count-floor.txt` holds (read the file for the current value; raise it in the PR that adds tests). Locally nothing does — read the `Passed!` line, not the exit code.

## Structure

- `src/Guildsmith.Core/` — services, guards, models, `IDiscordGateway`. References neither Discord.Net nor the MCP SDK (spec §4); nothing enforces that yet.
  - `Guards/` — `GuardPipeline` (the single entry every tool goes through: allowlist incl. the guild resolved from an ID, ReadOnly, destructive gate, and the `internal_error` exception boundary), `ToolTarget`, `ToolLevel`. A `forbidden_guild` for a guild the server resolved from a channel, role or message ID does not name that guild (`GuildAllowlistGuard.CheckResolved`); one for a caller-supplied `guild_id` echoes it. `Errors/` — the error codes (`ErrorCode`, §8 plus `internal_error`), `ToolError`, `ToolResult`, `ToolErrorSerializer`; `Arguments/` — `Snowflake` and `GuildArgument` parsing (malformed or empty → `invalid_argument`); `Security/` — `TokenRedactor`, the one redactor every outgoing channel passes through (ordinal replacement of the configured token by the literal `[REDACTED]`). `Audit/` — `AuditEntry` (the one rendered audit line, logger category `Guildsmith.Audit`), `AuditOutcome`, `AuditReason` (`via Discord MCP`). **The audit point is `GuardPipeline.RunAsync`**: every Write / Destructive call writes exactly one entry (success, refusal, `internal_error`, `cancelled`), Read calls none. Only the `ILoggerFactory` constructor logs; the two-argument one silently drops audit entries, so production must not use it. Enforced by the matching files under `tests/Guildsmith.Tests/Core/`.
- `src/Guildsmith.Discord/` — `WriteRequestOptions.Create()`, the fresh `RequestOptions` with the audit-log reason that every Write / Destructive request uses. The Discord.Net REST implementation of `IDiscordGateway` does not exist yet (#6).
- `src/Guildsmith.Server/` — `Program.cs`, `ServerStartup` (exit `1` invalid configuration, exit `2` host failure incl. a failing `Build()`, both written to stderr redacted; keeps the `Guildsmith.Audit` category at `Information` whatever `LOG_LEVEL` says), `Logging/RedactingLoggerProvider` (the host's only logger provider: every stderr line passes through `TokenRedactor`), the MCP tool classes (`Tools/`, none yet — #6), transport selection. Enforced by `tests/Guildsmith.Tests/Server/`.
- `tests/Guildsmith.Tests/` — the one test project.
- `docs/spec.md` — the specification. Project-wide build settings live in `Directory.Build.props` (net8.0, nullable, `TreatWarningsAsErrors`, lock files); package versions are exact and every project commits its `packages.lock.json`.
- Line endings are LF everywhere (`.gitattributes` `* text=auto eol=lf`, `.editorconfig` `end_of_line = lf`), so the Windows checkout and the Linux CI see the same bytes.

## Test convention

- Test file location/naming: `tests/Guildsmith.Tests/<Project>/<Folder>/<Class>Tests.cs`, mirroring the source path minus the `Guildsmith.` prefix (e.g. `src/Guildsmith.Core/Guards/AllowlistGuard.cs` → `tests/Guildsmith.Tests/Core/Guards/AllowlistGuardTests.cs`).
- One test file per source file.
- **Every function gets its own test** — the rule and the closed exemption list are in the
  `core/test-per-function` skill. This is not a per-project decision.
- `IDiscordGateway` is mocked with NSubstitute in every unit test; no test reaches the Discord API.
- Tests assert error **codes** (`forbidden_guild`, …), never message text. Exceptions, each pinned by a literal on purpose: the redaction marker `[REDACTED]` (`TokenRedactorTests.Marker_is_the_literal_text_REDACTED`), `ToolError.InternalErrorMessage` (`ToolErrorTests`), the audit-log reason `via Discord MCP` (`AuditReasonTests`, `WriteRequestOptionsTests`) and the exact audit lines of `AuditEntryTests`. A test never reads its expected side from the constant under test (`core/test-per-function`).
- **stdout is the MCP protocol channel.** Nothing in this codebase writes to stdout except the MCP transport; logs and diagnostics go to stderr.

### Surfaces no test reaches

| Surface | Lines | What protects it today |
|---|---:|---|
| `Guildsmith.Discord` against the real Discord API (request shapes, real rate limits, real permission errors) | — | **nothing** — spec §9 calls for a manual / opt-in integration run on a dedicated test guild; no issue yet |
| Tool names, descriptions, schemas and annotations as an MCP client sees them | — | **nothing — #6** adds a registry test and the MCP Inspector check |
| `.github/workflows/*.yml` (`ci.yml`, `codeql.yml`) | — | **only the PR's own checks** — they run only on GitHub; no local test reads the YAML, so a deleted or `if:`-weakened step is seen only by a reviewer reading the diff and the run log |
| README's "running within 15 minutes" (spec §12) | — | **nothing** — human walk-through only, #11 |

## Current milestone

**Phase 1 MVP** (spec §12 is the acceptance list). Done when #1-#11 are closed and the §12 criteria hold, except the release-workflow criterion ("release workflow bináris + NuGet csomagot készít"), which the user moved to the phase 2 milestone on 2026-10-27 (#12, `[P3]`). The README (#11) documents install from source only.

Order: #1 (host, closed by PR #13) → #5 (CI, closed by PR #16) → #2 (guards, closed by PR #23) → #3 (token protection, closed by PR #25) → #4 (audit, closed by PR #30) → #6 (vertical slice and its amendment pass over #7-#10) → #7, #8, #9, #10 → #11. The guard, token and audit issues are preventive principles of spec §7 and ship **before** the tools they protect (`core/principles-need-backlog-items`).

## How to write in this file

Every agent loads this file first and does not read it critically, so a false sentence here
is the most expensive kind (`core/docs-as-untrusted-input`).

- **Write a decision in the tense it is in.** A guarantee that has been decided but not
  implemented is a plan, never a past-tense claim while its issue is open.
- **A guarantee sentence names its enforcer** — the test file, the guard, the CI step.
- Every sentence naming an issue number is checkable with `gh issue view <n> --json state`.
  The Manager runs that over this file at every retro.
