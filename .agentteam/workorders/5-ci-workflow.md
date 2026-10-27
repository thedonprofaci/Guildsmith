# WO-5: CI workflow with test-count floor, format check, Dependabot and CodeQL

- Issue: #5
- Branch: chore/dev-ci-workflow
- Priority: P1

## Task
Add the project's first automated gate: a GitHub Actions workflow that restores, builds, tests and format-checks the solution on every pull request and on every push to `main`, so the suite is no longer only as good as the last local session. Because `dotnet test` exits 0 when it discovers zero tests, the job must also read the executed-test count out of a `.trx` result file and fail when it is below a minimum committed in the repository; that reader is code of this round and gets its own tests, which the CI job runs too. Add Dependabot for `nuget` and `github-actions`, and a CodeQL analysis for C#. This PR is the first one CI will see, so its own checks are the evidence: the PR must show the floor job failing once (floor set above the real count) and then passing.

## Acceptance criteria
- [ ] `.github/workflows/ci.yml`, triggered on `pull_request` and on `push` to `main`, runs on `ubuntu-latest` and contains, in this order:
  - `actions/setup-dotnet` installing the SDK from the committed `global.json` through the action's `global-json-file` input (see Measured facts), not through a hard-coded `dotnet-version`;
  - `dotnet restore Guildsmith.sln --locked-mode`;
  - `dotnet build Guildsmith.sln -c Release --no-restore`;
  - `dotnet test Guildsmith.sln -c Release --no-build` with a `.trx` logger writing to a known results directory;
  - the test-count floor check (next criterion);
  - `dotnet format Guildsmith.sln --verify-no-changes --no-restore`.
- [ ] **The test-count floor is an instrument, not a number in a comment.** One committed file holds the minimum (a single integer, its path yours, e.g. `tests/test-count-floor.txt`). A checker reads the `.trx` produced by the run and fails the job (non-zero exit, a one-line stderr message naming the found count and the floor) when the `executed` count of the `.trx` `Counters` element is below the floor. It **fails closed**: no `.trx` found, more than one where exactly one is expected (or define and test the rule you choose for several), a missing `Counters` element, a missing or non-numeric `executed` attribute, and a missing, empty or non-numeric floor file all fail the job — none of them is read as zero-and-pass or as no-floor.
- [ ] The floor value committed at the end of the PR equals the suite's executed count at the PR's head (124 today, plus whatever tests this PR adds). The PR description states that count and the command that reproduces it.
- [ ] **The PR shows the floor failing once.** One commit on the branch sets the floor above the real count; its CI run is red at the floor step (not at an earlier step); the next commit restores the real value and the run is green. The PR description links both runs.
- [ ] The checker's tests run in the same CI job as the rest of the suite (they are part of `Guildsmith.sln` or are invoked by an explicit workflow step whose failure fails the job). A checker whose tests CI never runs does not meet this criterion.
- [ ] The `.trx` file and its results directory are not committed: after a local run of the CI test command, `git status --short` prints nothing. The workflow uploads the `.trx` as an artifact (`actions/upload-artifact`, `if: always()`).
- [ ] `.github/dependabot.yml` with `version: 2`, one `nuget` entry and one `github-actions` entry, both `directory: "/"`, with a schedule. Because every project commits a `packages.lock.json` and CI restores with `--locked-mode`, state in the PR description whether a Dependabot NuGet PR updates the lock files (look this up in the Dependabot documentation, do not assume either way).
- [ ] `.github/workflows/codeql.yml` (or a CodeQL job in `ci.yml`) analysing `csharp` on `pull_request` and `push` to `main`, using `github/codeql-action` `init` / `analyze` with a build the analyser can observe (an explicit `dotnet build` of the solution or `build-mode` as the action documents for C#). The PR's own checks list shows the CodeQL run green.
- [ ] The PR description contains the verbatim output of `gh api repos/thedonprofaci/Guildsmith --jq .security_and_analysis`, showing `secret_scanning` and `secret_scanning_push_protection` as `enabled`.
- [ ] Every function written or changed in this task has its own test, and the test fails without the implementation (core/test-per-function) — the `.trx` checker counts as a function, and each fail-closed case above has its own test.
- [ ] The checker is test-side code delivered by a gate round: the PR carries the per-clause inventory of `core/clause-inventory-for-new-test-code` over it (every clause `killed by <test>` / `deleted` / `EQUIVALENT: <total mechanism>`).

## Measured facts
| Fact | Value |
|---|---|
| Suite today | `dotnet test Guildsmith.sln --nologo -v q` → `Passed!  - Failed: 0, Passed: 124, Skipped: 0, Total: 124` (`Guildsmith.Tests.dll`, net8.0), measured on `7de40cd`, 2026-10-27 |
| Format gate today | `dotnet format Guildsmith.sln --verify-no-changes` exits `0` on `7de40cd` |
| `actions/setup-dotnet` | latest release `v6.0.0`; its `action.yml` declares input `global-json-file` ("Optional global.json location, if your global.json isn't located in the root of the repo.", `required: false`) — read from `gh api repos/actions/setup-dotnet/contents/action.yml`, 2026-10-27 |
| `global.json` | `"version": "9.0.300"`, `"rollForward": "latestFeature"`; local SDK is `9.0.318` only. CI resolves its own 9.0 feature band from this file; projects target `net8.0`. If the net8.0 test host needs the .NET 8 runtime on the runner, install it as an additional `dotnet-version: 8.0.x` in the same setup step and say so in the PR |
| Repository security settings | `secret_scanning: enabled`, `secret_scanning_push_protection: enabled`, `dependabot_security_updates: disabled`; repo is `public` (`gh api repos/thedonprofaci/Guildsmith`, 2026-10-27). Nothing needs to be switched on for the secret-scanning criterion |
| CodeQL default setup | `gh api repos/thedonprofaci/Guildsmith/code-scanning/default-setup --jq .state` → `not-configured`, so a workflow-based (advanced) CodeQL setup does not conflict with it |
| `.gitignore` | from `dotnet new gitignore`; contains `test-results/` (line 423) and the MSTest results block (around line 46). Verify the results directory you choose is actually ignored rather than relying on this |
| Test project layout | one test project `tests/Guildsmith.Tests/`; helpers live in `tests/Guildsmith.Tests/Support/` (`ServerProcess.cs` + `ServerProcessTests.cs`); test files mirror `src/` paths minus the `Guildsmith.` prefix (`project.md`, Test convention) |
| Current `HEAD` of `main` | `7de40cd` |

## Skills to read first
- dotnet-efcore/project-bootstrap — the "dotnet test exits 0 on zero tests" section and the floor it prescribes
- core/fail-closed-guards — when: writing the checker; "The runner's discovery is a guard too" says what the floor does not catch
- core/clause-inventory-for-new-test-code — when: writing and reviewing the `.trx` checker
- core/coverage-stops-at-the-host-language — when: deciding what evidence covers the YAML files (no unit test sees them; the PR's own CI runs are the evidence)
- core/normalise-line-endings-in-fixtures — when: a checker test embeds a multi-line `.trx` fixture
- core/keep-tool-output-small — when: running the suite, the format check or a mutation battery
- core/windows-paths-in-git-bash — when: a Bash command carries a Windows path, or you post the PR body (`--body-file`)

## What NOT to do
- Do not create the release workflow, tag triggers, packaging or `dotnet tool` settings (#12, phase 2).
- Do not change branch protection, rulesets, or any repository setting through `gh api` (out of scope of #5; settings are the user's). Do not enable CodeQL default setup — it conflicts with a workflow-based setup.
- Do not change product code under `src/`, `Directory.Build.props`, `global.json`, `.editorconfig` or package versions to make CI green. If CI fails on something the local run does not show, report it in the PR description and stop at that step, rather than relaxing a gate (`--no-locked-mode`, `continue-on-error`, a lower floor, `|| true`).
- Do not compare the floor against the `.trx` `total` or `passed` counter alone, and do not count tests by grepping the console output of `dotnet test`.
- Do not add a second language toolchain to the job just for the checker (no Node or a separate Python test run); write the checker so that the existing `dotnet test` run tests it.
- Do not touch `.agentteam/` or `docs/spec.md`.
