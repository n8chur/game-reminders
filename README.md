# Game Reminders

Game Reminders is a private Windows and iPhone system for saving a reminder by voice and displaying it the next time its associated game launches. iCloud Drive is the synchronization layer and durable reminder store; there is no server or account system.

The repository is currently implementing **Milestone 4 (reliability and packaging)**. It contains the file protocol, Windows process detection, persistent reminders, a notification-area client, Steam discovery, conservative foreground-game detection, game/alias/executable management, and a cross-device iPhone Shortcut. The installer, diagnostics, and complete end-user documentation are later work.

## Development-build setup

1. Download the `game-reminders-win-x64` artifact from the latest successful GitHub Actions run and extract it.
2. Run `GameReminders.exe`.
3. In first-run setup, select the existing `iCloud Drive/Shortcuts/Game Reminders` folder. The app validates that single authoritative store before monitoring starts; it does not copy, merge, or create a second reminder store.
4. Mark that folder **Always keep on this device** in File Explorer. Optionally enable **Launch Game Reminders when I sign in to Windows** during setup or toggle **Launch at login** later in the main window.
5. Use **Add game** or **Scan Steam** to populate the catalog, then review any **NEW** or **ACTION REQUIRED** entries and edit optional alternate speech aliases or executable paths as needed. The canonical game name already works for speech matching.
6. Download and import [`shortcuts/GameReminder.shortcut`](shortcuts/GameReminder.shortcut). It uses the built-in iCloud Shortcuts container with no folder prompts or device-specific bookmarks, so the same Shortcut works across Mac and iPhone. Create a reminder, then launch the configured executable.

Closing the main window leaves Game Reminders running in the notification area. **Scan Steam** discovers installed Steam games and may update the catalog; opening the iCloud folder and fully exiting are available from the tray menu. Steam games are added automatically and summarized with a non-blocking notification. Discovery stores the complete Steam-library-relative executable path when one candidate can be identified confidently; an exact root executable is preferred over a similarly named nested binary. The editor identifies Steam entries and explains that their executable paths are relative to `steamapps\common`.

If the saved iCloud folder is moved, unavailable, malformed, or no longer writable, startup presents a recovery screen and preserves the saved path until a replacement is deliberately validated and confirmed. Launch at login uses the current user's Windows startup registration and does not require administrator privileges.

Ambiguous games are added with **ACTION REQUIRED** instead of guessing. Their editor marks the executable field in red and disables **Save** until that field is resolved. Detected paths remain available for later correction; select one to replace the current mapping, or use its circular **+** button to append another executable. Manual entries can use **Browse** to select one or more `.exe` files. A blank `games.json` or `{}` is treated as a new empty catalog and rewritten safely. Distinct red **ACTION REQUIRED** and green **NEW** badges persist until a new row is selected or is actually visible when the window is hidden or deactivated; acknowledging a badge does not clear the selected row. Off-screen entries remain new. Configured and pending discoveries share the searchable **Games** view. Removed Steam games and ignored foreground discoveries appear in **Ignored** and can be restored. An example catalog and reminder are available in [`samples`](samples/). Full installation, Shortcut, usage, supported-feature, and troubleshooting documentation will be completed with Milestone 5.

The auditable iPhone workflow definition, generated unsigned Shortcut, test vectors, Apple signing instructions, and tested Apple **Anyone**-signed distribution file are in [`shortcuts`](shortcuts/).

## Development

The Windows application targets .NET 10 and WPF. On Windows with the .NET 10 SDK installed:

```powershell
dotnet restore GameReminders.slnx
dotnet test GameReminders.slnx --configuration Release --no-restore
dotnet build GameReminders.slnx --configuration Release --no-restore
```

The application requires no administrator privileges and does not inject code into games.
