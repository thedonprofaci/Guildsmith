# WO-31: Pin AuditEntry.Render's ID field order for channel+role and role+message (fix round of PR #30)

- Issue: #31
- Branch: feature/dev-audit-logging
- Priority: P1
- Cause-class: test-gap
- Parent: #4 / PR #30 (Tester verdict `FAIL`, 2 mutation survivors, both in `src/Guildsmith.Core/Audit/AuditEntry.cs` `Render`)

## Task
The Tester's battery on PR #30 left two mutants alive in `AuditEntry.Render`, both test gaps; the product code is believed correct. Swapping the `channel` and `role` `AppendId` lines (O3), or the `role` and `message` lines (O4), leaves all 433 tests green, because no `AuditEntryTests` case passes channel+role or role+message together, and the pipeline tests build their expected side through `AuditEntry.Render` itself. Add an exact-string `AuditEntryTests` case (or cases) that knows every ID, typed as a literal, so the field order `guild, channel, role, message` is pinned between every adjacent pair. Run both mutants against it and record the runs in the PR. Set `tests/test-count-floor.txt` to the final executed count. All of this goes on the existing branch, in the same PR #30.

## Acceptance criteria
- [ ] A new `AuditEntryTests` case calls `AuditEntry.Render` with guild, channel, role and message all non-null and asserts with `Assert.Equal` the whole entry as a literal string typed into the test (for example `"tool=set_channel_permissions level=write guild=111 channel=5001 role=6001 message=7001 outcome=success"`). The expected side is not built with `AuditEntry.Render`, `AppendId`, `Snowflake.Format` or any other production symbol.
- [ ] Use four distinct ID values, so that a swap of any two slots changes the string (two equal values could hide a swap).
- [ ] With O3 (swap `AuditEntry.cs:41` `AppendId(entry, "channel", channelId);` and `:42` `AppendId(entry, "role", roleId);`) the new test goes red; with O4 (swap `:42` and `:43` `AppendId(entry, "message", messageId);`) it goes red; restored, it is green. Also run the guild/channel swap (`:40`/`:41`) and record it; it is expected red already, from `Render_writes_a_success_with_a_channel_and_a_message`. The PR records all three runs, one line each.
- [ ] A test that fails on the parent commit reproduces the bug, at the surface where the symptom is visible, and is committed in the same PR (core/test-per-function). Here "the parent commit" is the mutated `Render`: the new test is red under O3 and O4 and green on `d05771b` plus your change.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)
- [ ] Family sweep: issue #31 holds the Manager's pass (`grep -rn "StringBuilder\|string.Join(" src --include=*.cs`, 4 hits in 3 files at `d05771b`, 1 member: `AuditEntry`). Re-run it at your final head and paste the command and its hit count in the PR. If a new hit appears, classify it the same way.
- [ ] `tests/test-count-floor.txt` equals the executed count at the PR's final head (433 at `d05771b`, plus the tests this round adds). The PR description states the number and the command that reproduces it, and CI on the final head is green at the floor step.
- [ ] At the end of the round, the PR states the real cause of each survivor in one sentence. If it is not `test-gap`, it names the class from `core/bug-report-format` that fits.

## Measured facts
| Fact | Value |
|---|---|
| PR head when this was written | `d05771b` |
| The four slots | `src/Guildsmith.Core/Audit/AuditEntry.cs:40-43`, order `guild`, `channel`, `role`, `message` |
| Render signature | `Render(string toolName, ToolLevel level, ulong? guildId, ulong? channelId, ulong? roleId, ulong? messageId, AuditOutcome outcome)` — `AuditEntry.cs:26` |
| Existing exact cases | `tests/Guildsmith.Tests/Core/Audit/AuditEntryTests.cs` — none passes channel+role or role+message; `:22` passes guild+channel+message |
| Survivor runs (Tester) | O3 full suite 433/433 green; O4 Audit-filtered run 50/50 green |
| Suite at `d05771b` | 433 executed, 0 failed; floor file `tests/test-count-floor.txt` = `433` |

## Skills to read first
- dotnet-mcp/test-runner — the quiet test command; a mutant that does not compile is not a kill
- core/fix-round-gate — the existing tests must not be weakened; the Tester compares the suite hash
- core/test-per-function — when: choosing the expected side ("the expected side is read from the code under test")
- core/keep-tool-output-small — when: running the suite or the mutants
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path or you post a PR comment (`--body-file`)

## What NOT to do
- Do not change `AuditEntry.Render`'s output, its field order, or any production code. This is a test-gap survivor, not a behaviour bug. If the new test shows a behaviour is wrong, stop and say so in the PR.
- Do not rewrite the pipeline tests' expected sides away from `AuditEntry.Render`; the Render tests are where the format is pinned.
- Do not weaken, skip or delete any existing test or theory row.
- Do not act on the Tester's note about the 2-argument `GuardPipeline(options, gateway)` constructor dropping audit entries; it belongs to #6's production construction path, not this round.
- Do not build any MCP tool (#6 onwards), do not lower the floor, and do not touch `.github/` workflows.
- Do not touch `.agentteam/`, `docs/spec.md`, `Directory.Build.props` or `global.json`. This work order and `.agentteam/rounds.jsonl` may look missing or modified in your checkout. That is expected: this file is committed on `main`, not on your branch (read it with `core/round-recovery`).
