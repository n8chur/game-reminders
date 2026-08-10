# Game Reminders

Game Reminders is a private Windows and iPhone system for saving a reminder by voice and displaying it the next time its associated game launches. iCloud Drive is the synchronization layer and durable reminder store; there is no server or account system.

The repository is currently implementing **Milestone 2 (game management)**. It contains the file protocol, Windows process detection, persistent reminders, a notification-area client, Steam discovery, conservative foreground-game detection, and game/alias/executable management. The importable Shortcut, installer, and complete end-user documentation are later milestones.

## Development-build setup

1. Download the `game-reminders-win-x64` artifact from the latest successful GitHub Actions run and extract it.
2. Run `GameReminders.App.exe`.
3. Select the `Game Reminders` folder in iCloud Drive when prompted.
4. Mark that folder **Always keep on this device** in File Explorer.
5. Use **Add game** or **Scan Steam** to populate the catalog, then edit aliases or executable names as needed.
6. Add a conforming reminder file under `inbox`, then launch the configured executable.

Closing the main window leaves Game Reminders running in the notification area. Steam games are added automatically and summarized with a non-blocking notification; uncertain foreground detections remain pending until configured or explicitly ignored. An example catalog and reminder are available in [`samples`](samples/). Full installation, Shortcut, usage, supported-feature, and troubleshooting documentation will be completed with Milestone 5.

## Development

The Windows application targets .NET 10 and WPF. On Windows with the .NET 10 SDK installed:

```powershell
dotnet restore GameReminders.slnx
dotnet test GameReminders.slnx --configuration Release --no-restore
dotnet build GameReminders.slnx --configuration Release --no-restore
```

The application requires no administrator privileges and does not inject code into games.
