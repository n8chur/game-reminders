# Game Reminders iPhone Shortcut

This directory contains the auditable source definition for the Milestone 3 iPhone Shortcut. The Shortcut reads the Windows-managed catalog from iCloud Drive and writes new reminder files; it never edits `games.json`.

## Artifact status

`GameReminders.shortcut-source.json` is a platform-neutral, ordered definition of the workflow. It is deliberately not named `.shortcut`: Apple validates exported Shortcut files, and an unsigned hand-built property list must not be presented as importable.

The final `GameReminders.shortcut` will be committed after this definition has been assembled and tested in Shortcuts and exported with **Anyone** sharing. Apple documents [file export from iPhone and iPad](https://support.apple.com/guide/shortcuts/share-shortcuts-apdf01f8c054/ios) and [validation/signing of shared files](https://support.apple.com/guide/shortcuts-mac/run-shortcuts-from-the-command-line-apd455c82f02/mac).

## Configuration

The distributed Shortcut asks for the existing `Game Reminders` folder during import. That folder must contain:

```text
Game Reminders/
├── games.json
├── inbox/
├── completed/
└── invalid/
```

The selected folder is the only configurable path. No server URL, account, token, or duplicate database is used.

## Human-readable workflow

1. Get `games.json` from the configured Game Reminders folder. If it cannot be read, show an error and stop.
2. Parse the file as a dictionary. Require `schemaVersion` to equal `1` and `games` to be a list; otherwise show an error and stop.
3. Ask **Which game?** for text. This prompt also accepts dictated text when the Shortcut is run through Siri.
4. Normalize the answer by converting it to lowercase and removing every character that is not a Unicode letter or number. If nothing remains, show an error and stop.
5. Repeat over each game. Normalize its canonical `name` and each string in `aliases` the same way. Add the game to `matches` at most once, keyed by its stable `id`.
6. Count `matches`:
   - zero: show **No game matched. Add or edit the game on Windows, then try again.** and stop;
   - more than one: show **More than one game matched. Make the aliases unique on Windows, then try again.** and stop;
   - exactly one: continue with that game.
7. Ask **What should I remind you?** for text. Reject an empty or whitespace-only answer.
8. Generate a UUID and capture the current date in ISO 8601 form.
9. Build a dictionary with exactly these fields:

   | Key | Value |
   | --- | --- |
   | `schemaVersion` | Number `1` |
   | `id` | Generated UUID |
   | `gameId` | Matched game's stable `id` |
   | `gameNameAtCreation` | Matched game's current canonical `name` |
   | `message` | Reminder text |
   | `createdAt` | ISO 8601 current date |

10. Serialize the dictionary as JSON. Save it first as `.<UUID>.tmp` in `inbox`, with overwrite disabled, then rename it to `<UUID>.json`, also without overwrite. A failed operation remains visible and is never reported as success.
11. Only after the final rename succeeds, show **Reminder saved for <game name>.**

The temporary extension prevents the Windows scanner from treating an incompletely saved file as a reminder. The final JSON file is never modified by the Shortcut.

## Build and export on iPhone or iPad

1. Assemble the actions in the order above, using `GameReminders.shortcut-source.json` for the exact variables, branches, and messages.
2. Add an import question to the folder parameter: **Choose your Game Reminders folder in iCloud Drive.**
3. Test every case in `test-vectors.json` using a temporary iCloud folder.
4. In the Shortcut editor, choose **Export File → Options → File → Anyone**, then save the exported file as `GameReminders.shortcut`.
5. Inspect the exported Shortcut on a second device or after re-importing it. Confirm that the folder import question appears and that no personal path or reminder text is embedded.

Do not commit a privately shared export or a file containing Apple contact information. The distributable must use the **Anyone** option.

## Manual validation

- `Farever`, `FAREVER!`, and the configured `Forever` alias resolve to the same stable game ID.
- An unknown name creates no file.
- Two games whose names/aliases normalize to the same value create no file.
- Empty game and reminder prompts create no file.
- Quotes, emoji, and line breaks in the reminder message remain valid escaped JSON.
- A successful run creates exactly one new UUID-named `.json` file in `inbox`.
- An existing destination is not overwritten.
- If iCloud is unavailable or the save/rename fails, no success message appears.
- After iCloud sync, launching the matched game on Windows displays the reminder.
