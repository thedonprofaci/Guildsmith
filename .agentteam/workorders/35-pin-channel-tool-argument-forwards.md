# WO-35: Pin every argument the channel tools forward to the service, and record the Inspector run

- Issue: #35
- Parent: #7 (PR #34, fix round 1)
- Branch: feature/dev-channel-tools
- Priority: P1
- Cause-class: test-gap

## Task
The Tester's review of PR #34 at `e98ad06` found five survivors in the tool layer: `CreateChannelTool.cs:43` can drop `position`, and `EditChannelTool.cs:44` can drop `topic`, `position`, `slowmode_seconds` or `nsfw` (each replaced by `null`), with all 732 tests green. The production lines are correct; what is missing is a successful tool call per argument that asserts the value on the Discord.Net properties object the substituted `IDiscordRestApi` callback receives. The rejected-pair tests cannot see a drop, because once the field is gone the empty-edit check answers `invalid_argument` in its place. Add those tests, then walk the family: every argument forward of every `[McpServerTool]` method gets a row. Fix the weak `FirstUnsupported` assertion. Finally, put the MCP Inspector run the main session made on `e98ad06` (with the user's approval) into the PR body. This continues PR #34 on the same branch; do not open a new PR.

## Acceptance criteria

### The survivors
- [ ] `CreateChannelToolTests`: a successful `create_channel` call giving `position` asserts that exact value on the creation properties the substituted creation callback receives. Red under N4b (`CreateChannelTool.cs:43` `position` → `null`).
- [ ] `EditChannelToolTests`: for each of `topic`, `position`, `slowmode_seconds`, `nsfw`, a successful `edit_channel` call on a channel type that carries the field asserts the given value on the properties object the substituted `ModifyAsync` callback receives. Each test is red under its own drop mutation (FS3, FS1, FS2, A2: `EditChannelTool.cs:44` `<field>` → `null`). Use values that cannot be confused with a default: not `0`, not `false`, not an empty string, and a different value per field.
- [ ] A test that fails on the parent commit reproduces the bug, at the surface where the symptom is visible, and is committed in the same PR (core/test-per-function). This is a test-gap: the code at `e98ad06` is correct, so "fails on the parent commit" here means **fails under the survivor's mutation**. For each of the five survivors the PR shows the test name, the one-line `test-summary.py` result of the red run under the mutation, and the green run on the real code.

### The family (`core/clause-inventory-for-new-test-code`, "a fix round opened from a survivor inventories its kind across the whole diff")
- [ ] The PR carries a table with **one row per argument forward** in the six tool methods, reproduced by `grep -rn "_service\.\w*Async(" src/Guildsmith.Server/Tools/` (6 hits at `e98ad06`: 17 tool arguments plus 6 `cancellationToken` forwards = 23 rows; if your count differs, say why). The mutation per row is "the argument → `null`" (`default` for a non-nullable one, `CancellationToken.None` for the token). Each row has exactly one disposition: `killed by <test name>`, `new test <name>` (added in this round, then shown red under the mutation), or `EQUIVALENT: <a mechanism covering every input>`. No fourth disposition; "the test exists" without a mutation run is not one.
- [ ] Rows the review already killed may cite it: `ListChannelsTool.cs:40` `type` → `null` (N4c). A **swap** kill (A1 name↔topic, N4 position↔slowmode_seconds, A7 parent_id↔topic) does not cover a drop row; run the drop.

### The weak assertion
- [ ] `ChannelFieldRulesTests` gets a `FirstUnsupported` case whose first given field is itself unsupported (for example a category given `["topic", "nsfw"]` expecting `topic`), so `givenFields.Skip(1).FirstOrDefault(...)` goes red in `ChannelFieldRulesTests` alone. The PR shows that scoped red run.

### The Inspector criterion of WO-7
- [ ] The PR body's Inspector line ("NOT RUN") is replaced by the exact command that ran (project.md, Commands, the MCP Inspector row), a statement that the main session ran it on `e98ad06` with the user's approval, and its output: the `tools/list` JSON from `C:\Users\User\AppData\Local\Temp\claude\C--Users-User-source-repos-Guildsmith\2e22aa4a-c5fa-4c73-89f9-70177d890f05\scratchpad\inspector34-tools-list.json` (in a collapsible `<details>` block) and the relevant stderr lines from `C:\Users\User\AppData\Local\Temp\claude\C--Users-User-source-repos-Guildsmith\2e22aa4a-c5fa-4c73-89f9-70177d890f05\scratchpad\inspector34-stderr.txt`, including `Schema portability: 0 errors, 13 warnings across 4 tools.` Say that the attribution of the 13 warnings to the 13 nullable union-typed properties is the Tester's inference, unverified (`--strict` not run). Paste no token value; the run used `FAKE-TOKEN-inspector`.
- [ ] Any gap-sweep line in the PR body that cited the missing Inspector run is updated.

### Bookkeeping
- [ ] `tests/test-count-floor.txt` equals the executed count at the final head, and the PR states the number and the command.
- [ ] One sentence in the PR, under "Fix round 1", stating the real cause of the survivors. If it contradicts `Cause-class: test-gap`, say so; your class wins.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)

## Measured facts
| Fact | Value |
|---|---|
| Head under review | `e98ad06` |
| Survivors | N4b `CreateChannelTool.cs:43` `topic, position, cancellationToken` → `topic, null, cancellationToken`; FS3/FS1/FS2/A2 `EditChannelTool.cs:44` `topic` / `position` / `slowmode_seconds` / `nsfw` → `null`; each `PASS  dotnet: 0 failed, 732 passed, 0 skipped  (exit 0)` |
| Why the rejected-pair tests miss it | with the field dropped, `ChannelService.EditChannelAsync`'s empty-edit check (`ChannelService.cs:113`) answers `invalid_argument` before the field table does |
| Already pinned below the tool | gateway `DiscordGatewayTests.EditChannelAsync_sets_only_the_<field>_…`, service `ChannelServiceTests.EditChannelAsync_accepts_a_call_giving_only_the_<field>` — the gap is the tool layer only |
| Weak assertion | `FirstUnsupported_names_the_first_given_field_the_kind_cannot_carry`: only positive input `["position","parent_id","nsfw"]`; `Skip(1)` mutant 9/9 green in its own file |
| Family | `grep -rn "_service\.\w*Async(" src/Guildsmith.Server/Tools/` → 6 hits, listed in #35 |
| Test-count floor | `tests/test-count-floor.txt` = `732` |
| Inspector output | the two scratchpad files named above; the Tester judged them and they meet WO-7's criterion |

## Skills to read first
- core/fix-round-gate — the Tester re-verifies that no existing test was weakened
- core/clause-inventory-for-new-test-code — the family table's three dispositions
- core/review-differential-and-mutations — when: running each drop mutation of the family table
- core/keep-tool-output-small — when: running the suite and the mutations (scoped test file first, then the whole project for a row that stays green; `BUILD FAILED` is not a kill)
- core/windows-paths-in-git-bash — when: editing the PR body (`--body-file`, written from Bash)

## What NOT to do
- Do not run `npx` or the MCP Inspector. This round changes tests only, so the tool surface the Inspector saw on `e98ad06` is unchanged and that run is the evidence. If you conclude a production line under `src/` must change (a family row that turns out to be a real bug), make the change, name it in the PR, and state that the Inspector run is then on an older head; do **not** re-run it yourself — the main session decides that with the user.
- Do not change `CreateChannelTool.cs`, `EditChannelTool.cs` or any other production line to make a test pass; the code is correct.
- Do not edit or weaken any existing test, including the rejected-pair tests; add new ones.
- Do not reorder `ChannelService.CheckParentAsync`'s checks (#37), add argument range checks or map a Discord 400 (#36), or reduce `edit_channel`'s GET count. Those are separate issues.
- Do not edit issues #7-#10 or #35-#37, `docs/spec.md`, `.agentteam/`, `.github/workflows/` or the floor mechanism (only the floor value).
- `.agentteam/rounds.jsonl` showing as modified in your checkout is expected, and this work order is on `main` only (read it from `origin/main`, `core/round-recovery`); neither belongs in this PR.
