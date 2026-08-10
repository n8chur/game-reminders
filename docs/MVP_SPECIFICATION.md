# Game Reminders MVP specification

Status: approved August 10, 2026.

## Product boundary

The MVP consists of an iPhone Shortcut, a Windows 11 tray application, and an iCloud Drive folder. It has no server, cloud database, application account, OpenAI API, or native iOS app. The iCloud folder is both the synchronization mechanism and authoritative reminder store.

## Reminder creation

The Shortcut reads `games.json`, accepts a dictated or typed game name, and matches it against canonical names and aliases after ignoring capitalization, punctuation, and spacing. It creates a reminder only when exactly one game resolves. An unknown game fails safely and creates no file.

Each reminder is an immutable JSON file with schema version 1, UUID, stable game ID, display name at creation, message, and creation timestamp. The Shortcut writes one file per reminder to `inbox`.

## Reminder display

Five seconds after a configured game launches, Windows displays all pending reminders for that game in one persistent window. Each reminder has independent controls:

- **Dismiss** moves the file from `inbox` to `completed`.
- **Show on next launch** closes that reminder while leaving its file in `inbox`.
- Closing the window, Alt+F4, shutdown, crash, or forced termination never completes a reminder.

The dependable display target is borderless fullscreen. The application will not inject into games or interact with anti-cheat systems. A normal Windows notification may be used if the popup cannot be displayed.

## Game discovery and aliases

The client supports Steam metadata, conservative foreground-application detection, and manual addition. An unknown likely game is saved to Windows-only pending state before its setup prompt appears. Closing an unresolved setup prompt makes it return after the next login.

The setup and management UI supports canonical names, aliases, and associated executables. Suggestions come from local names and metadata; speech-recognition variants such as `Forever` for `Farever` are added manually.

## File layout

```text
Game Reminders/
├── games.json
├── inbox/
├── completed/
└── invalid/
```

The folder is configured as **Always keep on this device**. The client scans on startup, on filesystem changes, before showing reminders, and every 60 seconds as a fallback. Malformed files move to `invalid` only after repeated failures. Dismissed reminders are archived until manually cleared.

Only the Windows client writes `games.json`, using a temporary file followed by atomic replacement. IDs remain stable when display names and aliases change.

## Windows implementation

The Windows application uses C#, the current .NET LTS release, and WPF. It requires no administrator privileges. Windows-only settings, pending detections, ignored processes, and diagnostic logs live under the user's application-data directory; reminder state does not.

Development builds are unsigned portable ZIP artifacts built by GitHub Actions. A conventional installer and launch-at-login behavior follow after core behavior stabilizes.

## Delivery milestones

1. File/process prototype: catalog loading, reminder scanning, configured process detection, persistent popup, Dismiss, and Show on next launch.
2. Game management: tray UI, Steam discovery, persistent new-game prompts, and alias/executable editing.
3. iPhone Shortcut: exact normalized matching, no-match handling, reminder creation, repository-hosted importable Shortcut, and human-readable definition.
4. Reliability and packaging: startup registration, rescans, invalid-file handling, diagnostics, first-run wizard, and portable builds.
5. Polish: Windows 11 appearance, multiple reminders and monitors, accessibility, installer, and a complete README covering installation of both components, usage, supported features, limitations, configuration, and troubleshooting.

## Acceptance criteria

- A configured `Forever` alias resolves to Farever; an unknown dictated name creates no reminder.
- A reminder created while the PC is off appears after iCloud synchronizes.
- Launching a matching game displays its reminders and the popup persists until handled.
- Closing or crashing cannot complete a reminder.
- Show on next launch redisplays the reminder on a later launch; Dismiss prevents redisplay.
- A new-game setup prompt persists when closed without a decision.
- Already-downloaded files work offline.
- Normal operation requires neither administrator privileges nor game injection.

