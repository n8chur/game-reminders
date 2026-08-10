# Milestone 3: iPhone Shortcut

Status: implementation in progress.

## Goal

Add the iPhone half of Game Reminders without introducing a server or a second reminder store. The Shortcut reads the Windows-managed `games.json` catalog from the configured iCloud Drive folder and creates immutable schema-version-1 reminder files in `inbox`.

## Required behavior

1. Accept a game name by voice or typing.
2. Normalize the requested name, every canonical game name, and every alias by removing capitalization, punctuation, symbols, and spacing.
3. Continue only when exactly one game matches. Unknown and ambiguous names must create no file and must explain the failure.
4. Ask for a non-empty reminder message.
5. Create a reminder with a new UUID, the stable game ID, the current display name, the message, and an ISO 8601 creation timestamp.
6. Stage one new reminder with a non-JSON temporary extension in `inbox`, then rename it to `<UUID>.json` without modifying `games.json` or overwriting another reminder.
7. Report success only after the final rename succeeds.

## Deliverables

- A human-readable, reviewable Shortcut definition.
- A repository artifact representing the Shortcut workflow.
- Validation fixtures for exact, alias, unknown, ambiguous, and malformed-catalog cases.
- Installation, configuration, and manual-validation instructions.
- A final Apple-exported/signed `.shortcut` file that can be imported on iPhone.

## Apple signing boundary

Apple validates shared Shortcut files. This repository can generate and audit the workflow source on non-Apple build infrastructure, but the final distributable must be exported or signed by the Shortcuts app/CLI on an Apple device. The source definition and signing instructions will make that final step reproducible and isolated; no signing identity or Apple account material belongs in the repository.

## Safety constraints

- iCloud files remain authoritative.
- The Shortcut never writes `games.json`.
- Unknown or ambiguous game input never creates a reminder.
- Reminder text is not logged.
- Existing files are never overwritten.
- A failed save is visible and is never reported as success.
