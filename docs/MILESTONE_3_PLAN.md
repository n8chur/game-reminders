# Milestone 3: iPhone Shortcut

Status: completed and superseded by the fixed-list workflow.

## Goal

Add the iPhone half of Game Reminders without introducing a server or a second reminder store. The Shortcut reads the Windows-managed `games.json` catalog from the fixed iCloud Drive folder and creates immutable schema-version-1 reminder files in `inbox`.

## Delivered behavior

1. Present every canonical catalog name in a native, single-selection list.
2. Resolve the selection to exactly one stable game ID; cancel or fail visibly if the selection cannot be resolved uniquely.
3. Ask for a non-empty reminder message.
4. Stage one UUID-named reminder with a temporary extension, move it into `inbox`, and finalize it as JSON without overwriting another reminder.
5. Run on Mac and iPhone using the built-in iCloud Shortcuts container with no device-specific folder bookmark.

The Shortcut never writes `games.json`. Repository source, generated artifacts, validators, signing instructions, and manual validation steps live in `shortcuts/`.
