# Game Reminders contributor guidance

## Product invariants

- iCloud files are the authoritative reminder store. Do not introduce a duplicate reminder database.
- A reminder is completed only after an explicit **Dismiss** action successfully archives it.
- **Show on next launch**, closing a window, application shutdown, and crashes must leave the reminder pending.
- Resolve reminders by stable game ID, not by a mutable display name.
- Treat filesystem operations as sync-provider operations: expect temporary locks, retries, duplicates, and concurrent changes.
- Preserve data before convenience. Ambiguous archive collisions or malformed files must fail visibly rather than overwrite or delete data.
- Never include reminder message text in diagnostic logs by default.
- Do not require administrator privileges, inject into games, or interact with anti-cheat systems.

## Architecture

- `GameReminders.Core` owns the versioned JSON protocol and filesystem store.
- `GameReminders.App` is a Windows WPF client and should keep platform-specific behavior out of Core.
- `games.json` is written atomically. Reminder files are immutable while pending and move from `inbox` to `completed` only on dismissal.
- Keep protocol changes backward-aware and update samples, tests, and `docs/MVP_SPECIFICATION.md` when behavior changes.

## Validation

Run on Windows with the .NET 10 SDK:

```powershell
dotnet restore GameReminders.slnx
dotnet test GameReminders.slnx --configuration Release --no-restore
dotnet build GameReminders.slnx --configuration Release --no-restore
```

Add regression tests for protocol validation, filesystem state transitions, process lifecycle behavior, and any bug fixed from review feedback.

## AI attribution

Anyone reading this repository later must be able to tell that an AI produced a change and **which** AI produced it. Treat that as a hard requirement for every artifact an agent can create or edit on GitHub — commits, pull requests, issues, and comments — not only for code.

Two facts belong in every disclosure:

- **Which agent did the work**, named from the fixed vocabulary below.
- **Which GitHub user directed the work.** Refer to that person only by GitHub username, for example `@n8chur`. Never substitute a profile display name, legal name, or first name anywhere in a disclosure.

### Agent names

| Agent | Disclosure name | Commit trailer |
| --- | --- | --- |
| Anthropic's Claude Code | `Claude` | `Co-Authored-By: Claude <noreply@anthropic.com>` |
| OpenAI's Codex | `Codex` | `Co-Authored-By: Codex <noreply@openai.com>` |
| GitHub Copilot | `Copilot` | commits under its own `copilot-swe-agent[bot]` identity; add no trailer |

Use only these names. The list is a fixed vocabulary so that disclosures stay consistent, greppable, and mechanically checkable; add an agent by amending this table in its own pull request rather than inventing a name inline.

Do not add a model version (`Opus 5`, `GPT-5`) or a vendor prefix (`Anthropic`, `OpenAI`). One pull request or issue routinely spans several models, so a version pinned in a durable line goes stale and misattributes part of the work, and the trailer address already records the vendor. When a specific model genuinely matters to a decision, say so in the surrounding prose instead of in the disclosure line.

### Work that spans agents

A single pull request or issue often passes through more than one agent — Codex starts it, Claude finishes it after review. Changing models inside one agent changes nothing, because this vocabulary names agents rather than models. Changing agents does.

An agent that continues another's work adds itself to the existing disclosure and never replaces the name already there:

```
> Implemented by Codex and Claude under @n8chur's direction.
```

Name agents in the order they contributed, joined by `and`, with commas for three or more (`Codex, Claude, and Copilot`). Add yourself if you authored any part of the work as it stands, including revisions made after review; reading or reviewing it is not authoring it.

The line records who was involved, not who did what. Commit trailers already carry that per commit, and when the division of labor matters to a reviewer — one agent wrote the feature, another reworked it after review — describe it in the prose, as the review follow-up sections in this repository already do.

Commits and comments stay single-agent: whichever agent writes one signs it. An issue that a second agent revises keeps its original `Filed by` line and gains an `Edited by` line below it.

### Placement

- Attribute in the artifact itself. Each commit message, PR description, issue, and comment is read on its own, so each carries its own disclosure; a disclosure elsewhere in the thread does not cover it.
- Attribute per agent, as described in **Work that spans agents**.
- Never remove or weaken an existing disclosure. If a human materially rewrites AI output, keep the disclosure and note what the human changed.
- When an action cannot carry a disclosure of its own — applying a label, resolving a review thread — perform it on the same PR or issue where the disclosure already appears, so it stays attributable in context.

### Commits

End every AI-authored commit message with the agent's `Co-Authored-By` trailer from the table above:

```
Co-Authored-By: Claude <noreply@anthropic.com>
```

Keep the directing human as the commit author; the trailer records the agent.

### Pull request descriptions

Place this disclosure immediately below the issue reference, naming the acting agent:

```
> Implemented by Claude under @n8chur's direction.
```

Add the `AI Generated` label to every PR containing AI-authored changes.

### Issues

An agent that files or rewrites an issue discloses that in the issue body, on its own line immediately below the first paragraph or issue reference and before the first section heading:

```
> Filed by Claude under @n8chur's direction.
```

Use `> Drafted by ...` when a human files agent-written text under their own account, and `> Edited by ...` when an agent revises an issue someone else filed — add the line without disturbing existing content. Label AI-written issues `AI Generated` as well.

### Comments and reviews

Agent-written comments — PR discussion replies, review comments, review verdicts, and issue comments — end with the same disclosure on its own line:

```
> Posted by Claude under @n8chur's direction.
```

Review bots that comment from their own bot account already identify themselves and need no added line; an agent posting through a human's account always adds one.

## Pull requests

- Write PR titles as imperative squash-commit subjects.
- Write individual commit messages as concise, descriptive imperative subjects that state the user-visible intent; avoid generic subjects such as "Update files."
- Disclose AI authorship in the description, the commit trailers, and every agent-written comment as described in **AI attribution**, and add the `AI Generated` label.
- Write the description as a durable squash-commit body, including motivation and completed validation.
- Every PR description must include current, concrete instructions for manually validating the user-visible changes. Update those instructions whenever the implementation changes what reviewers should test.
- Before implementing a PR change whose rationale is not already recorded in the PR (for example, a decision made in ChatGPT), add a top-level PR comment describing the planned change and why it is needed.
- When a commit addresses a review discussion or suggestion, reply in that discussion with a link to the fixing commit. If multiple commits collectively address it, link a GitHub comparison whose visible label is the commit range (for example, `abc1234...def5678`) so the complete fix is reviewable as one diff.
- Resolve a review discussion only after its fix is verified. Re-request review after substantive follow-up changes.
- Keep a PR in draft while implementation or CI is incomplete; mark it ready when it is awaiting human review.
