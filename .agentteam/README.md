# .agentteam/

The operational state of **this game project**. Version-controlled, in the project's own
repo, because the retro works from it.

| Path | What it is | Who writes it |
|---|---|---|
| `project.md` | The project's living state: framework, scope, test and build commands, current milestone. Created from the template at bootstrap. | Manager |
| `workorders/` | Issued work orders, named `<issue-number>-<slug>.md`. The Developer works from these, not from the issue directly. | Manager |
| `retro/` | Retro notes, named `<date>-<slug>.md`. New skills are born from these. | Manager |

## What is NOT here

**The backlog** — that is the GitHub issue list (`gh issue list`). Deliberately not duplicated
into a file: of two sources of truth, one always goes stale.

**The skills** — those live in the team's shared home (the `GameDevAgents` repo), because
learning has to accumulate across projects. A skill placed here would not be found by the next
project.
