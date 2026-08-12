# Game Reminders iPhone Shortcut

This directory contains the auditable source definition for the Milestone 3 iPhone Shortcut. The Shortcut reads the Windows-managed catalog from iCloud Drive and writes new reminder files; it never edits `games.json`.

## Artifact status

`GameReminders.shortcut-source.json` is a platform-neutral, ordered definition of the workflow. `GameReminder.cherri` is the auditable native source, and `GameReminder-unsigned.shortcut` is the generated unsigned property-list artifact validated by CI.

Apple will not import the unsigned artifact directly. `Game Reminder.shortcut` is the currently signed distribution; whenever the unsigned payload changes, that signed file remains intentionally unchanged until the replacement passes cross-device testing and is signed with **Anyone** sharing on a Mac. Apple documents [file export from iPhone and iPad](https://support.apple.com/guide/shortcuts/share-shortcuts-apdf01f8c054/ios) and [validation/signing of shared files](https://support.apple.com/guide/shortcuts-mac/run-shortcuts-from-the-command-line-apd455c82f02/mac).

## Configuration

The canonical Shortcut uses the fixed nested folder `iCloud Drive/Shortcuts/Game Reminders`. Both `games.json` and `inbox` are resolved relative to Shortcuts' built-in iCloud container, so the workflow contains no imported folder bookmark or setup question. Nothing is stored at the root of the `Shortcuts` directory.

The authoritative folder must contain `games.json`. The Shortcut creates `inbox` on first use if it is missing; Windows creates the remaining managed folders as needed:

```text
iCloud Drive/
└── Shortcuts/
    └── Game Reminders/
        ├── games.json
        ├── inbox/
        ├── completed/
        └── invalid/
```

The Windows app must be pointed at that same `Game Reminders` folder and it should be marked **Always keep on this device**. The folder name is fixed because a user-selected external folder is represented by a device-specific bookmark that does not reliably transfer between Mac and iPhone. No server URL, account, token, or duplicate database is used.

## Human-readable workflow

1. Get `games.json` from `iCloud Drive/Shortcuts/Game Reminders`. If it cannot be read, show an error and stop.
2. Parse the file as a dictionary. Require `schemaVersion` to equal `1` and `games` to be a list; otherwise show an error and stop.
3. Read the canonical `name` from every registered game and immediately present all names in one native, single-selection **Choose from List** interface.
4. Resolve the selected canonical name back to exactly one catalog entry and retain that entry's stable `id`. Canceling stops the Shortcut. If no entry or more than one entry has the selected name, show an error and stop; duplicate canonical names must be renamed on Windows. The Shortcut does not ask for, dictate, normalize, or alias-match a game name.
5. Ask **What should I remind you?** for text. Reject an empty or whitespace-only answer.
6. Generate a UUID and capture the current date in ISO 8601 form.
7. Build a dictionary with exactly these fields:

   | Key | Value |
   | --- | --- |
   | `schemaVersion` | Number `1` |
   | `id` | Generated UUID |
   | `gameId` | Selected game's stable `id` |
   | `gameNameAtCreation` | Selected game's current canonical `name` |
   | `message` | Reminder text |
   | `createdAt` | ISO 8601 current date |

8. Serialize the dictionary as JSON. Create or reuse `inbox` beneath the same fixed `iCloud Drive/Shortcuts/Game Reminders` folder; save `<UUID>.tmp` in the Shortcut's private iCloud staging folder with overwrite disabled; move the completed temporary file into `inbox`; then rename it to `<UUID>.json`. A failed operation remains visible and is never reported as success.
9. Only after the final rename succeeds, show **Reminder saved for <game name>.**

The temporary extension prevents the Windows scanner from treating an incompletely saved file as a reminder. The staging filename intentionally has no leading dot so macOS does not preserve the finalized reminder as hidden. The temporary file enters `inbox` only after Shortcuts finishes writing it, and the final JSON file is never modified by the Shortcut.

## Sign and import on macOS

1. Move the single authoritative `Game Reminders` directory to `iCloud Drive/Shortcuts/Game Reminders` and point the Windows app at that location.
2. Download `GameReminder-unsigned.shortcut` from the repository.
3. With Cherri 2.3.0 or later installed, run `cherri GameReminder.cherri --share=anyone --output="Game Reminder.shortcut" --no-ansi`. Do not use `--derive-uuids`; Cherri 2.3.0 reuses control-flow group identifiers in that mode.
4. Import the signed Shortcut on Mac. It must ask for no folders.
5. Let the same Shortcut sync to iPhone and run it there without editing either file action.
6. Test every case in `test-vectors.json` using a temporary catalog where destructive or collision behavior is involved.
7. Inspect the signed Shortcut after importing it. Confirm that both file lookups target the nested `Game Reminders` folder, **Get Parent Directory** is absent, and no personal path, bookmark, or reminder text is embedded.

Do not commit a privately shared export or a file containing Apple contact information. The distributable must be signed with the **Anyone** option.

## Manual validation

- The first interaction after catalog loading is one native **Choose from List** sheet containing every canonical game name exactly once and permitting only one selection.
- Selecting each listed game writes that game's stable ID and current canonical name, regardless of its aliases.
- No **Which game?** voice/text prompt, normalization, alias matching, or unknown-alias path appears.
- Canceling game selection or leaving the reminder prompt empty creates no file.
- Duplicate canonical names fail visibly before the reminder prompt and create no file.
- Quotes, emoji, and line breaks in the reminder message remain valid escaped JSON.
- With `inbox` absent, the first successful run creates it and writes exactly one new UUID-named `.json` file there.
- With `inbox` already present, a successful run reuses it and creates exactly one new UUID-named `.json` file.
- An existing destination is not overwritten.
- If iCloud is unavailable or the save/rename fails, no success message appears.
- After iCloud sync, launching the selected game on Windows displays the reminder.
- A clean import asks for no folder bindings and runs without any unavailable-action warning.
- The same synced Shortcut runs on Mac and iPhone without editing either file action.
