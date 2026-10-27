# WO-26: Pin the redaction marker's literal text and the host build failure path (fix round of PR #25)

- Issue: #26
- Also closes: #27
- Branch: feature/dev-token-leak-protection
- Priority: P1
- Cause-class: test-gap
- Parent: #3 / PR #25 (Tester verdict `FAIL`, 2 mutation survivors: `src/Guildsmith.Core/Security/TokenRedactor.cs:15` and `src/Guildsmith.Server/ServerStartup.cs:61`)

## Task
The Tester's battery on PR #25 left two mutants alive, both test gaps; the product code is believed correct. First, every assertion that checks the redaction marker reads its expected value from `TokenRedactor.Marker`, so `Marker = ""` turns the redactor into a silent deleter and all 375 tests stay green. Pin the marker's text with a literal that does not move with the constant, and make the marker-presence assertions fail when the marker is empty. Second, the only host-failure test throws from the `run` delegate, so moving `using IHost host = builder.Build();` out of the `try` in `ServerStartup.RunAsync` goes unnoticed. Add a test that makes `builder.Build()` throw with the token in the exception text and reaches the catch through the existing `RunAsync(stderr, builder, run)` seam. Run the family sweep each issue carries, pin any other member it finds, and set `tests/test-count-floor.txt` to the final executed count. All of this goes on the existing branch, in the same PR #25.

## Acceptance criteria

### #26 — the redaction marker
- [ ] At least one test asserts the marker's exact text as a literal typed into the test (`"[REDACTED]"`), not read from `TokenRedactor.Marker` or any other production symbol. With `TokenRedactor.cs:15` mutated to `Marker = ""` it goes red; restored, it is green. The PR records both runs.
- [ ] The existing marker-presence assertions (`grep -rn "TokenRedactor.Marker" tests`, 18 lines in 4 files at `14c935c`) still mean something when the marker changes. Pick one: they use the literal; or they use one shared test-side constant typed as a literal; or they keep `TokenRedactor.Marker` **and** one test pins that constant to its literal. Say in the PR which one you chose and why. Changing an assertion's expected side is an edit to an existing test, so list every edited assertion in the PR, one line each, so the Tester can tell it from a weakened test. No assertion is removed, and none becomes looser (no `Contains` replacing an `Equal`).
- [ ] Also run `Marker = "x"` (a different non-empty marker) and record the result. The literal test is meant to go red here too.
- [ ] Family sweep: classify every hit of `grep -rn "public const" src --include=*.cs` (6 at `14c935c`) in the PR. Each one is either a member (read on an assertion's expected side, with no literal pin anywhere) or not a member (say why in a few words). Issue #26 holds the Manager's first pass: `TokenRedactor.Marker` is a member; `ToolError.InternalErrorMessage` is unverified. For every member, add a literal pin and record its mutant run (for `InternalErrorMessage`, the mutant is `""`).

### #27 — the host build failure
- [ ] A test reaches the `catch` in `ServerStartup.RunAsync(TextWriter, HostApplicationBuilder, Func<IHost, Task>)` by making `builder.Build()` throw, with the fake token in the exception's message or in its inner exception's message. The test asserts all of these: the exit code is `ServerStartup.HostFailureExitCode`, stderr is not empty, the token is absent from stderr, and a non-secret marker word from that exception is on stderr next to the literal marker text.
- [ ] Its `run` delegate is not what throws. It fails the test if it is ever called, so the test cannot pass through the path the existing test already covers.
- [ ] Move `ServerStartup.cs:61` `using IHost host = builder.Build();` to just before the `try` at :59, and the new test goes red; restore it, and the test is green. The PR records both runs.
- [ ] Family sweep: the sweep in issue #27 (`grep -rnE "^\s*try\s*$" src --include=*.cs`, 2 hits at `14c935c`) leaves one question for you. `ConfigureServices(builder, ...)` (:57) and `new TokenRedactor(...)` (:58) are outside the `try`. Say in the PR whether either one can throw text that carries the token. If one can, move it inside the `try` and pin it the same way. If neither can, say why, one sentence each.

### Both
- [ ] A test that fails on the parent commit reproduces the bug, at the surface where the symptom is visible, and is committed in the same PR (core/test-per-function). Here "the parent commit" is the mutated line: each new test is red under its mutant above and green on `14c935c` plus your change.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)
- [ ] `tests/test-count-floor.txt` equals the executed count at the PR's final head (375 at `14c935c`, plus the tests this round adds). The PR description states the number and the command that reproduces it, and CI on the final head is green at the floor step.
- [ ] At the end of the round, the PR states the real cause of each survivor in one sentence. If it is not `test-gap`, it names the class from `core/bug-report-format` that fits.

## Measured facts
| Fact | Value |
|---|---|
| PR head when this was written | `14c935c` |
| Survivor 1 | `src/Guildsmith.Core/Security/TokenRedactor.cs:15` `public const string Marker = "[REDACTED]";` → `""`; full suite 375/375 green (Tester) |
| Survivor 2 | `src/Guildsmith.Server/ServerStartup.cs:61` `using IHost host = builder.Build();` moved before the `try` at :59; full suite 375/375 green (Tester) |
| Marker reads in tests | `grep -rn "TokenRedactor.Marker" tests` → 18 lines: `Core/Security/TokenRedactorTests.cs` 10, `Server/ServerStartupTests.cs` 6, `Server/Logging/RedactingLoggerProviderTests.cs` 2, `Core/Guards/GuardPipelineTests.cs:587` 1. No test spells `[REDACTED]` |
| The existing host-failure test | `tests/Guildsmith.Tests/Server/ServerStartupTests.cs:230-243`, throws from `run`; builder from `ValidBuilder()`; the fake token is the `Token` constant at :22 |
| Exit codes | `ServerStartup.cs:19` `InvalidConfigurationExitCode = 1`, `:22` `HostFailureExitCode = 2`; the latter is pinned by the literal at `ServerStartupTests.cs:248` |
| Suite at `14c935c` | 375 executed, 0 failed; floor file `tests/test-count-floor.txt` = `375` |

## Skills to read first
- dotnet-mcp/test-runner — the quiet test command; a mutant that does not compile is not a kill
- core/fix-round-gate — the existing tests must not be weakened; the Tester compares the suite hash. This round edits existing assertions on purpose, so list them
- core/test-per-function — when: choosing the expected side of the marker assertions ("the expected side is read from the code under test")
- core/review-differential-and-mutations — when: running your pre-PR mutation pass over the new tests
- core/bug-report-format — when: running and recording the two family sweeps
- core/keep-tool-output-small — when: running the suite or the mutants
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path or you post a PR comment (`--body-file`)

## What NOT to do
- Do not change the logic of `TokenRedactor`, `GuardPipeline`, `RedactingLoggerProvider` or `ServerStartup`, and do not change the marker's value. These are test-gap survivors, not behaviour bugs. There is one allowed exception: under the #27 family-sweep criterion you may move `ConfigureServices` or the redactor construction inside the `try`, if you show it can throw the token. If a new test shows any other behaviour is wrong, stop and say so in the PR.
- Do not weaken, skip or delete any existing test or theory row. Changing an expected side from `TokenRedactor.Marker` to a literal is allowed, and it must be listed.
- Do not act on the Developer's other findings from PR #25. These are: `CheckSuppliedGuild` naming an allowed guild in its `invalid_argument` message; the unpinned wording of the guild_id-addressed `forbidden_guild` refusal (WO-3 allows it to name the caller's own input, and this project asserts codes, not text); the lock in `RedactingLoggerProvider.Write`; and the intermediate commit `ee23ffe` that does not compile (the squash merge removes it). None of them belong to this round.
- Do not build audit logging (#4) or any MCP tool (#6 onwards).
- Do not lower the floor, and do not touch `.github/` workflows.
- Do not touch `.agentteam/`, `docs/spec.md`, `Directory.Build.props` or `global.json`. This work order and `.agentteam/rounds.jsonl` may look missing or modified in your checkout. That is expected: this file is committed on `main`, not on your branch (read it with `core/round-recovery`).
