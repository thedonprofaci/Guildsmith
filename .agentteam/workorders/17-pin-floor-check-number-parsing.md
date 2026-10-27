# WO-17: Pin the floor check's number parsing on the inputs its comments say it rejects (fix round of PR #16)

- Issue: #17
- Branch: chore/dev-ci-workflow
- Priority: P1
- Cause-class: test-gap
- Parent: #5 / PR #16 (Tester verdict `FAIL`, 3 open mutation survivors in `tests/Guildsmith.TestCountFloor/FloorCheck.cs`)

## Task
The Tester's battery on PR #16 left three mutants alive in the test-count floor checker. The checker's behaviour is believed correct. What is missing are tests for the inputs its own comments name. `FloorCheck.cs:140` says `"1,000"` and `" 5"` are rejected, and the `ReadFloor` docstring (`:58-60`) says surrounding whitespace on the floor file is allowed. Only the sign dimension of `NumberStyles.None` and the full removal of `Trim` are pinned. Add the tests on the existing branch `chore/dev-ci-workflow`, in the same PR #16, so that each mutant below turns at least one test red. Close the one product-side member the family sweep found by adding a single test-data line. Then set `tests/test-count-floor.txt` to the final executed count.

## Acceptance criteria

### #17 — the floor checker
- [ ] With `FloorCheck.cs:142` `NumberStyles.None` changed to `NumberStyles.AllowThousands`, at least one test goes red. A floor file holding `1,000` is rejected (`ReadFloor` throws `FloorCheckException`), and an `executed="1,000"` attribute is rejected (`ReadExecuted` throws).
- [ ] With `FloorCheck.cs:142` `NumberStyles.None` changed to `NumberStyles.AllowLeadingWhite`, at least one test goes red. An `executed=" 5"` attribute is rejected by `ReadExecuted`.
- [ ] With `FloorCheck.cs:69` `.Trim()` changed to `.TrimEnd()`, at least one test goes red. A floor file holding `" 124"` (leading whitespace) is read as exactly `124`.
- [ ] The PR records, for each of the three mutants, the mutated run (red, with the test names) and the restored run (green).
- [ ] After this round, the comment at `:140` and the docstring at `:58-60` name no input that no test feeds the parser. If you narrow the comment instead of adding a test, the PR names the input and says why.
- [ ] The clause inventory in the PR description is corrected. The rows for `:69 .Trim()` and `:142 NumberStyles.None` name the test that kills **each** dimension (sign, thousands separator, leading whitespace, and trailing whitespace where it applies), not one test for the whole clause.
- [ ] `tests/test-count-floor.txt` equals the executed count at the PR's final head (156 after `e8b0e16`, plus the tests this round adds). The PR description states the number and the command that reproduces it, and CI on the final head is green at the floor step.

### Family member — `MAX_MESSAGES_PER_READ` (`GuildsmithOptionsReader.cs:155`)
- [ ] `Read_rejects_a_non_numeric_message_limit` (`tests/Guildsmith.Tests/Core/Configuration/GuildsmithOptionsReaderTests.cs:171-182`) gets a thousands-separator case that would parse **in range** under `AllowThousands`. For example `"1,0"`, which would read as 10. With `:155` `NumberStyles.None` changed to `NumberStyles.AllowThousands`, that case goes red. The mutated and restored runs are recorded in the PR.
- [ ] The issue's sweep marks `ALLOWED_GUILD_IDS` (`GuildsmithOptionsReader.cs:89-92`) as no live member, but that rests on reasoning only. Run the two mutants at `:92` (`None` → `AllowThousands`, `None` → `AllowLeadingWhite`) and record the result. If one survives, write either a test or an `EQUIVALENT:` line naming the mechanism that covers every input (the split on `,`, then `Trim`).

### Both
- [ ] A test that fails on the parent commit reproduces the bug, at the surface where the symptom is visible, and is committed in the same PR (core/test-per-function). Here "the parent commit" is the mutated line: each new test is red under its mutant above and green on `e8b0e16` plus your change.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)
- [ ] The PR carries the family-sweep result: the `git grep` command from #17 and its hit count (today 7 lines in 3 files, three members, listed in the issue).
- [ ] At the end of the round, the PR states the real cause of the three survivors in one sentence. If it is not `test-gap`, it names the class from `core/bug-report-format` that fits.

## Measured facts
| Fact | Value |
|---|---|
| Survivor 1 | `tests/Guildsmith.TestCountFloor/FloorCheck.cs:69` `string text = File.ReadAllText(path).Trim();` → `.TrimEnd()` |
| Survivors 2 and 3 | `FloorCheck.cs:142` `int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);` → `AllowThousands` / `AllowLeadingWhite` |
| Comment the tests must back | `FloorCheck.cs:140` `// NumberStyles.None: digits only, so "-1", "+5", "1,000" and " 5" are rejected rather than read.` |
| Existing `ReadFloor` tests | `tests/Guildsmith.Tests/TestCountFloor/FloorCheckTests.cs:35-77`: trailing newline, missing, empty, whitespace-only, non-numeric, signed, zero, one. No leading whitespace, no thousands separator |
| Existing `ReadExecuted` tests | `FloorCheckTests.cs:120-172`. No `executed` value with whitespace or a thousands separator |
| Tester's filtered run | `dotnet test tests/Guildsmith.Tests --nologo -v q --filter FullyQualifiedName~Guildsmith.Tests.TestCountFloor` → 31 tests before `e8b0e16` |
| Suite at `e8b0e16` | 156 executed, 0 failed (local: `test-count floor: executed 156, floor 155`) |
| Floor file at `e8b0e16` | `tests/test-count-floor.txt` = `155` |
| Product-side member's invalid cases | `"abc"`, `""`, `"1.5"`, `"-1"`, `"+5"`, `" 5"`, `"5 "`, `"99999999999"` (`GuildsmithOptionsReaderTests.cs:172-179`) |

## Skills to read first
- dotnet-mcp/test-runner — the quiet test command; a mutant that does not compile is not a kill
- core/fix-round-gate — the existing tests must not be weakened; the Tester compares the suite hash
- core/clause-inventory-for-new-test-code — when: correcting the inventory rows for `:69` and `:142`
- core/review-differential-and-mutations — when: running your pre-PR mutation pass
- core/keep-tool-output-small — when: running the suite or the mutants
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path or you post a PR comment (`--body-file`)

## What NOT to do
- Do not change the logic of `FloorCheck.cs` or of `GuildsmithOptionsReader.cs`. These are test-gap survivors, not behaviour bugs. You may edit the comment at `FloorCheck.cs:140` or `:58-60` only under the narrowing criterion above. If a new test shows the behaviour is actually wrong, stop and say so in the PR.
- Do not weaken, skip or delete any existing test, including the Tester's `Run_passes_above_the_floor_and_names_each_count_in_its_own_slot` from `e8b0e16`.
- Do not lower the floor, add `continue-on-error`, or touch `.github/` workflows, Dependabot or CodeQL.
- Do not widen the floor's contract, for example to catch a partial drop that stays above the floor. That is a known limit and outside this round's scope.
- Do not touch `.agentteam/`, `docs/spec.md`, `Directory.Build.props` or `global.json`. This work order and `.agentteam/rounds.jsonl` may look missing or modified in your checkout. That is expected: this file is committed on `main`, not on your branch (read it with `core/round-recovery`).
