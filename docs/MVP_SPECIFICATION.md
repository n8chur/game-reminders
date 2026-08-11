# Game Reminders MVP specification

Status: approved August 10, 2026.

## Product boundary

The MVP consists of an iPhone Shortcut, a Windows 11 tray application, and an iCloud Drive folder. It has no server, cloud database, application account, OpenAI API, or native iOS app. The iCloud folder is both the synchronization mechanism and authoritative reminder store.

## Reminder creation

The Shortcut asks for the shared Game Reminders folder separately for its catalog and inbox lookups during import, then reads `games.json`, accepts a dictated or typed game name, and matches it against canonical names and aliases after ignoring capitalization, punctuation, and spacing. Both setup questions must select the same folder. Those iCloud folder bookmarks are configured once per device because Shortcuts does not reliably transfer them between macOS and iPhone. The workflow uses only iPhone-supported actions, creates a reminder only when exactly one game resolves, and fails safely without creating a file for an unknown game. Its zero-match error repeats the submitted game name so the user can identify the missing alias.

Each reminder is an immutable JSON file with schema version 1, UUID, stable game ID, display name at creation, message, and creation timestamp. The Shortcut writes the serialized reminder as visible `<UUID>.tmp` in its private iCloud staging folder, moves the completed temporary file into `inbox`, then renames it to visible `<UUID>.json` without overwrite. It reports success only after finalization succeeds and never modifies the pending file afterward.

## Reminder display

Five seconds after a configured game launches, Windows displays all pending reminders for that game in one persistent window. Each reminder has independent controls:

- **Dismiss** moves the file from `inbox` to `completed`.
- **Show on next launch** closes that reminder while leaving its file in `inbox`.
- Closing the window, Alt+F4, shutdown, crash, or forced termination never completes a reminder.

The dependable display target is borderless fullscreen. The application will not inject into games or interact with anti-cheat systems. A normal Windows notification may be used if the popup cannot be displayed.

## Game discovery and aliases

The client supports Steam metadata, conservative foreground-application detection, and manual addition. Installed Steam games are added automatically with a stable Steam app ID and summarized in one non-blocking notification. Clicking the notification opens game management. Notifications are informational and may be suppressed by Windows without affecting discovery or reminder behavior. Removing an imported Steam game creates Windows-only suppression state keyed by app ID; scans do not recreate it unless the user explicitly allows it to be re-added.

Uncertain foreground-application detections are saved to Windows-only pending state and shown in the **Needs review** section of **Games** for manual configuration or dismissal. Discovery never opens a blocking setup prompt, including during startup and manual scans. Ignored discoveries retain Windows-only display metadata and appear alongside removed Steam games in a launcher-neutral **Ignored** view, where either kind can be restored.

The setup and management UI supports canonical names, optional alternate speech aliases, and one or more associated executables. The canonical name already participates in speech matching; Steam metadata does not provide reliable alternate spoken names, so imported aliases begin empty. Steam discovery excludes known helper executables and ranks remaining candidates by their relationship to the game name, preferring an exact root-level executable over a similarly named nested helper or shipping binary. A confident match stores the complete path relative to `steamapps\common`; this distinguishes generic executable filenames while remaining portable across Steam library roots. The editor displays the source type and explicitly explains the path base for Steam entries.

An ambiguous or missing match is left unconfigured and marked **ACTION REQUIRED**. Its editor marks the executable-path field in red and disables **Save** until at least one executable has been selected or entered, without inserting or removing layout-shifting content. Selecting a detected path replaces the current executable mapping; the right-aligned `+` action appends another path for games that need multiple executables. Candidate paths remain available after selection so the mapping can be corrected later. Manual games offer an executable file picker and store selected files as absolute paths. Existing filename-only mappings remain supported, and absolute paths may still be entered manually.

Newly imported games and games needing executable review are indicated on the Games list and by a persistent tray-icon badge. Informational balloon notifications are optional. Selecting a new game's row acknowledges it immediately without clearing the row selection. Otherwise, **NEW** is acknowledged only for rows that are actually visible in the Games list when the management window is hidden or deactivated; merely opening the tab does not clear off-screen items. An executable-review badge remains until a valid executable mapping is saved.

The management window supports search and uses **Scan Steam** for discovering installed Steam titles and updating the catalog. Closing the window hides it to the notification area. Opening the authoritative iCloud folder and fully exiting are notification-area commands rather than window actions.

## File layout

```text
Game Reminders/
├── games.json
├── inbox/
├── completed/
└── invalid/
```

The folder is configured as **Always keep on this device**. The client scans on startup, on filesystem changes, before showing reminders, and every 60 seconds as a fallback. Malformed files move to `invalid` only after repeated failures. Dismissed reminders are archived until manually cleared.

Only the Windows client writes `games.json`, using a temporary file followed by atomic replacement. IDs remain stable when display names and aliases change. A blank file or empty JSON object is treated as a new empty catalog and rewritten in canonical schema-versioned form; other malformed content still fails visibly.

## Windows implementation

The Windows application uses C#, the current .NET LTS release, and WPF. It follows the Windows light/dark app preference and requires no administrator privileges. Windows-only settings, pending detections, ignored-discovery metadata, suppressed Steam app IDs, review indicators, and diagnostic logs live under the user's application-data directory; reminder state does not.

Development builds are unsigned portable ZIP artifacts built by GitHub Actions. A conventional installer and launch-at-login behavior follow after core behavior stabilizes.

## Delivery milestones

1. File/process prototype: catalog loading, reminder scanning, configured process detection, persistent popup, Dismiss, and Show on next launch.
2. Game management: tray UI, automatic trusted Steam discovery, persistent uncertain detections, and alias/executable editing.
3. iPhone Shortcut: exact normalized matching, no-match handling, reminder creation, repository-hosted importable Shortcut, and human-readable definition.
4. Reliability and packaging: startup registration, rescans, invalid-file handling, diagnostics, first-run wizard, and portable builds.
5. Polish: Windows 11 appearance, multiple reminders and monitors, accessibility, installer, and a complete README covering installation of both components, usage, supported features, limitations, configuration, and troubleshooting.

## Acceptance criteria

- A configured `Forever` alias resolves to Farever; an unknown dictated name is repeated in the error and creates no reminder.
- A reminder created while the PC is off appears after iCloud synchronizes.
- Launching a matching game displays its reminders and the popup persists until handled.
- Closing or crashing cannot complete a reminder.
- Show on next launch redisplays the reminder on a later launch; Dismiss prevents redisplay.
- A Steam game is added without a blocking prompt; ambiguous executable selection is visibly marked for review, and an uncertain foreground detection persists until configured or ignored.
- Removing a Steam game prevents its automatic re-addition, and the user can deliberately restore it.
- Games with identical executable filenames in different paths resolve by path rather than triggering each other's reminders.
- Already-downloaded files work offline.
- Normal operation requires neither administrator privileges nor game injection.
