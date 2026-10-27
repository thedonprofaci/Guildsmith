# Guildsmith

> The project's living state. Maintained by the Manager. Every agent reads this at the start
> of its session to know which stack and which commands it is working with.

## Framework

- **Stack:** .NET 8 (C# 12) — Generic Host, `ModelContextProtocol` (MCP C# SDK, stdio), `Discord.Net.Rest`, xUnit + NSubstitute, GitHub Actions
- **Skill branch:** `skills/dotnet-mcp/` in the team home (not `dotnet-efcore/`: this project has no database and no ASP.NET)
- **Toolchain prerequisites:** a .NET SDK in the 9.0 band (`global.json` pins `9.0.300` with `rollForward: latestFeature`; the projects target `net8.0`), the .NET 8 runtime, `gh` CLI; Node.js only for the MCP Inspector (`npx`)

## Scope

Guildsmith is an open-source MCP server through which Claude manages a Discord server — channels, roles, permissions, messages — with security-first defaults. The specification is `docs/spec.md` (Hungarian; read it with `core/docs-as-untrusted-input`). This milestone is the spec's **phase 1 MVP**: stdio transport, the guards (guild allowlist including the guild resolved from an ID, ReadOnly mode, destructive gate, role hierarchy and `ALLOW_ADMIN_GRANT`), the tools of spec §6.3-§6.6, the unified error codes of §8, token-leak protection, audit logging, README and CI. **Not goals of this milestone:** member tools (§6.7, v1.1 — no issue yet), packaging as `dotnet tool` / binaries / `.mcpb` (phase 2, #12 waits for a user decision), Streamable HTTP / OAuth / Docker (phase 3), voice, a general-purpose bot, any UI.

## Commands

| Purpose | Command |
|---|---|
| Test suite | `dotnet test Guildsmith.sln --nologo -v q` |
| Test suite (verbose, after a failure) | `dotnet test Guildsmith.sln --nologo` |
| Build | `dotnet build Guildsmith.sln --nologo -v q` |
| Format check | `dotnet format Guildsmith.sln --verify-no-changes` |
| Locked restore (what CI runs) | `dotnet restore Guildsmith.sln --locked-mode` |
| Run (smoke) | **No such command yet.** `src/Guildsmith.Server/Program.cs` is a placeholder that writes one line to stderr and exits 1. #1 delivers the host; #6 makes the MCP Inspector listing (`npx @modelcontextprotocol/inspector` against the built server, fake token, no network) the run smoke. |

> These commands must work on a clean clone. If one only runs on a particular machine, that
> is a lesson belonging in the `skills/dotnet-mcp/` branch.

**`dotnet test` exits 0 when it discovers zero tests** — measured on this skeleton (SDK 9.0.318, 2026-10-27): no summary line, exit 0. The suite currently holds no test, so a green run proves nothing until a test exists, and nothing enforces a floor until #5 (open) adds one to CI. Read the `Passed!` line, not the exit code.

## Structure

- `src/Guildsmith.Core/` — services, guards, models, `IDiscordGateway`. References neither Discord.Net nor the MCP SDK (spec §4); nothing enforces that yet.
- `src/Guildsmith.Discord/` — the Discord.Net REST implementation of `IDiscordGateway`.
- `src/Guildsmith.Server/` — `Program.cs`, the MCP tool classes (`Tools/`), transport selection.
- `tests/Guildsmith.Tests/` — the one test project.
- `docs/spec.md` — the specification. Project-wide build settings live in `Directory.Build.props` (net8.0, nullable, `TreatWarningsAsErrors`, lock files); package versions are exact and every project commits its `packages.lock.json`.
- Line endings are LF everywhere (`.gitattributes` `* text=auto eol=lf`, `.editorconfig` `end_of_line = lf`), so the Windows checkout and the Linux CI see the same bytes.

## Test convention

- Test file location/naming: `tests/Guildsmith.Tests/<Project>/<Folder>/<Class>Tests.cs`, mirroring the source path minus the `Guildsmith.` prefix (e.g. `src/Guildsmith.Core/Guards/AllowlistGuard.cs` → `tests/Guildsmith.Tests/Core/Guards/AllowlistGuardTests.cs`).
- One test file per source file.
- **Every function gets its own test** — the rule and the closed exemption list are in the
  `core/test-per-function` skill. This is not a per-project decision.
- `IDiscordGateway` is mocked with NSubstitute in every unit test; no test reaches the Discord API.
- Tests assert error **codes** (`forbidden_guild`, …), never message text.
- **stdout is the MCP protocol channel.** Nothing in this codebase writes to stdout except the MCP transport; logs and diagnostics go to stderr.

### Surfaces no test reaches

| Surface | Lines | What protects it today |
|---|---:|---|
| `Guildsmith.Discord` against the real Discord API (request shapes, real rate limits, real permission errors) | — | **nothing** — spec §9 calls for a manual / opt-in integration run on a dedicated test guild; no issue yet |
| Tool names, descriptions, schemas and annotations as an MCP client sees them | — | **nothing — #6** adds a registry test and the MCP Inspector check |
| `.github/workflows/*.yml` | — | **nothing** — runs only on GitHub; #5 (open) creates the workflow |
| README's "running within 15 minutes" (spec §12) | — | **nothing** — human walk-through only, #11 |

## Current milestone

**Phase 1 MVP** (spec §12 is the acceptance list). Done when #1-#11 are closed and the §12 criteria hold, except the release-workflow criterion, which belongs to #12 and waits for a user decision.

Order: #1 (host, `[P1]`) → #5 (CI, so the floor exists from the second round) → #2 (guards) → #3 (token protection) → #4 (audit) → #6 (vertical slice and its amendment pass over #7-#10) → #7, #8, #9, #10 → #11. The guard, token and audit issues are preventive principles of spec §7 and ship **before** the tools they protect (`core/principles-need-backlog-items`).

## How to write in this file

Every agent loads this file first and does not read it critically, so a false sentence here
is the most expensive kind (`core/docs-as-untrusted-input`).

- **Write a decision in the tense it is in.** A guarantee that has been decided but not
  implemented is a plan, never a past-tense claim while its issue is open.
- **A guarantee sentence names its enforcer** — the test file, the guard, the CI step.
- Every sentence naming an issue number is checkable with `gh issue view <n> --json state`.
  The Manager runs that over this file at every retro.
