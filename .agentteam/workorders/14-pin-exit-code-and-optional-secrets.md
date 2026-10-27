# WO-14: Pin the clean-session exit code and the optional user secrets (fix round of PR #13)

- Issue: #14
- Also closes: #15
- Branch: feature/dev-stdio-mcp-host
- Priority: P1
- Cause-class: test-gap
- Parent: #1 / PR #13 (Tester verdict `FAIL`, 2 mutation survivors in `src/Guildsmith.Server/ServerStartup.cs`)

## Task
The Tester's battery on PR #13 left two mutants alive in `ServerStartup.cs`. The product code is believed correct for both: the server exits 0 after a clean session (measured by hand) and user secrets are added with `optional: true`. What is missing are tests that hold those two lines. Add them on the existing branch `feature/dev-stdio-mcp-host`, in the same PR #13, so that each mutant below turns at least one test red. Change product code only if a new test shows the behaviour is actually wrong, and say so in the PR if it does.

## Acceptance criteria

### #14 — Pin the stdio server's exit code after a clean session
- [ ] A process test runs a clean stdio session against the built server (valid configuration, `DOTNET_ENVIRONMENT=Production`, stdin closed) and asserts `ExitCode` is exactly `0`. Extending `RunAsync_writes_only_json_rpc_messages_to_stdout_during_a_session` (it already waits for the exit at `tests/Guildsmith.Tests/Server/ProgramTests.cs:100`) is acceptable.
- [ ] With `return 0;` at `ServerStartup.cs:42` changed to `return 1;`, that test goes red. The PR records the mutated run (red, test name) and the restored run (green).

### #15 — Pin user secrets as optional in Development
- [ ] A process test starts the built server with `DOTNET_ENVIRONMENT=Development`, a fresh scratch `APPDATA` holding **no** `Microsoft/UserSecrets/<id>/secrets.json`, and a valid configuration in environment variables. It asserts the server exits `0` at stdin EOF and that stderr contains neither `secrets.json` nor `Unhandled exception`.
- [ ] `AddConfigurationSources_reads_user_secrets_then_environment_variables_in_development` (`tests/Guildsmith.Tests/Server/ServerStartupTests.cs:53`) also asserts that the user-secrets `JsonConfigurationSource` has `Optional == true`.
- [ ] With `optional: true` at `ServerStartup.cs:60` changed to `optional: false`, at least one of the two tests above goes red. The PR records the mutated run (red, test names) and the restored run (green).

### Both
- [ ] A test that fails on the parent commit reproduces the bug, at the surface where the symptom is visible, and is committed in the same PR (core/test-per-function). Here the "parent commit" is the mutated line: each new or extended test must be red under its mutant above and green on `eaa84e0` plus your change.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function)
- [ ] The PR carries the family-sweep result: the two `git grep` commands from #14 and #15, with their hit counts (today: one unasserted `WaitForExitAsync` at `ProgramTests.cs:100`; one `optional:` in `src`, at `ServerStartup.cs:60`).
- [ ] At the end of the round, one sentence in the PR stating the real cause of the two survivors. If it is not `test-gap`, name the class from `core/bug-report-format` that fits.

## Measured facts
| Fact | Value |
|---|---|
| Survivor 1 | `src/Guildsmith.Server/ServerStartup.cs:42` `return 0;` (after `await host.RunAsync();`) |
| Survivor 2 | `src/Guildsmith.Server/ServerStartup.cs:60` `configuration.AddUserSecrets(typeof(ServerStartup).Assembly, optional: true, reloadOnChange: false);` |
| Session test exit wait, no assertion | `tests/Guildsmith.Tests/Server/ProgramTests.cs:100` `await server.WaitForExitAsync(cancel.Token);` |
| Existing scratch-`APPDATA` pattern to reuse | `ProgramTests.RejectedVariablesWithSecretsFileAsync` (around `ProgramTests.cs:163`): temp directory as `APPDATA`, passed through `ServerProcess.StartInfo(settings)`. It creates the secrets directory; the new test must **not** create it |
| Development unit test, `Path` only | `ServerStartupTests.cs:53-64`, asserts `JsonConfigurationSource.Path == "secrets.json"` |
| Tester's crash under the mutant | `System.IO.FileNotFoundException: The configuration file 'secrets.json' was not found and is not optional.` |
| Suite on `eaa84e0` | 123 tests, 0 failed |

## Skills to read first
- dotnet-mcp/test-runner — the quiet test command; a mutant that does not compile is not a kill
- core/fix-round-gate — the existing tests must not be weakened; the Tester compares the suite hash
- core/review-differential-and-mutations — when: running your pre-PR mutation pass over the two lines
- core/keep-tool-output-small — when: running the suite or the two mutants
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path or you post a PR comment

## What NOT to do
- Do not change `ServerStartup.cs` or any other product file unless a new test proves the behaviour wrong. These are test-gap survivors, not product bugs.
- Do not weaken, skip or delete any existing test, including the Tester's two tests from `eaa84e0`.
- Do not change how the user-secrets path is resolved. The Tester noted that, when the secrets directory does not exist, the framework appears to look for `secrets.json` next to the binary (unverified). That is not in this round's scope; record what you observe in the PR if you see it, and nothing more.
- Process tests must not inherit the developer's environment: pass every variable explicitly, and use a fresh scratch `APPDATA` (and delete it afterwards).
- Do not touch `.agentteam/`, `docs/spec.md`, `Directory.Build.props` or `global.json`. `.agentteam/rounds.jsonl` and this work order showing as untracked or missing in your checkout is expected: this file is committed on `main`, not on your branch (read it with `core/round-recovery`).
