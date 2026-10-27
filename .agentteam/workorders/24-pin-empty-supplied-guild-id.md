# WO-24: Pin an empty guild_id supplied next to a channel, role or message ID to invalid_argument (fix round of PR #23)

- Issue: #24
- Branch: feature/dev-guard-pipeline
- Priority: P1
- Cause-class: test-gap
- Parent: #2 / PR #23 (Tester verdict `FAIL`, 1 mutation survivor at `src/Guildsmith.Core/Guards/GuardPipeline.cs:74`)

## Task
The Tester's battery on PR #23 left one mutant alive in the guard pipeline: at `GuardPipeline.cs:74`, `target.GuildId is not null` can become `!string.IsNullOrEmpty(target.GuildId)` with the whole suite green. The product code is believed correct today: an empty supplied `guild_id` reaches `Snowflake.ParseArgument` and is rejected. What is missing is a test that sends an empty string as the supplied `guild_id` on the ID-addressed path, the only input that tells the two spellings apart. Add those cases on the existing branch `feature/dev-guard-pipeline`, in the same PR #23, then widen the family sweep to every null check on a string argument in `src/Guildsmith.Core/` and pin any other unpinned member the same way. Finally set `tests/test-count-floor.txt` to the final executed count.

## Acceptance criteria

### #24 — the supplied guild_id on the ID-addressed path
- [ ] `MalformedTargets()` (or a sibling theory in the same file) carries an empty supplied `guild_id` for each ID-addressed kind: `ToolTarget.Channel("5001", "")`, `ToolTarget.Role("6002", "")`, and a message target with a valid channel and message ID plus `""` as the guild. Each gives `invalid_argument` and `Assert.Empty(_gateway.ReceivedCalls())`.
- [ ] With `GuardPipeline.cs:74` `target.GuildId is not null` changed to `!string.IsNullOrEmpty(target.GuildId)`, at least one of those cases goes red; restored, all are green. The PR records both runs (the mutated run with the red test names, and the restored run).
- [ ] Also run `GuardPipeline.cs:74` → `!string.IsNullOrWhiteSpace(target.GuildId)` and record the result. A whitespace-only supplied `guild_id` (for example `" "`) gets a case if that mutant survives.

### Family sweep (widened)
- [ ] Run `grep -rnE "is (not )?null" src/Guildsmith.Core --include=*.cs` (11 lines at `e3594d4`) and classify each hit in the PR: a string argument where `""` must count as supplied (a member of this family) or not this shape (say why in a few words). For every member, name the test that sends `""` there, or add one. The Tester's narrower sweep found 2 members: `GuildArgument.cs:22` (pinned, MU3) and `GuardPipeline.cs:74` (this bug).

### Both
- [ ] A test that fails on the parent commit reproduces the bug, at the surface where the symptom is visible, and is committed in the same PR (core/test-per-function). Here "the parent commit" is the mutated line: each new case is red under its mutant above and green on `e3594d4` plus your change.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)
- [ ] `tests/test-count-floor.txt` equals the executed count at the PR's final head (300 at `e3594d4`, plus the cases this round adds). The PR description states the number and the command that reproduces it, and CI on the final head is green at the floor step.
- [ ] At the end of the round, the PR states the real cause of the survivor in one sentence. If it is not `test-gap`, it names the class from `core/bug-report-format` that fits.

## Measured facts
| Fact | Value |
|---|---|
| PR head when this was written | `e3594d4` |
| Survivor | `src/Guildsmith.Core/Guards/GuardPipeline.cs:74` `if (target.GuildId is not null)` → `if (!string.IsNullOrEmpty(target.GuildId))`; `GuardPipelineTests` 0 failed, 43 passed |
| Tester's control | adding `ToolTarget.Channel("5001", ""),` to `MalformedTargets()`: original 44 passed, mutant 1 failed in `RunAsync_gives_invalid_argument_for_a_malformed_id_and_makes_no_gateway_call` |
| Test data today | `tests/Guildsmith.Tests/Core/Guards/GuardPipelineTests.cs:250-262` (`MalformedTargets`): supplied guilds `"0"`, `" 222"`, `"x"` only. All three are rejected by the parser whichever way line 74 is written |
| Filtered run | `dotnet test tests/Guildsmith.Tests --nologo -v q --filter FullyQualifiedName~GuardPipelineTests` → 43 tests at `e3594d4` |
| Suite at `e3594d4` | 300 executed, 0 failed; floor file `tests/test-count-floor.txt` = `300` |
| Already pinned twin | `src/Guildsmith.Core/Arguments/GuildArgument.cs:22`, killed by dev mutation MU3 with `""` |

## Skills to read first
- dotnet-mcp/test-runner — the quiet test command; a mutant that does not compile is not a kill
- core/fix-round-gate — the existing tests must not be weakened; the Tester compares the suite hash
- core/review-differential-and-mutations — when: running your pre-PR mutation pass over the new cases
- core/bug-report-format — when: running and recording the widened family sweep
- core/keep-tool-output-small — when: running the suite or the mutants
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path or you post a PR comment (`--body-file`)

## What NOT to do
- Do not change the logic of `GuardPipeline.cs`, `GuildArgument.cs` or `Snowflake.cs`. This is a test-gap survivor, not a behaviour bug. If a new test shows the behaviour is actually wrong, stop and say so in the PR.
- Do not weaken, skip, reorder away or delete any existing test or theory row.
- Do not act on the Developer's other findings from PR #23 (role lookup by ID, `list_guilds` target kind, the foreign guild ID in the `forbidden_guild` message). They are recorded on #6 and #3 and belong to those rounds.
- Do not lower the floor, and do not touch `.github/` workflows.
- Do not touch `.agentteam/`, `docs/spec.md`, `Directory.Build.props` or `global.json`. This work order and `.agentteam/rounds.jsonl` may look missing or modified in your checkout. That is expected: this file is committed on `main`, not on your branch (read it with `core/round-recovery`).
