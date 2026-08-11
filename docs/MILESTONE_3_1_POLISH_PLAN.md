# Milestone 3.1: Shortcut polish

Status: Phase A implementation and cross-device validation.

Tracking issue: [#5](https://github.com/n8chur/game-reminders/issues/5)

## Goal

Reduce cross-device setup friction and recover safely from unknown spoken aliases without introducing a second reminder store or allowing the Shortcut to modify `games.json`.

This is a post-MVP polish milestone. It does not replace Milestone 4 reliability and packaging or Milestone 5 application polish in the approved MVP specification.

## Phase A: cross-device iCloud layout

Use `iCloud Drive/Shortcuts/Game Reminders` as the authoritative store. The canonical Shortcut resolves both `Game Reminders/games.json` and `Game Reminders/inbox` relative to its built-in iCloud Shortcuts folder instead of embedding external folder bookmarks. The canonical unsigned artifact and source are replaced in place; Git history preserves the prior implementation.

The currently signed distribution remains unchanged until the exact replacement payload passes Mac and iPhone testing and is signed again.

### Acceptance criteria

- A newly imported signed Shortcut runs on a clean Mac and iPhone without repairing folder actions on either device.
- The Windows client reads and writes the same authoritative folder.
- No files are copied, moved, or deleted automatically by the application or Shortcut.
- Existing custom store locations remain supported.
- The workflow uses no macOS-only actions.
- Canonical artifact validation rejects external folder bookmarks and import questions.
- If Apple still persists device-specific state for the built-in Shortcuts folder, the limitation is recorded and the fixed-path change is not distributed.

### Migration boundary

Changing the authoritative root is explicit and user-driven:

1. Stop the Windows app.
2. Verify iCloud has finished synchronizing.
3. Move the complete `Game Reminders` directory as one unit into the iCloud `Shortcuts` directory.
4. Point Windows at the new root.
5. Run the canonical replacement Shortcut and verify one reminder end to end.
6. Retain the old location until the new root is verified; never merge two reminder trees automatically.

Detailed migration and rollback instructions remain part of the PR until the replacement passes.

## Phase B: unknown-alias recovery

When exact normalized matching finds no game, the Shortcut may offer the catalog's canonical game names for explicit selection. Selecting a game must use its stable ID, create the reminder for that game in the same run, and submit an immutable alias request for later Windows review.

The Shortcut must never edit `games.json`.

### Proposed alias-request protocol

Pending files live under a new `alias-requests/inbox` directory and use UUID filenames. A schema-version-1 request contains:

| Field | Meaning |
| --- | --- |
| `schemaVersion` | Protocol version `1` |
| `id` | Request UUID |
| `gameId` | Explicitly selected stable game ID |
| `alias` | Original submitted game text |
| `createdAt` | ISO 8601 creation time |

Alias requests never contain reminder message text.

The final directory names and archive lifecycle are not approved until Core store behavior and collision handling are designed and tested.

### Required Windows behavior

- Validate every request before presenting it.
- Deduplicate by request UUID without overwriting either copy.
- Show the submitted alias and selected canonical game for explicit approval.
- Reject unknown game IDs, blank aliases, malformed files, and normalized collisions visibly.
- Apply an approved alias through the existing atomic `games.json` update path with concurrent-change detection.
- Archive accepted and rejected requests without deleting ambiguous collisions.
- Preserve pending requests across shutdowns, crashes, and sync-provider failures.

### Shortcut behavior

- Zero matches: show the submitted input and offer **Choose a game** or **Cancel**.
- Choosing a game: select from canonical names, retain its stable ID, ask for the reminder message, and create both the reminder and alias request through safe staged writes.
- Canceling or failing any validation: create neither a reminder nor an alias request.
- More than one normalized match remains an error; the Shortcut must not guess.

Atomicity across two iCloud files is not available. The implementation must define a recoverable order and idempotent retry behavior before this phase is coded.

## Phase C: distribution and documentation

- Update the versioned protocol, samples, validators, tests, and `docs/MVP_SPECIFICATION.md`.
- Document portable-layout migration, custom-folder fallback, alias approval, recovery, and rollback.
- Sign the final artifact with Apple's **Anyone** mode.
- Validate the exact repository artifact on clean Mac and iPhone installs, through iCloud sync, and in the Windows client.
- Keep the PR Draft until implementation, CI, signed-artifact validation, and manual cross-device validation are complete.

## Validation plan

On Windows with .NET 10:

```powershell
dotnet restore GameReminders.slnx
dotnet test GameReminders.slnx --configuration Release --no-restore
dotnet build GameReminders.slnx --configuration Release --no-restore
```

Additional regression coverage is required for:

- canonical Shortcut structure and absence of bookmarks/import questions;
- alias-request protocol parsing and validation;
- staged filesystem transitions, duplicates, retryable locks, and archive collisions;
- catalog changes between request discovery and approval;
- normalized alias collisions;
- app shutdown and crash behavior with pending requests;
- exact repository-hosted Shortcut behavior on Mac, iPhone, and Windows.
