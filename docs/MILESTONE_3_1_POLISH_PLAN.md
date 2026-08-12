# Milestone 3.1: Shortcut polish

Status: completed.

Tracking issue: [#5](https://github.com/n8chur/game-reminders/issues/5)

## Goal

Reduce cross-device setup friction while keeping iCloud files authoritative and preventing the Shortcut from modifying `games.json`.

## Delivered iCloud layout

The canonical Shortcut resolves `Game Reminders/games.json` and `Game Reminders/inbox` relative to its built-in iCloud Shortcuts folder. It embeds no external folder bookmark or import question, uses only iPhone-supported actions, and retains the selected game's stable ID.

Changing the authoritative root remains explicit and user-driven:

1. Stop the Windows app and verify iCloud synchronization is complete.
2. Move the complete `Game Reminders` directory as one unit into the iCloud `Shortcuts` directory.
3. Point Windows at the new root and verify one reminder end to end.
4. Retain the old location until the new root is verified; never merge reminder trees automatically.

Git history preserves the earlier design work. Current workflow and validation requirements are documented in `shortcuts/README.md` and `docs/MVP_SPECIFICATION.md`.
