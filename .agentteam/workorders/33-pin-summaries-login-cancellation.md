# WO-33: Pin the cancellation token GetGuildSummariesAsync hands to the login, and record the Inspector run

- Issue: #33
- Parent: #6 (PR #32, fix round 1)
- Branch: feature/dev-server-tools-template
- Priority: P1
- Cause-class: test-gap

## Task
The Tester's review of PR #32 at `a9a1d67` found one survivor: in `src/Guildsmith.Discord/DiscordRestApi.cs:39`, `GetGuildSummariesAsync` can hand `CancellationToken.None` to `LoggedInClientAsync` instead of `options.CancelToken` and the whole suite (556) stays green. The code is correct as written; the missing piece is the test, the twin of `DiscordRestApiTests.A_call_waiting_for_the_login_honours_its_cancellation_token`, which pins this only for `GetGuildAsync` (`:47`). Add that test, then walk the rest of the family: every cancellation-token forward this PR adds (15 forwards on 13 lines, listed in #33) must be shown killed by a named test, or gain one. Finally, put the MCP Inspector run the main session made (with the user's approval) into the PR body, because the criterion requires the command and output in the PR and it still says "OPEN, not run". This continues PR #32 on the same branch; do not open a new PR.

## Acceptance criteria

### The survivor
- [ ] A test in `tests/Guildsmith.Tests/Discord/DiscordRestApiTests.cs` calls `GetGuildSummariesAsync` with a pre-cancelled `RequestOptions.CancelToken` while the login is pending, and asserts `OperationCanceledException` **and** that no Discord request was sent. It is committed in this PR.
- [ ] A test that fails on the parent commit reproduces the bug, at the surface where the symptom is visible, and is committed in the same PR (core/test-per-function). This is a test-gap: the code at `a9a1d67` is correct, so "fails on the parent commit" here means **fails under mutation H** (`DiscordRestApi.cs:39` `options.CancelToken` → `CancellationToken.None`). The PR shows that red run (the test name and the one-line `test-summary.py` result) and the green run on the real code.

### The family (`core/clause-inventory-for-new-test-code`, "a fix round opened from a survivor inventories its kind across the whole diff")
- [ ] The PR carries a table with **one row per cancellation-token forward this PR adds under `src/`**, reproduced by the command in #33's family sweep and stating its hit count (15 forwards on 13 lines at `a9a1d67`; if your count differs, say why). Each row has exactly one disposition: `killed by <test name>` (the forward mutated to `CancellationToken.None` / `default`, the named test goes red), `new test <name>` (added in this round, then shown red under the mutation), or `EQUIVALENT: <a mechanism covering every input>`. No fourth disposition, and "the test exists" without a mutation run is not one.
- [ ] Rows the review already killed may cite it: `GuardPipeline.cs:78` (mutation A), `GuardPipeline.cs:99` inner (T6), `DiscordGateway.cs:37` (D), `DiscordRestApi.cs:47` (the existing login-cancellation test). Every other row is run by you.

### The Inspector criterion of WO-6
- [ ] The PR body's Inspector line is replaced by the exact command that ran, the form that worked (CLI mode `--cli`, env via `-e`, no `--`, the built dll run through `dotnet`), and its output: the `tools/list` JSON from `C:\Users\User\AppData\Local\Temp\claude\C--Users-User-source-repos-Guildsmith\2e22aa4a-c5fa-4c73-89f9-70177d890f05\scratchpad\inspector-tools-list.json` and the relevant stderr lines from `C:\Users\User\AppData\Local\Temp\claude\C--Users-User-source-repos-Guildsmith\2e22aa4a-c5fa-4c73-89f9-70177d890f05\scratchpad\inspector-stderr.txt`, including the line `Schema portability: 0 errors, 1 warning across 1 tool.` State that the main session ran it on `a9a1d67` with the user's approval, and that which tool the warning concerns is unverified (likely the `["string","null"]` union on `guild_id`). Use a collapsible `<details>` block if the JSON is long. Do not paste the token value from stderr if one is there; the run used `FAKE-TOKEN-inspector`.
- [ ] The gap-sweep lines 4 and 6 in the PR body are updated to reflect the Inspector evidence, and the PR is no longer described as a draft with an open criterion (mark it ready if it is a draft).

### Bookkeeping
- [ ] `tests/test-count-floor.txt` equals the executed count at the final head, and the PR states the number and the command.
- [ ] One sentence in the PR stating the real cause of the survivor, under "Fix round 1". If it contradicts `Cause-class: test-gap`, say so; your class wins.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)

## Measured facts
| Fact | Value |
|---|---|
| Head under review | `a9a1d67` |
| Survivor | `src/Guildsmith.Discord/DiscordRestApi.cs:39` `LoggedInClientAsync(options.CancelToken)` → `CancellationToken.None`: 556 passed |
| The pinned twin | `DiscordRestApi.cs:47` (`GetGuildAsync`), test `DiscordRestApiTests.A_call_waiting_for_the_login_honours_its_cancellation_token` |
| Observed under the mutation | the login requests (`GET users/@me`, `GET oauth2/applications/@me`) are sent and the call ends in `TimeoutException` from `Discord.Net.Queue.RequestBucket.EnterAsync`, not `OperationCanceledException` (Tester's probe) |
| Test-count floor | `tests/test-count-floor.txt` = `556` |
| Family | 15 forwards on 13 lines, listed in issue #33 with the command |
| Inspector output | two files in the main session's scratchpad, paths in the criterion above; the Tester judged them and they hold |

## Skills to read first
- core/fix-round-gate — the Tester re-verifies that no existing test was weakened
- core/clause-inventory-for-new-test-code — the family table's three dispositions
- core/review-differential-and-mutations — when: running each mutation of the family table
- core/keep-tool-output-small — when: running the suite and the mutations (scoped test file first, `BUILD FAILED` is not a kill)
- core/windows-paths-in-git-bash — when: editing the PR body (`--body-file`, written from Bash)

## What NOT to do
- Do not change `src/Guildsmith.Discord/DiscordRestApi.cs:39` or any other production line to make a test pass; the code is correct. A production change is in scope only if a family row turns out to be a real bug, and then the PR names it.
- Do not edit or weaken any existing test, including `A_call_waiting_for_the_login_honours_its_cancellation_token`; add new ones.
- Do not touch `rate_limited`, the `#8` role-guild resolution, `GetChannelGuildIdAsync` / `GetRoleGuildIdAsync`, or `WriteRequestOptions.Create()`'s missing `CancelToken`. Those are open decisions or later issues, and the Manager handles them at merge.
- Do not run `npx` or the Inspector again; the run that exists is the evidence.
- Do not edit issues #6-#10 or #33, `docs/spec.md`, `.agentteam/`, `.github/workflows/` or the floor mechanism (only the floor value).
