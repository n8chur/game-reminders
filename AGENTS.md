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

## Pull requests

- Before beginning any work that will upload source code or other repository content, confirm that the user has explicitly authorized the target repository, branch or PR, and the intended file/change scope. If that context is not sufficient for Auto-review, stop before editing or publishing and give the user one copy-ready message that supplies the missing authorization. Do not attempt the upload until they send it.
- Write PR titles as imperative squash-commit subjects.
- Write individual commit messages as concise, descriptive imperative subjects that state the user-visible intent; avoid generic subjects such as "Update files."
- Attribute AI-authored changes in every PR description with this disclosure immediately below the issue reference: `> Implemented by OpenAI Codex under <user name>'s direction.`
- Write the description as a durable squash-commit body, including motivation and completed validation.
- Every PR description must include current, concrete instructions for manually validating the user-visible changes. Update those instructions whenever the implementation changes what reviewers should test.
- Before implementing a PR change whose rationale is not already recorded in the PR (for example, a decision made in ChatGPT), add a top-level PR comment describing the planned change and why it is needed.
- When a commit addresses a review discussion or suggestion, reply in that discussion with a link to the fixing commit. If multiple commits collectively address it, link a GitHub comparison whose visible label is the commit range (for example, `abc1234...def5678`) so the complete fix is reviewable as one diff.
- Resolve a review discussion only after its fix is verified. Re-request review after substantive follow-up changes.
- Keep a PR in draft while implementation or CI is incomplete; mark it ready when it is awaiting human review.
