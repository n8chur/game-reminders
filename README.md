# Game Reminders

Game Reminders is a private Windows and iPhone system for saving a reminder by voice and displaying it the next time its associated game launches. iCloud Drive is the synchronization layer and durable reminder store; there is no server or account system.

The repository is currently implementing **Milestone 2 (game management)**. It contains the file protocol, Windows process detection, persistent reminders, a notification-area client, Steam discovery, conservative foreground-game detection, and game/alias/executable management. The importable Shortcut, installer, and complete end-user documentation are later milestones.

## Development-build setup

1. Download the `game-reminders-win-x64` artifact from the latest successful GitHub Actions run and extract it.
2. Run `GameReminders.App.exe`.
3. Select the `Game Reminders` folder in iCloud Drive when prompted.
4. Mark that folder **Always keep on this device** in File Explorer.
5. Use **Add game** or **Scan Steam** to populate the catalog, then review any **NEW** or **ACTION REQUIRED** entries and edit optional alternate speech aliases or executable paths as needed. The canonical game name already works for speech matching.
6. Add a conforming reminder file under `inbox`, then launch the configured executable.

Closing the main window leaves Game Reminders running in the notification area. Steam games are added automatically and summarized with a non-blocking notification. Discovery stores the complete Steam-library-relative executable path when one candidate can be identified confidently; an exact root executable is preferred over a similarly named nested binary. Ambiguous games are added with **ACTION REQUIRED** instead of guessing, and detected paths remain available for one-click selection or later correction. A red tray badge and per-game badges persist until a new row is selected or is actually visible when the window is hidden or deactivated; off-screen entries remain new. Removing a Steam game suppresses future automatic imports until **Removed Steam games → Allow re-add** is used. Uncertain foreground detections remain pending until configured or explicitly ignored. An example catalog and reminder are available in [`samples`](samples/). Full installation, Shortcut, usage, supported-feature, and troubleshooting documentation will be completed with Milestone 5.

## Development

The Windows application targets .NET 10 and WPF. On Windows with the .NET 10 SDK installed:

```powershell
dotnet restore GameReminders.slnx
dotnet test GameReminders.slnx --configuration Release --no-restore
dotnet build GameReminders.slnx --configuration Release --no-restore
```

The application requires no administrator privileges and does not inject code into games.
